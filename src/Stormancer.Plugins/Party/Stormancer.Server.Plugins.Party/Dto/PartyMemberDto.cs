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

using MsgPack.Serialization;
using Stormancer.Server.Plugins.GameSession.Admin;
using Stormancer.Server.Plugins.Party.Model;
using System;
using System.Collections.Generic;

namespace Stormancer.Server.Plugins.Party.Dto
{
    /// <summary>
    /// Informations about a party member.
    /// </summary>
    public class PartyMemberDto
    {
        /// <summary>
        /// Gets or sets the Party member's user id.
        /// </summary>
        [MessagePackMember(0)]
        public string UserId { get; set; } = default!;

        [MessagePackMember(1)]
        public PartyMemberStatus PartyUserStatus { get; set; }

        /// <summary>
        /// Gets or sets the party member user data.
        /// </summary>
        [MessagePackMember(2)]
        public byte[] UserData { get; set; }

        /// <summary>
        /// Gets or sets the Party member's session id.
        /// </summary>
        [MessagePackMember(3)]
        public SessionId SessionId { get; set; }

        /// <summary>
        /// 
        /// </summary>
        [MessagePackMember(4)]
        public List<Models.LocalPlayerInfos> LocalPlayers { get; set; } = default!;

        /// <summary>
        /// Represents the connection status of the member.
        /// </summary>
        [MessagePackMember(5)]
        public PartyMemberConnectionStatus ConnectionStatus { get; set; }
    }

    
}
