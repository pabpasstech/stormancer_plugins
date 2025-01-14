﻿using Stormancer.Server.Plugins.GameSession.ServerProviders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stormancer.GameServers.Agent
{

    internal class ClientsManager
    {
        public ClientsManager(ILogger<ClientsManager> logger)
        {
            _logger = logger;
        }

        private class AgentClient
        {

            public AgentClient(int index, bool autoUpdate, Client client, ApplicationConfigurationOptions configuration)
            {
                Index = index;
                Client = client;
                Configuration = configuration;

            }

            public int Index { get; }
            public Stormancer.Client Client { get; }
            public ApplicationConfigurationOptions Configuration { get; }
            public bool AutoUpdate { get; set; }
        }


        object _syncRoot = new object();
        private int _nextClientId = 0;
        private Dictionary<int, AgentClient> _clients = new Dictionary<int, AgentClient>();
        private readonly ILogger _logger;

        public CancellationToken StoppingToken { get; set; } = CancellationToken.None;

        /// <summary>
        /// Connects the agent to a Stormancer application.
        /// </summary>
        /// <param name="applicationConfiguration"></param>
        /// <param name="autoUpdate">
        /// A <see cref="bool"/> value indicating if the agent should automatically reconnect when 
        /// a new version of the application is deployed.
        /// </param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        internal async Task ConnectAsync(ApplicationConfigurationOptions applicationConfiguration, bool autoUpdate,CancellationToken cancellationToken)
        {
            int i = -1;
            try
            {
                _logger.Log(LogLevel.Information, "Connecting to application {app}", applicationConfiguration);
                Stormancer.Client client;

                lock (_syncRoot)
                {
                    var old = _clients.Values.FirstOrDefault(x=>x.Configuration.ApplicationUid == applicationConfiguration.ApplicationUid);
                    if(old!=null)
                    {
                        RemoveClient(old.Index);
                    }
                    i = _nextClientId++;
                    client = ClientFactory.GetClient(i);
                    
                    _clients[i] = new AgentClient(i, autoUpdate, client, applicationConfiguration);
                }

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, StoppingToken);
                await client.DependencyResolver.Resolve<AgentApi>().StartAgent(i, applicationConfiguration.UserId ?? Guid.NewGuid().ToString(), applicationConfiguration, cts.Token);



            }
            catch (Exception ex)
            {
                lock (_syncRoot)
                {
                    if (i != -1)
                    {
                        _clients.Remove(i);
                    }
                }
                _logger.Log(LogLevel.Error, "failed to connect to application {app}. Error: {ex}", applicationConfiguration, ex);
            }
        }

        internal async Task StopAsync(ApplicationConfigurationOptions parameters)
        {
            AgentClient? found = default;
            lock (_syncRoot)
            {
                found = _clients.Values.FirstOrDefault(c => c.Configuration == parameters);
            }

            if (found != default)
            {
                found.Client.Disconnect();
                await found.Client.DisposeAsync();
                RemoveClient(found.Index);
            }
        }


        internal void RemoveClient(int clientId)
        {
            lock (_syncRoot)
            {
                if (_clients.Remove(clientId, out var container))
                {
                    try
                    {
                        container.Client.Disconnect();
                    }
                    catch (Exception) { }
                    ClientFactory.ReleaseClient(container.Index);
                }

            }
        }

        internal void AppDeploymentUpdated(int clientId, string activeDeploymentId)
        {
            lock (_syncRoot)
            {
                if (_clients.TryGetValue(clientId, out var client) && client.AutoUpdate)
                {
                    var app = client.Configuration;
                    //Reconnect
                    _ = ConnectAsync(app, client.AutoUpdate, StoppingToken);
                }
            }
        }

        internal void EnsureRunning(ApplicationConfigurationOptions app)
        {
            bool found = false;
            lock (_syncRoot)
            {
                found = _clients.Values.Any(c => c.Configuration == app);
            }

            if (!found)
            {
                _ = ConnectAsync(app, true, StoppingToken);
            }
        }

        internal IEnumerable<ApplicationConfigurationOptions> GetClients()
        {
           
            lock (_syncRoot)
            {
                return _clients.Values.Select(c=>c.Configuration).ToList();
            }

        }
    }
}
