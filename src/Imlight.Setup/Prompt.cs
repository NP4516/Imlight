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
using System.Text;

namespace Imlight.Setup;

/// <summary>
/// Console questions.
/// </summary>
internal static class Prompt {

    internal static string Ask(string question, string? defaultValue = null) {
        while (true) {
            Console.Write(defaultValue is null ? $"{question}: " : $"{question} [{defaultValue}]: ");
            var answer = Console.ReadLine()?.Trim() ?? "";
            if (answer.Length > 0) {
                return answer;
            }

            if (defaultValue is not null) {
                return defaultValue;
            }
        }
    }

    internal static string AskSecret(string question) {
        while (true) {
            Console.Write($"{question}: ");
            var text = new StringBuilder();
            while (true) {
                var key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.Enter) {
                    break;
                }

                if (key.Key == ConsoleKey.Backspace) {
                    if (text.Length > 0) {
                        text.Length--;
                        Console.Write("\b \b");
                    }

                    continue;
                }

                if (!char.IsControl(key.KeyChar)) {
                    text.Append(key.KeyChar);
                    Console.Write('*');
                }
            }
            Console.WriteLine();

            if (text.Length > 0) {
                return text.ToString();
            }
        }
    }

    internal static bool YesNo(string question, bool defaultValue) {
        var answer = Ask($"{question} (y/n)", defaultValue ? "y" : "n");

        return answer.StartsWith('y') || answer.StartsWith('Y');
    }

}
