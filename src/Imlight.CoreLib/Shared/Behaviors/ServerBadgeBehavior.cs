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
 * BADGE STATE
 * ========================================================================
 *
 * PURPOSE:
 * Persisted per-character badge state: kill counters, earned badges, the displayed badge and the lists of the client's BadgeBehavior blob.
 *
 * USAGE EXAMPLE:
 * wizard.BadgeBehavior.Counters[counterKey] = 3;
 *
 * NOTE:
 * Earned is concurrent because purchase gates and requirement handlers on other actors read it while BadgeService writes it.
 *
 * TODO:
 *
 * Created by: Jay
 * Version: KALI 1.0
 * Last Updated: 10/14/2026
 */

using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Imlight.CoreLib.Shared.Behaviors;

public class ServerBadgeBehavior {

    public Dictionary<string, ulong> Counters { get; set; } = [];
    public ConcurrentDictionary<uint, EarnedBadge> Earned { get; set; } = [];
    public uint SelectedBadgeId { get; set; }
    public uint NextAwardSequence { get; set; }
    public List<NewBadgeEntry> NewBadges { get; set; } = [];
    public List<uint> ClaimedRewards { get; set; } = [];
    public List<uint> PendingOvercount { get; set; } = [];

}
