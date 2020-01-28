// MIT License
//
// Copyright (c) 2019 Stormancer
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using Newtonsoft.Json.Linq;
using Stormancer.Core;
using Stormancer.Diagnostics;
using Stormancer.Plugins;
using Stormancer.Server.Plugins.Configuration;
using Stormancer.Server.Plugins.GameFinder;
using Stormancer.Server.Plugins.Party.Dto;
using Stormancer.Server.Plugins.Party.Interfaces;
using Stormancer.Server.Plugins.Party.Model;
using Stormancer.Server.Plugins.ServiceLocator;
using Stormancer.Server.Plugins.Users;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading;
using System.Threading.Tasks;

namespace Stormancer.Server.Plugins.Party
{
    class PartyService : IPartyService
    {
        // stormancer.party => <protocol version>
        // stormancer.party.revision => <PartyService revision>
        // Revision is independent from protocol version. Revision changes when a modification is made to server code (e.g bugfix).
        // Protocol version changes when a change to the communication protocol is made.
        // Protocol versions between client and server are not obligated to match.
        public const string REVISION = "2020-01-28.1";
        public const string REVISION_METADATA_KEY = "stormancer.party.revision";
        private const string LOG_CATEGORY = "PartyService";

        //Dependencies
        private readonly ISceneHost _scene;
        private readonly ILogger _logger;
        private readonly IUserSessions _userSessions;
        private readonly IPartyProxy _partyProxy;
        private readonly IServiceLocator _locator;
        private readonly Func<IEnumerable<IPartyEventHandler>> _handlers;
        private readonly PartyState _partyState;
        private readonly RpcService _rpcService;
        private readonly IUserService _users;
        private readonly IEnumerable<IPartyPlatformSupport> _platformSupports;
        private readonly StormancerPartyPlatformSupport _stormancerPartyPlatformSupport;

        public IReadOnlyDictionary<string, PartyMember> PartyMembers => _partyState.PartyMembers;

        public PartyConfiguration Settings => _partyState.Settings;

        private TimeSpan _clientRpcTimeout = TimeSpan.FromSeconds(2);

        public PartyService(
            ISceneHost scene,
            ILogger logger,
            IUserSessions userSessions,
            IPartyProxy partyProxy,
            IServiceLocator locator,
            Func<IEnumerable<IPartyEventHandler>> handlers,
            PartyState partyState,
            RpcService rpcService,
            IConfiguration configuration,
            IUserService users,
            IEnumerable<IPartyPlatformSupport> platformSupports,
            StormancerPartyPlatformSupport stormancerPartyPlatformSupport)
        {
            _handlers = handlers;
            _scene = scene;
            _logger = logger;
            _userSessions = userSessions;
            _partyProxy = partyProxy;
            _locator = locator;
            _partyState = partyState;
            _rpcService = rpcService;
            _users = users;
            _platformSupports = platformSupports;
            _stormancerPartyPlatformSupport = stormancerPartyPlatformSupport;

            ApplySettings(configuration.Settings);
        }

        private const string JoinDeniedError = "party.joinDenied";
        private const string GameFinderNameError = "party.badArgument.GameFinderName";
        private const string CannotKickLeaderError = "party.cannotKickLeader";
        private const string SettingsOutdatedError = "party.settingsOutdated";
        private const string GenericJoinError = "party.joinError";

        private const string LeaderChangedRoute = "party.leaderChanged";
        private const string MemberConnectedRoute = "party.memberConnected";
        private const string SendPartyStateRoute = "party.getPartyStateResponse";
        private const string GameFinderFailedRoute = "party.gameFinderFailed";

        private void ApplySettings(dynamic settings)
        {
            var timeoutSetting = settings?.party?.clientAckTimeoutSeconds;
            var timeout = (double?)timeoutSetting;

            if (timeout.HasValue && timeout.Value > 0)
            {
                _clientRpcTimeout = TimeSpan.FromSeconds(timeout.Value);
            }
            else if (timeoutSetting != null)
            {
                _logger.Warn("PartyService.ApplySettings", "party.clientAckTimeoutSeconds must be a strictly positive decimal number");
            }
        }

        private string NoSuchMemberError(string userId) => $"party.noSuchMember?userId={userId}";

        private string NoSuchUserError(string userId) => $"party.noSuchUser?userId={userId}";

        private bool TryGetMemberByUserId(string userId, out PartyMember member)
        {
            member = _partyState.PartyMembers.FirstOrDefault(kvp => kvp.Value.UserId == userId).Value;
            return member != null;
        }

        private void Log(LogLevel level, string methodName, string message, string sessionId, string userId = null)
        {
            if (string.IsNullOrEmpty(userId))
            {
                _logger.Log(level, $"PartyService.{methodName}", message,
                    new { PartyId = _partyState.Settings.PartyId, SessionId = sessionId },
                    _partyState.Settings.PartyId, sessionId);
            }
            else
            {
                _logger.Log(level, $"PartyService.{methodName}", message, new { _partyState.Settings.PartyId, SessionId = sessionId, UserId = userId },
                    _partyState.Settings.PartyId, sessionId, userId);
            }
        }

        private void Log(LogLevel level, string methodName, string message, object data = null, params string[] tags)
        {
            var totalParams = tags?.Append(_partyState.Settings.PartyId)?.ToArray() ?? new string[] { _partyState.Settings.PartyId };

            _logger.Log(level, $"PartyService.{methodName}", message,
                new { PartyId = _partyState.Settings.PartyId, Data = data },
                totalParams);
        }

        internal Task OnConnecting(IScenePeerClient peer)
        {
            return _partyState.TaskQueue.PushWork(async () =>
            {
                //Todo jojo later
                // Quand un utilisateur essais de ce connecter au party.
                // Il faut : 
                //  1. V�rfier via une requ�te S2S si il n'est pas d�j� connecter � un party
                //      1. R�cup�rer en S2S les informations de session dans les UserSessionData
                //      1. V�rifier si un party est pr�sent
                //      1. Si il y a un party demande � celui-ci (S2S) si l'utilisateur et bien connecter dessus.
                //  2. Si il ne l'est pas alors on continue le pipeline normal
                //  3. Si il l'est alors on selon la config on change du SA on bloque ou on d�connect depuis l'autre scene et on la co � la nouvelle.
                if (!_partyState.Settings.IsJoinable)
                {
                    throw new ClientException(JoinDeniedError);
                }

                var session = await _userSessions.GetSession(peer);
                if (session == null)
                {
                    throw new ClientException("notAuthenticated");
                }

                var ctx = new JoiningPartyContext(this, session, _partyState.PendingAcceptedPeers.Count + _partyState.PartyMembers.Count);
                await _handlers().RunEventHandler(h => h.OnJoining(ctx), ex => _logger.Log(LogLevel.Error, "party", "An error occured while running OnJoining", ex));
                if (!ctx.Accept)
                {
                    Log(LogLevel.Trace, "OnConnecting", "Join denied by event handler", peer.SessionId, session.User.Id);
                    throw new ClientException(JoinDeniedError);
                }

                Log(LogLevel.Trace, "OnConnecting", "Join accepted", peer.SessionId, session.User.Id);
                _partyState.PendingAcceptedPeers.Add(peer);
            });
        }

        internal Task OnConnectionRejected(IScenePeerClient peer)
        {
            return _partyState.TaskQueue.PushWork(() =>
            {
                Log(LogLevel.Trace, "OnConnectionRejected", "Connection to party was rejected", peer.SessionId);
                _partyState.PendingAcceptedPeers.Remove(peer);
                _ = RunOperationCompletedEventHandler(async (service, handlers, scope) =>
                {
                    var deniedCtx = new JoinDeniedContext(service, await service._userSessions.GetSession(peer));
                    await handlers.RunEventHandler(handler => handler.OnJoinDenied(deniedCtx), exception =>
                    {
                        service.Log(LogLevel.Error, "OnConnectionRejected", "An exception was thrown by an OnJoinDenied event handler", new { exception }, peer.SessionId);
                    });
                });
                return Task.CompletedTask;
            });
        }

        internal Task OnConnected(IScenePeerClient peer)
        {
            return _partyState.TaskQueue.PushWork(async () =>
            {
                _partyState.PendingAcceptedPeers.Remove(peer);
                var user = await _userSessions.GetUser(peer);
                if (user == null)
                {
                    await peer.Disconnect(GenericJoinError);
                    return;
                }
                var partyUser = new PartyMember { UserId = user.Id, StatusInParty = PartyMemberStatus.NotReady, Peer = peer };
                _partyState.PartyMembers.TryAdd(peer.SessionId, partyUser);
                // Complete existing invtations for the new user. These invitations should all have been completed by now, but this is hard to guarantee.
                if (_partyState.PendingInvitations.TryGetValue(user.Id, out var invitations))
                {
                    foreach (var invitation in invitations)
                    {
                        invitation.Value.TaskCompletionSource.TrySetResult(true);
                    }
                    _partyState.PendingInvitations.Remove(user.Id);
                }

                var session = await _userSessions.GetSession(peer);
                var ctx = new JoinedPartyContext(this, session);

                var ClientPluginVersion = peer.Metadata[PartyPlugin.CLIENT_METADATA_KEY];
                Log(LogLevel.Trace, "OnConnected", "Connection complete", new { peer.SessionId, user.Id, ClientPluginVersion }, peer.SessionId, user.Id);

                await BroadcastStateUpdateRpc(MemberConnectedRoute, new PartyMemberDto { PartyUserStatus = partyUser.StatusInParty, UserData = partyUser.UserData, UserId = partyUser.UserId });

                _ = RunOperationCompletedEventHandler((service, handlers, scope) =>
                {
                    var joinedCtx = new JoinedPartyContext(service, session);
                    return handlers.RunEventHandler(handler => handler.OnJoined(joinedCtx), exception =>
                    {
                        service.Log(LogLevel.Error, "OnConnected", "An exception was thrown by an OnJoined event handler", new { exception }, peer.SessionId, user.Id);
                    });
                });
            });
        }

        private async Task RunOperationCompletedEventHandler(Func<PartyService, IEnumerable<IPartyEventHandler>, IDependencyResolver, Task> runner)
        {
            using (var scope = _scene.DependencyResolver.CreateChild(global::Stormancer.Server.Plugins.API.Constants.ApiRequestTag))
            {
                // Resolve the service on the new scope to avoid scope errors in event handlers
                var service = scope.Resolve<IPartyService>() as PartyService;
                var handlers = scope.ResolveAll<IPartyEventHandler>();
                await runner(service, handlers, scope);
            }
        }

        private PartyDisconnectionReason ParseDisconnectionReason(string reason)
        {
            if (reason == "party.kicked")
            {
                return PartyDisconnectionReason.Kicked;
            }
            return PartyDisconnectionReason.Left;
        }

        internal Task OnDisconnected(DisconnectedArgs args)
        {
            return _partyState.TaskQueue.PushWork(async () =>
            {
                PartyMember partyUser = null;
                if (_partyState.PartyMembers.TryRemove(args.Peer.SessionId, out partyUser))
                {
                    Log(LogLevel.Trace, "OnDisconnected", $"Member left the party, reason: {args.Reason}", args.Peer.SessionId, partyUser.UserId);
                    await TryCancelPendingGameFinder();

                    if (_partyState.Settings.PartyLeaderId == partyUser.UserId && _partyState.PartyMembers.Count != 0)
                    {
                        // Change party leader
                        _partyState.Settings.PartyLeaderId = _partyState.PartyMembers.FirstOrDefault().Value.UserId;
                        Log(LogLevel.Trace, "OnDisconnected", $"New leader elected: {_partyState.Settings.PartyLeaderId}", args.Peer.SessionId, partyUser.UserId);
                        await BroadcastStateUpdateRpc(LeaderChangedRoute, _partyState.Settings.PartyLeaderId);
                    }
                    await BroadcastStateUpdateRpc(PartyMemberDisconnection.Route, new PartyMemberDisconnection { UserId = partyUser.UserId, Reason = ParseDisconnectionReason(args.Reason) });
                }

                _ = RunOperationCompletedEventHandler((service, handlers, scope) =>
                {
                    var ctx = new QuitPartyContext(service, args);
                    return handlers.RunEventHandler(handler => handler.OnQuit(ctx), exception => service.Log(LogLevel.Error, "OnDisconnected", "An exception was thrown by an OnQuit event handler", new { exception }, args.Peer.SessionId));
                });
            });
        }

        public void SetConfiguration(dynamic metadata)
        {
            if (metadata.party != null)
            {
                _partyState.Settings = ((JObject)metadata.party).ToObject<PartyConfiguration>();
                _partyState.VersionNumber = 1;
            }
            else
            {
                throw new InvalidOperationException("Scene metadata has no 'party' field");
            }
        }

        public Task UpdateSettings(PartySettingsDto partySettingsDto)
        {
            return _partyState.TaskQueue.PushWork(async () =>
            {
                if (partySettingsDto.GameFinderName == "")
                {
                    throw new ClientException(GameFinderNameError);
                }
                var originalDto = partySettingsDto.Clone();
                var ctx = new PartySettingsUpdateCtx(this, partySettingsDto);
                await _handlers().RunEventHandler(h => h.OnUpdatingSettings(ctx), ex => _logger.Log(LogLevel.Error, "party", "An error occured while running OnOpudatingPartySettings", ex));

                if (!ctx.ApplyChanges)
                {
                    Log(LogLevel.Trace, "UpdateSettings", "Settings update refused by event handler", partySettingsDto);
                    throw new ClientException(ctx.ErrorMsg);
                }

                Log(LogLevel.Trace, "UpdateSettings", "Settings update accepted", partySettingsDto);
                await _handlers().RunEventHandler(h => h.OnUpdateSettings(ctx), ex => _logger.Log(LogLevel.Error, "party", "An error occured while running OnOpudatePartySettings", ex));

                // If the event handlers have modified the settings, we need to notify the leader to invalidate their local copy.
                // Make an additional bump to the version number to achieve this.
                int newSettingsVersion = _partyState.SettingsVersionNumber + 1;
                if (!partySettingsDto.Equals(originalDto))
                {
                    newSettingsVersion = _partyState.SettingsVersionNumber + 2;
                }

                _partyState.Settings.GameFinderName = partySettingsDto.GameFinderName;
                _partyState.Settings.CustomData = partySettingsDto.CustomData;
                _partyState.Settings.OnlyLeaderCanInvite = partySettingsDto.OnlyLeaderCanInvite;
                _partyState.Settings.IsJoinable = partySettingsDto.IsJoinable;
                if (partySettingsDto.PublicServerData != null)
                {
                    _partyState.Settings.PublicServerData = partySettingsDto.PublicServerData;
                }
                _partyState.SettingsVersionNumber = newSettingsVersion;

                await TryCancelPendingGameFinder();

                await BroadcastStateUpdateRpc(
                    PartySettingsUpdateDto.Route,
                    new PartySettingsUpdateDto(_partyState)
                    );
            });
        }

        /// <summary>
        /// Player status 
        /// </summary>
        /// <param name="peer"></param>
        /// <param name="partyUserStatus"></param>
        /// <returns></returns>
        public Task UpdateGameFinderPlayerStatus(string userId, PartyMemberStatusUpdateRequest partyUserStatus)
        {
            return _partyState.TaskQueue.PushWork(async () =>
            {
                PartyMember user = null;
                if (!TryGetMemberByUserId(userId, out user))
                {
                    throw new ClientException(NoSuchMemberError(userId));
                }

                if (user.StatusInParty == partyUserStatus.DesiredStatus)
                {
                    return;
                }
                // Prevent the member from setting themselves to ready if they have outdated party settings
                if (partyUserStatus.DesiredStatus == PartyMemberStatus.Ready && partyUserStatus.ClientSettingsVersion < _partyState.SettingsVersionNumber)
                {
                    throw new ClientException(SettingsOutdatedError);
                }

                user.StatusInParty = partyUserStatus.DesiredStatus;
                Log(LogLevel.Trace, "UpdateGameFinderPlayerStatus", $"Updated user status, new value: {partyUserStatus}", user.Peer.SessionId, user.UserId);

                var update = new BatchStatusUpdate();
                update.UserStatus.Add(new PartyMemberStatusUpdate { UserId = userId, Status = user.StatusInParty });
                await BroadcastStateUpdateRpc(BatchStatusUpdate.Route, update);

                var eventHandlerCtx = new PlayerReadyStateContext(this, user);
                await _handlers().RunEventHandler(h => h.OnPlayerReadyStateChanged(eventHandlerCtx), ex => _logger.Log(LogLevel.Error, "party", "An error occured while running OnPlayerReadyStateChanged", ex));

                bool shouldLaunchGameFinderRequest = false;
                switch (eventHandlerCtx.GameFinderPolicy)
                {
                    case GameFinderRequestPolicy.StartNow:
                        shouldLaunchGameFinderRequest = true;
                        break;
                    case GameFinderRequestPolicy.StartWhenAllMembersReady:
                        shouldLaunchGameFinderRequest = _partyState.PartyMembers.All(kvp => kvp.Value.StatusInParty == PartyMemberStatus.Ready);
                        break;
                    case GameFinderRequestPolicy.DoNotStart:
                        shouldLaunchGameFinderRequest = false;
                        break;
                }

                if (shouldLaunchGameFinderRequest)
                {
                    Log(LogLevel.Trace, "UpdateGameFinderPlayerStatus", "Launching a FindGame request");
                    LaunchGameFinderRequest();
                }
                else if (IsGameFinderRunning)
                {
                    await TryCancelPendingGameFinder();
                }
            });
        }

        private bool IsGameFinderRunning
        {
            get
            {
                return _partyState.FindGameRequest != null;
            }
        }

        public ConcurrentDictionary<string, object> ServerData => _partyState.ServerData;

        public Task UpdatePartyUserData(string userId, byte[] data)
        {
            return _partyState.TaskQueue.PushWork(async () =>
            {
                PartyMember partyUser = null;
                if (!TryGetMemberByUserId(userId, out partyUser))
                {
                    throw new ClientException(NoSuchMemberError(userId));
                }

                partyUser.UserData = data;
                Log(LogLevel.Trace, "UpdatePartyUserData", "Updated user data", new { partyUser.Peer.SessionId, partyUser.UserId, UserData = data });

                await BroadcastStateUpdateRpc(PartyMemberDataUpdate.Route, new PartyMemberDataUpdate { UserId = userId, UserData = partyUser.UserData });
            });
        }

        public Task PromoteLeader(string playerToPromote)
        {
            return _partyState.TaskQueue.PushWork(async () =>
            {
                PartyMember user;
                if (!TryGetMemberByUserId(playerToPromote, out user))
                {
                    throw new ClientException(NoSuchMemberError(playerToPromote));
                }

                if (_partyState.Settings.PartyLeaderId == playerToPromote)
                {
                    return;
                }

                _partyState.Settings.PartyLeaderId = playerToPromote;
                Log(LogLevel.Trace, "PromoteLeader", $"Promoted new leader, userId: {user.UserId}", user.Peer.SessionId, user.UserId);

                await BroadcastStateUpdateRpc(LeaderChangedRoute, _partyState.Settings.PartyLeaderId);
            });
        }

        public Task KickPlayerByLeader(string playerToKick)
        {
            return _partyState.TaskQueue.PushWork(async () =>
            {
                if (TryGetMemberByUserId(playerToKick, out var partyUser))
                {
                    if (playerToKick == _partyState.Settings.PartyLeaderId)
                    {
                        throw new ClientException(CannotKickLeaderError);
                    }
                    await TryCancelPendingGameFinder();

                    _partyState.PartyMembers.TryRemove(partyUser.Peer.SessionId, out partyUser);
                    Log(LogLevel.Trace, "KickPlayerByLeader", $"Kicked a player, userId: {partyUser.UserId}", partyUser.Peer.SessionId, partyUser.UserId);

                    await partyUser.Peer.Disconnect("party.kicked");
                    await BroadcastStateUpdateRpc(PartyMemberDisconnection.Route, new PartyMemberDisconnection { UserId = partyUser.UserId, Reason = PartyDisconnectionReason.Kicked });
                }
                // Do not return an error if the player is already gone
            });
        }

        private void LaunchGameFinderRequest()
        {
            if (!IsGameFinderRunning)
            {
                _partyState.FindGameCts = new CancellationTokenSource();
                _partyState.FindGameRequest = FindGame_Impl();
            }
        }

        private async Task FindGame_Impl()
        {

            //Select provider for the gamefinder extractor
            var provider = "PartyQueue";

            //Construct gameFinder request
            GameFinderRequest gameFinderRequest = new GameFinderRequest();
            foreach (var partyUser in _partyState.PartyMembers.Values)
            {
                gameFinderRequest.ProfileIds.Add(partyUser.UserId, partyUser.ProfileId);
            }

            gameFinderRequest.CustomData = _partyState.Settings.CustomData;

            //Send S2S find match request
            try
            {
                var sceneUri = await _locator.GetSceneId("stormancer.plugins.gamefinder", _partyState.Settings.GameFinderName);
                await _partyProxy.FindMatch(provider, sceneUri, gameFinderRequest, _partyState.FindGameCts.Token);
            }
            catch (TaskCanceledException)
            {
                Log(LogLevel.Trace, "FindGame_Impl", "The S2S FindGame request was canceled");
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, "FindGame_Impl", "An error occurred during the S2S FindGame request", ex);
                BroadcastFFNotification(GameFinderFailedRoute, new GameFinderFailureDto { Reason = ex.Message });
            }
            finally
            {
                await _partyState.TaskQueue.PushWork(async () =>
                {
                    await ResetMembersReadiness();

                    _partyState.FindGameRequest = null;
                    _partyState.FindGameCts.Dispose();
                    _partyState.FindGameCts = null;
                });
            }
        }

        private Task TryCancelPendingGameFinder()
        {
            if (_partyState.FindGameCts != null)
            {
                // In this case, the party members' status will be reset after the request is canceled.
                _partyState.FindGameCts.Cancel();
                return Task.CompletedTask;
            }
            else
            {
                return ResetMembersReadiness();
            }
        }

        private async Task ResetMembersReadiness()
        {
            var update = new BatchStatusUpdate();
            foreach (var partyUser in _partyState.PartyMembers.Values)
            {
                if (partyUser.StatusInParty != PartyMemberStatus.NotReady)
                {
                    partyUser.StatusInParty = PartyMemberStatus.NotReady;
                    update.UserStatus.Add(new PartyMemberStatusUpdate { UserId = partyUser.UserId, Status = partyUser.StatusInParty });
                }
            }

            if (update.UserStatus.Count > 0)
            {
                await BroadcastStateUpdateRpc(BatchStatusUpdate.Route, update);
            }
        }

        // This method should be called to notify party members when the party's state is updated.
        private async Task BroadcastStateUpdateRpc<T>(string route, T data)
        {
            _partyState.VersionNumber++;

            if (_partyState.PartyMembers.IsEmpty)
            {
                return;
            }

            using (var cts = new CancellationTokenSource())
            {
                cts.CancelAfter(_clientRpcTimeout);

                await Task.WhenAll(
                    _partyState.PartyMembers.Values.Select(member =>
                        _rpcService.Rpc(
                            route,
                            member.Peer,
                            s =>
                            {
                                member.Peer.Serializer().Serialize(_partyState.VersionNumber, s);
                                member.Peer.Serializer().Serialize(data, s);
                            },
                            PacketPriority.MEDIUM_PRIORITY,
                            cts.Token
                        ).LastOrDefaultAsync().ToTask()
                        .ContinueWith(task =>
                        {
                            if (task.IsFaulted && !(task.Exception.InnerException is OperationCanceledException))
                            {
                                Log(
                                    LogLevel.Trace,
                                    "BroadcastStateUpdateRpc",
                                    $"An error occurred during a client RPC (route: '{route}')",
                                    new { member.UserId, member.Peer.SessionId, task.Exception, Route = route },
                                    member.UserId, member.Peer.SessionId
                                );
                            }
                        })
                    ) // PartyMembers.Values.Select()
                ); // Task.WhenAll()
            } // using cts
        }

        // This method should be used to broadcast a notification to party members that is not part of the party state.
        private void BroadcastFFNotification<T>(string route, T data)
        {
            _scene.Broadcast(route, data);
        }

        public Task SendPartyState(string recipientUserId)
        {
            return _partyState.TaskQueue.PushWork(async () =>
            {
                PartyMember partyUser;
                if (!TryGetMemberByUserId(recipientUserId, out partyUser))
                {
                    throw new ClientException(NoSuchMemberError(recipientUserId));
                }

                var state = MakePartyStateDto();

                using (var cts = new CancellationTokenSource())
                {
                    cts.CancelAfter(_clientRpcTimeout);

                    await _rpcService.Rpc(
                        SendPartyStateRoute,
                        partyUser.Peer,
                        s => partyUser.Peer.Serializer().Serialize(state, s),
                        PacketPriority.HIGH_PRIORITY,
                        cts.Token
                        ).LastOrDefaultAsync().ToTask();
                }
            });
        }

        public Task SendPartyStateAsRequestAnswer(RequestContext<IScenePeerClient> ctx)
        {
            return _partyState.TaskQueue.PushWork(() =>
            {
                if (!_partyState.PartyMembers.ContainsKey(ctx.RemotePeer.SessionId))
                {
                    throw new ClientException(NoSuchMemberError(ctx.RemotePeer.SessionId));
                }

                var dto = MakePartyStateDto();

                return ctx.SendValue(dto);
            });
        }

        private PartyStateDto MakePartyStateDto()
        {
            return new PartyStateDto
            {
                LeaderId = _partyState.Settings.PartyLeaderId,
                Settings = new PartySettingsUpdateDto(_partyState),
                PartyMembers = _partyState.PartyMembers.Values.Select(member =>
                    new PartyMemberDto { PartyUserStatus = member.StatusInParty, UserData = member.UserData, UserId = member.UserId }).ToList(),
                Version = _partyState.VersionNumber
            };
        }

        public async Task<bool> SendInvitation(string senderUserId, string recipientUserId, bool forceStormancerInvite, CancellationToken cancellationToken)
        {
            PartyMember senderMember;
            if (!TryGetMemberByUserId(senderUserId, out senderMember))
            {
                throw new ClientException(NoSuchMemberError(senderUserId));
            }

            User recipientUser = null;
            var recipientSession = await _userSessions.GetSessionByUserId(recipientUserId);
            if (recipientSession == null)
            {
                recipientUser = await _users.GetUser(recipientUserId);
                if (recipientUser == null)
                {
                    throw new ClientException(NoSuchUserError(recipientUserId));
                }
            }

            var senderSession = await _userSessions.GetSession(senderMember.Peer);

            IPartyPlatformSupport ChooseInvitationPlatform()
            {
                if (forceStormancerInvite)
                {
                    return _stormancerPartyPlatformSupport;
                }

                IPartyPlatformSupport platform = null;
                // If the recipient is connected
                if (recipientSession != null)
                {
                    // If they are on the same platform, choose this platform's handler in priority
                    if (recipientSession.platformId.Platform == senderSession.platformId.Platform)
                    {
                        platform = _platformSupports.FirstOrDefault(platformSupport => platformSupport.PlatformName == senderSession.platformId.Platform);
                    }
                    // If they aren't, or if there is no handler for their platform, try a generic one (stormancer)
                    if (platform == null)
                    {
                        platform = _platformSupports.FirstOrDefault(platformSupport =>
                        platformSupport.IsInvitationCompatibleWith(recipientSession.platformId.Platform) && platformSupport.IsInvitationCompatibleWith(senderSession.platformId.Platform));
                    }
                }
                else
                {
                    if (recipientUser.Auth.ContainsKey(senderSession.platformId.Platform))
                    {
                        platform = _platformSupports.FirstOrDefault(platformSupport =>
                        platformSupport.PlatformName == senderSession.platformId.Platform && platformSupport.CanSendInviteToDisconnectedPlayer);
                    }
                    if (platform == null)
                    {
                        platform = _platformSupports.FirstOrDefault(platformSupport =>
                        platformSupport.CanSendInviteToDisconnectedPlayer &&
                        recipientUser.Auth.Properties().Any(prop => platformSupport.IsInvitationCompatibleWith(prop.Name)) &&
                        platformSupport.IsInvitationCompatibleWith(senderSession.platformId.Platform));
                    }
                }
                return platform;
            }

            var platform = ChooseInvitationPlatform();
            if (platform == null)
            {
                Log(LogLevel.Error, "SendInvitation", "No suitable invitation platform found", new
                {
                    senderUserId,
                    recipientUserId,
                    senderPlatformId = senderSession.platformId,
                    recipientPlatformId = recipientSession?.platformId.Platform ?? "<N.A.: recipient is not online>",
                    recipientAuth = recipientUser?.Auth.Properties().Select(prop => prop.Name),
                    recipientIsOnline = recipientSession != null
                }, senderUserId, senderSession.SessionId, recipientUserId);
                throw new Exception("No suitable invitation platform found");
            }

            // Do not block the party's TaskQueue.
            var invitation = new Invitation(platform.PlatformName, cancellationToken);
            await _partyState.TaskQueue.PushWork(async () =>
            {
                if (TryGetMemberByUserId(recipientUserId, out _))
                {
                    invitation.TaskCompletionSource.TrySetResult(true);
                    return;
                }

                ConcurrentDictionary<string, Invitation> recipientInvitations;
                if (!_partyState.PendingInvitations.TryGetValue(recipientUserId, out recipientInvitations))
                {
                    recipientInvitations = new ConcurrentDictionary<string, Invitation>();
                    _partyState.PendingInvitations.Add(recipientUserId, recipientInvitations);
                }

                if (recipientInvitations.TryGetValue(senderUserId, out var existingInvitation))
                {
                    if (existingInvitation.PlatformName == platform.PlatformName)
                    {
                        invitation.TaskCompletionSource = existingInvitation.TaskCompletionSource;
                        return;
                    }
                    else
                    {
                        existingInvitation.Cts.Cancel();
                    }
                }
                recipientInvitations[senderUserId] = invitation;
                _ = platform.SendInvitation(new InvitationContext(this, senderSession, recipientUserId, invitation.Cts.Token))
                .ContinueWith(task =>
                {
                    if (task.Exception != null)
                    {
                        invitation.TaskCompletionSource.TrySetException(task.Exception);
                    }
                    else if (task.IsCanceled)
                    {
                        invitation.TaskCompletionSource.TrySetCanceled();
                    }
                    else
                    {
                        invitation.TaskCompletionSource.TrySetResult(task.Result);
                    }
                    // This line here is the reason recipientInvitations is a concurrent dictionary. This may happen concurrently with the invitee's connection process that iterates over the dic.
                    recipientInvitations.TryRemove(senderUserId, out _);
                });
                await Task.CompletedTask; // Silence warning
            });

            return await invitation.TaskCompletionSource.Task;
        }

        public bool CanSendInvitation(string senderUserId)
        {
            if (Settings.PartyLeaderId == senderUserId)
            {
                return true;
            }

            if (TryGetMemberByUserId(senderUserId, out _) && !Settings.OnlyLeaderCanInvite)
            {
                return true;
            }

            return false;
        }
    }
}
