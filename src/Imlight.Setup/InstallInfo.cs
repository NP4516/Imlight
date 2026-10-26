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
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Imlight.Setup;

/// <summary>
/// What setup remembers about an install, in imlight-install.json, so Start, Play and Update need no questions.
/// </summary>
internal sealed class InstallInfo {

    internal const string FILE_NAME = "imlight-install.json";

    /// <summary>The Root.wad in the player's Wizard101 install.</summary>
    public string? RootWad { get; set; }

    /// <summary>The folder holding Root.wad and the other game wads.</summary>
    [JsonIgnore]
    public string? GameDataFolder => RootWad is null ? null : Path.GetDirectoryName(RootWad);

    /// <summary>The Wizard101 folder (the one with Bin and Data), when Root.wad is in the usual place.</summary>
    [JsonIgnore]
    public string? GameFolder {
        get {
            var data = GameDataFolder is null ? null : Directory.GetParent(GameDataFolder);
            var isUsualLayout = data is not null && data.Name.Equals("Data", StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(GameDataFolder!).Equals("GameData", StringComparison.OrdinalIgnoreCase);

            return isUsualLayout ? data!.Parent?.FullName : null;
        }
    }

    internal static InstallInfo Load(string installFolder) {
        var path = Path.Combine(installFolder, FILE_NAME);
        try {
            return File.Exists(path) ? JsonSerializer.Deserialize<InstallInfo>(File.ReadAllText(path)) ?? new() : new();
        }
        catch (JsonException) {
            return new();
        }
    }

    internal void Save(string installFolder)
        => File.WriteAllText(Path.Combine(installFolder, FILE_NAME),
            JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));

}
