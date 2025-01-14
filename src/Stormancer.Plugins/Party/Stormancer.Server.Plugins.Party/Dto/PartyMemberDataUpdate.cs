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
using Stormancer.Server.Plugins.Party.Model;
using System.Collections.Generic;
using System.Linq;

namespace Stormancer.Server.Plugins.Party.Dto
{
    /// <summary>
    /// DTO for member update.
    /// </summary>
    public class PartyMemberDataUpdate
    {
        /// <summary>
        /// Member updated route.
        /// </summary>
        public const string Route = "party.memberDataUpdated";


        /// <summary>
        /// The user Id.
        /// </summary>
        [MessagePackMember(0)]
        public string UserId { get; set; } = default!;

        /// <summary>
        /// User data
        /// </summary>
        [MessagePackMember(1)]
        public byte[] UserData { get; set; } = default!;

        /// <summary>
        /// Local players associated with the party member.
        /// </summary>
        [MessagePackMember(2)]
        public IEnumerable<Models.LocalPlayerInfos> LocalPlayers { get;  set; } = Enumerable.Empty<Models.LocalPlayerInfos>();

        /// <summary>
        /// Represents the connection status of the member.
        /// </summary>
        [MessagePackMember(3)]
        public PartyMemberConnectionStatus ConnectionStatus { get; set; }
    }
}
