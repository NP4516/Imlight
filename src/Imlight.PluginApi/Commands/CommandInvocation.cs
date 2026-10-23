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
 * Created by: phazejeff
 * Version: KALI 1.0
 * Last Updated: 10/23/2026
 */

namespace Imlight.PluginApi.Commands;

/// <summary>
/// One use of a plugin command.
/// </summary>
public sealed class CommandInvocation(IPlayer player, string name, string[] args) {

    /// <summary>
    /// The player who used the command.
    /// </summary>
    public IPlayer Player { get; } = player;

    /// <summary>
    /// The name or alias the player typed, in lower case.
    /// </summary>
    public string Name { get; } = name;

    /// <summary>
    /// The words after the command name, with empty entries removed.
    /// </summary>
    public string[] Args { get; } = args;

    /// <summary>
    /// The arguments joined back into one string, for commands that take free text.
    /// </summary>
    public string RawArgs => string.Join(' ', Args);

    /// <summary>
    /// Shows a server message to the player who used the command.
    /// </summary>
    public void Reply(string message) => Player.SendMessage(message);

}

/// <summary>
/// The account level needed to use a command. Higher levels include the ones below them.
/// </summary>
public enum CommandPermission {

    None,
    QualityAssurance,
    HallMonitor,
    Developer,
    Administrator

}
