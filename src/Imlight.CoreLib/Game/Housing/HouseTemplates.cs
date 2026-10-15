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
 * Reads what a house template says about itself: its lot zone, interior structure, preview zone, initial
 * layout and item limits. A house template is any item template with a DeedBehaviorTemplate.
 * 
 * USAGE EXAMPLE:
 * if (HouseTemplates.TryGet(item.m_templateID, out var info)) {
 *     var zone = info.LotZone;
 * }
 * 
 * NOTE:
 * - Templates are resolved by id when asked, never by scanning the catalog or by file name.
 * - A deed with a structure has two lots: the deed's zone is the exterior and the structure's zone the interior.
 *   A deed without one is a house of one zone.
 * 
 * TODO:
 * 
 * Created by: Jay
 * Version: KALI 1.0
 * Last Updated: 10/14/2026
 */

using System.Collections.Concurrent;
using System.Linq;
using Imcodec.ObjectProperty.TypeCache;
using Imlight.CoreLib.Shared.Resources;

namespace Imlight.CoreLib.Game.Housing;

/// <summary>
/// What a house template defines.
/// </summary>
/// <param name="TemplateId">The template id of the deed or structure item.</param>
/// <param name="LotZone">The zone of the house's lot.</param>
/// <param name="StructureTemplateId">The interior structure's template id, or 0 for none. A structure may name itself.</param>
/// <param name="PreviewZone">The zone shoppers preview the house in, or empty.</param>
/// <param name="LayoutFile">The initial layout in the client's pipe-delimited asset form, or empty.</param>
/// <param name="ItemLimit">The template's item limit, before any bonus slots.</param>
/// <param name="CastleBlockLimit">The template's castle block limit.</param>
internal readonly record struct HouseTemplateInfo(
    uint TemplateId,
    string LotZone,
    uint StructureTemplateId,
    string PreviewZone,
    string LayoutFile,
    int ItemLimit,
    int CastleBlockLimit) {

    /// <summary>
    /// Whether the house has a structure of its own besides the lot.
    /// </summary>
    internal bool HasStructure => StructureTemplateId != 0 && StructureTemplateId != TemplateId;

}

/// <summary>
/// Finds the house definition of a template.
/// </summary>
internal static class HouseTemplates {

    private static readonly ConcurrentDictionary<uint, HouseTemplateInfo> s_known = new();

    /// <summary>
    /// Gets the house definition of a template.
    /// </summary>
    /// <param name="templateId">The template id of an item.</param>
    /// <param name="info">The definition when this returns <c>true</c>.</param>
    /// <returns><c>false</c> when the template does not exist or is not a house.</returns>
    internal static bool TryGet(uint templateId, out HouseTemplateInfo info) {
        if (s_known.TryGetValue(templateId, out info)) {
            return true;
        }

        var deed = CoreObjectFactory.GetCoreTemplate(templateId)?.m_behaviors?.OfType<DeedBehaviorTemplate>().FirstOrDefault();
        var lotZone = deed?.m_lotWorldZone.ToString();
        if (string.IsNullOrEmpty(lotZone)) {
            info = default;

            return false;
        }

        info = s_known.GetOrAdd(templateId, new HouseTemplateInfo(
            templateId,
            lotZone,
            deed.m_structureTemplateID,
            deed.m_previewWorldZone.ToString(),
            deed.m_initialItemLayoutFile.ToString(),
            deed.m_itemLimit,
            deed.m_castleBlockLimit));

        return true;
    }

}
