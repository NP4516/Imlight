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
 * Gives a character the Dorm Room: the stored house and the deed item in the backpack that the
 * client reads the house's lot from.
 *
 * USAGE EXAMPLE:
 * if (DormDeed.Ensure(wizard, out var entry, out var failure)) {
 *     // entry.House.LotGid is the lot to send the player to.
 * }
 *
 * NOTE:
 * - Running it again changes nothing. A house whose deed was trashed or traded keeps its furniture and
 *   only gets a new deed.
 * - The client shows the edit controls only when the backpack holds a deed whose lot is the zone it is in.
 *
 * TODO:
 *
 * Created by: Jay
 * Version: KALI 1.0
 * Last Updated: 10/14/2026
 */

using System.Collections.Generic;
using System.Linq;
using Imcodec.ObjectProperty.TypeCache;
using Imcodec.Types;
using Imlight.Common;
using Imlight.CoreLib.Shared.Resources;
using Imlight.CoreLib.WizardData.Collections;
using Imlight.CoreLib.WizardData.Models.Player;

namespace Imlight.CoreLib.Game.Housing;

/// <summary>
/// Why <see cref="DormDeed.Ensure"/> could not give a character the Dorm Room.
/// </summary>
internal enum DormFailure {
    None,
    BackpackFull,
    Unavailable,
}

/// <summary>
/// A character's Dorm Room as <see cref="DormDeed.Ensure"/> left it.
/// </summary>
/// <param name="House">The live house.</param>
/// <param name="DeedGid">The global id of the deed item in the backpack.</param>
/// <param name="HouseCreated">Whether this call created the house.</param>
/// <param name="ItemCreated">Whether this call created the deed item.</param>
internal readonly record struct DormEntry(HouseState House, ulong DeedGid, bool HouseCreated, bool ItemCreated);

internal static class DormDeed {

    /// <summary>
    /// Gets the lot of a character's Dorm Room.
    /// </summary>
    /// <param name="wizard">The character.</param>
    /// <returns>The lot, or 0 when the character has no Dorm Room yet.</returns>
    internal static ulong LotOf(Wizard wizard)
        => wizard.HouseLots.GetValueOrDefault(HousingZones.DormInfo.TemplateId);

    /// <summary>
    /// Makes sure a character has a Dorm Room and a deed item that points at it.
    /// </summary>
    /// <param name="wizard">The character.</param>
    /// <param name="entry">The house and deed when this returns <c>true</c>.</param>
    /// <param name="failure">The reason when this returns <c>false</c>.</param>
    /// <returns>
    /// <c>false</c> when a new deed is needed and the backpack is full, or when the house cannot be loaded or stored.
    /// Nothing is created that a retry cannot reuse.
    /// </returns>
    internal static bool Ensure(Wizard wizard, out DormEntry entry, out DormFailure failure) {
        entry = default;
        failure = DormFailure.None;

        var templateId = HousingZones.DormInfo.TemplateId;
        var item = wizard.InventoryBehavior.Items.FirstOrDefault(candidate => candidate.m_templateID == templateId);
        if (item is null && wizard.InventoryBehavior.FreeSlots <= 0) {
            failure = DormFailure.BackpackFull;

            return false;
        }

        var house = HouseRegistry.GetOrCreateDorm(wizard, out var houseCreated);
        if (house is null) {
            Logger.Error("Character {CharId} has no dorm and none could be stored.", Logger.Args(wizard.CharId));
            failure = DormFailure.Unavailable;

            return false;
        }

        var itemCreated = item is null;
        if (itemCreated) {
            item = CreateDeed(wizard, house.LotGid);
            if (item is null) {
                failure = wizard.InventoryBehavior.FreeSlots <= 0 ? DormFailure.BackpackFull : DormFailure.Unavailable;

                return false;
            }
        } else if (!TrySetLot(item, house.LotGid, out var changed)) {
            Logger.Error("Deed item {ItemId} of character {CharId} has no deed behavior.",
                Logger.Args(item.m_globalID, wizard.CharId));
            failure = DormFailure.Unavailable;

            return false;
        } else if (changed && !WizardItemCollection.ApplyDeedLot(item, house.LotGid)) {
            Logger.Warning("Could not store the lot on deed item {ItemId} of character {CharId}.",
                Logger.Args(item.m_globalID, wizard.CharId));
        }

        entry = new DormEntry(house, item.m_globalID, houseCreated, itemCreated);

        return true;
    }

    private static WizClientObjectItem CreateDeed(Wizard wizard, ulong lotGid) {
        if (CoreObjectFactory.FinalizeCoreObject(HousingZones.DormInfo.TemplateId) is not WizClientObjectItem item) {
            Logger.Error("The dorm structure template {TemplateId} is not an item.", Logger.Args(HousingZones.DormInfo.TemplateId));

            return null;
        }

        item.m_characterId = (GID) wizard.CharId;
        CoreObjectFactory.InitializeCoreObjectBehaviors(item, item.m_templateID);
        if (!TrySetLot(item, lotGid, out _)) {
            Logger.Error("The dorm structure template {TemplateId} has no deed behavior.", Logger.Args(HousingZones.DormInfo.TemplateId));

            return null;
        }

        return wizard.AddInitializedItemToInventory(item) ? item : null;
    }

    /// <summary>
    /// Points a deed item at a lot.
    /// </summary>
    /// <param name="item">The deed item.</param>
    /// <param name="lotGid">The lot.</param>
    /// <param name="changed">Whether the item pointed at another lot before.</param>
    /// <returns><c>false</c> when the item has no deed behavior.</returns>
    internal static bool TrySetLot(WizClientObjectItem item, ulong lotGid, out bool changed) {
        changed = false;
        if (item.m_inactiveBehaviors is null) {
            CoreObjectFactory.InitializeCoreObjectBehaviors(item, item.m_templateID);
        }

        if (!CoreObjectFactory.FindBehaviorInstance<DeedBehavior>(item, out var deed)) {
            return false;
        }

        changed = deed.m_lotInstanceGID != lotGid;
        deed.m_lotInstanceGID = lotGid;

        return true;
    }

}
