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
 * Indexes the house zones the game data defines (deed and structure templates) so the server
 * can tell a house zone from an ordinary one, and reads the Dorm Room's initial layout.
 *
 * USAGE EXAMPLE:
 * HousingZones.Preload();
 * var isHouse = HousingZones.IsHousingZone(zoneName);
 * var limit = HousingZones.EffectiveItemLimit(HousingZones.DormInfo, bonusSlots: 0);
 *
 * NOTE:
 * The whole deed and structure catalog is indexed, but only the Dorm Room can be entered.
 * The index is built from CoreObjectFactory, so Root.wad must be loaded first.
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
using System.Threading;
using Imcodec.ObjectProperty;
using Imcodec.ObjectProperty.TypeCache;
using Imlight.Common;
using Imlight.CoreLib.Game.World;
using Imlight.CoreLib.Shared.Resources;

namespace Imlight.CoreLib.Game.Housing;

/// <summary>
/// A house zone and the deed or structure template that defines it.
/// </summary>
/// <param name="TemplateId">The template id of the deed or structure item.</param>
/// <param name="ZoneName">The zone the template's <c>DeedBehaviorTemplate</c> names as its lot.</param>
/// <param name="ItemLimit">The template's <c>m_itemLimit</c>, before any bonus slots.</param>
/// <param name="IsDorm">Whether the template is the Dorm Room structure.</param>
internal readonly record struct HousingZoneInfo(uint TemplateId, string ZoneName, int ItemLimit, bool IsDorm);

/// <summary>
/// Finds house zones in the game data and loads the Dorm Room's initial layout.
/// </summary>
internal static class HousingZones {

    private const string DormTemplateFile = "ObjectData/Housing/WC/WC_Structure_Dorm.xml";
    private const string DeedsDirectory = "ObjectData/Housing/Deeds/";
    private const string StructuresDirectory = "ObjectData/Housing/Structures/";
    private const string DormLayoutFile = "DormObjects.xml";
    private const string ItemLimitOverrideKey = "Housing.ItemLimitOverride";

    private static Catalog s_catalog;
    private static int s_privateZoneTestRegistered;

    /// <summary>
    /// The Dorm Room every character owns.
    /// </summary>
    internal static HousingZoneInfo DormInfo => GetCatalog().Dorm;

    /// <summary>
    /// Builds the zone index and registers house zones as always-private attach targets.
    /// Throws when the Dorm Room template is missing from the game data.
    /// </summary>
    internal static void Preload() {
        GetCatalog();

        if (Interlocked.Exchange(ref s_privateZoneTestRegistered, 1) == 0) {
            ZoneInstanceOwners.RegisterPrivateZoneTest(IsHousingZone);
        }
    }

    /// <summary>
    /// Checks whether a zone is the lot of any deed or structure in the game data.
    /// </summary>
    /// <param name="zoneName">The zone name, compared without regard to case.</param>
    /// <returns><c>true</c> for a house zone.</returns>
    internal static bool IsHousingZone(string zoneName)
        => !string.IsNullOrEmpty(zoneName) && GetCatalog().ByZone.ContainsKey(zoneName);

    /// <summary>
    /// Gets the deed or structure definition of a house zone.
    /// </summary>
    /// <param name="zoneName">The zone name, compared without regard to case.</param>
    /// <param name="info">The house zone's definition when this returns <c>true</c>.</param>
    /// <returns><c>true</c> for a house zone.</returns>
    internal static bool TryGet(string zoneName, out HousingZoneInfo info) {
        info = default;

        return !string.IsNullOrEmpty(zoneName) && GetCatalog().ByZone.TryGetValue(zoneName, out info);
    }

    /// <summary>
    /// Gets the number of objects a house may hold: the client's own formula, so the server and
    /// the client stop placing at the same count.
    /// </summary>
    /// <param name="info">The house zone's definition.</param>
    /// <param name="bonusSlots">The house's purchased bonus slots.</param>
    /// <returns>The template limit plus the bonus slots, or <c>Housing.ItemLimitOverride</c> when it is positive.</returns>
    internal static int EffectiveItemLimit(HousingZoneInfo info, int bonusSlots) {
        var itemLimitOverride = ConfigurationManager.Settings[ItemLimitOverrideKey].AsInt();

        return itemLimitOverride > 0 ? itemLimitOverride : info.ItemLimit + bonusSlots;
    }

    /// <summary>
    /// Reads the Dorm Room's initial layout from the dorm zone archive.
    /// </summary>
    /// <returns>The layout, or <c>null</c> when the archive or the layout file cannot be read.</returns>
    internal static HousingLayout LoadDormLayout() {
        var zoneName = DormInfo.ZoneName;
        if (!ResourceManager.TryLoadArchive(zoneName, out var wad)) {
            Logger.Error("Could not load the dorm zone archive {ZoneName}", Logger.Args(zoneName));

            return null;
        }

        var bytes = wad.OpenFile(DormLayoutFile);
        if (!bytes.HasValue) {
            Logger.Error("The dorm zone archive {ZoneName} has no {FileName}",
                Logger.Args(zoneName, DormLayoutFile));

            return null;
        }

        var serializer = new BindSerializer();
        if (!serializer.Deserialize<HousingLayout>(bytes.Value.ToArray(), 1, out var layout)) {
            Logger.Error("Could not deserialize {FileName} of {ZoneName}",
                Logger.Args(DormLayoutFile, zoneName));

            return null;
        }

        return layout;
    }

    private static Catalog GetCatalog()
        => LazyInitializer.EnsureInitialized(ref s_catalog, BuildCatalog);

    private static Catalog BuildCatalog() {
        var byZone = new Dictionary<string, HousingZoneInfo>(StringComparer.OrdinalIgnoreCase);
        HousingZoneInfo? dorm = null;

        var locations = CoreObjectFactory.TemplateManifest.m_serializedTemplates
            .Where(location => location is not null && IsCatalogFile(location.m_filename.ToString()))
            .OrderBy(location => location.m_id);
        foreach (var location in locations) {
            var template = CoreObjectFactory.GetCoreTemplate(location.m_id);
            var deed = template?.m_behaviors?.OfType<DeedBehaviorTemplate>().FirstOrDefault();
            var zoneName = deed?.m_lotWorldZone.ToString();
            if (string.IsNullOrEmpty(zoneName)) {
                Logger.Debug("Housing template {FileName} has no lot zone", Logger.Args(location.m_filename));

                continue;
            }

            var isDorm = location.m_filename == DormTemplateFile;
            var info = new HousingZoneInfo(location.m_id, zoneName, deed.m_itemLimit, isDorm);
            if (isDorm) {
                dorm = info;
            }

            byZone.TryAdd(zoneName, info);
        }

        if (dorm is not { } dormInfo) {
            throw new InvalidOperationException($"The dorm structure {DormTemplateFile} is missing from the game data.");
        }

        byZone[dormInfo.ZoneName] = dormInfo;
        Logger.Information("Indexed {Count} house zones.", Logger.Args(byZone.Count));

        return new Catalog(byZone, dormInfo);
    }

    private static bool IsCatalogFile(string fileName)
        => fileName.StartsWith(DeedsDirectory, StringComparison.Ordinal)
            || fileName.StartsWith(StructuresDirectory, StringComparison.Ordinal)
            || fileName == DormTemplateFile;

    private sealed record Catalog(Dictionary<string, HousingZoneInfo> ByZone, HousingZoneInfo Dorm);

}
