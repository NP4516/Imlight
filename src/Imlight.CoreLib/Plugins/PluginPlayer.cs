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
 * Created by: phazejeff
 * Version: KALI 1.0
 * Last Updated: 10/23/2026
 */

using Akka.Actor;
using Imcodec.MessageLayer.Generated;
using Imlight.CoreLib.WizardData.Models.Player;
using Imlight.PluginApi;

namespace Imlight.CoreLib.Plugins;

/// <summary>
/// Exposes a session's wizard to plugins as an <see cref="IPlayer"/>.
/// </summary>
internal sealed class PluginPlayer(Wizard wizard, IActorRef sessionActor, IZone zone) : IPlayer {

    public ulong CharacterId => wizard.CharId;

    public string Name => wizard.PlayerNameBehavior.GetWizardName();

    public IZone Zone => zone;

    internal IActorRef SessionActor => sessionActor;

    public void SendMessage(string message)
        => sessionActor.Tell(new EXTENDEDBASE_2_PROTOCOL.MSG_SERVERMESSAGE { Message = message });

    /// <summary>
    /// The plugin view of a session, taken from its zone when it is in one.
    /// </summary>
    public static IPlayer For(Wizard wizard, IActorRef sessionActor, IActorRef zoneActor)
        => PluginZone.Of(zoneActor)?.PlayerFor(wizard, sessionActor)
           ?? new PluginPlayer(wizard, sessionActor, null);

}
