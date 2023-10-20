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

using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace Stormancer.Server.Plugins.GameSession
{
    public class GameServerInstance
    {
        public string Id { get; set; }
        public Action OnClosed { get; set; }
    }

    public class StartGameServerResult
    {
        public StartGameServerResult(bool success, GameServerInstance? instance, object? context )
        {
            Success = success;
            Instance = instance;
            Context = context;
        }

        [MemberNotNullWhen(true,"Instance")]
        public bool Success { get; }
        public GameServerInstance? Instance { get; }
        public object? Context { get; set; }

        /// <summary>
        /// Gets or sets the region the game server was created in.
        /// </summary>
        public string? Region { get; set; }
    }
    public interface IGameServerProvider
    {
        string Type { get; }
        Task<StartGameServerResult> TryStartServer(string id,string authToken, JObject config, GameServerEvent record,IEnumerable<string> preferredRegions, CancellationToken ct);

        Task StopServer(string id, object? context);
    }
}
