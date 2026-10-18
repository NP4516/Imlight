# ImlightSetup.exe

A one-click installer for **original Imlight** ([Revive101/Imlight](https://github.com/Revive101/Imlight)), for people with no development tools.

The same program also builds as **ImlightManager.exe**, which installs this fork (randomizer mode, ID lookup) into `%LOCALAPPDATA%\ImlightManager` instead. The two can be installed side by side.

## What it does

1. **Finds Wizard101** in the usual places (KingsIsle or Steam). If it can't, it asks for the folder.
2. **Installs the .NET 10 SDK** into the Imlight folder if the computer doesn't have it. Nothing else on the computer changes.
3. **Downloads the latest Imlight** and the exact Imcodec version it uses from GitHub. Git is not needed.
4. **Reads the game files Imcodec needs** (`*Messages*.xml`) straight out of the player's own `Root.wad`, and uses the type dump Imcodec ships. Nothing copyrighted is downloaded or shared.
5. **Builds the server** into `%LOCALAPPDATA%\ImlightServer\Server`.
6. **Asks for an admin account** on a first install.
7. **Installs `Imlight.exe`, the app, and opens it**. One desktop shortcut opens the app; the Start menu also has Start Imlight Server (the server in a plain window), Server Settings, Server Logs, Setup Log, Update Imlight and Uninstall Imlight. Imlight is also listed under **Settings > Apps**, so it can be uninstalled from there.

## No patch server needed

Imlight reads game files from a patch server. On a home computer, starting the server runs a small one built into `Imlight.exe` that hands the server the files from the player's own Wizard101 folder (`Data\GameData`), then starts the server. Nothing is downloaded and nothing leaves the computer. If something else (such as Aurorium) already uses the patch server port, Imlight uses that instead.

If the server asks for an area's file that the player's game hasn't downloaded yet, the window says so. Visiting that area once in the real game downloads it.

## The app

Opening `Imlight.exe` (the desktop shortcut) shows the app in its own window. The window is drawn by the program itself (Avalonia), so it uses no web browser, no web view and no account. Closing the window stops the server. Everything is in one place:

- **Play**: play Wizard101 on the server on this computer (it starts the server first if needed), or on another private server you save by address and port. The live game is refused, by name (`wizard101.com`, `kingsisle.com`, `gameforge.com`) and by address. A server on this computer cannot use a port Imlight needs (login, game, patch, database, game files, which is also Aurorium's, or the app's own 12380 to 12399).
- **Command box**: paste an in-game command instead of typing it in game. It runs for the character the chosen account has logged in, with that account's permissions, and the reply shows in game. The server reads these from `Server\command-inbox`.
- **Server**: start, stop and restart the server, switch randomizer mode on or off, and read the log with repeated lines folded into one (`×30`) and debug lines hidden.
- **Settings**: change any setting in `Config\Imlight.ini`, with each setting's help text.
- **Accounts**: create accounts, change their level or password, and lock or unlock them (the server must be running).

## Shortcuts

| Shortcut | What it does |
| --- | --- |
| Imlight Server (or Imlight Manager) | Opens the app described above. |
| Start Imlight Server | Serves the game files and runs the server in a plain window. Close the window to stop it. |
| Update Imlight | Gets the newest setup if there is one, downloads the latest Imlight and rebuilds. `Config\Imlight.ini`, the logs and the player database are kept. |
| Uninstall Imlight | Removes Imlight, its shortcuts and its Apps entry. It asks before deleting accounts and characters. Wizard101 is not touched. |

Everything it does is written to `setup-log.txt` in the install folder. If setup fails, that file says why. Share it when asking for help.

## Options

```
ImlightSetup --dir <folder>          Install somewhere else.
             --game <folder>         Your Wizard101 folder, if setup can't find it.
             --type-dump <file>      Your own wiztype type dump, if the bundled one doesn't match your client.
             --repo <owner/repo>     Build a different GitHub repository.
             --branch <name>         Build a different branch.
             --yes --admin-password <text>   Install without asking anything.
             --panel                 Open the app (the installed Imlight.exe does this with no option).
             --update                Update Imlight (what the Update Imlight shortcut runs).
```

## Building it

`dotnet publish src/Imlight.Setup -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true`. Add `-p:ImlightEdition=Manager` for ImlightManager.exe. It only uses Imcodec's archive reader, so it needs no game files to build. The Release workflow attaches ImlightManager.exe to every release; ImlightSetup.exe is only built by hand.
