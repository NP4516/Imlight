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
using System;
using System.Linq;
using Akka.Actor;
using Imlight.CoreLib.Shared.Packets;
using Imlight.CoreLib.WizardData.Models.Player;

namespace Imlight.CoreLib.Game.Commands.Protocols;

/// <summary>
/// `.c1 Fire Cat` and `.c2 Fire Cat` (were .daybreaker/.db and .nightbringer/.nb; Niduus renamed them 2026-10-20): in
/// battle, casts a spell for free this round, before (c1) or after (c2) your own card. `.cf` casts it before anyone's
/// turn and `.cl` after everyone's (Niduus, 2026-10-22: they were the f and l options of .c1/.c2; `.cl se all pwn`).
/// Type it several times to chain spells.
/// </summary>
internal class CommandDelayCastProtocol : CommandProtocol {

    internal override string Group { get; set; } = "";

    [Command("c1")]
    [AuthRequired(AuthLevel.QualityAssurance)]
    private void DaybreakerCommand([Remainder] string spell) => QueueAll(spell, atStart: true, 'n', skip: false);

    [Command("c1s")]
    [AuthRequired(AuthLevel.QualityAssurance)]
    private void FirstSkipCommand([Remainder] string spell) => QueueAll(spell, atStart: true, 'n', skip: true);

    [Command("c2s")]
    [AuthRequired(AuthLevel.QualityAssurance)]
    private void AfterSkipCommand([Remainder] string spell) => QueueAll(spell, atStart: false, 'n', skip: true);

    [Command("c2")]
    [AuthRequired(AuthLevel.QualityAssurance)]
    private void NightbringerCommand([Remainder] string spell) => QueueAll(spell, atStart: false, 'n', skip: false);

    // .cf / .cl (Niduus, 2026-10-22): the round's first queue (before anyone's turn) and last one (after everyone's).
    // Each has one queue per caster, kept as the c1 side of the f or l queue.
    [Command("cf")]
    [AuthRequired(AuthLevel.QualityAssurance)]
    private void RoundFirstCommand([Remainder] string spell) => QueueAll(spell, atStart: true, 'f', skip: false);

    [Command("cfs")]
    [AuthRequired(AuthLevel.QualityAssurance)]
    private void RoundFirstSkipCommand([Remainder] string spell) => QueueAll(spell, atStart: true, 'f', skip: true);

    [Command("cl")]
    [AuthRequired(AuthLevel.QualityAssurance)]
    private void RoundLastCommand([Remainder] string spell) => QueueAll(spell, atStart: true, 'l', skip: false);

    [Command("cls")]
    [AuthRequired(AuthLevel.QualityAssurance)]
    private void RoundLastSkipCommand([Remainder] string spell) => QueueAll(spell, atStart: true, 'l', skip: true);

    /// <summary>
    /// Several spells in one line, split by commas; each part can start with c1, c2, cf or cl (or c1s, c2s, cfs, cls)
    /// to pick its queue, else it goes in the typed command's queue (Niduus, 2026-10-20:
    /// `.c1 Tower Shields, c2 Pyrodynamics`).
    /// </summary>
    private void QueueAll(string spells, bool atStart, char when, bool skip) {
        // A ca part (Niduus, 2026-10-20: `.c1 Fire Cat, ca 1, Fire Cat`) goes in the queue of the part before it,
        // and shows just before the spell after it.
        var lastAtStart = atStart;
        var lastWhen = when;
        foreach (var part in (spells ?? "").Split(',', StringSplitOptions.TrimEntries)) {
            var words = part.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var first = words.Length > 0 ? words[0].TrimStart('.').ToLowerInvariant() : "";
            if (TryParseQueue(first, out var partAtStart, out var partWhen, out var partSkip)) {
                lastAtStart = partAtStart;
                lastWhen = partWhen;
                Queue(words.Length > 1 ? words[1] : "", partAtStart, partWhen, partSkip);
            } else if (first == "ca") {
                QueueAnnouncement(words.Length > 1 ? words[1] : "", lastAtStart, lastWhen);
            } else {
                lastAtStart = atStart;
                lastWhen = when;
                Queue(part, atStart, when, skip);
            }
        }
    }

    // A chain part's c1, c2, cf or cl, each with or without s.
    private static bool TryParseQueue(string word, out bool atStart, out char when, out bool skip) {
        atStart = true;
        when = 'n';
        skip = word.Length == 3 && word[2] == 's';
        if (word.Length is < 2 or > 3 || word[0] != 'c' || word.Length == 3 && !skip) {
            return false;
        }

        switch (word[1]) {
            case '1':
                return true;
            case '2':
                atStart = false;
                return true;
            case 'f':
            case 'l':
                when = word[1];
                return true;
            default:
                return false;
        }
    }

    // The command that types a queue, for messages.
    private static string CommandName(bool atStart, char when)
        => when switch { 'f' => ".cf", 'l' => ".cl", _ => atStart ? ".c1" : ".c2" };

    private void QueueAnnouncement(string phrase, bool atStart, char when) {
        if (!CommandAnnounceProtocol.TryResolve(phrase, out var key, out var shown, out var problem)) {
            InformSenderClient(phrase.Trim().Length == 0 ? "Put a phrase number or words after ca, such as .c1 Fire Cat, ca 1." : problem);

            return;
        }

        Context.SessionActor.Tell(new COMBAT_106_PROTOCOL.MSG_CHEATDELAYCAST {
            AtStart = atStart,
            When = when,
            Announcement = key,
            AnnouncementShown = shown,
        });
    }

    /// <summary>
    /// `.cX [s/e] [target] spell` (Niduus, 2026-10-20): s skips the casting animation, e adds the best enchantment
    /// that fits (Epic, Sharpened Blade...); they mix, like se. First and last in the round are their own commands,
    /// .cf and .cl (Niduus, 2026-10-22). The target (Niduus, 2026-10-22) is p1-p4 for the circles of your own side in
    /// the game's order, e1-e4 for the enemy side's, me (or self) for yourself, or all (a single-target spell then
    /// casts once on each enemy, or each teammate). s is only ever the silent option, never a target. A number after
    /// the spell repeats it.
    /// </summary>
    private void Queue(string spell, bool atStart, char when, bool skip) {
        var words = (spell ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        var enchant = false;
        var command = CommandName(atStart, when);

        if (words.Count > 1 && words[0].ToLowerInvariant() is var options && options.Length <= 4
            && options.Distinct().Count() == options.Length && options.All(c => c is 's' or 'f' or 'l' or 'e')) {
            // f and l were options of .c1/.c2 until they became .cf and .cl.
            if (options.Contains('f') || options.Contains('l')) {
                var kept = new string([.. options.Where(c => c is 's' or 'e')]);
                var example = string.Join(' ', words.Skip(1).Prepend(kept).Where(w => w.Length > 0));
                var use = options.Contains('f') && options.Contains('l')
                    ? $"Pick one: .cf {example} or .cl {example}."
                    : $"Try {(options.Contains('f') ? ".cf" : ".cl")} {example}.";
                InformSenderClient($"f and l are their own commands now: .cf casts first (before anyone's turn) and .cl last (after everyone's turn). {use}");

                return;
            }

            skip |= options.Contains('s');
            enchant = options.Contains('e');
            words.RemoveAt(0);
        }

        string target = null;
        if (words.Count > 1 && words[0].ToLowerInvariant() is var code
            && (code is "all" or "me" or "self"
                || code.Length == 2 && code[0] is 'p' or 'e' && code[1] is >= '1' and <= '4')) {
            target = code == "self" ? "me" : code;
            words.RemoveAt(0);
        }

        var name = string.Join(' ', words);
        if (name.Length == 0) {
            InformSenderClient($"Use {command}, optional s (no casting animation) and e (best enchantment), an optional target (me for you, p1-p4 your side's circles, e1-e4 the enemy circles, all for everyone), a spell's name or ID and an optional number of times, such as {command} se all Fire Cat 3 or {command} s me Tower Shield. .c1/.c2 cast before/after your card, .cf before anyone's turn, .cl after everyone's.");

            return;
        }

        // A number at the end repeats the spell (Niduus, 2026-10-20: `.c1 Fire Cat 3`), unless it is part of the
        // spell's name ("Detonate All 200").
        var repeat = 1;
        if (!CommandDrawProtocol.TryFindSpell(name, out var templateId, out var spellName)) {
            if (words.Count > 1 && int.TryParse(words[^1], out var times) && times > 0
                && CommandDrawProtocol.TryFindSpell(string.Join(' ', words[..^1]), out templateId, out spellName)) {
                repeat = Math.Min(times, MaxRepeat);
            }
            else {
                InformSenderClient($"No spell called \"{name}\" was found. Try .lookup spell {name} to see the names.");

                return;
            }
        }

        for (var i = 0; i < repeat; i++) {
            Context.SessionActor.Tell(new COMBAT_106_PROTOCOL.MSG_CHEATDELAYCAST {
                SpellTemplateId = templateId,
                SpellName = spellName,
                AtStart = atStart,
                Target = target,
                SkipCast = skip,
                When = when,
                Enchant = enchant,
            });
        }
    }

    // The most times one spell repeats.
    private const int MaxRepeat = 20;

}
