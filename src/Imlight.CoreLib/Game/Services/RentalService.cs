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
 * RENTAL SERVICE
 * ========================================================================
 *
 * PURPOSE:
 * Starts the timer of a rental item when it is first equipped and tells the client the absolute time it runs out.
 *
 * USAGE EXAMPLE:
 * EquipmentService tells MSG_STARTRENTAL after it equipped an item that carries a rental behavior.
 *
 * NOTE:
 * The expiry is written to the item document before memory and before the client, so a failed write leaves the timer not started and the next equip retries it.
 * The duration comes from the template only (RentalBehaviorTemplate.m_expireTime).
 *
 * TODO:
 *
 * Created by: Jay
 * Version: KALI 1.0
 * Last Updated: 10/14/2026
 */

using System;
using System.Collections.Generic;
using Akka.Actor;
using Imcodec.MessageLayer.Generated;
using Imcodec.ObjectProperty.TypeCache;
using Imlight.Common;
using Imlight.CoreLib.Game.Rentals;
using Imlight.CoreLib.Shared.Networking;
using Imlight.CoreLib.Shared.Packets;
using Imlight.CoreLib.WizardData.Collections;

namespace Imlight.CoreLib.Game.Services;

internal class RentalService(SessionActor sessionActor) : MessageService(sessionActor) {

    private readonly HashSet<uint> _warnedTemplates = [];

    protected static Props Props(SessionActor parentActor)
        => Akka.Actor.Props.Create(() => new RentalService(parentActor));

    [MessageHandler(typeof(CHARACTER_103_PROTOCOL.MSG_STARTRENTAL))]
    private void ReceiveStartRental(CHARACTER_103_PROTOCOL.MSG_STARTRENTAL message) {
        var wizard = GetActiveWizard();
        var item = wizard.EquipmentBehavior.GetItem(message.ItemId) ?? wizard.InventoryBehavior.GetItem(message.ItemId);
        if (item is null || !RentalItems.TryGetBehavior(item, out var behavior) || RentalItems.IsStarted(behavior)) {
            return;
        }

        if (!RentalItems.TryGetTemplate(item, out var rentalTemplate, out var template)) {
            return;
        }

        if (!RentalItems.TryGetExpireTime(rentalTemplate, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), out var expireTime)) {
            if (_warnedTemplates.Add(template.m_templateID)) {
                Logger.Warning("Rental template {0} has timer type {1} and duration '{2}', which is not a calendar rental duration; its timer is never started.",
                    Logger.Args(template.m_templateID, rentalTemplate.m_timerType, (string) rentalTemplate.m_expireTime));
            }

            return;
        }

        if (!WizardItemCollection.SetRentalExpireTime(item, expireTime)) {
            return;
        }

        behavior.m_expireTime = expireTime;
        SendToSocket(new WIZARD_12_PROTOCOL.MSG_SETRENTALTIMER {
            GlobalID = item.m_globalID,
            TimerTime = expireTime
        });
    }

}
