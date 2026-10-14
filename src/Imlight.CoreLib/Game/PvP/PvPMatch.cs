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
 * One match between queued players, from the moment the matchmaker paired them. Plain state owned by the
 * PvPDirector; only the director thread uses it.
 * 
 * USAGE EXAMPLE:
 * var match = new PvPMatch(matchId, [first, second]);
 * 
 * NOTE:
 * 
 * 
 * TODO:
 * 
 * 
 * Created by: Jay
 * Version: KALI 1.0
 * Last Updated: 10/14/2026
 */

using System.Collections.Generic;
using System.Linq;

namespace Imlight.CoreLib.Game.PvP;

internal sealed class PvPMatch(ulong id, IReadOnlyList<PvPQueueEntry> entries) {

    public ulong Id { get; } = id;

    public IReadOnlyList<PvPQueueEntry> Entries { get; } = entries;

    public IEnumerable<ulong> CharIds => Entries.Select(entry => entry.CharId);

}
