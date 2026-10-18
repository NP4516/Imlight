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
 * COMMAND INBOX
 * ========================================================================
 *
 * PURPOSE:
 * Runs in-game commands sent from the Imlight app on this computer, so they
 * can be pasted instead of typed in game chat. The app drops a small JSON file
 * ({"Username": "...", "Command": "mod additem 123"}) into the inbox folder;
 * the command runs for that account's character exactly as if the player had
 * typed it, with the same permission checks, and the replies show in game.
 * A result file with the same name and ".result" before ".json" says whether
 * it was handed to the character.
 *
 * NOTE:
 * Only someone who can write files in the server folder can use it.
 */

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Akka.Actor;
using Imcodec.MessageLayer.Generated;
using Imlight.Common;
using Imlight.CoreLib.Shared.Packets;
using Imlight.CoreLib.WizardData.Collections;

namespace Imlight.CoreLib.Game.Commands;

public static class CommandInbox {

    private const string RESULT_SUFFIX = ".result.json";
    private static readonly TimeSpan s_pollInterval = TimeSpan.FromMilliseconds(500);

    private static Timer s_timer;
    private static ActorSystem s_system;
    private static string s_folder;
    private static int s_busy;

    private sealed record Request(string Username, string Command);

    private sealed record Result(bool Ok, string Message);

    /// <summary>
    /// Starts watching the inbox folder. Requests left from an earlier run are dropped.
    /// </summary>
    public static void Start(ActorSystem system, string folder) {
        s_system = system;
        s_folder = Path.GetFullPath(folder);
        Directory.CreateDirectory(s_folder);
        foreach (var stale in Directory.EnumerateFiles(s_folder, "*.json")) {
            TryDelete(stale);
        }

        s_timer = new Timer(_ => Poll(), null, s_pollInterval, s_pollInterval);
        Logger.Information("Commands from the Imlight app are read from {0}.", Logger.Args(s_folder));
    }

    private static void Poll() {
        // Skip a tick while the previous one is still working.
        if (Interlocked.Exchange(ref s_busy, 1) == 1) {
            return;
        }

        try {
            foreach (var file in Directory.EnumerateFiles(s_folder, "*.json")
                         .Where(f => !f.EndsWith(RESULT_SUFFIX, StringComparison.OrdinalIgnoreCase))
                         .ToList()) {
                // Taken out of the inbox before it runs, so a command never runs twice.
                string text;
                try {
                    text = File.ReadAllText(file);
                    File.Delete(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
                    continue;
                }

                var result = Handle(text);
                File.WriteAllText(file[..^".json".Length] + RESULT_SUFFIX, JsonSerializer.Serialize(result));
            }
        }
        catch (Exception ex) {
            Logger.Warning("The command inbox could not be read: {0}", Logger.Args(ex.Message));
        }
        finally {
            Interlocked.Exchange(ref s_busy, 0);
        }
    }

    private static Result Handle(string text) {
        Request request;
        try {
            request = JsonSerializer.Deserialize<Request>(text);
        }
        catch (JsonException) {
            return new Result(false, "The request could not be read.");
        }

        var command = request?.Command?.Trim().TrimStart('.', '/').Trim();
        if (string.IsNullOrEmpty(command) || string.IsNullOrWhiteSpace(request.Username)) {
            return new Result(false, "Choose an account and type a command.");
        }

        var account = AccountCollection.GetAccount(request.Username.Trim());
        if (account is null) {
            return new Result(false, $"There is no account named {request.Username}.");
        }

        var player = OnlinePlayerCollection.GetOnlinePlayers().FirstOrDefault(p => p.AccountId == account.AccountId);
        if (player?.ActorPath is null) {
            return new Result(false, $"{account.Username} is not in game. Log in with a character first.");
        }

        // Handed to the player's session as if their game had sent it, so it gets the same checks.
        s_system.ActorSelection(player.ActorPath).Tell(new SERVER_100_PROTOCOL.MSG_RECEIVEDPACKET {
            Packet = new GAME_5_PROTOCOL.MSG_COMMAND { Command = command }
        });
        Logger.Information("{0} sent a command from the Imlight app: {1}", Logger.Args(account.Username, command));

        return new Result(true, $"Sent to {account.Username}'s character. The reply shows in game.");
    }

    private static void TryDelete(string file) {
        try {
            File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            // Picked up again on the next tick.
        }
    }

}
