# Getting Started with the Imlight Server

## What you need

1. **The Wizard101 game client**, installed on this computer or another one.
2. **A patch server**, which hands game data to the server and to your game. [Aurorium](https://github.com/Revive101/Aurorium) is the free one made for Imlight. Its first start takes a while.

## Starting the server

1. Start your patch server first.
2. Open the Start menu, go to **Imlight**, and click **Start Imlight Server**. A window opens and fills with messages. The server is ready when it says `Imlight may now be connected to.`
3. Start the game with the launch option `-L 127.0.0.1 12000` and log in with the admin account you chose during setup.

Leave the server window open while you play. Closing it stops the server.

## Updates

**Start Imlight Server** opens the Imlight Launcher. Each time it starts, it checks the project's GitHub releases, updates itself and the server if a newer release is out, then starts the server. Your settings are always kept. While the server runs, the launcher checks again every 30 minutes and tells you when an update is waiting; it installs the next time you start it.

To change this, edit `launcher.json` in the Imlight folder (`%LOCALAPPDATA%\Imlight`). Setting `RestartServerForUpdates` to `true` makes the launcher restart the server as soon as an update comes out. That disconnects anyone playing, which is why it is off by default.

## Finding item, spell and mob IDs

The first time the server starts, it builds a list of every item, spell, mob and NPC with its ID. This takes a few minutes and only happens again when the game updates.

- Click **ID Lookup** in the Imlight Start menu folder. It opens in your browser. Type a name to search, then click a command to copy it.
- Or type these into the in-game chat:
  - `.lookup spell fire cat`
  - `.lookup item dragon`
  - `.lookup mob rattlebones`
- Then give yourself something with `.mod additem <id>` or `.sb learn <id>`.

## Changing settings

Click **Server Settings** in the Imlight Start menu folder. Save the file and restart the server for changes to apply. The `[Randomizer]` section turns randomizer mode on or off and picks what it shuffles.

## When something goes wrong

Click **Server Logs** in the Imlight Start menu folder and open the newest file. The lines marked `ERR` or `FTL` usually say what is wrong. Share that file when asking for help.

---

## For developers: building and releasing

On Windows, with the .NET 10 SDK, [Inno Setup 6](https://jrsoftware.org/isinfo.php), and the Imcodec generator inputs from the main README in place, run:

```powershell
powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1 -Version 1.0.0
```

The installer is written to `installer\output\ImlightSetup.exe`.

### Releasing an update

Push a version tag, for example `git tag v1.2.0 && git push origin v1.2.0`. The Release workflow (`.github/workflows/release.yml`) builds the launcher and publishes the GitHub release, and every installed launcher picks it up on its next start.

The server can only be built in GitHub Actions if the `IMCODEC_GENERATOR_INPUTS` repository secret is set; the top of the workflow file explains what goes in it. Without it, the release holds only the launcher, and you attach `ImlightServer-win-x64.zip` yourself: zip the contents of `installer\publish\server` after running the build script above.
