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
 * RANDOMIZER MODE
 * ========================================================================
 *
 * PURPOSE:
 * An optional server mode, configured in the [Randomizer] section of
 * Imlight.ini, that shuffles a character's school, the spells they learn,
 * the loot they get and the creatures in the world.
 *
 * USAGE EXAMPLE:
 * var spellId = Randomizer.MapSpell(wizard, trainerSpellId);
 * var itemId = Randomizer.MapDrop(wizard, droppedItemId);
 *
 * NOTE:
 * - Swaps stay balanced by only trading within a bucket: spells keep their
 *   type and pip cost, gear keeps its slot, school and level range, treasure
 *   cards keep their type and pip cost, creatures keep their level range.
 * - Spell and loot swaps are fixed per character (the same spell always
 *   becomes the same spell), and never map two IDs to one, so prerequisite
 *   chains at trainers still line up.
 * - The pools are built in the background on boot; a swap asked for before
 *   then waits for them, so no character ever gets an unswapped spell.
 *
 * TODO:
 *
 * Version: KALI 1.0
 * Last Updated: 10/18/2026
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Imcodec.ObjectProperty.TypeCache;
using Imlight.Common;
using Imlight.CoreLib.Shared.Behaviors;
using Imlight.CoreLib.Shared.Resources;
using Imlight.CoreLib.WizardData;
using Imlight.CoreLib.WizardData.Models.Player;

namespace Imlight.CoreLib.Game.Randomization;

/// <summary>
/// The randomizer server mode.
/// </summary>
public static class Randomizer {

    private static readonly MagicSchool[] s_primarySchools = [
        MagicSchool.Fire, MagicSchool.Ice, MagicSchool.Storm, MagicSchool.Myth,
        MagicSchool.Life, MagicSchool.Death, MagicSchool.Balance,
    ];

    // Gear that is safe to swap. Quest items, keys and housing are left alone so quests still work.
    private static readonly HashSet<ObjectType> s_gearTypes = [
        ObjectType.OT_EQUIP_HEAD, ObjectType.OT_EQUIP_CHEST, ObjectType.OT_EQUIP_LEGS,
        ObjectType.OT_EQUIP_HANDS, ObjectType.OT_EQUIP_FINGER, ObjectType.OT_EQUIP_FEET,
        ObjectType.OT_EQUIP_EAR, ObjectType.OT_PET, ObjectType.OT_HORSE,
    ];

    internal static readonly bool Enabled
        = ConfigurationManager.GetValue("Randomizer.Enabled", false);
    internal static readonly bool RandomizeSchool
        = Enabled && ConfigurationManager.GetValue("Randomizer.RandomizeSchool", true);
    internal static readonly bool RandomizeSpells
        = Enabled && ConfigurationManager.GetValue("Randomizer.RandomizeSpells", true);
    internal static readonly bool RandomizeDrops
        = Enabled && ConfigurationManager.GetValue("Randomizer.RandomizeDrops", true);
    internal static readonly bool RandomizeCreatures
        = Enabled && ConfigurationManager.GetValue("Randomizer.RandomizeCreatures", false);

    private static readonly bool s_sameForEveryone
        = ConfigurationManager.GetValue("Randomizer.SameForEveryone", false);
    private static readonly ulong s_seed
        = SeededShuffle.Fnv1a(ConfigurationManager.GetValue("Randomizer.Seed", ""));
    private static readonly int s_levelRange
        = Math.Max(1, ConfigurationManager.GetValue("Randomizer.LevelRange", 10));

    private static readonly Lazy<SeededShuffle> s_spells = new(BuildSpellPool, LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly Lazy<SeededShuffle> s_drops = new(BuildDropPool, LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly Lazy<SeededShuffle> s_creatures = new(BuildCreaturePool, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Builds the swap pools in the background so the first swap rarely has to wait for them.
    /// </summary>
    public static void StartBackgroundBuild() {
        if (!Enabled) {
            return;
        }

        Logger.Information("Randomizer mode is on. School: {School}, spells: {Spells}, drops: {Drops}, creatures: {Creatures}.",
            Logger.Args(RandomizeSchool, RandomizeSpells, RandomizeDrops, RandomizeCreatures));

        _ = Task.Run(() => {
            try {
                if (RandomizeSpells) {
                    Logger.Information("Randomizer can swap {Count} spells.", Logger.Args(s_spells.Value.Count));
                }
                if (RandomizeDrops) {
                    Logger.Information("Randomizer can swap {Count} drops.", Logger.Args(s_drops.Value.Count));
                }
                if (RandomizeCreatures) {
                    Logger.Information("Randomizer can swap {Count} creatures.", Logger.Args(s_creatures.Value.Count));
                }
            }
            catch (Exception ex) {
                Logger.Error("Randomizer could not build its pools; nothing will be swapped. {Exception}", Logger.Args(ex));
            }
        });
    }

    /// <summary>
    /// Picks the school for a new character. Returns <paramref name="chosen"/> when school randomizing is off.
    /// </summary>
    internal static MagicSchool PickSchool(MagicSchool chosen)
        => RandomizeSchool
            ? s_primarySchools[Random.Shared.Next(s_primarySchools.Length)]
            : chosen;

    /// <summary>
    /// Returns the spell a wizard really learns in place of <paramref name="spellTemplateId"/>.
    /// </summary>
    internal static uint MapSpell(Wizard wizard, uint spellTemplateId)
        => RandomizeSpells && wizard is not null
            ? (uint) s_spells.Value.Map(spellTemplateId, SeedFor(wizard, "spells"))
            : spellTemplateId;

    /// <summary>
    /// Returns the item or treasure card a wizard really gets in place of a dropped <paramref name="templateId"/>.
    /// </summary>
    internal static ulong MapDrop(Wizard wizard, ulong templateId)
        => RandomizeDrops && wizard is not null
            ? s_drops.Value.Map(templateId, SeedFor(wizard, "drops"))
            : templateId;

    /// <summary>
    /// Returns the creature that spawns in place of <paramref name="templateId"/>. The same for every player.
    /// </summary>
    internal static ulong MapCreature(ulong templateId)
        => RandomizeCreatures
            ? s_creatures.Value.Map(templateId, SeededShuffle.Mix(s_seed ^ SeededShuffle.Fnv1a("creatures")))
            : templateId;

    private static ulong SeedFor(Wizard wizard, string what) {
        var seed = s_seed ^ SeededShuffle.Fnv1a(what);
        if (!s_sameForEveryone) {
            seed ^= SeededShuffle.Mix(wizard.CharId);
        }

        return SeededShuffle.Mix(seed);
    }

    // ---------------------------------------------------------------------
    // Pools
    // ---------------------------------------------------------------------

    private static SeededShuffle BuildSpellPool() {
        // Only spells some trainer teaches: they are known to be learnable by players.
        var entries = new List<(ulong, string)>();
        var trainerSpellIds = SpiralDB.NpcSpellInventories.Values
            .Where(inventory => inventory?.Spells is not null)
            .SelectMany(inventory => inventory.Spells)
            .Select(entry => entry.TemplateID)
            .Distinct();

        foreach (var id in trainerSpellIds) {
            if (CoreObjectFactory.GetCoreTemplate(id) is SpellTemplate spell && !spell.m_Treasure) {
                entries.Add((id, SpellBucket(spell)));
            }
        }

        return new SeededShuffle(entries);
    }

    private static SeededShuffle BuildDropPool() {
        // Everything any drop table can give, limited to gear and treasure cards.
        var entries = new List<(ulong, string)>();
        var dropIds = SpiralDB.DropTables.Values
            .Where(table => table?.Items is not null)
            .SelectMany(table => table.Items)
            .Select(item => ulong.TryParse(item.ItemId, out var id) ? id : 0)
            .Where(id => id != 0)
            .Distinct();

        foreach (var id in dropIds) {
            switch (CoreObjectFactory.GetCoreTemplate(id)) {
                case WizItemTemplate item when s_gearTypes.Contains(item.m_nObjectType): {
                    string school = item.m_school;
                    entries.Add((id, $"gear:{item.m_nObjectType}:{school}:{LevelBand(EquipLevel(item))}"));

                    break;
                }
                case SpellTemplate spell when spell.m_Treasure:
                    entries.Add((id, "tc:" + SpellBucket(spell)));

                    break;
            }
        }

        return new SeededShuffle(entries);
    }

    private static SeededShuffle BuildCreaturePool() {
        var entries = new List<(ulong, string)>();
        foreach (var id in Imlight.CoreLib.Game.Zone.AprilFools.CreatureList.TemplateIDs) {
            if (CoreObjectFactory.GetCoreTemplate(id) is not GameObjectTemplate template) {
                continue;
            }

            var npc = template.m_behaviors.OfType<NPCBehaviorTemplate>().FirstOrDefault();
            if (npc is null || !template.m_behaviors.OfType<DuelistBehaviorTemplate>().Any()) {
                continue;
            }

            // Bosses only swap with bosses.
            var rank = npc.m_bossMob ? "boss" : "mob";
            entries.Add((id, $"{rank}:{LevelBand(npc.m_nLevel)}"));
        }

        return new SeededShuffle(entries);
    }

    private static string SpellBucket(SpellTemplate spell) {
        string type = spell.m_sTypeName;
        var rank = spell.m_spellRank;
        var pips = rank is null ? "?" : rank.m_xPipSpell ? "X" : rank.m_spellRank.ToString();

        return $"{type}:{pips}";
    }

    private static int EquipLevel(ItemTemplate item)
        => (int) (item.m_equipRequirements?.m_requirements?
            .OfType<ReqMagicLevel>()
            .Select(req => req.m_numericValue)
            .DefaultIfEmpty(0)
            .Max() ?? 0);

    private static int LevelBand(int level)
        => Math.Max(0, level) / s_levelRange;

}
