# ImlightSetup.exe

A one-click installer for **original Imlight** ([Revive101/Imlight](https://github.com/Revive101/Imlight)), for people with no development tools.

## What it does

1. **Finds Wizard101** in the usual places (KingsIsle or Steam). If it can't, it asks for the folder.
2. **Installs the .NET 10 SDK** into the Imlight folder if the computer doesn't have it. Nothing else on the computer changes.
3. **Downloads the latest Imlight** and the exact Imcodec version it uses from GitHub. Git is not needed.
4. **Reads the game files Imcodec needs** (`*Messages*.xml`) straight out of the player's own `Root.wad`, and uses the type dump Imcodec ships. Nothing copyrighted is downloaded or shared.
5. **Builds the server** into `%LOCALAPPDATA%\ImlightServer\Server`.
6. **Asks for an admin account and the patch server address** on a first install.
7. **Adds shortcuts**: Start Imlight Server (desktop and Start menu), Server Settings, Server Logs, Setup Log and Update Imlight.

Running it again, or clicking **Update Imlight**, downloads the latest Imlight and rebuilds. `Config\Imlight.ini`, the logs and the player database are kept.

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
