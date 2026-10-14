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
 * SEAMSTRESS SERVICE
 * ========================================================================
 *
 * PURPOSE:
 * Handles the stitch and unstitch requests of a player at a seamstress NPC: validates them, then lets
 * a SeamstressTransaction swap the items and charge the Crowns.
 *
 * USAGE EXAMPLE:
 * The client sends MSG_STITCHITEMS or MSG_UNSTITCHITEMS after the seamstress window's confirm. Every
 * request is answered with one MSG_STITCHITEMSCONFIRM.
 *
 * NOTE:
 * The client sends item ids only. It never checks for a free backpack slot and never takes off a worn
 * input, so the transaction does both. A refusal shows the client only its generic failure box.
 *
 * TODO:
 *
 * Created by: Jay
 * Version: KALI 1.0
 * Last Updated: 10/14/2026
 */

using System;
using Akka.Actor;
using Imcodec.MessageLayer.Generated;
using Imcodec.ObjectProperty.TypeCache;
using Imlight.Common;
using Imlight.CoreLib.Game.Seamstress;
using Imlight.CoreLib.Game.Zone.Components;
using Imlight.CoreLib.Shared.Networking;
using Imlight.CoreLib.Shared.Packets;
using Imlight.CoreLib.WizardData.Models.Misc;
using Imlight.CoreLib.WizardData.Models.Player;

namespace Imlight.CoreLib.Game.Services;

internal class SeamstressService(SessionActor sessionActor) : MessageService(sessionActor) {

    protected static Props Props(SessionActor parentActor)
        => Akka.Actor.Props.Create(() => new SeamstressService(parentActor));

    [MessageHandler(typeof(WIZARD_12_PROTOCOL.MSG_STITCHITEMS))]
    private void ReceiveStitchItems(WIZARD_12_PROTOCOL.MSG_STITCHITEMS message) {
        var wizard = GetActiveWizard();
        var success = SeamstressConfig.TryGetPrices(out var stitchCrowns, out _)
            && IsAtSeamstress(wizard, message.GlobalID)
            && TryStitch(wizard, message.DisplayID, message.StatsID, stitchCrowns);

        SendConfirm(wizard, success);
    }

    private bool TryStitch(Wizard wizard, ulong displayItemId, ulong statsItemId, int stitchCrowns) {
        var displayItem = FindItem(wizard, displayItemId);
        var statsItem = FindItem(wizard, statsItemId);
        if (displayItemId == statsItemId || displayItem is null || statsItem is null) {
            ReportTampering(wizard, $"Player tried to stitch items {displayItemId} and {statsItemId}, which are not two items they own!");

            return false;
        }

        if (!StitchRules.TryPlanStitch(displayItem, statsItem, out var plan, out var reason)) {
            return Deny(wizard, $"stitch of items {displayItemId} and {statsItemId}", reason);
        }

        if (!StitchedItemFactory.TryCreateStitched(wizard, plan, out var stitchedItem)) {
            return Deny(wizard, $"stitch of items {displayItemId} and {statsItemId}", "the result could not be built");
        }

        ReportJewelSockets(wizard, displayItem, statsItem);
        if (!TryApply(wizard, [displayItem, statsItem], [stitchedItem], stitchCrowns, out var failure)) {
            return Deny(wizard, $"stitch of items {displayItemId} and {statsItemId}", failure);
        }

        return true;
    }

    private bool TryApply(Wizard wizard, WizClientObjectItem[] inputs, WizClientObjectItem[] results, int crownsCost,
                          out string failure) {
        try {
            var transaction = new SeamstressTransaction(wizard, SessionActor.ActorRef, UnequipItem);

            return transaction.TryApply(inputs, results, crownsCost, out failure);
        }
        catch (Exception ex) {
            Logger.Error("[Seamstress] Error in a request of {0}: {1} {2}", Logger.Args(wizard.CharId, ex.Message, ex.StackTrace));
            failure = "an error occurred";

            return false;
        }
    }

    private static bool Deny(Wizard wizard, string request, string reason) {
        Logger.Warning("[Seamstress] Refused the {0} for {1}: {2}", Logger.Args(request, wizard.CharId, reason));

        return false;
    }

    private bool IsAtSeamstress(Wizard wizard, ulong npcGlobalId) {
        var npc = GetZoneObject(npcGlobalId);
        if (npc is null) {
            Logger.Warning("[Seamstress] Failed to find NPC {0} in the zone of {1}", Logger.Args(npcGlobalId, wizard.CharId));

            return false;
        }

        var seamstress = npc.GetComponentOfType<InteractSeamstressComponent>();
        if (seamstress is null) {
            ReportTampering(wizard, $"Player sent a seamstress request to NPC {npcGlobalId}, which is not a seamstress!");

            return false;
        }

        if (!seamstress.IsInServiceRange(GetActiveGameObject())) {
            ReportTampering(wizard, $"Player sent a seamstress request to NPC {npcGlobalId} from out of range!");

            return false;
        }

        return true;
    }

    private bool UnequipItem(ulong itemId)
        => AskOtherService<CHARACTER_103_PROTOCOL.MSG_UNEQUIPITEMRSP>(
            new CHARACTER_103_PROTOCOL.MSG_UNEQUIPITEM { ItemId = itemId }).Success;

    private static void ReportTampering(Wizard wizard, string infractionText) {
        wizard.Account.AddInfraction(InfractionType.SuspiciousBehavior, infractionText);

        Logger.Warning("[Seamstress] {0} This has been logged as a suspicious behavior infraction.", Logger.Args(infractionText));
    }

    private static void ReportJewelSockets(Wizard wizard, params WizClientObjectItem[] items) {
        foreach (var item in items) {
            if (StitchedItemFactory.HasJewelSockets(item)) {
                Logger.Warning("[Seamstress] Item {0} of {1} has jewel sockets, which the seamstress does not handle.",
                    Logger.Args(item.m_globalID.Full, wizard.CharId));
            }
        }
    }

    private static WizClientObjectItem FindItem(Wizard wizard, ulong itemId)
        => wizard.InventoryBehavior.GetItem(itemId) ?? wizard.EquipmentBehavior.GetItem(itemId);

    private void SendConfirm(Wizard wizard, bool success)
        => SendToSocket(new WIZARD_12_PROTOCOL.MSG_STITCHITEMSCONFIRM {
            Failure = success ? 0 : 1,
            WebFailure = 0,
            Credits = wizard.Account.Crowns
        });

}
