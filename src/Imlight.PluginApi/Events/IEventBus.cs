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

using System;

namespace Imlight.PluginApi.Events;

/// <summary>
/// Delivers events to plugin handlers.
/// </summary>
/// <remarks>
/// Handlers run synchronously, in the order they subscribed, on the thread of the actor that fired the
/// event. That is what lets a handler cancel an action before it happens, but it also means a slow
/// handler stalls that player's session: return quickly and never block.
/// </remarks>
public interface IEventBus {

    /// <summary>
    /// Calls the handler for every event of type <typeparamref name="TEvent"/>.
    /// </summary>
    void Subscribe<TEvent>(Action<TEvent> handler) where TEvent : ImlightEvent;

}
