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
 * BADGE ENGINE
 * ========================================================================
 *
 * PURPOSE:
 * Changes a character's stored badge state (counters, awards, auto-add rechecks) and reports which badges the client has to be told about.
 *
 * USAGE EXAMPLE:
 * var changes = new BadgeEngine(wizard, catalog).Execute(engine => engine.Increment(counterKey, 1));
 *
 * NOTE:
 * Pure logic over the Wizard and the catalog: it persists nothing, sends nothing and runs no results. BadgeService does that with the returned Changes. Counters stop at the highest tier of their family.
 *
 * TODO:
 *
 * Created by: Jay
 * Version: KALI 1.0
 * Last Updated: 10/14/2026
 */

using System;
using System.Collections.Generic;
using System.Linq;
using Imlight.CoreLib.WizardData.Models.Player;
using Imlight.CoreLib.WizardData.Models.World;
using Imlight.CoreLib.Shared.Behaviors;

namespace Imlight.CoreLib.Game.Badges;

internal sealed class BadgeEngine(Wizard wizard, BadgeCatalog catalog) {

    private readonly ServerBadgeBehavior _state = wizard.BadgeBehavior;
    private bool _stateChanged;

    /// <summary>
    /// What one engine run changed, in the order the client is told: the entries that moved first, then the ones that completed.
    /// </summary>
    /// <param name="Batches">The incremental MSG_BADGES payloads, each sorted by Index; empty batches are left out.</param>
    /// <param name="Completed">The badges that became complete in this run.</param>
    /// <param name="StateChanged">Whether the stored state changed and has to be persisted.</param>
    internal sealed record Changes(IReadOnlyList<IReadOnlyList<BadgeDefinition>> Batches, IReadOnlyList<BadgeDefinition> Completed, bool StateChanged) {

        internal static Changes None { get; } = new([], [], false);

    }

    /// <summary>
    /// Runs the operations and reports what changed since before they ran.
    /// </summary>
    internal Changes Execute(Action<BadgeEngine> operations) {
        _stateChanged = false;
        var before = Snapshot();
        operations(this);
        var after = Snapshot();

        var completed = new List<BadgeDefinition>();
        var moved = new List<BadgeDefinition>();
        foreach (var (id, entry) in after) {
            var known = before.TryGetValue(id, out var previous);
            if (entry.Complete && !(known && previous.Complete)) {
                completed.Add(entry.Definition);
            } else if (!known || previous.Progress != entry.Progress) {
                moved.Add(entry.Definition);
            }
        }

        var batches = new List<IReadOnlyList<BadgeDefinition>>();
        foreach (var batch in new[] { moved, completed }) {
            if (batch.Count > 0) {
                batches.Add([.. batch.OrderBy(d => d.Index).ThenBy(d => d.Id)]);
            }
        }

        return new Changes(batches, [.. completed.OrderBy(d => d.Index).ThenBy(d => d.Id)], _stateChanged);
    }

    internal void Increment(string counterKey, ulong amount) {
        var family = catalog.GetFamily(counterKey);
        if (family.Count == 0 || amount == 0) {
            return;
        }

        var top = (ulong) family[^1].Max;
        var current = _state.Counters.GetValueOrDefault(counterKey);
        var updated = Math.Min(current + amount, top);
        if (updated != current) {
            _state.Counters[counterKey] = updated;
            _stateChanged = true;
        }

        foreach (var tier in family.Where(t => updated >= (ulong) t.Max)) {
            Complete(tier);
        }
    }

    internal void Award(uint badgeId) {
        if (catalog.TryGet(badgeId, out var definition)) {
            Complete(definition);
        }
    }

    /// <summary>
    /// Awards every auto-add badge whose prerequisites and requirements hold, until no further badge qualifies.
    /// </summary>
    /// <param name="meetsRequirements">Evaluates the requirement list of a record for this character.</param>
    internal void Recheck(Func<BadgeDefinition, bool> meetsRequirements) {
        bool awarded;
        do {
            awarded = false;
            foreach (var definition in catalog.AutoAddRecords) {
                if (BadgeView.IsComplete(_state, definition) || definition.Retired) {
                    continue;
                }

                if (definition.RequiredAutoAddBadges.Any(id => !_state.Earned.ContainsKey(id))) {
                    continue;
                }

                if (meetsRequirements(definition)) {
                    Complete(definition);
                    awarded = true;
                }
            }
        } while (awarded);
    }

    private void Complete(BadgeDefinition definition) {
        if (definition.Retired || BadgeView.IsComplete(_state, definition)) {
            return;
        }

        var sequence = Math.Max(1u, _state.NextAwardSequence);
        _state.NextAwardSequence = sequence + 1;
        _state.Earned[definition.Id] = new EarnedBadge {
            AwardSequence = sequence,
            CompletedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
        _stateChanged = true;
    }

    private Dictionary<uint, (BadgeDefinition Definition, int Progress, bool Complete)> Snapshot()
        => BadgeView.GetHeld(_state, catalog).ToDictionary(
            d => d.Id,
            d => (d, BadgeView.GetProgress(_state, d), BadgeView.IsComplete(_state, d)));

}
