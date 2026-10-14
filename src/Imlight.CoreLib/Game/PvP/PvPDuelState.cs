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
 * The PvP side of a duel: which match it is, who plays it, and whose half of the round it is.
 * 
 * USAGE EXAMPLE:
 * var state = new PvPDuelState(matchId, roster, rules, CombatTeam.Player, swapFirstTeamEachRound: true);
 * 
 * NOTE:
 * A round of an alternating duel is two half turns: the team that starts acts first, then the other team. The
 * starting team swaps every round when the setting asks for it.
 * 
 * TODO:
 * Is the swap of the starting team every round what live does? No client code reads the initiative switch fields.
 * 
 * Created by: Jay
 * Version: KALI 1.0
 * Last Updated: 10/14/2026
 */

using System.Collections.Generic;
using Akka.Actor;
using Imlight.CoreLib.Game.Combat;

namespace Imlight.CoreLib.Game.PvP;

internal sealed class PvPDuelState(
    ulong matchId,
    uint tournamentNameId,
    IReadOnlyList<PvPRosterEntry> roster,
    PvPDuelRules rules,
    CombatTeam firstTeam,
    bool swapFirstTeamEachRound) {

    private readonly HashSet<ulong> _forfeitedCharIds = [];
    private readonly HashSet<IActorRef> _disconnectedSessions = [];

    public ulong MatchId { get; } = matchId;

    public uint TournamentNameId { get; } = tournamentNameId;

    public IReadOnlyList<PvPRosterEntry> Roster { get; } = roster;

    public PvPDuelRules Rules { get; } = rules;

    public CombatTeam OriginalFirstTeam { get; } = firstTeam;

    // The team that acts first in the current round.
    public CombatTeam RoundStarter { get; private set; } = firstTeam;

    // 1 for the first half turn of a round, 2 for the second, 0 before the first round.
    public int HalfTurn { get; private set; }

    // The team that plans now, or null when both teams plan at once.
    public CombatTeam? ActingTeam
        => !Rules.AlternateTurns || HalfTurn == 0 ? null : HalfTurn == 2 ? Opposite(RoundStarter) : RoundStarter;

    public void BeginRound(int roundNumber) {
        if (roundNumber > 1 && swapFirstTeamEachRound) {
            RoundStarter = Opposite(RoundStarter);
        }

        HalfTurn = 1;
    }

    /// <summary>
    /// Moves a round that alternates on to the half turn of the other team.
    /// </summary>
    /// <returns><c>true</c> when the round has a second half turn to play.</returns>
    public bool TryBeginSecondHalfTurn() {
        if (!Rules.AlternateTurns || HalfTurn != 1) {
            return false;
        }

        HalfTurn = 2;

        return true;
    }

    public IReadOnlyCollection<ulong> ForfeitedCharIds => _forfeitedCharIds;

    public bool HasForfeited(ulong charId)
        => _forfeitedCharIds.Contains(charId);

    public void MarkForfeited(ulong charId)
        => _forfeitedCharIds.Add(charId);

    public bool IsDisconnected(IActorRef session)
        => _disconnectedSessions.Contains(session);

    public void MarkDisconnected(IActorRef session)
        => _disconnectedSessions.Add(session);

    public static CombatTeam Opposite(CombatTeam team)
        => team == CombatTeam.Player ? CombatTeam.Monster : CombatTeam.Player;

}
