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
 * PET XP AWARD
 * ========================================================================
 *
 * PURPOSE:
 * Gives a pet XP the way a player should see it: adds it, saves the pet and tells the client.
 *
 * USAGE EXAMPLE:
 * PetXpAward.Give(SessionActor.ActorRef, wizard, pet, 4, PetXpSource.Combat);
 *
 * NOTE:
 * The client expects the pet's new state before the floating XP text, so the behavior update goes first.
 *
 * TODO:
 *
 * Created by: Jay
 * Version: KALI 1.0
 * Last Updated: 10/14/2026
 */

using System.Collections.Generic;
using Akka.Actor;
using Imcodec.MessageLayer;
using Imcodec.MessageLayer.Generated;
using Imcodec.ObjectProperty.TypeCache;
using Imlight.CoreLib.WizardData.Models.Player;

namespace Imlight.CoreLib.Game.Pet;

/// <summary>
/// Gives pets XP and tells the owner's client about it.
/// </summary>
internal static class PetXpAward {

    /// <summary>
    /// Adds XP to a pet, saves the pet and sends the client its new state.
    /// </summary>
    /// <param name="session">The owner's session, which receives the messages.</param>
    /// <param name="wizard">The owner.</param>
    /// <param name="pet">The pet item.</param>
    /// <param name="xp">The XP to add, before any bonus for the source.</param>
    /// <param name="source">Where the XP comes from.</param>
    /// <returns>What the pet received, which is <see cref="PetXpResult.None"/> if it cannot gain XP.</returns>
    internal static PetXpResult Give(IActorRef session, Wizard wizard, WizClientObjectItem pet, uint xp, PetXpSource source) {
        var result = PetProgression.AddXp(pet, xp, source);
        if (!result.Applied) {
            return result;
        }

        // Persistent save.
        wizard.SavePet(pet);

        foreach (var message in CreateMessages(pet, result)) {
            session.Tell(message, null);
        }

        return result;
    }

    /// <summary>
    /// Creates the messages that follow a grant of XP, in the order the client expects.
    /// </summary>
    /// <param name="pet">The pet item, already holding the new state.</param>
    /// <param name="result">What the grant did.</param>
    /// <returns>The messages to send.</returns>
    internal static List<IMessage> CreateMessages(WizClientObjectItem pet, PetXpResult result) {
        List<IMessage> messages = [];
        if (PetBehaviorUpdate.TryCreate(pet, out var update)) {
            messages.Add(update);
        }

        messages.Add(new WIZARD2_53_PROTOCOL.MSG_GAINPETXP { PetGID = pet.m_globalID, XP = result.Granted });

        return messages;
    }

}
