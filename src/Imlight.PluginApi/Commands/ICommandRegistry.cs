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
 * COMMAND REGISTRY
 * ========================================================================
 *
 * PURPOSE:
 * Lets a plugin add its own chat commands alongside the server's.
 *
 * USAGE EXAMPLE:
 * public override void OnEnable()
 *     => Commands.Register("roll", c => c.Reply($"{c.Player.Name} rolled {Random.Shared.Next(1, 101)}"));
 *
 * Created by: phazejeff
 * Version: KALI 1.0
 * Last Updated: 10/23/2026
 */

using System;

namespace Imlight.PluginApi.Commands;

/// <summary>
/// Registers chat commands for a plugin.
/// </summary>
/// <remarks>
/// Every command, the server's and every plugin's, runs on a single dispatcher actor. A slow handler
/// stalls commands for every player on the server, not just the sender: return quickly and never block.
/// </remarks>
public interface ICommandRegistry {

    /// <summary>
    /// Adds a command. Names and aliases are single words and are matched case-insensitively.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// A name is empty or contains whitespace, or is already used by the server or another plugin.
    /// Nothing is registered when this is thrown.
    /// </exception>
    void Register(string name, Action<CommandInvocation> handler,
                  CommandPermission permission = CommandPermission.None, params string[] aliases);

}
