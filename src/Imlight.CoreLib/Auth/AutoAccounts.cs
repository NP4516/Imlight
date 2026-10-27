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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography;
using Imlight.Common;
using Imlight.CoreLib.WizardData;
using Imlight.CoreLib.WizardData.Collections;
using Imlight.CoreLib.WizardData.Models.Player;

namespace Imlight.CoreLib.Auth;

/// <summary>
/// [Login Server] CreateAccountOnLogin (Niduus, 2026-10-27): logging in with a username that has no account makes it.
/// </summary>
/// <remarks>
/// The game sends a hash of the password salted with the session, never the password, so the server cannot learn
/// it. A made account starts with a random password nobody knows and remembers the computer it was made on; that
/// computer gets in until the player types .setpassword, and from then on the password is checked like any account's.
/// </remarks>
internal static class AutoAccounts {

    internal const int MAX_USERNAME = 32;

    internal static bool On => ConfigurationManager.GetValue("Login Server.CreateAccountOnLogin", false);

    // Accounts that logged in without a password, to remind on their next arrival in the world.
    private static readonly ConcurrentDictionary<ulong, byte> s_toRemind = new();

    /// <summary>Makes the account for a login with an unknown username; null when it cannot be made.</summary>
    internal static Account Create(string username, ulong machineId, string ip) {
        if (string.IsNullOrWhiteSpace(username) || username.Length > MAX_USERNAME) {
            return null;
        }

        var unknown = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        var account = new Account(username, "", DatabaseUtilities.CreateHashedPassword(unknown)) {
            AuthLevel = AuthLevel.None,
            PasswordNotSet = true,
            FirstLoginMachineId = machineId,
        };

        if (AccountCollection.CreateAccount(account)) {
            Logger.Information("Create Account on Login: made account {0} for a new player (from {1}).",
                Logger.Args(username, ip));
        }

        // Two logins at once: the other one made it; both use the stored one.
        return AccountCollection.GetAccountForCharacterList(username);
    }

    /// <summary>A login to an account with no password yet: only from the computer that made it.</summary>
    internal static bool LetsIn(Account account, ulong machineId)
        => account.PasswordNotSet && account.FirstLoginMachineId == machineId;

    internal static void LoggedInWithoutPassword(ulong accountId) => s_toRemind[accountId] = 0;

    /// <summary>The reminder a wizard arriving in the world is told, once per login, while the account has no password.</summary>
    internal static List<string> GreetingFor(ulong accountId) {
        if (!s_toRemind.TryRemove(accountId, out _)) {
            return [];
        }

        return [
            "Your account was made when you first logged in, but it has no password yet, so it only works on this computer.",
            "Type .setpassword followed by your password twice, like: .setpassword mypass mypass",
        ];
    }

}
