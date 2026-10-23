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

namespace Imlight.PluginApi.Events;

/// <summary>
/// Fired when a player says something in zone chat, before it is logged and shown to the zone.
/// Cancelling it drops the message. Whispers, group chat, quick chat and chat commands don't fire it.
/// </summary>
public sealed class PlayerChatEvent(IPlayer player, string message) : ImlightEvent, ICancellable {

    /// <summary>
    /// The player who sent the message.
    /// </summary>
    public IPlayer Player { get; } = player;

    /// <summary>
    /// The message, with unsupported characters already removed.
    /// </summary>
    public string Message { get; } = message;

    /// <inheritdoc />
    public bool IsCancelled { get; set; }

}

/// <summary>
/// Fired when a player has been added to a zone: on first login to a wizard, and after every zone change.
/// </summary>
public sealed class PlayerZoneEnterEvent(IPlayer player, string zone) : ImlightEvent {

    /// <summary>
    /// The player who entered the zone.
    /// </summary>
    public IPlayer Player { get; } = player;

    /// <summary>
    /// The internal name of the zone.
    /// </summary>
    public string Zone { get; } = zone;

}
