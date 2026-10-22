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
 * HATCHING ROOM
 * ========================================================================
 *
 * PURPOSE:
 * One player's side of a visit to the hatching room: where to go back to, the partner, the pets each
 * side picked, Hatch or Lend, the ready boxes and whether the hatch is done.
 *
 * USAGE EXAMPLE:
 * _room = new HatchingRoom(message.DestinationZone, message.PartnerCharId, message.ReturnZone, message.ReturnLocation);
 *
 * NOTE:
 * Lives in the player's HatchingService. The partner's service keeps its own and they trade changes.
 *
 * TODO:
 *
 * Created by: Jay
 * Version: KALI 1.0
 * Last Updated: 10/22/2026
 */

using System.Collections.Generic;
using Akka.Actor;
using Imcodec.ObjectProperty.TypeCache;

namespace Imlight.CoreLib.Game.Pet.Hatching;

/// <summary>
/// The state of one player's visit to the hatching room.
/// </summary>
/// <param name="zone">The hatching room zone.</param>
/// <param name="partnerCharId">The player who came from the sigil too, or zero.</param>
/// <param name="returnZone">The zone to go back to.</param>
/// <param name="returnLocation">Where to go back to in that zone.</param>
internal sealed class HatchingRoom(string zone, ulong partnerCharId, string returnZone, string returnLocation) {

    internal const int OWN_PET_PICK_SIDE = 0;
    internal const int SOLO_SECOND_PET_PICK_SIDE = 1;

    /// <summary>
    /// The hatching room zone.
    /// </summary>
    internal string Zone { get; } = zone;

    /// <summary>
    /// The zone to go back to.
    /// </summary>
    internal string ReturnZone { get; } = returnZone;

    /// <summary>
    /// Where to go back to in <see cref="ReturnZone"/>.
    /// </summary>
    internal string ReturnLocation { get; } = returnLocation;

    /// <summary>
    /// The partner, or zero for a solo hatch.
    /// </summary>
    internal ulong PartnerCharId { get; private set; } = partnerCharId;

    /// <summary>
    /// The partner's session, once the partner is in the room.
    /// </summary>
    internal IActorRef PartnerSession { get; set; }

    /// <summary>
    /// The pets the partner can hatch with, once the partner sent them.
    /// </summary>
    internal List<WizClientObjectItem> PartnerPets { get; set; }

    /// <summary>
    /// The pet game the room runs, once the room said so.
    /// </summary>
    internal string GameName { get; set; }

    internal bool Attached { get; set; }
    internal bool WindowOpened { get; set; }
    internal bool ClientReady { get; set; }
    internal bool Started { get; set; }
    internal bool PetsSentToPartner { get; set; }

    /// <summary>
    /// The global ids of the pets the player picked, by pick side.
    /// </summary>
    internal ulong[] Picks { get; } = new ulong[2];

    internal ulong PartnerPickId { get; set; }

    /// <summary>
    /// True if the player picked Lend, false for Hatch, null before a pick.
    /// </summary>
    internal bool? Lends { get; set; }

    internal bool? PartnerLends { get; set; }
    internal bool Ready { get; set; }
    internal bool PartnerReady { get; set; }

    /// <summary>
    /// True once the player got an egg or lent a pet.
    /// </summary>
    internal bool Done { get; set; }

    /// <summary>
    /// True when the player came with a partner who has not been given up on.
    /// </summary>
    internal bool IsDuo
        => PartnerCharId != 0;

    /// <summary>
    /// True when both players picked a side and at least one hatches.
    /// </summary>
    internal bool ChoicesAgree
        => Lends is not null && PartnerLends is not null && !(Lends.Value && PartnerLends.Value);

    /// <summary>
    /// Gives up on the partner: the player hatches alone.
    /// </summary>
    internal void HatchAlone() {
        PartnerCharId = 0;
        PartnerSession = null;
        PartnerPets = null;
    }

}
