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

namespace Imlight.PluginApi;

/// <summary>
/// A player in the game.
/// </summary>
/// <remarks>
/// An IPlayer belongs to one game session, and a player gets a new session on every zone change.
/// Store <see cref="CharacterId"/> rather than the IPlayer itself.
/// </remarks>
public interface IPlayer {

    /// <summary>
    /// The wizard's character ID.
    /// </summary>
    ulong CharacterId { get; }

    /// <summary>
    /// The wizard's display name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The zone the wizard is in, or null while the wizard is between zones.
    /// </summary>
    IZone? Zone { get; }

    /// <summary>
    /// Shows a server message to the player. Safe to call from any thread.
    /// </summary>
    void SendMessage(string message);

}
