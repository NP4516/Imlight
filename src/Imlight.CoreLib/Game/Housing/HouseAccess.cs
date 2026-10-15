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
 * Decides what a character may do in a house: be its owner, visit it, or stay out.
 *
 * USAGE EXAMPLE:
 * if (HouseAccess.Evaluate(visitor, house) == HouseAccessLevel.Denied) {
 *     // refuse the visit
 * }
 *
 * NOTE:
 * - The owner always has access. Anyone else is a guest only when the owner is a friend of theirs and has not hidden
 *   from friends (WizGameStats.m_invisibleToFriends). The protocol knows no finer setting.
 * - Only the owner decorates. A guest sees the house and never edits it.
 * - The owner's data comes from the store, so the owner can be offline.
 *
 * TODO:
 * - A house listed on Castle Tours is open to everyone. Add that rule with the Castle Tours feature.
 *
 * Created by: Jay
 * Version: KALI 1.0
 * Last Updated: 10/15/2026
 */

using Imlight.CoreLib.WizardData.Collections;
using Imlight.CoreLib.WizardData.Models.Player;

namespace Imlight.CoreLib.Game.Housing;

/// <summary>
/// What a character may do in a house.
/// </summary>
internal enum HouseAccessLevel {
    Denied,
    Guest,
    Owner,
}

internal static class HouseAccess {

    /// <summary>
    /// Decides what a character may do in a house.
    /// </summary>
    /// <param name="visitor">The character who wants in.</param>
    /// <param name="house">The stored house.</param>
    /// <returns>The access level; <see cref="HouseAccessLevel.Denied"/> when the house is missing or its owner cannot be loaded.</returns>
    internal static HouseAccessLevel Evaluate(Wizard visitor, House house) {
        if (visitor is null || house is null) {
            return HouseAccessLevel.Denied;
        }

        if (visitor.CharId == house.OwnerCharId) {
            return HouseAccessLevel.Owner;
        }

        var owner = WizardCollection.GetCharacterUnloaded(house.OwnerCharId);
        if (owner?.GameStats is null || owner.GameStats.m_invisibleToFriends) {
            return HouseAccessLevel.Denied;
        }

        return BuddyRelationshipCollection.AreFriends(house.OwnerCharId, visitor.CharId) ? HouseAccessLevel.Guest : HouseAccessLevel.Denied;
    }

}
