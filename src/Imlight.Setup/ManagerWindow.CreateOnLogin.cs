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
using System.Collections.Generic;
using Avalonia.Controls;

namespace Imlight.Setup;

/// <summary>
/// Home's "Create Account on Login" switch (Niduus, 2026-10-27): [Login Server] CreateAccountOnLogin, live.
/// </summary>
internal sealed partial class ManagerWindow {

    private const string CREATE_ON_LOGIN_SECTION = "Login Server";
    private const string CREATE_ON_LOGIN_KEY = "CreateAccountOnLogin";

    private readonly ToggleSwitch _createOnLogin = new() { OnContent = "On", OffContent = "Off" };
    private readonly TextBlock _createOnLoginNote = Sub("");

    private Control HomeCreateOnLogin() {
        _createOnLogin.IsEnabled = false;
        _createOnLogin.IsCheckedChanged += async (_, _) => {
            if (!_updatingRandomizer) {
                await Guard(() => SetSwitchAsync(CREATE_ON_LOGIN_SECTION, CREATE_ON_LOGIN_KEY, "Create Account on Login",
                    _createOnLogin.IsChecked == true));
            }
        };

        return Fold("users", "Accounts", "Create Account on Login",
            "Let new players make their own account just by logging in.",
            SwitchRow("Create Account on Login", _createOnLogin,
                "On: logging in with a username that has no account makes that account and lets the player in. " +
                "Off: unknown usernames are turned away. A wrong password for an account that already exists is always turned away. " +
                "Works right away, no restart."),
            Sub("The game never sends the server the password itself, so a new account has no password yet. It works only on the " +
                "computer it was made on, until the player types .setpassword mypass mypass in game (or you give it one on the " +
                "Accounts tab). After that it needs that password like any account."),
            Sub("While this is on, anyone who can reach your server can make accounts. Every account made this way is written in the server log."),
            _createOnLoginNote);
    }

    private void ShowCreateOnLogin(Dictionary<(string Section, string Key), string> ini) {
        var has = ini.TryGetValue((CREATE_ON_LOGIN_SECTION, CREATE_ON_LOGIN_KEY), out var value);
        _createOnLogin.IsEnabled = has;
        _createOnLogin.IsChecked = has && value!.Equals("true", StringComparison.OrdinalIgnoreCase);
        _createOnLoginNote.Text = has ? "" : "Click \"Update Ahziael\" at the top of the app to get this switch.";
        _createOnLoginNote.IsVisible = !has;
    }

}
