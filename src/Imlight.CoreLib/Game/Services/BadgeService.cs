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
 *
 * TODO:
 *
 * Created by: Jay
 * Version: KALI 1.0
 * Last Updated: 10/14/2026
 */

using Akka.Actor;
using Imlight.CoreLib.Game.Badges;
using Imlight.CoreLib.Shared.Networking;
using Imlight.CoreLib.Shared.Packets;

namespace Imlight.CoreLib.Game.Services;

internal class BadgeService(SessionActor sessionActor) : MessageService(sessionActor) {

    protected static Props Props(SessionActor parentActor)
        => Akka.Actor.Props.Create(() => new BadgeService(parentActor));

    [MessageHandler(typeof(ZONE_102_PROTOCOL.MSG_PRELOGIN))]
    private void ReceivePreLogin(ZONE_102_PROTOCOL.MSG_PRELOGIN message) {
        var catalog = BadgeCatalog.Current;
        if (!catalog.IsReady) {
            return;
        }

        var wizard = GetActiveWizard();
        foreach (var badgeMessage in BadgeWire.Build(wizard, catalog, BadgeView.GetHeld(wizard.BadgeBehavior, catalog), full: true)) {
            SendToSocket(badgeMessage);
        }
    }

}
