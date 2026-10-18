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
 * IMLIGHT SETUP
 * ========================================================================
 *
 * PURPOSE:
 * Installs a working Imlight server for someone with no development tools:
 * it finds their Wizard101 install, installs the .NET SDK if needed,
 * downloads the latest Imlight source from GitHub, reads the files Imcodec
 * needs out of their own game client, builds the server and adds shortcuts.
 * Running it again updates the server and keeps its settings and data.
 *
 * The installed copy, Imlight.exe, also starts the server with the game
 * files from the person's own Wizard101 folder (so no separate patch server
 * is needed), starts Wizard101 connected to it, and uninstalls.
 *
 * NOTE:
 * Nothing copyrighted is downloaded or shipped: the game files come from
 * the person's own Wizard101 install. Everything is logged to setup-log.txt.
 */

using System;
using System.IO;
using System.Threading.Tasks;

namespace Imlight.Setup;

internal static class Program {

    private const int STEPS = 7;

    private static async Task<int> Main(string[] args) {
        SetupOptions options;
        try {
            options = SetupOptions.Parse(args);
        }
        catch (SetupException ex) {
            Console.WriteLine(ex.Message);

            return 2;
        }

        switch (options.Mode) {
            case SetupMode.Start:
                return await InstalledCommands.StartAsync(options.InstallFolder);
            case SetupMode.Play:
                return InstalledCommands.Play(options.InstallFolder);
            case SetupMode.Uninstall:
                return InstalledCommands.Uninstall(options.InstallFolder, options.Unattended);
        }

        Console.Title = "Imlight Setup";
        if (await SelfUpdate.TryRunNewerAsync(args)) {
            return 0;
        }

        Console.WriteLine("Imlight Setup");
        Console.WriteLine($"This installs an Imlight server from github.com/{options.Repository} into {options.InstallFolder}.");
        Console.WriteLine("If Imlight is already there, it is updated and your settings and characters are kept.");
        if (!options.Unattended && !Prompt.YesNo("Continue?", true)) {
            return 0;
        }

        if (InstalledCommands.IsServerRunning(options.InstallFolder)) {
            Console.WriteLine("The Imlight server is running. Close its window, then run setup again.");
            WaitForKey(options);

            return 1;
        }

        SetupLog.Open(options.InstallFolder);
        SetupLog.Detail($"Setup {SelfUpdate.CurrentVersion}. Options: repo={options.Repository} branch={options.Branch} dir={options.InstallFolder}");
        using var steps = new SetupSteps(options);

        try {
            SetupLog.Step(1, STEPS, "Finding Wizard101");
            var rootWad = steps.FindRootWad();

            SetupLog.Step(2, STEPS, "Checking for the .NET SDK");
            var dotnet = await steps.EnsureDotnetAsync();

            SetupLog.Step(3, STEPS, "Downloading the latest Imlight");
            await steps.DownloadSourceAsync();

            SetupLog.Step(4, STEPS, "Reading game files from your Wizard101 install");
            steps.PrepareGeneratorInputs(rootWad);

            SetupLog.Step(5, STEPS, "Building the server");
            var isNewConfig = !File.Exists(steps.ConfigFile);
            await steps.BuildServerAsync(dotnet);

            SetupLog.Step(6, STEPS, "Setting up the server");
            steps.Configure(isNewConfig);

            SetupLog.Step(7, STEPS, "Adding shortcuts");
            await steps.CreateShortcutsAsync();
        }
        catch (Exception ex) {
            SetupLog.Info("");
            SetupLog.Error(ex is SetupException ? ex.Message : $"Setup stopped because of an unexpected problem: {ex.Message}");
            SetupLog.Detail(ex.ToString());
            SetupLog.Info($"The full log is at {SetupLog.FilePath}. Share it when asking for help.");
            WaitForKey(options);

            return 1;
        }

        SetupLog.Info("");
        SetupLog.Info("Imlight is installed. Everything it needs is set up, including the game files from your Wizard101.");
        SetupLog.Info("1. Double-click \"Start Imlight Server\" on your desktop and wait for \"Imlight may now be connected to.\"");
        SetupLog.Info("2. Double-click \"Play Wizard101 on Imlight\" and log in with your admin account.");
        SetupLog.Info("To update later, use \"Update Imlight\" in the Start menu. To remove it, use \"Uninstall Imlight\" there,");
        SetupLog.Info("or uninstall \"Imlight Server\" from Settings > Apps.");
        WaitForKey(options);

        return 0;
    }

    private static void WaitForKey(SetupOptions options) {
        if (options.Unattended || Console.IsInputRedirected) {
            return;
        }

        Console.WriteLine("Press any key to close this window.");
        Console.ReadKey(true);
    }

}
