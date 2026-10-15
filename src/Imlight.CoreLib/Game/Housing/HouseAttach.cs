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
 * Decides whether a client may attach to a house zone, and keeps the deed item it receives at login
 * pointing at the character's lot.
 *
 * USAGE EXAMPLE:
 * if (!HouseAttach.TryAuthorize(wizard, zoneName, message.ZoneID, out var house)) {
 *     // refuse the attach
 * }
 *
 * NOTE:
 * - The client names the zone and the lot in MSG_ATTACH, so neither is trusted: the lot must be the one
 *   stored for the character, and the house must belong to the character.
 * - Only the owner enters a house. Visitors are not supported yet.
 *
 * TODO:
 *
 * Created by: Jay
 * Version: KALI 1.0
 * Last Updated: 10/14/2026
 */

using System;
using System.Linq;
using Imlight.Common;
using Imlight.CoreLib.WizardData.Models.Player;

namespace Imlight.CoreLib.Game.Housing;

internal static class HouseAttach {

    /// <summary>
    /// Checks whether a character may attach to a zone.
    /// </summary>
    /// <param name="wizard">The character.</param>
    /// <param name="zoneName">The zone the client attaches to.</param>
    /// <param name="messageZoneId">The zone id the client sent. It is not used for an ordinary zone.</param>
    /// <param name="house">The character's house when the zone is a house; otherwise <c>null</c>.</param>
    /// <returns>
    /// <c>true</c> for an ordinary zone, and for the character's own house in the zone when the client names no lot or the stored lot.
    /// </returns>
    internal static bool TryAuthorize(Wizard wizard, string zoneName, ulong messageZoneId, out HouseState house) {
        house = null;
        if (!HouseZoneIndex.IsHouseZone(zoneName)) {
            return true;
        }

        var lotGid = LotOfZone(wizard, zoneName);
        if (lotGid == 0 || (messageZoneId != 0 && messageZoneId != lotGid)) {
            Logger.Warning("Character {CharId} may not attach to house zone {Zone} with lot {MessageZoneId}.",
                Logger.Args(wizard.CharId, zoneName, messageZoneId));

            return false;
        }

        if (!HouseRegistry.TryGetOwned(wizard.CharId, lotGid, out house)
            || !string.Equals(house.ZoneName, zoneName, StringComparison.OrdinalIgnoreCase)) {
            Logger.Warning("Character {CharId} has no house with lot {LotGid} in zone {Zone}.",
                Logger.Args(wizard.CharId, lotGid, zoneName));
            house = null;

            return false;
        }

        return true;
    }

    /// <summary>
    /// Points the deed item in the backpack at the character's lot, so what the client receives never depends on
    /// how the database stored the behavior.
    /// </summary>
    /// <param name="wizard">The character.</param>
    internal static void ApplyDeedLot(Wizard wizard) {
        foreach (var (templateId, lotGid) in wizard.HouseLots) {
            var deed = wizard.InventoryBehavior.Items.FirstOrDefault(item => item.m_templateID == templateId);
            if (deed is not null && !DormDeed.TrySetLot(deed, lotGid, out _)) {
                Logger.Warning("Deed item {ItemId} of character {CharId} has no deed behavior.",
                    Logger.Args(deed.m_globalID, wizard.CharId));
            }
        }
    }

    private static ulong LotOfZone(Wizard wizard, string zoneName) {
        foreach (var (templateId, lotGid) in wizard.HouseLots) {
            if (HouseTemplates.TryGet(templateId, out var info)
                && string.Equals(info.LotZone, zoneName, StringComparison.OrdinalIgnoreCase)) {
                return lotGid;
            }
        }

        return 0;
    }

}
