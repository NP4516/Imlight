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
 * BADGE PURCHASE GATE
 * ========================================================================
 *
 * PURPOSE:
 * Tells shops and trainers whether a purchase requirement list denies the sale because of a badge.
 *
 * USAGE EXAMPLE:
 * if (BadgeGate.Blocks(template.m_purchaseRequirements, SessionActor.ActorRef, GetActiveGameObject(), wizard, SessionActor.GetZoneActor())) { ... }
 *
 * NOTE:
 * Only lists that name a badge are enforced. The other requirement kinds in purchase lists are not all answered by the server yet, and an unanswered kind evaluates to false, which would deny items that sell today.
 *
 * TODO:
 * Enforce the other purchase requirement kinds once their handlers exist?
 *
 * Created by: Jay
 * Version: KALI 1.0
 * Last Updated: 10/14/2026
 */

using System.Linq;
using Akka.Actor;
using Imcodec.ObjectProperty.TypeCache;
using Imlight.CoreLib.Game.Requirements;
using Imlight.CoreLib.Game.Requirements.Contexts;
using Imlight.CoreLib.WizardData.Models.Player;

namespace Imlight.CoreLib.Game.Badges;

internal static class BadgeGate {

    internal static bool Blocks(RequirementList requirements, IActorRef playerRef, CoreObject playerObj, Wizard wizard, IActorRef zoneRef)
        => NamesBadge(requirements)
            && !RequirementDispatcher.EvaluateRequirements(requirements, new GenericRequirementContext(requirements, playerRef, playerObj, wizard, zoneRef));

    private static bool NamesBadge(RequirementList requirements)
        => requirements?.m_requirements?.Any(requirement => requirement switch {
            ReqHasBadge or ReqBaseHasBadge => true,
            RequirementList nested => NamesBadge(nested),
            _ => false
        }) ?? false;

}
