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
 * PVP
 * ========================================================================
 * 
 * PURPOSE:
 * Announces the session to the PvPDirector and takes it back out. The PvP requests of the client are added
 * here as the matchmaking features arrive.
 * 
 * USAGE EXAMPLE:
 * Attached to every game session by GameServiceFactory.
 * 
 * NOTE:
 * The client sends MSG_BUDDYREQUESTLIST once it is in the world after every login and zone transfer; that is
 * when this session announces itself to the director.
 * 
 * TODO:
 * 
 * 
 * Created by: Jay
 * Version: KALI 1.0
 * Last Updated: 10/14/2026
 */

using System;
using System.Collections.Generic;
using Akka.Actor;
using Imcodec.MessageLayer;
using Imcodec.MessageLayer.Generated;
using Imlight.Common;
using Imlight.CoreLib.Game.PvP;
using Imlight.CoreLib.Shared.Networking;
using Imlight.CoreLib.Shared.Packets;

namespace Imlight.CoreLib.Game.Services;

internal class PvPService(SessionActor sessionActor) : MessageService(sessionActor) {

    private ulong _charId;
    private bool _isPresent;
    private bool _isLoggingOut;
    private string _realmName;
    private readonly PvPWindowState _window = new();

    protected static Props Props(SessionActor parentActor)
        => Akka.Actor.Props.Create(() => new PvPService(parentActor));

    [MessageHandler(typeof(GAME_5_PROTOCOL.MSG_BUDDYREQUESTLIST))]
    private void ReceiveBuddyRequestList(GAME_5_PROTOCOL.MSG_BUDDYREQUESTLIST message) {
        var wizard = GetActiveWizard();
        if (wizard is null) {
            return;
        }

        var realmName = GetRealmName();
        if (realmName is null) {
            Logger.Error("PvP presence of {CharId} skipped: the realm name is unknown.", Logger.Args(wizard.CharId));

            return;
        }

        _charId = wizard.CharId;
        _isPresent = true;

        TellDirector(new PVP_111_PROTOCOL.MSG_PLAYERPRESENT {
            Session = SessionActor.ActorRef,
            Profile = PvPPlayerProfile.FromWizard(wizard, SessionActor.GetZoneActor(), realmName),
            PlayerObject = GetActiveGameObject()
        });
    }

    [MessageHandler(typeof(PVP_111_PROTOCOL.MSG_KIOSKUSED))]
    private void ReceiveKioskUsed(PVP_111_PROTOCOL.MSG_KIOSKUSED message)
        => _window.KioskUsed(message.LeagueId);

    [MessageHandler(typeof(WIZARD3_56_PROTOCOL.MSG_PVP5THAGEOPENPVPWINDOW))]
    private void ReceiveOpenPvPWindow(WIZARD3_56_PROTOCOL.MSG_PVP5THAGEOPENPVPWINDOW message)
        => SendAll(_window.OpenWindow(message, SessionActor.ActorRef, GetActiveGameObject(), GetActiveWizard(), DateTime.UtcNow));

    [MessageHandler(typeof(WIZARD3_56_PROTOCOL.MSG_PVP5THAGECANJOINMATCH))]
    private void ReceiveCanJoinMatch(WIZARD3_56_PROTOCOL.MSG_PVP5THAGECANJOINMATCH message)
        => SendAll(_window.CanJoin(message, SessionActor.ActorRef, GetActiveGameObject(), GetActiveWizard(), DateTime.UtcNow));

    [MessageHandler(typeof(WIZARD_12_PROTOCOL.MSG_PREPVPKIOSK))]
    private void ReceivePrePvPKiosk(WIZARD_12_PROTOCOL.MSG_PREPVPKIOSK message)
        => SendAll(_window.PrePvPKioskEcho(message, GetActiveWizard()));

    [MessageHandler(typeof(WIZARD_12_PROTOCOL.MSG_PVPUPDATEREQUEST))]
    private void ReceivePvPUpdateRequest(WIZARD_12_PROTOCOL.MSG_PVPUPDATEREQUEST message)
        => SendAll(_window.UpdateRequest(message, GetActiveWizard()));

    [MessageHandler(typeof(WIZARD_12_PROTOCOL.MSG_PVPINTENT))]
    private void ReceivePvPIntent(WIZARD_12_PROTOCOL.MSG_PVPINTENT message) {
        var (replies, toDirector) = _window.Intent(message, SessionActor.ActorRef, GetActiveGameObject(), GetActiveWizard(), DateTime.UtcNow);

        SendAll(replies);
        if (toDirector is not null) {
            TellDirector(toDirector);
        }
    }

    [MessageHandler(typeof(WIZARD_12_PROTOCOL.MSG_EXPANDPVPSEARCH))]
    private void ReceiveExpandPvPSearch(WIZARD_12_PROTOCOL.MSG_EXPANDPVPSEARCH message) {
        var expand = _window.ExpandSearch(message, GetActiveWizard());
        if (expand is not null) {
            TellDirector(expand);
        }
    }

    [MessageHandler(typeof(WIZARD3_56_PROTOCOL.MSG_AdvPvPEloData))]
    private void ReceiveAdvPvPEloData(WIZARD3_56_PROTOCOL.MSG_AdvPvPEloData message)
        => SendToSocket(new WIZARD3_56_PROTOCOL.MSG_AdvPvPEloData {
            GlobalID = message.GlobalID,
            EntryList = PvPBlobs.EmptyEloEntryList(),
            CheckCompletion = 0,
            RecordCacheID = 0
        });

    [MessageHandler(typeof(GAME_5_PROTOCOL.MSG_QUERY_LOGOUT))]
    private void ReceiveQueryLogout(GAME_5_PROTOCOL.MSG_QUERY_LOGOUT message)
        => AnnounceDeparture(isLogout: true);

    [MessageHandler(typeof(GAME_5_PROTOCOL.MSG_CLIENT_DISCONNECT))]
    private void ReceiveClientDisconnect(GAME_5_PROTOCOL.MSG_CLIENT_DISCONNECT message)
        => AnnounceDeparture(isLogout: true);

    protected override void OnPreDispose() {
        AnnounceDeparture(_isLoggingOut);

        base.OnPreDispose();
    }

    private void AnnounceDeparture(bool isLogout) {
        if (!_isPresent) {
            return;
        }

        _isPresent = false;
        _isLoggingOut |= isLogout;

        TellDirector(new PVP_111_PROTOCOL.MSG_PLAYERDEPARTED {
            CharId = _charId,
            Session = SessionActor.ActorRef,
            IsLogout = isLogout
        });
    }

    private void SendAll(IReadOnlyList<IMessage> messages) {
        foreach (var message in messages) {
            SendToSocket(message);
        }
    }

    private string GetRealmName() {
        if (_realmName is not null) {
            return _realmName;
        }

        try {
            _realmName = AskServer<SERVER_100_PROTOCOL.MSG_SERVERINFO>(new SERVER_100_PROTOCOL.MSG_QUERYSERVER())?.RealmName;
        } catch (Exception ex) {
            Logger.Error("PvP could not ask the server for its realm name: {Exception}", Logger.Args(ex.Message));
        }

        return _realmName;
    }

    private static void TellDirector(IServerMessage message)
        => PvPDirector.Instance?.Tell(message);

}
