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

using Imlight.Common;
using Imlight.CoreLib.WizardData.Collections;

namespace Imlight.CoreLib.Game.Commands.Protocols;

/// <summary>
/// .setpassword: anyone sets their own account's password, such as an account Create Account on Login made.
/// </summary>
internal class CommandSetPasswordProtocol : CommandProtocol {

    internal override string Group { get; set; } = "";

    [Command("setpassword")]
    private void SetPasswordCommand(string newPassword = "", string newPasswordConfirm = "") {
        var account = Context.Account;
        if (account is null) {
            return;
        }

        if (newPassword.Length == 0 || newPasswordConfirm.Length == 0) {
            InformSenderClient("Type your new password twice, like: .setpassword mypass mypass");

            return;
        }

        if (newPassword != newPasswordConfirm) {
            InformSenderClient("The two passwords are not the same. Try again: .setpassword mypass mypass");

            return;
        }

        if (!AccountCollection.ChangePassword(account.AccountId, newPassword)) {
            InformSenderClient("Your password could not be changed. Try again in a moment.");

            return;
        }

        account.PasswordNotSet = false;
        Logger.Information("Account {0} set its own password.", Logger.Args(account.Username));
        InformSenderClient("Your password is set. Log in with it from now on, on any computer.");
    }

}
