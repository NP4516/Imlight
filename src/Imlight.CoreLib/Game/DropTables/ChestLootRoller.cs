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
 * CHEST LOOT ROLLER
 * ========================================================================
 *
 * PURPOSE:
 * Finds and rolls the loot of a chest from the NpcDropTable of its template id, the same data mobs use.
 *
 * USAGE EXAMPLE:
 * if (ChestLootRoller.HasLootTable(templateId)) {
 *     var rolled = ChestLootRoller.Roll(templateId, playerRef, playerObject, wizard);
 * }
 *
 * NOTE:
 * A chest without a complete loot table is inert. A table name that is not in the drop tables is
 * reported once per name.
 *
 * TODO:
 *
 * Created by: Jay
 * Version: KALI 1.0
 * Last Updated: 10/14/2026
 */

using System.Collections.Concurrent;
using Akka.Actor;
using Imcodec.ObjectProperty.TypeCache;
using Imlight.Common;
using Imlight.CoreLib.WizardData.Collections;
using Imlight.CoreLib.WizardData.Models.Player;
using Imlight.CoreLib.WizardData.Models.World;

namespace Imlight.CoreLib.Game.DropTables;

/// <summary>
/// Rolls the loot of a chest template through its NpcDropTable.
/// </summary>
internal static class ChestLootRoller {

    private static readonly ConcurrentDictionary<string, byte> s_reportedTableNames = new();

    internal static bool HasLootTable(ulong chestTemplateId) {
        if (!NpcDropTableCollection.TryGetDropTable(chestTemplateId, out var npcDropTable)
            || npcDropTable.DropTableNames is not { Count: > 0 } tableNames) {
            return false;
        }

        var allResolve = true;
        foreach (var tableName in tableNames) {
            if (!string.IsNullOrWhiteSpace(tableName) && DropTableCollection.GetDropTable(tableName) is not null) {
                continue;
            }

            allResolve = false;
            if (s_reportedTableNames.TryAdd(tableName ?? "", 0)) {
                Logger.Warning("Chest {Template} names the drop table {Table}, which is not in the drop tables",
                    Logger.Args(chestTemplateId, tableName));
            }
        }

        return allResolve;
    }

    internal static DropTableResult Roll(ulong chestTemplateId, IActorRef playerRef, CoreObject playerObj, Wizard wizard) {
        if (!NpcDropTableCollection.TryGetDropTable(chestTemplateId, out var npcDropTable)) {
            return new DropTableResult();
        }

        return DropTableRoller.Roll([.. npcDropTable.DropTableNames], playerRef, playerObj, wizard);
    }

}
