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

using MsgPack.Serialization;
using Newtonsoft.Json.Linq;
using Stormancer.Server.Plugins.Users;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace Stormancer.Server.Plugins.GameSession.ServerPool
{
    public interface IServerPoolProvider
    {
        bool TryCreate(string id, JObject config,[NotNullWhen(true)] out IServerPool? pool);
    }

    public class Server : IDisposable
    {
        public string Id { get; internal set; }
        public GameServerInstance GameServer { get; internal set; }
        public DateTime CreatedOn { get; internal set; }
        public IScenePeerClient Peer { get; internal set; }
      
        public object? Context { get; internal set; }
        public GameSessionConfiguration GameSessionConfiguration { get; internal set; }
        public TaskCompletionSource<WaitGameServerResult> RequestCompletedCompletionSource { get; internal set; }
       
        /// <summary>
        /// Gets or sets the region the game server was created in.
        /// </summary>
        public string? Region { get; set; }
        public void Dispose()
        {
        }
    }

    /// <summary>
    /// A game server in the pool that should join a gamesession.
    /// </summary>
    public class GameServer
    {
        /// <summary>
        /// Gets or sets the session id of the gameServer
        /// </summary>
        [MessagePackMember(0)]
        public SessionId GameServerSessionId { get; set; }

        /// <summary>
        /// Gets or sets the Id of the gameserver
        /// </summary>
        [MessagePackMember(1)]
        public GameServerId GameServerId { get; set; } = default!;

        /// <summary>
        /// Gets or sets the region of hte game server.
        /// </summary>
        [MessagePackMember(2)]
        public string? Region { get; set; }
    }

    public class WaitGameServerResult
    {
        [MemberNotNullWhen(true,"Value")]
        public bool Success { get; set; }

      
        public GameServer? Value { get; set; }
    }

    /// <summary>
    /// Id of gameservers in the system.
    /// </summary>
    public class GameServerId
    {
        /// <summary>
        /// Id of the pool containing the gameserver
        /// </summary>
        [MessagePackMember(0)]
        public string PoolId { get; set; } = default!;

        /// <summary>
        /// Id of the gameserver in the pool
        /// </summary>
        [MessagePackMember(1)]
        public string Id { get; set; } = default!;
    }
    
    /// <summary>
    /// A pool that manages game servers.
    /// </summary>
    public interface IServerPool: IDisposable
    {
        /// <summary>
        /// Gets the id of the pool.
        /// </summary>
        string Id { get; }

        /// <summary>
        /// Waits for a server to be available, then re
        /// </summary>
        /// <param name="gameSessionId"></param>
        /// <param name="gameSessionConfig"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task<WaitGameServerResult> TryWaitGameServerAsync(string gameSessionId, GameSessionConfiguration gameSessionConfig, CancellationToken cancellationToken);

        void UpdateConfiguration(JObject config);

        int ServersReady { get; }
        int ServersStarting { get; }
        int ServersRunning { get; }
        int TotalServersInPool { get; }
        int PendingServerRequests { get; }
        bool CanAcceptRequest { get; }
        int MaxServersInPool { get; }
        int MinServerReady { get; }

        bool CanManage(Session session, IScenePeerClient peer);
        Task<GameServerStartupParameters?> WaitGameSessionAsync(Session session, IScenePeerClient client, CancellationToken cancellationToken);
        Task OnGameServerDisconnected(string serverId);
        Task CloseServer(string serverId);
        IAsyncEnumerable<string> QueryLogsAsync(string gameSessionId, DateTime? since, DateTime? until, uint size, bool follow, CancellationToken cancellationToken);
        
        /// <summary>
        /// Extends the lifetime of a game server, if supported.
        /// </summary>
        /// <param name="gameSessionId"></param>
        /// <returns></returns>
        Task<bool> KeepServerAlive(string gameSessionId);
    }
}
