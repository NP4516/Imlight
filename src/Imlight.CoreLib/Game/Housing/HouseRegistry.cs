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
 * HOUSING SYSTEM
 * ========================================================================
 * 
 * PURPOSE:
 * Finds, creates and shares the live state of every house in use. All players in a house get
 * the same HouseState.
 * 
 * USAGE EXAMPLE:
 * var state = HouseRegistry.GetOrCreateDorm(wizard, out var created);
 * var entered = HouseRegistry.TryEnter(wizard.CharId, lotGid, out var house);
 * 
 * NOTE:
 * - Zone loads never create houses; only an owner's Home click does.
 * - A state leaves the registry when its last viewer leaves. It is already persisted by then.
 * 
 * TODO:
 * 
 * Created by: Jay
 * Version: KALI 1.0
 * Last Updated: 10/14/2026
 */

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Imlight.Common;
using Imlight.CoreLib.Shared.Utilities;
using Imlight.CoreLib.WizardData.Collections;
using Imlight.CoreLib.WizardData.Models.Player;

namespace Imlight.CoreLib.Game.Housing;

/// <summary>
/// The houses that are loaded, by lot.
/// </summary>
internal static class HouseRegistry {

    private const int CreationLockCount = 32;

    private static readonly ConcurrentDictionary<ulong, HouseState> s_houses = new();
    private static readonly Lock[] s_creationLocks = [.. Enumerable.Range(0, CreationLockCount).Select(_ => new Lock())];

    /// <summary>
    /// Finds a character's Dorm Room, creating and storing it when the character has none. However many
    /// logins race, one document and one lot exist per character.
    /// </summary>
    /// <param name="owner">The owning character.</param>
    /// <param name="created">Whether this call created the house.</param>
    /// <returns>The live house, or <c>null</c> when it cannot be loaded or stored.</returns>
    internal static HouseState GetOrCreateDorm(Wizard owner, out bool created) {
        created = false;
        if (!DormDeed.TryGetStarter(out var dorm)) {
            return null;
        }

        var lotGid = owner.HouseLots.GetValueOrDefault(dorm.TemplateId);
        var house = lotGid == 0 ? null : HouseCollection.Load(lotGid);
        if (house is null) {
            var layout = HouseLayouts.Resolve(dorm, dorm.LotZone);
            if (layout is null) {
                return null;
            }

            var items = layout
                .Select((item, index) => item with { Id = (uint) index + 1 })
                .ToList();
            lotGid = lotGid == 0 ? (ulong) RandomGen.GenerateGUID() : lotGid;
            var candidate = new House {
                Id = House.GetDocumentId(lotGid),
                LotGid = lotGid,
                OwnerCharId = owner.CharId,
                TemplateId = dorm.TemplateId,
                Revision = 1,
                BonusSlots = 0,
                NextItemId = (uint) items.Count + 1,
                Items = items,
                CreatedUtc = DateTime.UtcNow,
            };
            created = HouseCollection.TryCreate(candidate, owner, out house);
            if (house is null) {
                return null;
            }

            if (created) {
                Logger.Information("Created the dorm {LotGid} of character {CharId}", Logger.Args(house.LotGid, owner.CharId));
            }
        }

        if (house.OwnerCharId != owner.CharId || house.TemplateId != dorm.TemplateId) {
            Logger.Error("House {LotGid} does not belong to the dorm of character {CharId}.", Logger.Args(house.LotGid, owner.CharId));

            return null;
        }

        owner.HouseLots[dorm.TemplateId] = house.LotGid;

        return Publish(house.LotGid, owner.CharId);
    }

    /// <summary>
    /// Finds the house of a character by its lot, loading it from the database after a restart.
    /// </summary>
    /// <param name="ownerCharId">The character who must own the house.</param>
    /// <param name="lotGid">The lot to find.</param>
    /// <param name="state">The live house when this returns <c>true</c>.</param>
    /// <returns><c>false</c> when the character has no house with that lot.</returns>
    internal static bool TryGetOwned(ulong ownerCharId, ulong lotGid, out HouseState state) {
        if (!s_houses.TryGetValue(lotGid, out state)) {
            state = Publish(lotGid, ownerCharId);
        }

        if (state is null || state.OwnerCharId != ownerCharId) {
            state = null;

            return false;
        }

        return true;
    }

    /// <summary>
    /// Finds a character's house by its lot and counts the character in as a viewer.
    /// </summary>
    /// <param name="ownerCharId">The character who must own the house.</param>
    /// <param name="lotGid">The lot to find.</param>
    /// <param name="state">The live house when this returns <c>true</c>; call <see cref="Release"/> when the viewer is gone.</param>
    /// <returns><c>false</c> when the character has no house with that lot.</returns>
    internal static bool TryEnter(ulong ownerCharId, ulong lotGid, out HouseState state) {
        while (TryGetOwned(ownerCharId, lotGid, out state)) {
            if (state.Enter()) {
                return true;
            }

            Evict(state);
        }

        return false;
    }

    /// <summary>
    /// Counts a viewer out of a house and drops the house from the registry when nobody is left.
    /// </summary>
    /// <param name="state">The house the viewer entered with <see cref="TryEnter"/>.</param>
    internal static void Release(HouseState state) {
        if (state.Leave()) {
            Evict(state);
        }
    }

    private static HouseState Publish(ulong lotGid, ulong ownerCharId) {
        while (true) {
            if (s_houses.TryGetValue(lotGid, out var existing)) {
                if (!existing.IsRetired) {
                    return existing;
                }

                Evict(existing);

                continue;
            }

            // The house is loaded under the lock, after any earlier state has stored its last change.
            lock (s_creationLocks[(int) (lotGid % CreationLockCount)]) {
                if (s_houses.ContainsKey(lotGid)) {
                    continue;
                }

                var house = HouseCollection.Load(lotGid);
                if (house is null || house.OwnerCharId != ownerCharId || !HouseTemplates.TryGet(house.TemplateId, out var info)
                    || !HouseState.TryCreate(house, info, out var created)) {
                    return null;
                }

                s_houses[lotGid] = created;

                return created;
            }
        }
    }

    private static void Evict(HouseState state)
        => s_houses.TryRemove(new KeyValuePair<ulong, HouseState>(state.LotGid, state));

}
