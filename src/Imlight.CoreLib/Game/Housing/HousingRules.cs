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
 * The rules that decide which items may be placed in a house and how many copies of one item a house
 * or a backpack may hold.
 *
 * USAGE EXAMPLE:
 * if (HousingRules.TryGetPlaceable(templateId, out var template)
 *     && !HousingRules.TemplateCopyLimitReached(template, copies)) {
 *     // the item may be placed
 * }
 *
 * NOTE:
 * - The client lists items for placing by their category tags, which the server cannot see, so the rule
 *   here is the strict one: plain furniture only. An item with a behavior that needs a custom blob entry
 *   or a server feature that does not exist yet is refused.
 * - The behavior names come from every template under ObjectData/Housing.
 *
 * TODO:
 * - Which of the refused behaviors the client can place as a plain "Item" entry is unknown.
 *
 * Created by: Jay
 * Version: KALI 1.0
 * Last Updated: 10/14/2026
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Imcodec.ObjectProperty.TypeCache;
using Imlight.CoreLib.Shared.Resources;

namespace Imlight.CoreLib.Game.Housing;

internal static class HousingRules {

    private const string HousingDirectory = "ObjectData/Housing/";

    private static readonly HashSet<string> s_behaviorsWithoutHousingSupport = new(StringComparer.Ordinal) {
        "AlchemyStationBehavior", "AquariumBehavior", "BasicNPCServiceBehavior", "BreadCrumbBehavior",
        "CastleBlockDoorBehavior", "CastleBlocksBehavior", "CastleMagicBehavior", "ExtraHousingZoneBehavior",
        "GardeningBehavior", "GardeningShedBehavior", "GearVaultBehavior", "HatchmakingKioskBehavior",
        "HousingHallOfFameBehavior", "HousingMusicBehavior", "HousingMusicPlayerBehavior", "HousingPaletteBehavior",
        "HousingPetBehavior", "HousingSigilBehavior", "HousingSignBehavior", "HousingTeleporterBehavior",
        "HousingTextureBehavior", "InteractiveMusicBehavior", "ItemFinderBehavior", "JewelSocketBehavior",
        "JewelVaultBehavior", "MonsterArenaBehavior", "ObjectRemapBehavior", "PetItemBehavior", "PetNameBehavior",
        "PvPKioskBehavior", "RaidKeyBehavior", "RidableBehavior", "RitualObjectBehavior", "SeedBehavior",
        "ShoppingBehavior", "TrainBehavior", "TreasureCardPosterBehavior", "TreasureCardVaultBehavior",
        "WizardEquipmentBehavior", "WizardQuestingBehavior",
    };

    private static HashSet<uint> s_housingTemplateIds;

    /// <summary>
    /// Finds the template of an item and checks that it is plain furniture: an object template under
    /// <c>ObjectData/Housing</c> whose behaviors are all supported.
    /// </summary>
    /// <param name="templateId">The item's template id.</param>
    /// <param name="template">The item template when this returns <c>true</c>.</param>
    /// <returns><c>true</c> when the item may be placed in a house.</returns>
    internal static bool TryGetPlaceable(uint templateId, out WizItemTemplate template) {
        template = null;
        if (!HousingTemplateIds.Contains(templateId)
            || CoreObjectFactory.GetCoreTemplate(templateId) is not WizItemTemplate item
            || item.m_nObjectType != ObjectType.OT_OBJECT) {
            return false;
        }

        foreach (var behavior in item.m_behaviors ?? []) {
            if (behavior is null || s_behaviorsWithoutHousingSupport.Contains(behavior.m_behaviorName.ToString())) {
                return false;
            }
        }

        template = item;

        return true;
    }

    /// <summary>
    /// Checks whether a template may not have another copy: the client's limit on how often a house may hold
    /// one item, and how many copies a backpack may hold of it.
    /// </summary>
    /// <param name="template">The item template.</param>
    /// <param name="copies">The copies already held.</param>
    /// <returns><c>true</c> when the template has a limit and <paramref name="copies"/> reaches it.</returns>
    internal static bool TemplateCopyLimitReached(WizItemTemplate template, int copies)
        => template.m_itemLimit > 0 && copies >= template.m_itemLimit;

    /// <summary>
    /// Counts the placed objects of a template.
    /// </summary>
    /// <param name="objects">The blob objects of a house, removed slots included.</param>
    /// <param name="templateId">The template id.</param>
    /// <returns>The number of placed copies.</returns>
    internal static int CountPlaced(IEnumerable<HousingBlobObject> objects, uint templateId)
        => objects.Count(obj => HouseBlobCodec.TemplateId(obj) == templateId);

    private static HashSet<uint> HousingTemplateIds
        => LazyInitializer.EnsureInitialized(ref s_housingTemplateIds, BuildHousingTemplateIds);

    private static HashSet<uint> BuildHousingTemplateIds()
        => [.. CoreObjectFactory.TemplateManifest.m_serializedTemplates
            .Where(location => location is not null
                && location.m_filename.ToString().StartsWith(HousingDirectory, StringComparison.Ordinal))
            .Select(location => (uint) location.m_id)];

}
