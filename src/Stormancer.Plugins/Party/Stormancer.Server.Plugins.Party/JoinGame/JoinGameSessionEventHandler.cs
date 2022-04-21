﻿using Stormancer.Server.Plugins.GameSession;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stormancer.Server.Plugins.Party.JoinGame
{
    /// <summary>
    /// Contains the state related to the Join current gamesession functionality.
    /// </summary>
    internal class JoinGameSessionState
    {
        public Dictionary<string,string> UserIdToPartyId = new Dictionary<string,string>();
        internal object syncRoot = new object();
    }
    internal class JoinGameSessionEventHandler : IGameSessionEventHandler
    {
        private readonly PartyProxy party;
        private readonly IGameSessionService gameSession;
        private readonly JoinGameSessionState state;

        public JoinGameSessionEventHandler(PartyProxy party, IGameSessionService gameSession, JoinGameSessionState state)
        {
            this.party = party;
            this.gameSession = gameSession;
            this.state = state;
        }
       
        public async Task OnClientConnected(ClientConnectedContext ctx)
        {
            string? partyId = null;
            lock (state.syncRoot)
            {
                var party = ctx.GameSession.GetGameSessionConfig().Teams.SelectMany(t => t.Parties).FirstOrDefault(p => p.Players.ContainsKey(ctx.Player.Player.UserId));

                if (party != null)
                {
                    if(!state.UserIdToPartyId.Values.Contains(party.PartyId))
                    {
                        partyId = party.PartyId;
                    }
                    state.UserIdToPartyId[ctx.Player.Player.UserId] = party.PartyId;
                    
                    partyId = party.PartyId;
                }
            }

            if(partyId !=null)
            {
                await party.UpdatePartyStatusAsync(partyId, null, "gamesession", ctx.GameSession.GameSessionId, default);
            }

        }


     
        public async Task OnClientLeaving(ClientLeavingContext ctx)
        {
            string? partyId = null;
            lock (state.syncRoot)
            {
                var party = ctx.GameSession.GetGameSessionConfig().Teams.SelectMany(t => t.Parties).FirstOrDefault(p => p.Players.ContainsKey(ctx.Player.Player.UserId));
                if (party != null)
                {
                    
                    state.UserIdToPartyId.Remove(ctx.Player.Player.UserId);
                    if (!state.UserIdToPartyId.Values.Contains(party.PartyId))
                    {
                        partyId = party.PartyId;
                    }
                }
            }

            if(partyId !=null)
            {
                await party.UpdatePartyStatusAsync(partyId, "gamesession","", null, default);
            }
        }


    }
}
