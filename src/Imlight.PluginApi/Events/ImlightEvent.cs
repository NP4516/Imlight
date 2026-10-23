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
/// The base class of every event.
/// </summary>
public abstract class ImlightEvent { }

/// <summary>
/// An event whose action a handler can stop by setting <see cref="IsCancelled"/>.
/// </summary>
public interface ICancellable {

    /// <summary>
    /// True if the action this event describes will not happen.
    /// </summary>
    bool IsCancelled { get; set; }

}
