/*
 * Imlight
 * Copyright (C) 2025 Revive101
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with this program. If not, see <http://www.gnu.org/licenses/>.
 *
 * ========================================================================
 * PVP
 * ========================================================================
 * 
 * PURPOSE:
 * One practice match a host created and listed, until a second player joins it or the host cancels it.
 * 
 * USAGE EXAMPLE:
 * var hosted = new PvPHostedMatch(id, hostEntry, profile, template, request);
 * 
 * NOTE:
 * The listed info is built once from the request of the host. Team one holds only the host, whatever the client
 * sent, and the teams the template wants but the request lacked are added empty.
 * 
 * TODO:
 * 
 * 
 * Created by: Jay
 * Version: KALI 1.0
 * Last Updated: 10/18/2026
 */

using System.Collections.Generic;
using System.Linq;
using Imcodec.Cryptography;
using Imcodec.ObjectProperty.TypeCache;
using Imlight.CoreLib.Game.PvP.Arena;

namespace Imlight.CoreLib.Game.PvP.Matchmaking;

internal sealed class PvPHostedMatch {

    // Status 4 means in progress to the client, which then shows the match on the watch tab only.
    private const int OPEN_STATUS = 0;

    // The client keys teams by id across all matches, so ids must not repeat between matches.
    private const ulong TEAM_ID_STRIDE = 10;

    public ulong Id { get; }
    public PvPQueueEntry Host { get; }
    public PvPHostedState State { get; set; } = PvPHostedState.Open;
    public PvPMatchInfo Info { get; }

    // The level a joiner may not exceed; zero means no limit.
    public int MaxActorLevel { get; }

    public bool IsFriendsOnly => Info.m_friendsOnly;

    public PvPHostedMatch(ulong id, PvPQueueEntry host, PvPPlayerProfile profile, MatchTemplate template, PvPMatchRequest request) {
        Id = id;
        Host = host;
        Info = BuildInfo(request, profile, template);
        MaxActorLevel = Info.m_teams.Select(team => team.m_maxActorLevel).FirstOrDefault(level => level > 0);
    }

    private PvPMatchInfo BuildInfo(PvPMatchRequest request, PvPPlayerProfile profile, MatchTemplate template) {
        var teamCount = (int) template.m_teamCount;
        var matchNameId = StringHash.Compute(template.m_matchName);
        var teams = new List<MatchTeam>(request.m_teams ?? []);
        while (teams.Count < teamCount) {
            teams.Add(new MatchTeam { m_maxActorLevel = teams.Count > 0 ? teams[0].m_maxActorLevel : 0 });
        }

        for (var i = 0; i < teams.Count; i++) {
            teams[i].m_nTeamID = Id * TEAM_ID_STRIDE + (ulong) i + 1;
            teams[i].m_matchId = Id;
            teams[i].m_matchNameID = matchNameId;
            teams[i].m_creatorId = Host.CharId;
            teams[i].m_leagueID = Host.LeagueId;
            teams[i].m_seasonID = Host.SeasonId;
            teams[i].m_actors = [i == 0 ? PvPMatchActorBuilder.BuildListed(Host, profile, Id, teams[i].m_nTeamID) : PvPMatchActorBuilder.BuildOpenSlot(Host, Id, teams[i].m_nTeamID)];
        }

        return new PvPMatchInfo {
            m_matchID = Id,
            m_matchNameID = matchNameId,
            m_matchName = template.m_matchName,
            m_teams = teams,
            m_creatorID = Host.CharId,
            m_tournamentNameID = Host.TournamentNameId,
            m_leagueID = Host.LeagueId,
            m_seasonID = Host.SeasonId,
            m_friendsOnly = teams[0].m_friendsOnly,
            m_status = OPEN_STATUS,
            m_teamSize = template.m_teamSize,
            m_joinQueueRequirements = request.m_joinQueueRequirements,
            m_matchTimer = request.m_matchTimer,
            m_bonusTime = request.m_bonusTime,
            m_passPenalty = request.m_passPenalty,
            m_yellowTime = request.m_yellowTime,
            m_redTime = request.m_redTime,
            m_minTurnTime = request.m_minTurnTime,
            m_effects = request.m_effects ?? []
        };
    }

}
