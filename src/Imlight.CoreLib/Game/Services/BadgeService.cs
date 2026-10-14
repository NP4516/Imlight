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
 * BADGE SERVICE
 * ========================================================================
 *
 * PURPOSE:
 * Sends a character's badges to the client and keeps them current as the character earns them.
 *
 * USAGE EXAMPLE:
 * Attached to every game session by GameServiceFactory; MSG_PRELOGIN makes it send the full badge list.
 *
 * NOTE:
 * The list is sent before AttachService answers its own MSG_PRELOGIN with MSG_LOGINCOMPLETE, as the live server does.
 * Every state change goes through BadgeEngine; this service persists it once, tells the client and runs the completion results.
 *
 * TODO:
 *
 * Created by: Jay
 * Version: KALI 1.0
 * Last Updated: 10/14/2026
 */

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Imcodec.MessageLayer.Generated;
using Imcodec.ObjectProperty.TypeCache;
using Imlight.Common;
using Imlight.CoreLib.Game.Badges;
using Imlight.CoreLib.Game.Requirements;
using Imlight.CoreLib.Game.Requirements.Contexts;
using Imlight.CoreLib.Game.Results;
using Imlight.CoreLib.Shared.Behaviors;
using Imlight.CoreLib.Shared.Networking;
using Imlight.CoreLib.Shared.Packets;
using Imlight.CoreLib.Shared.Resources;
using Imlight.CoreLib.WizardData.Collections;
using Imlight.CoreLib.WizardData.Models.Player;
using Imlight.CoreLib.WizardData.Models.World;

namespace Imlight.CoreLib.Game.Services;

internal class BadgeService(SessionActor sessionActor) : MessageService(sessionActor) {

    private readonly HashSet<uint> _warnedUnknownBadges = [];

    protected static Props Props(SessionActor parentActor)
        => Akka.Actor.Props.Create(() => new BadgeService(parentActor));

    [MessageHandler(typeof(ZONE_102_PROTOCOL.MSG_PRELOGIN))]
    private void ReceivePreLogin(ZONE_102_PROTOCOL.MSG_PRELOGIN message) {
        var catalog = BadgeCatalog.Current;
        if (!catalog.IsReady) {
            return;
        }

        // An award that is due at login is already in the full list, so the changes are not sent on their own.
        var wizard = GetActiveWizard();
        var changes = new BadgeEngine(wizard, catalog).Execute(engine => engine.Recheck(definition => MeetsRequirements(wizard, definition)));
        FinishChanges(wizard, changes, sendBatches: false);

        foreach (var badgeMessage in BadgeWire.Build(wizard, catalog, BadgeView.GetHeld(wizard.BadgeBehavior, catalog), full: true)) {
            SendToSocket(badgeMessage);
        }
    }

    [MessageHandler(typeof(CHARACTER_103_PROTOCOL.MSG_BADGEEVENT))]
    private void ReceiveBadgeEvent(CHARACTER_103_PROTOCOL.MSG_BADGEEVENT message) {
        var catalog = BadgeCatalog.Current;
        if (!catalog.IsReady) {
            return;
        }

        var wizard = GetActiveWizard();
        var engine = new BadgeEngine(wizard, catalog);
        var changes = message.Kind switch {
            BadgeEventKind.Award => engine.Execute(e => e.Award(message.BadgeId)),
            BadgeEventKind.Recheck => engine.Execute(e => e.Recheck(definition => MeetsRequirements(wizard, definition))),
            _ => BadgeEngine.Changes.None
        };

        if (message.Kind == BadgeEventKind.Award && !catalog.TryGet(message.BadgeId, out _) && _warnedUnknownBadges.Add(message.BadgeId)) {
            Logger.Warning("Badge {0} is not in the badge catalog and was not awarded to {1}.",
                Logger.Args(message.BadgeId, wizard.CharId));
        }

        FinishChanges(wizard, changes, sendBatches: true);
    }

    [MessageHandler(typeof(GROUP_109_PROTOCOL.MSG_LEVELCHANGED))]
    private void ReceiveLevelChanged(GROUP_109_PROTOCOL.MSG_LEVELCHANGED message) {
        ReceiveBadgeEvent(new CHARACTER_103_PROTOCOL.MSG_BADGEEVENT { Kind = BadgeEventKind.Recheck });
    }

    [MessageHandler(typeof(COMBAT_106_PROTOCOL.MSG_COMBATWIN))]
    private void ReceiveCombatVictory(COMBAT_106_PROTOCOL.MSG_COMBATWIN message) {
        var catalog = BadgeCatalog.Current;
        if (!catalog.IsReady || message.MobTemplateIds is not { Length: > 0 }) {
            return;
        }

        // Each defeated mob advances a counter once, however many of its adjectives name that counter.
        var kills = new Dictionary<string, ulong>(StringComparer.Ordinal);
        foreach (var templateId in message.MobTemplateIds) {
            if (CoreObjectFactory.GetCoreTemplate(templateId) is not GameObjectTemplate mob) {
                continue;
            }

            var adjectives = mob.m_adjectiveList?.Where(a => !a.IsEmpty).Select(a => (string) a) ?? [];
            foreach (var counterKey in catalog.GetCounterKeysForAdjectives(adjectives)) {
                kills[counterKey] = kills.GetValueOrDefault(counterKey) + 1;
            }
        }

        if (kills.Count == 0) {
            return;
        }

        var wizard = GetActiveWizard();
        var changes = new BadgeEngine(wizard, catalog).Execute(engine => {
            foreach (var (counterKey, count) in kills) {
                engine.Increment(counterKey, count);
            }
        });

        FinishChanges(wizard, changes, sendBatches: true);
    }

    [MessageHandler(typeof(WIZARD3_56_PROTOCOL.MSG_REQUESTNEWBADGE))]
    private void ReceiveRequestNewBadge(WIZARD3_56_PROTOCOL.MSG_REQUESTNEWBADGE message) {
        var catalog = BadgeCatalog.Current;
        if (!catalog.IsReady) {
            return;
        }

        // The client sends this for each complete badge it has just been told about; no reply is expected.
        var wizard = GetActiveWizard();
        var state = wizard.BadgeBehavior;
        if (!catalog.TryGet(message.BadgeID, out var definition) || !BadgeView.IsComplete(state, definition)) {
            Logger.Warning("{0} acknowledged badge {1}, which they do not hold complete.",
                Logger.Args(wizard.PlayerNameBehavior.GetWizardName(), message.BadgeID));

            return;
        }

        if (state.NewBadges.Any(entry => entry.BadgeId == message.BadgeID)) {
            return;
        }

        state.NewBadges.Add(new NewBadgeEntry { BadgeId = message.BadgeID, Time = (uint) DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
        Persist(wizard);
    }

    private void FinishChanges(Wizard wizard, BadgeEngine.Changes changes, bool sendBatches) {
        if (changes.StateChanged) {
            Persist(wizard);
        }

        if (sendBatches) {
            foreach (var batch in changes.Batches) {
                foreach (var badgeMessage in BadgeWire.Build(wizard, BadgeCatalog.Current, batch, full: false)) {
                    SendToSocket(badgeMessage);
                }
            }
        }

        foreach (var completed in changes.Completed.Where(definition => definition.CompleteResults is not null)) {
            ResultDispatcher.ExecuteResults(
                actorContext: Context,
                results: completed.CompleteResults,
                playerRef: SessionActor.ActorRef,
                playerObj: GetActiveGameObject(),
                zoneActor: SessionActor.GetZoneActor());
        }
    }

    private static void Persist(Wizard wizard) {
        if (!WizardCollection.UpdateCharacterBadgeBehavior(wizard)) {
            Logger.Error("Failed to save the badge state of {0}.", Logger.Args(wizard.CharId));
        }
    }

    private bool MeetsRequirements(Wizard wizard, BadgeDefinition definition)
        => RequirementDispatcher.EvaluateRequirements(
            definition.Requirements,
            new GenericRequirementContext(definition.Requirements, SessionActor.ActorRef, GetActiveGameObject(), wizard, SessionActor.GetZoneActor()));

}
