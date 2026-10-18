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
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Imlight.Setup;

/// <summary>
/// Shows the app's page in a window of its own, with no tabs or address bar, using the Microsoft Edge
/// that comes with Windows. It keeps its own profile, so it never mixes with the person's browsing.
/// </summary>
internal static class AppWindow {

    /// <summary>
    /// The window's process, or null when it could not be opened and the page should go to the normal browser.
    /// </summary>
    internal static Process? Open(string url, string profileFolder) {
        if (!OperatingSystem.IsWindows() || FindEdge() is not { } edge) {
            return null;
        }

        try {
            Directory.CreateDirectory(profileFolder);
            var startInfo = new ProcessStartInfo(edge) { UseShellExecute = false };
            foreach (var arg in new[] {
                         $"--app={url}", $"--user-data-dir={profileFolder}", "--window-size=1200,860",
                         "--no-first-run", "--no-default-browser-check", "--disable-features=Translate",
                     }) {
                startInfo.ArgumentList.Add(arg);
            }

            var process = Process.Start(startInfo);

            // A window from an earlier run can take the request over and close this process at once;
            // without a process to watch, the normal browser is safer.
            if (process is null || process.WaitForExit(3000)) {
                process?.Dispose();

                return null;
            }

            return process;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException) {
            return null;
        }
    }

    private static string? FindEdge() {
        foreach (var root in new[] {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                 }) {
            var path = Path.Combine(root, "Microsoft", "Edge", "Application", "msedge.exe");
            if (root.Length > 0 && File.Exists(path)) {
                return path;
            }
        }

        return null;
    }

    /// <summary>
    /// Hides the console window behind the app, so the app is the only window.
    /// </summary>
    internal static void HideConsole() {
        if (OperatingSystem.IsWindows() && GetConsoleWindow() is var handle && handle != IntPtr.Zero) {
            ShowWindow(handle, 0);
        }
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr handle, int command);

}
