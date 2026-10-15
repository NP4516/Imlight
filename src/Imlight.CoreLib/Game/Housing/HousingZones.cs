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
 * Reads the starter house's initial layout from its zone archive.
 * 
 * USAGE EXAMPLE:
 * var layout = HousingZones.LoadDormLayout(info.LotZone);
 * 
 * NOTE:
 * 
 * TODO:
 * - Replace with a layout lookup that follows the template's layout file for every house.
 * 
 * Created by: Jay
 * Version: KALI 1.0
 * Last Updated: 10/14/2026
 */

using Imcodec.ObjectProperty;
using Imcodec.ObjectProperty.TypeCache;
using Imlight.Common;
using Imlight.CoreLib.Shared.Resources;

namespace Imlight.CoreLib.Game.Housing;

/// <summary>
/// Loads the starter house's initial layout.
/// </summary>
internal static class HousingZones {

    private const string DormLayoutFile = "DormObjects.xml";

    /// <summary>
    /// Reads the starter house's initial layout from the archive of its zone.
    /// </summary>
    /// <param name="zoneName">The lot zone of the starter house.</param>
    /// <returns>The layout, or <c>null</c> when the archive or the layout file cannot be read.</returns>
    internal static HousingLayout LoadDormLayout(string zoneName) {
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

}
