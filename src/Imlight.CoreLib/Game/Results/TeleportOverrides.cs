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
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Imlight.Common;
using Imlight.CoreLib.WizardData;

namespace Imlight.CoreLib.Game.Results;

/// <summary>
/// Replaces the arrival point of a named teleport door, from spiraldb-extra/TeleportOverrides.json:
/// [{ "zone": "WizardCity/Interiors/WC_SchoolFire", "trigger": "Teleport location (FireSchool Ravenwood Exit)",
///    "location": "x,y,z,yaw" }]. Zone and trigger are matched without regard to case. "//" comments are allowed.
/// The file is read at the first teleport after a start.
/// </summary>
internal static class TeleportOverrides {

    private sealed class Entry {
        public string Zone { get; set; } = "";
        public string Trigger { get; set; } = "";
        public string Location { get; set; } = "";
    }

    private static readonly JsonSerializerOptions s_options = new() {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private static readonly Lazy<Dictionary<string, string>> s_entries = new(Load);

    /// <summary>The replacement arrival point for this door, or null when it has none.</summary>
    internal static string Find(string zonePath, string triggerName)
        => !string.IsNullOrEmpty(zonePath) && !string.IsNullOrEmpty(triggerName)
           && s_entries.Value.TryGetValue(Key(zonePath, triggerName), out var location) ? location : null;

    private static string Key(string zone, string trigger) => (zone + "|" + trigger).ToLowerInvariant();

    private static Dictionary<string, string> Load() {
        var result = new Dictionary<string, string>();
        var file = Path.Combine(SpiralDB.ExtraPath, "TeleportOverrides.json");
        if (!File.Exists(file)) {
            return result;
        }

        try {
            var entries = JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(file), s_options) ?? [];
            foreach (var e in entries.Where(e => !string.IsNullOrWhiteSpace(e.Zone) && !string.IsNullOrWhiteSpace(e.Trigger)
                         && e.Location.Split(',').Length >= 3)) {
                result[Key(e.Zone.Trim(), e.Trigger.Trim())] = e.Location.Trim();
            }

            Logger.Information("Teleport overrides loaded for {0} doors.", Logger.Args(result.Count));
        }
        catch (Exception e) {
            Logger.Warning("TeleportOverrides.json could not be read: {0}", Logger.Args(e.Message));
        }

        return result;
    }

}
