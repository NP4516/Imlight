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

namespace Imlight.Setup;

/// <summary>
/// Edits a value in Imlight.ini, keeping every comment and every other line as it was.
/// </summary>
internal static class IniFile {

    internal static void Set(List<string> lines, string section, string key, string value) {
        var inSection = false;
        var sectionEnd = -1;

        for (var i = 0; i < lines.Count; i++) {
            var trimmed = lines[i].Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']')) {
                if (inSection) {
                    break;
                }

                inSection = trimmed[1..^1].Trim().Equals(section, StringComparison.OrdinalIgnoreCase);
                if (inSection) {
                    sectionEnd = i + 1;
                }

                continue;
            }

            if (!inSection) {
                continue;
            }

            if (trimmed.Length > 0) {
                sectionEnd = i + 1;
            }

            var equals = trimmed.IndexOf('=');
            if (!trimmed.StartsWith(';') && equals > 0
                && trimmed[..equals].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) {
                lines[i] = $"{key} = {value}";

                return;
            }
        }

        if (sectionEnd < 0) {
            lines.Add("");
            lines.Add($"[{section}]");
            lines.Add($"{key} = {value}");

            return;
        }

        lines.Insert(sectionEnd, $"{key} = {value}");
    }

}
