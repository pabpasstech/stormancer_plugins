﻿// MIT License
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

using Stormancer.Core;
using Stormancer.Core.Helpers;
using Stormancer.Diagnostics;
using Stormancer.Plugins;
using Stormancer.Server.Components;
using Stormancer.Server.Plugins.Database;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Reactive.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Stormancer.Server.Plugins.Users
{
    /// <summary>
    /// Possible disconnection reasons.
    /// </summary>
    public enum DisconnectionReason
    {
        /// <summary>
        /// The client disconnected.
        /// </summary>
        ClientDisconnected,

        /// <summary>
        /// The session with the client was lost.
        /// </summary>
        ConnectionLoss,

        /// <summary>
        /// The session was replaced with a new connection for the same user.
        /// </summary>
        NewConnection,

        /// <summary>
        /// The session was closed by the server.
        /// </summary>
        ServerRequest
    }
    internal interface IUserPeerIndex : IIndex<string> { }
    internal class UserPeerIndex : InMemoryIndex<string>, IUserPeerIndex { }

    internal interface IPeerUserIndex : IIndex<SessionRecord> { }
    internal class PeerUserIndex : InMemoryIndex<SessionRecord>, IPeerUserIndex { }

    internal interface IHandleUserIndex : IIndex<string> { }
    internal class HandleUserIndex : InMemoryIndex<string>, IHandleUserIndex { }

    /// <summary>
    /// Stored object representing a session. 
    /// </summary>
    public class SessionRecord
    {
        /// <summary>
        /// Gets the platformId associated with the session.
        /// </summary>
        public PlatformId platformId { get; set; }

        /// <summary>
        /// Gets the user associated with the session.
        /// </summary>
        /// <remarks>
        /// Can be null if the session is anonymous.
        /// </remarks>
        public User? User { get; set; }

        /// <summary>
        /// Gets the id of the session.
        /// </summary>
        public string SessionId { get; set; } = default!;

        /// <summary>
        /// Gets the identities associated with the session.
        /// </summary>
        public ConcurrentDictionary<string, string> Identities { get; set; } = new ConcurrentDictionary<string, string>();

        /// <summary>
        /// Gets the authentication expiration dates per identity.
        /// </summary>
        public Dictionary<string, DateTime> AuthenticationExpirationDates { get; } = new Dictionary<string, DateTime>();

        /// <summary>
        /// Gets the session data.
        /// </summary>
        public ConcurrentDictionary<string, byte[]> SessionData { get; internal set; } = new ConcurrentDictionary<string, byte[]>();

        /// <summary>
        /// Gets or sets the date the session was created.
        /// </summary>
        public DateTime ConnectedOn { get; internal set; }

        /// <summary>
        /// absolute scene Url to the authenticator scene
        /// </summary>
        public string AuthenticatorUrl { get; set; } = default!;

        /// <summary>
        /// If the session is cached, the date at which it should expire
        /// </summary>
        public DateTimeOffset? MaxAge { get => AuthenticationExpirationDates.Count > 0 ? AuthenticationExpirationDates.Min().Value : (DateTimeOffset?)null; }

        /// <summary>
        /// Creates a view of the session.
        /// </summary>
        /// <returns></returns>
        public Session CreateView()
        {
            return new Session
            {
                platformId = platformId,
                User = User,
                SessionId = SessionId,
                SessionData = new Dictionary<string, byte[]>(SessionData),
                Identities = new Dictionary<string, string>(Identities),
                ConnectedOn = ConnectedOn,
                AuthenticatorUrl = AuthenticatorUrl,
                MaxAge = MaxAge
            };
        }
    }
    
    /// <summary>
    /// Represents a session.
    /// </summary>
    public class Session
    {
        /// <summary>
        /// Gets the main platform id associated with the session.
        /// </summary>
        public PlatformId platformId { get; set; }

        /// <summary>
        /// Gets the user associated with the session if it exists.
        /// </summary>
        public User? User { get; set; }

        /// <summary>
        /// Gets the session id.
        /// </summary>
        public string SessionId { get; set; } = default!;

        /// <summary>
        /// List of identities of the session.
        /// </summary>
        public IReadOnlyDictionary<string, string> Identities { get; set; } = default!;

        /// <summary>
        /// Gets session data associated with the session.
        /// </summary>
        public IReadOnlyDictionary<string, byte[]> SessionData { get; internal set; } = default!;

        /// <summary>
        /// Gets the <see cref="DateTime"/> object representing when the session was created.
        /// </summary>
        public DateTime ConnectedOn { get; internal set; }

        /// <summary>
        /// absolute scene Url to the authenticator scene
        /// </summary>
        public string AuthenticatorUrl { get; set; } = default!;

        /// <summary>
        /// If the session is cached, the date at which it should expire
        /// </summary>
        public DateTimeOffset? MaxAge { get; internal set; }
    }

    internal class UserSessions : IUserSessions
    {
        private readonly IUserPeerIndex _userPeerIndex;
        private readonly IUserService _userService;
        private readonly IPeerUserIndex _peerUserIndex;
        private readonly Func<IEnumerable<IUserSessionEventHandler>> _eventHandlers;
        private readonly IESClientFactory _esClientFactory;
        private readonly ISerializer serializer;
        private readonly IEnvironment env;
        private readonly ISceneHost _scene;
        private readonly ILogger logger;
        private readonly RpcService rpcService;
        private readonly IHandleUserIndex _handleUserIndex;


        public UserSessions(IUserService userService,
            IPeerUserIndex peerUserIndex,
            IUserPeerIndex userPeerIndex,
            Func<IEnumerable<IUserSessionEventHandler>> eventHandlers,
            ISerializer serializer,
            IESClientFactory eSClientFactory,
            IEnvironment env,
            ISceneHost scene, ILogger logger,
            RpcService rpcService,
            IHandleUserIndex handleUserIndex)
        {
            _esClientFactory = eSClientFactory;
            _userService = userService;
            _peerUserIndex = peerUserIndex;
            _userPeerIndex = userPeerIndex;
            _eventHandlers = eventHandlers;
            _scene = scene;
            _handleUserIndex = handleUserIndex;

            this.serializer = serializer;
            this.env = env;
            this.logger = logger;
            this.rpcService = rpcService;
        }

        public async Task<User?> GetUser(IScenePeerClient peer)
        {
            var session = await GetSession(peer);

            return session?.User;
        }

        public async Task<bool> IsAuthenticated(IScenePeerClient peer)
        {
            return (await GetUser(peer)) != null;
        }

        public async Task<bool> LogOut(IScenePeerClient peer, DisconnectionReason reason)
        {
            var sessionId = peer.SessionId;
            var id = peer.SessionId;

            var result = await _peerUserIndex.TryRemove(sessionId);
            if (result.Success)
            {
                var session = result.Value.CreateView();
                if (result.Value.User != null)
                {
                    await _userPeerIndex.TryRemove(result.Value.User.Id);
                }
                await _userPeerIndex.TryRemove(result.Value.platformId.ToString());
                var logoutContext = new LogoutContext { Session = session, ConnectedOn = session.ConnectedOn, Reason = reason };
                await _eventHandlers().RunEventHandler(h => h.OnLoggedOut(logoutContext), ex => logger.Log(LogLevel.Error, "usersessions", "An error occured while running LoggedOut event handlers", ex));
                if (result.Value.User != null)
                {
                    await _userService.UpdateLastLoginDate(result.Value.User.Id);
                }
                //logger.Trace("usersessions", $"removed '{result.Value.Id}' (peer : '{peer.Id}') in scene '{_scene.Id}'.");
            }

            return result.Success;
        }

       

        public async Task Login(IScenePeerClient peer, User? user, PlatformId onlineId, Dictionary<string, byte[]> sessionData)
        {
            if (peer == null)
            {
                throw new ArgumentNullException("peer");
            }


            bool added = false;

            while (!added && user != null)
            {
                var r = await _userPeerIndex.GetOrAdd(user.Id, peer.SessionId);
                if (r.Value != peer.SessionId)
                {
                    if (!await LogOut(peer, DisconnectionReason.NewConnection))
                    {
                        logger.Warn("usersessions", $"user {user.Id} was found in _userPeerIndex but could not be logged out properly.", new { userId = user.Id, oldSessionId = r.Value, newSessionId = peer.SessionId });

                        await _userPeerIndex.TryRemove(user.Id);
                        await _userPeerIndex.TryRemove(onlineId.ToString().ToString());
                    }
                }
                else
                {
                    added = true;
                }
            }

            await _userPeerIndex.TryAdd(onlineId.ToString(), peer.SessionId);
            var session = new SessionRecord
            {
                User = user,
                platformId = onlineId,
                SessionData = new ConcurrentDictionary<string, byte[]>(sessionData.AsEnumerable()),
                SessionId = peer.SessionId,
                ConnectedOn = DateTime.UtcNow,
                AuthenticatorUrl = await GetAuthenticatorUrl()
            };

            await _peerUserIndex.AddOrUpdateWithRetries(peer.SessionId, session, s => { if (user != null) s.User = user; return s; });
            var loginContext = new LoginContext { Session = session, Client = peer };
            await _eventHandlers().RunEventHandler(h => h.OnLoggedIn(loginContext), ex => logger.Log(LogLevel.Error, "usersessions", "An error occured while running LoggedIn event handlers", ex));


        }

        private async Task<string> GetAuthenticatorUrl()
        {
            var infos = await env.GetApplicationInfos();
            // Support older grid versions where these values are null
            if (string.IsNullOrEmpty(infos.HostUrl) || string.IsNullOrEmpty(infos.ClusterId))
            {
                return _scene.Id;
            }
            return $"scene:/{infos.ClusterId}/{infos.HostUrl}/{_scene.Id}#{_scene.ShardId}";
        }

        internal Task UpdateSession(string id, Func<SessionRecord, Task<SessionRecord>> mutator)
        {
            return _peerUserIndex.UpdateWithRetries(id, mutator);
        }

        public async Task UpdateUserData<T>(IScenePeerClient peer, T data)
        {
            var user = await GetUser(peer);
            if (user == null)
            {
                throw new InvalidOperationException("User not found.");
            }
            else
            {
                user.UserData = Newtonsoft.Json.Linq.JObject.FromObject(data!);
                await _userService.UpdateUserData(user.Id, data);
            }
        }

        public async Task<IScenePeerClient?> GetPeer(string userId)
        {
            var result = await _userPeerIndex.TryGet(userId);

            if (result.Success)
            {
                var peer = _scene.RemotePeers.FirstOrDefault(p => p.SessionId == result.Value);
                //logger.Trace("usersessions", $"found '{userId}' (peer : '{result.Value}', '{peer.Id}') in scene '{_scene.Id}'.");
                if (peer == null)
                {
                    //logger.Trace("usersessions", $"didn't found '{userId}' (peer : '{result.Value}') in scene '{_scene.Id}'.");
                }
                return peer;
            }
            else
            {
                //logger.Trace("usersessions", $"didn't found '{userId}' in userpeer index.");
                return null;
            }
        }
        public async Task<Session?> GetSession(string userId, bool forceRefresh = false)
        {
            var result = await _userPeerIndex.TryGet(userId);

            if (result.Success)
            {
                return await GetSessionById(result.Value);
            }
            else
            {
                return null;
            }
        }

        public async Task<PlatformId> GetPlatformId(string userId)
        {
            var session = await GetSession(userId);

            if (session != null)
            {
                return session.platformId;
            }

            return PlatformId.Unknown;
        }

        public Task<Session?> GetSession(PlatformId id, bool forceRefresh = false)
        {
            return GetSession(id.ToString());
        }

        public async Task<Session?> GetSession(IScenePeerClient peer, bool forceRefresh = false)
        {
            return peer != null ? await GetSessionById(peer.SessionId) : null;
        }


        public async Task<SessionRecord?> GetSessionRecordById(string sessionId)
        {
            var result = await _peerUserIndex.TryGet(sessionId);
            if (result.Success)
            {
                return result.Value;
            }
            else
            {

                //logger.Log(LogLevel.Trace, "usersession", $"Get session failed for id {sessionId}, {_peerUserIndex.Count} sessions found", new { });
                return null;
            }
        }

        public async Task<Session?> GetSessionById(string sessionId, bool forceRefresh = false)
        {
            var session = await GetSessionRecordById(sessionId);
            return session?.CreateView();
        }

        public Task<Session?> GetSessionByUserId(string userId, bool forceRefresh = false)
        {
            return GetSession(userId);
        }

        public Task<Session?> GetSessionById(string sessionId, string authType, bool forceRefresh = false)
        {
            return GetSessionById(sessionId);
        }

        public async Task UpdateSessionData(string sessionId, string key, byte[] data)
        {
            var session = await GetSessionRecordById(sessionId);
            if (session == null)
            {
                throw new ClientException("session.notFound");
            }
            session.SessionData[key] = data;
        }

        public Task UpdateSessionData<T>(string sessionId, string key, T data)
        {
            var stream = new MemoryStream();
            serializer.Serialize(data, stream);
            return UpdateSessionData(sessionId, key, stream.ToArray());
        }

        public async Task<byte[]?> GetSessionData(string sessionId, string key)
        {
            var session = await GetSessionById(sessionId);
            if (session == null)
            {
                throw new ClientException("NotFound");
            }
            if (session.SessionData.TryGetValue(key, out var value))
            {
                return value;
            }
            else
            {
                return null;
            }
        }

        public async Task<T?> GetSessionData<T>(string sessionId, string key)
        {
            var data = await GetSessionData(sessionId, key);
            if (data != null)
            {
                using (var stream = new MemoryStream(data))
                {
                    return serializer.Deserialize<T>(stream);
                }
            }
            else
            {
                return default(T);
            }
        }

        //public async Task<string> GetBearerToken(string sessionId)
        //{

        //    var session = await GetSessionById(sessionId);
        //    return await GetBearerToken(session);
        //}

        //public async Task<string> GetBearerToken(Session session)
        //{
        //    var app = await env.GetApplicationInfos();
        //    return TokenGenerator.CreateToken(new BearerTokenData { SessionId = session.SessionId, pid = session.platformId, userId = session.User.Id, IssuedOn = DateTime.UtcNow, ValidUntil = DateTime.UtcNow + TimeSpan.FromHours(1) }, app.PrimaryKey);
        //}

        //public async Task<BearerTokenData> DecodeBearerToken(string token)
        //{
        //    var app = await env.GetApplicationInfos();
        //    return TokenGenerator.DecodeToken<BearerTokenData>(token, app.PrimaryKey);
        //}

        //public async Task<Session> GetSessionByBearerToken(string token, bool forceRefresh = false)
        //{
        //    var data = await DecodeBearerToken(token);
        //    return await GetSessionById(data.SessionId);
        //}

        public Task<Dictionary<string, User?>> GetUsers(params string[] userIds)
        {
            return _userService.GetUsers(userIds);
        }

        public Task<IEnumerable<User>> Query(IEnumerable<KeyValuePair<string, string>> query, int take, int skip)
        {
            return _userService.Query(query, take, skip);
        }

        private static int _randomTracker = 0;

        private static ThreadLocal<Random> _random = new ThreadLocal<Random>(() =>
        {
            var seed = (int)(Environment.TickCount & 0xFFFFFF00 | (byte)(Interlocked.Increment(ref _randomTracker) % 255));
            var random = new Random(seed);
            return random;
        });

        private static bool _handleUserMappingCreated = false;
        private static AsyncLock _mappingLock = new AsyncLock();

      
        private int _handleSuffixUpperBound = 10000;
        private int _handleMaxNumCharacters = 32;

        private async Task EnsureHandleUserMappingCreated()
        {
            if (!_handleUserMappingCreated)
            {
                using (await _mappingLock.LockAsync())
                {
                    if (!_handleUserMappingCreated)
                    {
                        _handleUserMappingCreated = true;
                        await _esClientFactory.EnsureMappingCreated<HandleUserRelation>("handleUserMapping", m => m
                            .Properties(pd => pd
                                .Keyword(kpd => kpd.Name(record => record.Id).Index())
                                .Keyword(kpd => kpd.Name(record => record.HandleWithoutNum).Index())
                                .Number(npd => npd.Name(record => record.HandleNum).Type(Nest.NumberType.Integer).Index())
                                .Keyword(kpd => kpd.Name(record => record.UserId).Index(false))
                                ));
                    }
                }
            }
        }

        public async Task<string> UpdateUserHandle(string userId, string newHandle, bool appendHash)
        {
            // Check handle validity
            if (!Regex.IsMatch(newHandle, @"^[\p{Ll}\p{Lu}\p{Lt}\p{Lo}0-9-_.]*$"))
            {
                throw new ClientException("badHandle?badCharacters");
            }
            if (newHandle.Length > _handleMaxNumCharacters)
            {
                throw new ClientException($"badHandle?tooLong&maxLength={_handleMaxNumCharacters}");
            }

            var ctx = new UpdateUserHandleCtx(userId, newHandle);
            await _eventHandlers().RunEventHandler(handler => handler.OnUpdatingUserHandle(ctx), ex => logger.Log(LogLevel.Error, "usersessions", "An exception was thrown by an OnUpdatingUserHandle event handler", ex));

            if (!ctx.Accept)
            {
                throw new ClientException(ctx.ErrorMessage);
            }

            var session = await GetSessionByUserId(userId);

            async Task UpdateHandleDatabase()
            {
                await EnsureHandleUserMappingCreated();
                var client = await _esClientFactory.CreateClient<HandleUserRelation>("handleUserRelationClient");
                var user = await _userService.GetUser(userId);
                if (user == null)
                {
                    throw new ClientException("notFound?user");
                }
                var newUserData = user.UserData;

                bool foundUnusedHandle = false;
                string newHandleWithSuffix;
                if (appendHash)
                {
                    do
                    {
                        var suffix = _random.Value.Next(0, _handleSuffixUpperBound);
                        newHandleWithSuffix = newHandle + "#" + suffix;

                        // Check conflicts
                        var relation = new HandleUserRelation { Id = newHandleWithSuffix, HandleNum = suffix, HandleWithoutNum = newHandle, UserId = userId };
                        var response = await client.IndexAsync(relation, d => d.OpType(Elasticsearch.Net.OpType.Create));
                        foundUnusedHandle = response.IsValid;

                    } while (!foundUnusedHandle);
                    newUserData[UsersConstants.UserHandleKey] = newHandleWithSuffix;
                }
                else
                {
                    newUserData[UsersConstants.UserHandleKey] = newHandle;
                }
                if (session != null)
                {
                    session.User.UserData = newUserData;
                }
                await _userService.UpdateUserData(userId, newUserData);
            }

            async Task UpdateHandleEphemeral()
            {
                var userData = session.User.UserData;
                if (!appendHash)
                {
                    userData[UsersConstants.UserHandleKey] = newHandle;
                }
                else
                {
                    string newHandleWithSuffix;
                    bool added = false;
                    do
                    {
                        var suffix = _random.Value.Next(0, _handleSuffixUpperBound);
                        newHandleWithSuffix = newHandle + "#" + suffix;
                        // Check conflicts
                        added = await _handleUserIndex.TryAdd(newHandleWithSuffix, userId);
                    } while (!added);

                    userData[UsersConstants.UserHandleKey] = newHandleWithSuffix;
                }
                session.User.UserData = userData;
            }

            if (session != null &&
                session.User.UserData.TryGetValue(EphemeralAuthenticationProvider.IsEphemeralKey, out var isEphemeral) && (bool)isEphemeral)
            {
                await UpdateHandleEphemeral();
            }
            else
            {
                await UpdateHandleDatabase();
            }

            return newHandle;
        }

        private class PeerRequest : IS2SRequest
        {
            
            public Pipe InputPipe = new Pipe();
            public Pipe OutputPipe = new Pipe();

            public PipeReader Reader =>  OutputPipe.Reader;

            public PipeWriter Writer => InputPipe.Writer;

            public void Dispose()
            {
                
            }
        }
        public IS2SRequest SendRequest(string operationName, string senderUserId, string recipientUserId, CancellationToken cancellationToken)
        {
            var rq = new PeerRequest();

            async Task SendRequestImpl()
            {
                try
                {
                    using var outputStream = rq.OutputPipe.Writer.AsStream();
                    var peer = await GetPeer(recipientUserId);
                    if (peer == null)
                    {
                        throw new ClientException("NotConnected");

                    }

                   
                    var rpc = peer.Rpc("sendRequest", s =>
                    {
                        try
                        {
                            peer.Serializer().Serialize(senderUserId, s);
                            peer.Serializer().Serialize(operationName, s);
                            var stream = rq.InputPipe.Reader.AsStream();
                            stream.CopyTo(s);

                        }
                        finally
                        {
                            rq.InputPipe.Reader.Complete();
                        }

                    }).ToAsyncEnumerable().WithCancellation(cancellationToken);
                    
                    await foreach (var packet in rpc)
                    {
                        using (packet)
                        {
                            packet.Stream.CopyTo(outputStream);
                        }
                    }
                }
                catch (Exception ex)
                {
                    rq.InputPipe.Reader.Complete(ex);
                    rq.OutputPipe.Writer.Complete(ex);
                }
            }
            _ = SendRequestImpl();


            return rq;
        }

        public async Task<Dictionary<PlatformId, Session?>> GetSessions(IEnumerable<PlatformId> platformIds, bool forceRefresh = false)
        {
            Dictionary<PlatformId, Session?> sessions = new Dictionary<PlatformId, Session?>();

            foreach (var id in platformIds)
            {
                var session = await GetSession(id, forceRefresh);
                if (session != null)
                {
                    sessions.TryAdd(id, session);
                }
            }

            return sessions;
        }

        public Task<int> GetAuthenticatedUsersCount()
        {
            return Task.FromResult(AuthenticatedUsersCount);
        }

        public async Task<Dictionary<string, Session?>> GetSessions(IEnumerable<string> sessionIds, bool forceRefresh = false)
        {

            var sessions = new Dictionary<string, Session?>();

            foreach (var id in sessionIds)
            {
                var session = await GetSession(id, forceRefresh);
                if (session != null)
                {
                    sessions.TryAdd(id, session);
                }
            }

            return sessions;

        }

        public async Task KickUser(string userId,string reason)
        {
            var peer = await GetPeer(userId);
            if(peer!=null)
            {
                await peer.Disconnect(reason);
            }
        }

        public int AuthenticatedUsersCount
        {
            get
            {
                return _peerUserIndex.Count;
            }
        }
    }

    /// <summary>
    /// Record object representing an association between handle &amp; user.
    /// </summary>
    public class HandleUserRelation
    {
        /// <summary>
        /// Indexed by user's handle
        /// </summary>       
        public string Id { get; set; } = default!;

        /// <summary>
        /// Gets or sets the user handle without numbered suffix.
        /// </summary>
        public string HandleWithoutNum { get; set; } = default!;

        /// <summary>
        /// Gets or sets the numbered suffix.
        /// </summary>
        public int HandleNum { get; set; }

        /// <summary>
        /// Gets or sets the user Id.
        /// </summary>
        public string UserId { get; set; } = default!;
    }
}
