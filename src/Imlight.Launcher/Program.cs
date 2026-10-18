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
 * IMLIGHT LAUNCHER
 * ========================================================================
 *
 * PURPOSE:
 * Keeps an Imlight server up to date from the GitHub releases of a public
 * repository, then runs it. The launcher updates itself the same way.
 *
 * USAGE:
 * ImlightLauncher                 Update if needed, then start the server.
 * ImlightLauncher --no-update     Start the server without checking.
 * ImlightLauncher --update-only   Update if needed, then exit.
 *
 * NOTE:
 * Settings live in launcher.json next to the launcher.
 */

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Imlight.Launcher;

internal static class Program {

    private static readonly string s_baseDirectory = AppContext.BaseDirectory;
    private static readonly Version s_launcherVersion = ReleaseUpdater.ParseVersion(
        typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion)
        ?? new Version(0, 0, 0);

    private static async Task<int> Main(string[] args) {
        Console.Title = "Imlight Launcher";
        Console.WriteLine($"Imlight Launcher {s_launcherVersion}");
        ReleaseUpdater.CleanUpOldLauncher();

        var settings = LauncherSettings.Load(Path.Combine(s_baseDirectory, "launcher.json"));
        var serverFolder = Path.Combine(s_baseDirectory, settings.ServerFolder);
        var serverExe = Path.Combine(serverFolder, settings.ServerExecutable);
        var noUpdate = args.Contains("--no-update", StringComparer.OrdinalIgnoreCase);
        var updateOnly = args.Contains("--update-only", StringComparer.OrdinalIgnoreCase);

        using var updater = new ReleaseUpdater(settings, s_launcherVersion.ToString());

        if (!noUpdate && (settings.CheckForUpdatesOnStart || updateOnly)) {
            if (IsServerRunning(serverExe)) {
                Console.WriteLine("The server is already running, so updates were skipped. Close it and start the launcher again to update.");
            }
            else {
                var release = await updater.GetLatestReleaseAsync();
                if (release is null) {
                    Console.WriteLine($"No release was found on github.com/{settings.Repository}; nothing to update.");
                }
                else {
                    if (await TryUpdateLauncherAsync(updater, release)) {
                        // The new launcher takes over from here.
                        Restart(args);

                        return 0;
                    }

                    await TryUpdateServerAsync(updater, release, serverFolder);
                }
            }
        }

        if (updateOnly) {
            return 0;
        }

        if (!File.Exists(serverExe)) {
            Console.WriteLine($"The server was not found at {serverExe}.");
            Console.WriteLine($"Make sure the latest release on github.com/{settings.Repository} has {ReleaseUpdater.SERVER_ASSET} attached, then start the launcher again.");
            WaitForKey();

            return 1;
        }

        return await RunServerAsync(settings, updater, serverFolder, serverExe);
    }

    private static async Task<bool> TryUpdateLauncherAsync(ReleaseUpdater updater, GitHubRelease release) {
        var asset = release.Asset(ReleaseUpdater.LAUNCHER_ASSET);
        if (asset is null || release.Version <= s_launcherVersion || s_launcherVersion == new Version(0, 0, 0)) {
            // 0.0.0 is a local build; never replace it.
            return false;
        }

        try {
            Console.WriteLine($"Updating the launcher to {release.Version}..");
            var zip = await updater.DownloadAsync(asset);
            ReleaseUpdater.InstallLauncher(zip);
            File.Delete(zip);

            return true;
        }
        catch (Exception ex) {
            Console.WriteLine($"The launcher could not update itself and will carry on with this version. {ex.Message}");

            return false;
        }
    }

    private static async Task<bool> TryUpdateServerAsync(ReleaseUpdater updater, GitHubRelease release, string serverFolder) {
        var installed = InstalledServerVersion(serverFolder);
        var asset = release.Asset(ReleaseUpdater.SERVER_ASSET);
        if (asset is null) {
            Console.WriteLine($"Release {release.TagName} has no server download attached.");

            return false;
        }

        if (installed is not null && release.Version <= installed) {
            Console.WriteLine($"The server is up to date ({installed}).");

            return false;
        }

        try {
            Console.WriteLine(installed is null
                ? $"Installing the server {release.Version}.."
                : $"Updating the server from {installed} to {release.Version}..");
            var zip = await updater.DownloadAsync(asset);
            ReleaseUpdater.InstallServer(zip, serverFolder, release.Version!);
            File.Delete(zip);
            Console.WriteLine("The server is up to date. Your settings in Config/Imlight.ini were kept.");

            return true;
        }
        catch (Exception ex) {
            Console.WriteLine($"The server could not be updated and will start as it is. {ex.Message}");

            return false;
        }
    }

    private static async Task<int> RunServerAsync(LauncherSettings settings, ReleaseUpdater updater, string serverFolder, string serverExe) {
        while (true) {
            Console.WriteLine("Starting the server. Close this window to stop it.");
            using var server = Process.Start(new ProcessStartInfo(serverExe) {
                WorkingDirectory = serverFolder,
                UseShellExecute = false,
            });
            if (server is null) {
                Console.WriteLine("The server could not be started.");
                WaitForKey();

                return 1;
            }

            // Closing the launcher window should stop the server with it.
            AppDomain.CurrentDomain.ProcessExit += (_, _) => StopServer(server);

            using var stopChecks = new CancellationTokenSource();
            var updateFound = WatchForUpdatesAsync(settings, updater, serverFolder, stopChecks.Token);
            var exited = server.WaitForExitAsync();

            var finished = await Task.WhenAny(exited, updateFound);
            stopChecks.Cancel();

            if (finished == exited) {
                Console.WriteLine($"The server stopped (exit code {server.ExitCode}).");
                WaitForKey();

                return server.ExitCode;
            }

            // An update was found and the owner allowed restarts for updates.
            var release = await updateFound;
            Console.WriteLine($"Restarting the server to install {release!.TagName}..");
            StopServer(server);
            await TryUpdateServerAsync(updater, release, serverFolder);
        }
    }

    /// <summary>
    /// Checks for updates while the server runs. Completes only when a server update is found and
    /// <see cref="LauncherSettings.RestartServerForUpdates"/> allows installing it now.
    /// </summary>
    private static async Task<GitHubRelease?> WatchForUpdatesAsync(LauncherSettings settings, ReleaseUpdater updater,
        string serverFolder, CancellationToken token) {
        string? announced = null;
        try {
            if (settings.CheckIntervalMinutes <= 0) {
                await Task.Delay(Timeout.Infinite, token);
            }

            while (true) {
                await Task.Delay(TimeSpan.FromMinutes(settings.CheckIntervalMinutes), token);

                var release = await updater.GetLatestReleaseAsync();
                var installed = InstalledServerVersion(serverFolder);
                if (release?.Asset(ReleaseUpdater.SERVER_ASSET) is null || (installed is not null && release.Version <= installed)) {
                    continue;
                }

                if (settings.RestartServerForUpdates) {
                    return release;
                }

                if (announced != release.TagName) {
                    announced = release.TagName;
                    Console.WriteLine($"[Launcher] Server update {release.TagName} is available. It installs the next time the launcher starts.");
                }
            }
        }
        catch (TaskCanceledException) {
            // The server stopped; nobody reads this result.
            return null;
        }
    }

    private static Version? InstalledServerVersion(string serverFolder) {
        var file = Path.Combine(serverFolder, ReleaseUpdater.SERVER_VERSION_FILE);

        return File.Exists(file) ? ReleaseUpdater.ParseVersion(File.ReadAllText(file)) : null;
    }

    private static bool IsServerRunning(string serverExe) {
        var name = Path.GetFileNameWithoutExtension(serverExe);
        var path = Path.GetFullPath(serverExe);

        return Process.GetProcessesByName(name).Any(p => {
            try {
                return string.Equals(p.MainModule?.FileName, path, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception) {
                // Another user's process; it cannot be ours.
                return false;
            }
        });
    }

    private static void StopServer(Process server) {
        try {
            if (!server.HasExited) {
                server.Kill(entireProcessTree: true);
                server.WaitForExit(10000);
            }
        }
        catch (InvalidOperationException) { }
    }

    private static void Restart(string[] args) {
        var startInfo = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
        foreach (var arg in args) {
            startInfo.ArgumentList.Add(arg);
        }
        Process.Start(startInfo);
    }

    private static void WaitForKey() {
        if (Console.IsInputRedirected) {
            return;
        }

        Console.WriteLine("Press any key to close this window.");
        Console.ReadKey(true);
    }

}
