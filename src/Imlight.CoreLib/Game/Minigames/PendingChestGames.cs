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
 * PENDING CHEST GAMES
 * ========================================================================
 *
 * PURPOSE:
 * Hands a silver chest game from the session that clicked the chest to the session that attaches to the
 * minigame zone, keyed by character.
 *
 * USAGE EXAMPLE:
 * if (PendingChestGames.TryAdd(context)) { ... }
 * if (PendingChestGames.TryTake(charId, out var context)) { ... }
 *
 * NOTE:
 * A zone transfer ends the session and the client attaches with a new one, so the services of the
 * clicking session are gone when the minigame zone is entered. The entries are runtime state and are lost
 * with the server; a character's next attach takes its entry out.
 *
 * TODO:
 *
 * Created by: Jay
 * Version: KALI 1.0
 * Last Updated: 10/14/2026
 */

using System.Collections.Concurrent;

namespace Imlight.CoreLib.Game.Minigames;

/// <summary>
/// The silver chest games that were started and whose minigame zone has not been attached yet.
/// </summary>
internal static class PendingChestGames {

    private static readonly ConcurrentDictionary<ulong, ChestGameContext> s_pending = new();

    internal static bool TryAdd(ChestGameContext context)
        => s_pending.TryAdd(context.CharId, context);

    internal static bool TryTake(ulong charId, out ChestGameContext context)
        => s_pending.TryRemove(charId, out context);

}
