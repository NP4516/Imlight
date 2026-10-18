# ImlightSetup.exe

A one-click installer for **original Imlight** ([Revive101/Imlight](https://github.com/Revive101/Imlight)), for people with no development tools.

## What it does

1. **Finds Wizard101** in the usual places (KingsIsle or Steam). If it can't, it asks for the folder.
2. **Installs the .NET 10 SDK** into the Imlight folder if the computer doesn't have it. Nothing else on the computer changes.
3. **Downloads the latest Imlight** and the exact Imcodec version it uses from GitHub. Git is not needed.
4. **Reads the game files Imcodec needs** (`*Messages*.xml`) straight out of the player's own `Root.wad`, and uses the type dump Imcodec ships. Nothing copyrighted is downloaded or shared.
5. **Builds the server** into `%LOCALAPPDATA%\ImlightServer\Server`.
6. **Asks for an admin account** on a first install.
7. **Installs `Imlight.exe` and adds shortcuts**: Start Imlight Server and Play Wizard101 on Imlight (desktop and Start menu), plus Server Settings, Server Logs, Setup Log, Update Imlight and Uninstall Imlight in the Start menu. Imlight Server is also listed under **Settings > Apps**, so it can be uninstalled from there.

## No patch server needed

Imlight reads game files from a patch server. On a home computer, **Start Imlight Server** runs a small one built into `Imlight.exe` that hands the server the files from the player's own Wizard101 folder (`Data\GameData`), then starts the server. Nothing is downloaded and nothing leaves the computer. If something else (such as Aurorium) already uses the patch server port, Imlight uses that instead.

If the server asks for an area's file that the player's game hasn't downloaded yet, the window says so. Visiting that area once in the real game downloads it.

## Shortcuts

| Shortcut | What it does |
| --- | --- |
| Start Imlight Server | Serves the game files and runs the server. Close the window to stop it. |
| Play Wizard101 on Imlight | Starts Wizard101 connected to the server on this computer (`-L 127.0.0.1 12000`). |
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
```

## Building it

`dotnet publish src/Imlight.Setup -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true`. It only uses Imcodec's archive reader, so it needs no game files to build. The Release workflow attaches it to every release.
