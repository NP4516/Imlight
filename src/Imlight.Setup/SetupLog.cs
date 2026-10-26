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

namespace Imlight.Setup;

/// <summary>
/// Writes setup progress to the console and to setup-log.txt, so a failed setup can be shared when asking for help.
/// </summary>
internal static class SetupLog {

    private static StreamWriter? s_file;

    internal static string? FilePath { get; private set; }

    /// <summary>
    /// Starts a new log. False when another setup window still has it open.
    /// </summary>
    internal static bool Open(string folder) {
        Directory.CreateDirectory(folder);
        FilePath = Path.Combine(folder, "setup-log.txt");
        try {
            var stream = new FileStream(FilePath, FileMode.Create, FileAccess.Write, FileShare.Read);
            s_file = new StreamWriter(stream) { AutoFlush = true };
        }
        catch (IOException) {
            return false;
        }

        s_file.WriteLine($"Imlight Setup log, {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        return true;
    }

    /// <summary>
    /// Lets go of the log, so a setup window left open does not keep the next setup from starting.
    /// </summary>
    internal static void Close() {
        s_file?.Dispose();
        s_file = null;
    }

    /// <summary>A line the person should read.</summary>
    internal static void Info(string message) {
        Console.WriteLine(message);
        s_file?.WriteLine(message);
    }

    /// <summary>A heading for a setup step.</summary>
    internal static void Step(int number, int total, string message) {
        var line = $"[{number}/{total}] {message}";
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(line);
        Console.ResetColor();
        s_file?.WriteLine();
        s_file?.WriteLine(line);
    }

    /// <summary>Detail that only matters when something goes wrong: kept in the log, not shown.</summary>
    internal static void Detail(string message)
        => s_file?.WriteLine("    " + message);

    internal static void Error(string message) {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(message);
        Console.ResetColor();
        s_file?.WriteLine("ERROR: " + message);
    }

}
