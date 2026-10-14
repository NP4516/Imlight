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
 * BANK RULES
 * ========================================================================
 *
 * PURPOSE:
 * The rules of the bank that need no database: which items count against the bank capacity.
 *
 * USAGE EXAMPLE:
 * if (BankRules.CountsTowardBank(item)) { ... }
 *
 * NOTE:
 * The count follows the client's CountRegularBankItems: reagents, pet snacks and items whose template has
 * the Emote adjective take no space.
 *
 * TODO:
 *
 * Created by: Jay
 * Version: KALI 1.0
 * Last Updated: 10/14/2026
 */

using System;
using System.Linq;
using Imcodec.ObjectProperty.TypeCache;
using Imlight.CoreLib.Shared.Resources;

namespace Imlight.CoreLib.Game.Bank;

/// <summary>
/// Pure rules of the bank.
/// </summary>
internal static class BankRules {

    private const string EmoteAdjective = "Emote";

    /// <summary>
    /// Whether an item takes a slot of the regular bank.
    /// </summary>
    /// <param name="item">The item in question.</param>
    /// <returns>False for reagents, pet snacks and emote items, which the bank does not count.</returns>
    internal static bool CountsTowardBank(WizClientObjectItem item) {
        if (item is ClientReagentItem or ClientPetSnackItem) {
            return false;
        }

        return !HasAdjective(GetTemplate(item), EmoteAdjective);
    }

    private static WizItemTemplate GetTemplate(WizClientObjectItem item)
        => CoreObjectFactory.GetCoreTemplate(item.m_templateID) as WizItemTemplate;

    private static bool HasAdjective(WizItemTemplate template, string name)
        => template?.m_adjectiveList is not null
            && template.m_adjectiveList.Any(adjective => string.Equals(adjective, name, StringComparison.OrdinalIgnoreCase));

}
