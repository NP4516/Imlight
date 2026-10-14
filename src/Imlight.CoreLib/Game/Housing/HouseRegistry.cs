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
 * var state = HouseRegistry.GetOrCreateDorm(wizard.CharId, out var created);
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
using Imlight.Common;
using Imlight.CoreLib.Shared.Utilities;
using Imlight.CoreLib.WizardData.Collections;
using Imlight.CoreLib.WizardData.Models.Player;

namespace Imlight.CoreLib.Game.Housing;

/// <summary>
/// The houses that are loaded, by lot.
/// </summary>
internal static class HouseRegistry {

    private static readonly ConcurrentDictionary<ulong, HouseState> s_houses = new();

    /// <summary>
    /// Finds a character's Dorm Room, creating and storing it when the character has none. However many
    /// logins race, one document and one lot exist per character.
    /// </summary>
    /// <param name="ownerCharId">The owning character.</param>
    /// <param name="created">Whether this call created the house.</param>
    /// <returns>The live house, or <c>null</c> when it cannot be loaded or stored.</returns>
    internal static HouseState GetOrCreateDorm(ulong ownerCharId, out bool created) {
        created = false;
        var dorm = HousingZones.DormInfo;
        var house = HouseCollection.Load(ownerCharId, dorm.TemplateId);
        if (house is null) {
            var layout = HousingZones.LoadDormLayout();
            if (layout is null) {
                return null;
            }

            var lotGid = (ulong) RandomGen.GenerateGUID();
            var candidate = new House {
                Id = House.GetDocumentId(ownerCharId, dorm.TemplateId),
                LotGid = lotGid,
                OwnerCharId = ownerCharId,
                DeedTemplateId = dorm.TemplateId,
                ZoneName = dorm.ZoneName,
                BlobVersion = 1,
                BonusSlots = 0,
                Objects = HouseBlobCodec.FromLayout(layout),
                CreatedUtc = DateTime.UtcNow,
            };
            created = HouseCollection.TryCreate(candidate, out house);
            if (house is null) {
                return null;
            }

            if (created) {
                Logger.Information("Created the dorm {LotGid} of character {CharId}", Logger.Args(house.LotGid, ownerCharId));
            }
        }

        return Publish(house, dorm);
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
            var dorm = HousingZones.DormInfo;
            var house = HouseCollection.Load(ownerCharId, dorm.TemplateId);
            state = house is not null && house.LotGid == lotGid ? Publish(house, dorm) : null;
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

    private static HouseState Publish(House house, HousingZoneInfo info) {
        while (true) {
            var state = s_houses.GetOrAdd(house.LotGid, _ => new HouseState(house, info, HouseBlobCodec.NewGidBase()));
            if (!state.IsRetired) {
                return state;
            }

            Evict(state);
        }
    }

    private static void Evict(HouseState state)
        => s_houses.TryRemove(new KeyValuePair<ulong, HouseState>(state.LotGid, state));

}
