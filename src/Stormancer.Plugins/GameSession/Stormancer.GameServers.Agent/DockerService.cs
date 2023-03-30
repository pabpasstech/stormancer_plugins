﻿
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Configuration;
using RakNet;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Stormancer.GameServers.Agent
{
    public enum ContainerEventType
    {
        Start,
        Stop
    }
    public class StartContainerResult
    {
        public bool Success { get; set; }

        public ServerContainer? Container { get; set; }
    }
    internal class DockerService : IDisposable, IProgress<Message>
    {
        private object _lock = new object();

        private DockerClient _docker;
        private readonly ILogger _logger;
        private readonly IConfiguration _configuration;
        private readonly PortsManager _portsManager;
        private readonly Messager _messager;
        private readonly DockerAgentConfigurationOptions _options;



        public DockerService(
            ILogger<DockerService> logger,
            IConfiguration configuration,
            PortsManager portsManager,
            Messager messager)
        {
            var dockerConfig = new DockerClientConfiguration();


            _docker = dockerConfig.CreateClient();
            _logger = logger;
            _configuration = configuration;
            _portsManager = portsManager;
            _messager = messager;
            _options = new DockerAgentConfigurationOptions();

            _configuration.Bind(_options.Section, _options);
            _ = StartMonitorDocker();
        }



        public string AgentId => _options.Id;
        public async Task<bool> IsDockerRunning()
        {
            try
            {
                await _docker.System.PingAsync();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public async Task StartAgent(CancellationToken cancellationToken)
        {
            while (!await IsDockerRunning() && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(1000);
                _logger.LogError("Failed to contact the docker daemon.");
            }
            _logger.LogInformation("Docker agent found.");
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var containers = await _docker.Containers.ListContainersAsync(new ContainersListParameters { All = true });
            foreach (var container in containers)
            {
                if (container.Labels.TryGetValue("stormancer.agent", out var agentId) && AgentId == agentId)
                {
                    await _docker.Containers.KillContainerAsync(container.ID, new ContainerKillParameters { Signal = "SIGKILL" });
                }
            }

        }

        private Dictionary<string, ServerContainer> _trackedContainers = new Dictionary<string, ServerContainer>();

        public long TotalMemory => _options.MaxMemory;
        public float TotalCpu => _options.MaxCpu;

        public long UsedMemory
        {
            get
            {
                lock (_lock)
                {
                    return _trackedContainers.Sum(kvp => kvp.Value.Memory);
                }
            }
        }

        public float UsedCpu
        {
            get
            {
                lock (_lock)
                {
                    return _trackedContainers.Sum(kvp => kvp.Value.CpuQuota);
                }
            }

        }
        public Action<ServerContainerStateChange>? OnContainerStateChanged { get; set; }

        public async Task<StartContainerResult> StartContainer(
            string image,
            string name,
            string agentUserId,
            Dictionary<string, string> labels,
            Dictionary<string, string> environmentVariables,
            long memory,
            float cpuQuota)
        {
            ServerContainer serverContainer;
            lock (_trackedContainers)
            {
                if (memory + this.UsedMemory > this.TotalMemory)
                {
                    return new StartContainerResult { Success = false };
                }

                if (cpuQuota + this.UsedCpu > this.TotalCpu)
                {
                    return new StartContainerResult { Success = false };
                }
                serverContainer = new ServerContainer(name, image, DateTime.UtcNow, memory, cpuQuota);
                _trackedContainers.Add(name, serverContainer);
            }

            try
            {

                _logger.Log(LogLevel.Information, "Starting docker container {name} from image '{image}'.", name, image);
                var images = await _docker.Images.ListImagesAsync(new ImagesListParameters { All = true });
                if (!images.Any(i => i.RepoTags.Contains(image)))
                {
                    _logger.Log(LogLevel.Information, "Downloading image {name}...", image);
                    await _docker.Images.CreateImageAsync(new ImagesCreateParameters { FromImage = image }, new AuthConfig { }, NullDockerJsonMessageProgress.Instance);
                    _logger.Log(LogLevel.Information, "Image {name} downloaded.", image);
                }


                var publicIp = _options.PublicIp;
                var portReservation = _portsManager.AcquirePort();

                labels["stormancer.agent"] = AgentId;
                labels["stormancer.agent.userId"] = agentUserId;
                CreateContainerParameters parameters = new CreateContainerParameters()
                {
                    Image = image,
                    Name = name,
                    Labels = labels,

                    HostConfig = new HostConfig()
                    {

                        DNS = new[] { "8.8.8.8", "8.8.4.4" },//Use Google DNS. 
                        PortBindings = new Dictionary<string, IList<PortBinding>>
                        {
                            [portReservation.Port + "/udp"] = new List<PortBinding>
                        {
                            new PortBinding
                            {
                                HostIP = publicIp,
                                HostPort = portReservation.Port.ToString()
                            }
                        }
                        },

                        Memory = memory,
                        CPUPeriod = 100000,
                        CPUQuota = (long)(100000 * cpuQuota)

                    },

                    Tty = true,
                    ExposedPorts = new Dictionary<string, EmptyStruct> { { portReservation.Port + "/udp", new EmptyStruct() } },
                    Env = environmentVariables.Select(kvp => $"{kvp.Key}={kvp.Value}").ToList(),

                };

                _logger.Log(LogLevel.Information, "Creating docker container from image {image}.", image);

                var response = await _docker.Containers.CreateContainerAsync(parameters);

                _logger.Log(LogLevel.Information, "Starting docker container {id} from image {image}.", response.ID, image);

                serverContainer.DockerContainerId = response.ID;


                var startResponse = await _docker.Containers.StartContainerAsync(response.ID, new ContainerStartParameters { });


                return new StartContainerResult { Success = true, Container = serverContainer };

            }
            catch
            {
                lock (_lock)
                {
                    _trackedContainers.Remove(name);
                }
                return new StartContainerResult { Success = false };
            }

        }

        public Task<bool> StopContainer(string id, uint waitBeforeKillSeconds)
        {

            return _docker.Containers.StopContainerAsync(id, new Docker.DotNet.Models.ContainerStopParameters { WaitBeforeKillSeconds = waitBeforeKillSeconds });

        }

        public async IAsyncEnumerable<ServerContainer> ListContainers()
        {
            var response = await _docker.Containers.ListContainersAsync(new Docker.DotNet.Models.ContainersListParameters { All = true });


            foreach (var container in response)
            {
                if (_trackedContainers.TryGetValue(container.ID, out var server))
                {
                    yield return server;
                }

            }
        }

        private class ResponseSource<T> : IProgress<T>
        {
            private Subject<T> _completed = new Subject<T>();


            public ResponseSource(Func<IProgress<T>, CancellationToken, Task> func, CancellationToken cancellationToken)
            {
                _ = RunRequest(func, cancellationToken);
            }

            public void Report(T value)
            {
                _completed.OnNext(value);
            }


            private async Task RunRequest(Func<IProgress<T>, CancellationToken, Task> func, CancellationToken cancellationToken)
            {
                try
                {
                    await func(this, cancellationToken);
                    _completed.OnCompleted();
                }
                catch (Exception ex)
                {
                    _completed.OnError(ex);
                }
            }
            public IAsyncEnumerable<T> GetResponses()
            {

                return _completed.ToAsyncEnumerable();
            }

            public IObservable<T> GetObservable() => _completed;


        }
        public IAsyncEnumerable<ContainerStatsResponse> GetContainerStatsAsync(string id, bool stream, bool oneShot, CancellationToken cancellationToken)
        {


            var source = new ResponseSource<ContainerStatsResponse>((progress, ct) => _docker.Containers.GetContainerStatsAsync(id, new ContainerStatsParameters { Stream = stream, OneShot = oneShot }, progress, ct), cancellationToken);
            return source.GetResponses();
        }



        internal IAsyncEnumerable<IEnumerable<string>> GetContainerLogsAsync(string containerId, DateTime? since, DateTime? until, uint size, bool follow, CancellationToken cancellationToken)
        {
            var source = new ResponseSource<string>((progress, ct) =>
            {
                var args = new ContainerLogsParameters { Follow = false, ShowStderr = true, ShowStdout = true, Timestamps = true };
                if (size > 0)
                {
                    args.Tail = size.ToString();
                }
                if (since != null)
                {
                    args.Since = since.ToString();

                }
                if (until != null)
                {
                    args.Until = until.ToString();
                }

                return _docker.Containers.GetContainerLogsAsync(containerId, args, cancellationToken, progress);
            }, cancellationToken);



            return source.GetObservable().Buffer(TimeSpan.FromSeconds(1), 100).ToAsyncEnumerable();

        }

        private bool _shouldMonitorDocker = true;
        private DateTime _monitorSince;
        private async Task StartMonitorDocker()
        {
            while (_docker != null)
            {
                await Task.Delay(1000);

                if (!_shouldMonitorDocker)
                {
                    return;
                }
                try
                {
                    await _docker.System.MonitorEventsAsync(new ContainerEventsParameters { Since = ((int)(_monitorSince - DateTime.UnixEpoch).TotalSeconds).ToString() }, this);
                }
                catch(TimeoutException)
                {
                    await Task.Delay(1000);
                }
                catch (Exception ex)
                {
                    _logger.Log(LogLevel.Warning, "an error occured while querying docker for events : {ex}", ex);
                    await Task.Delay(1000);

                }
            }
        }


        public void Dispose()
        {
            _shouldMonitorDocker = false;
            _docker?.Dispose();
        }

        public void Report(Message value)
        {
            _monitorSince = DateTime.UtcNow;
            if (value.Status == "stop")
            {

                if (_trackedContainers.Remove(value.ID, out var server))
                {
                    using (server)
                    {

                        _logger.Log(LogLevel.Information, "Docker container {id} stopped.", value.ID);

                        this.OnContainerStateChanged?.Invoke(new ServerContainerStateChange { Container = server, Status = ContainerEventType.Stop });
                        _messager.PostServerStoppedMessage(server);

                    }
                }
            }
            else if (value.Status == "start")
            {
                if (_trackedContainers.TryGetValue(value.ID, out var server))
                {
                    this.OnContainerStateChanged?.Invoke(new ServerContainerStateChange { Container = server, Status = ContainerEventType.Start });
                }
            }
        }

        internal async Task<AgentStatus> GetStatus()
        {
            try
            {
                var version = await _docker.System.GetVersionAsync();
                return new AgentStatus
                {
                    Claims = _options.Attributes,

                    AgentVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? string.Empty,
                    DockerVersion = version.Version
                };
            }
            catch (Exception ex)
            {
                return new AgentStatus
                {
                    Claims = _options.Attributes,

                    AgentVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? string.Empty,
                    DockerVersion = string.Empty,
                    Error = ex.ToString()
                };
            }
        }
    }

    public class AgentStatus
    {
        public Dictionary<string, string> Claims { get; set; }

        public string DockerVersion { get; set; }

        public string AgentVersion { get; set; }
        public string Error { get; set; }
    }

    public class ServerContainer : IDisposable
    {
        internal ServerContainer(string name, string image, DateTime created, long memory, float cpuCount)
        {

            Name = name;
            Image = image;
            Created = created;
            Memory = memory;
            CpuQuota = cpuCount;
        }

        public string? DockerContainerId { get; set; }
        public string Name { get; }
        public string Image { get; }
        public DateTime Created { get; }
        public long Memory { get; }
        public float CpuQuota { get; }

        public void AddResource(IDisposable resource)
        {
            _resources.Add(resource);
        }

        private List<IDisposable> _resources = new List<IDisposable>();
        public void Dispose()
        {
            foreach (var resource in _resources)
            {
                resource.Dispose();
            }
        }
    }

    public enum ServerContainerStateChangeEventType
    {
        Start,
        Stop
    }
    public class ServerContainerStateChange
    {
        public ServerContainer Container { get; set; }
        public ContainerEventType Status { get; internal set; }
    }

    public class GetLogsResult
    {
        public string StdOut { get; set; }
        public string StdErr { get; set; }
    }
}