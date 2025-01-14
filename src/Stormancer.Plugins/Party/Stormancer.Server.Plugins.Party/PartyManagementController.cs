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
using Newtonsoft.Json.Linq;
using Stormancer.Diagnostics;
using Stormancer.Plugins;
using Stormancer.Server.Plugins.API;
using Stormancer.Server.Plugins.Party;
using Stormancer.Server.Plugins.Queries;
using Stormancer.Server.Plugins.Users;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Stormancer.Server.PartyManagement
{
    class PartyManagementController : ControllerBase
    {
        private readonly IPartyManagementService _partyService;
        private readonly IUserSessions _sessions;
        private readonly ILogger _logger;
        private readonly IEnumerable<IPartyEventHandler> _handlers;
        private readonly PartyConfigurationService configuration;
        private readonly PartySearchService search;

        public PartyManagementController(
            IPartyManagementService partyService,
            IUserSessions sessions,
            ILogger logger,
            IEnumerable<IPartyEventHandler> handlers,
            PartyConfigurationService configuration,
            PartySearchService search)
        {
            _partyService = partyService;
            _sessions = sessions;
            _logger = logger;
            _handlers = handlers;
            this.configuration = configuration;
            this.search = search;
        }

        public async Task CreateSession(RequestContext<IScenePeerClient> ctx)
        {
            var partyArgs = ctx.ReadObject<PartyRequestDto>();
            if (string.IsNullOrEmpty(partyArgs.GameFinderName))
            {
                throw new ClientException("party.creationFailed?reason=gameFinderNotSet");
            }
            var user = await _sessions.GetUser(ctx.RemotePeer, ctx.CancellationToken);

            if (user == null)
            {
                throw new ClientException("notAuthenticated");
            }
            var eventCtx = new PartyCreationContext(partyArgs);

            configuration.OnPartyCreating(eventCtx);
            await _handlers.RunEventHandler(handler => handler.OnCreatingParty(eventCtx), ex =>
            {
                _logger.Log(LogLevel.Error, "PartyManagementController.CreateSession", "An exception was thrown by an OnCreatingParty event handler", ex);
            });

            if (!eventCtx.Accept)
            {
                _logger.Log(LogLevel.Warn, "PartyManagementController.CreateSession", "Party creation was rejected", new
                {
                    context = eventCtx,
                    userId = user.Id,
                    sessionId = ctx.RemotePeer.SessionId
                }, user.Id, ctx.RemotePeer.SessionId.ToString());

                throw new ClientException(eventCtx.ErrorMessage ?? "Bad request");
            }

            var token = await _partyService.CreateParty(partyArgs, user.Id);

            await ctx.SendValue(token);
        }

        [Api(ApiAccess.Public, ApiType.Rpc)]
        public async Task<string> CreateConnectionTokenFromInvitationCode(string invitationCode, byte[] userData, RequestContext<IScenePeerClient> ctx)
        {
            var session = await _sessions.GetSession(ctx.RemotePeer, ctx.CancellationToken);
            if (session == null)
            {
                throw new ClientException("notAuthenticated");
            }

            var token = await _partyService.CreateConnectionTokenFromInvitationCodeAsync(invitationCode,userData, ctx.CancellationToken);
            if (token == null)
            {
                throw new ClientException("codeNotFound");
            }

            return token;
        }

        [Api(ApiAccess.Public, ApiType.Rpc)]
        public async Task<string?> CreateConnectionTokenFromPartyId(string partyId, byte[] userData, RequestContext<IScenePeerClient> ctx)
        {
            var result = await _partyService.CreateConnectionTokenFromPartyId(partyId, userData, ctx.CancellationToken);
            if(result.Success)
            {
                return result.Value;
            }
            else
            {
                throw new ClientException(result.Error);
            }
        }

        [Api(ApiAccess.Public, ApiType.Rpc)]
        public async Task<PartySearchResultDto> SearchParties(string jsonQuery, uint skip, uint size, CancellationToken cancellationToken)
        {
            var result = await search.SearchParties(JObject.Parse(jsonQuery), skip, size, cancellationToken);

            return new PartySearchResultDto { Total = result.Total, Hits = result.Hits.Select(d => new PartySearchDocumentDto { Id = d.Id, Source = d.Source?.ToString(Newtonsoft.Json.Formatting.None) ?? "{}" }) };
        }
    }

    /// <summary>
    /// A party search document.
    /// </summary>
    public class PartySearchDocumentDto
    {
        /// <summary>
        /// Id of the party.
        /// </summary>
        [MessagePackMember(0)]
        public string Id { get; set; } = default!;

        /// <summary>
        /// Json Data associated with the party.
        /// </summary>
        [MessagePackMember(1)]
        public string Source { get; set; } = default!;
    }

    /// <summary>
    /// A party search result.
    /// </summary>
    public class PartySearchResultDto
    {
        /// <summary>
        /// Total number of documents returned by the search.
        /// </summary>
        [MessagePackMember(0)]
        public uint Total { get; set; }

        /// <summary>
        /// Results in the search result.
        /// </summary>
        [MessagePackMember(1)]
        public IEnumerable<PartySearchDocumentDto> Hits { get; set; } = default!;
    }
}
