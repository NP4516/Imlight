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
 * The single authority for PvP matchmaking: who is queued, who is in a match, and the client state each player
 * is shown. Plays the role the original matchmaking service played.
 * 
 * USAGE EXAMPLE:
 * Created once by the GameServerPool. Sessions talk to it through PvPService with PVP_111_PROTOCOL messages;
 * it answers each client with generated wire messages sent straight to the session.
 * 
 * NOTE:
 * One instance serves every realm and zone, so a queue entry survives zone transfers. Each transfer is a new
 * session; players re-announce themselves on arrival. Messages for a player between sessions are queued and
 * replayed on arrival. Other systems read PvPRegistry.
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
using Imcodec.ObjectProperty.TypeCache;
using Imlight.Common;
using Imlight.CoreLib.Shared.Networking;
using Imlight.CoreLib.Shared.Packets;

namespace Imlight.CoreLib.Game.PvP;

/// <summary>
/// Owns the PvP queue and every match on the server and speaks the PvP protocol to each player's client.
/// </summary>
internal sealed class PvPDirector : ReceiveProtocolDispatcher, IWithTimers {

    private const int MaxQueuedMessagesPerPlayer = 64;

    private static TimeSpan s_departureGrace = TimeSpan.FromSeconds(60);
    private static string DepartureTimerKey(ulong charId) => $"departure-{charId}";

    public static IActorRef Instance { get; private set; }

    public ITimerScheduler Timers { get; set; }

    private readonly Dictionary<ulong, IActorRef> _sessions = [];
    private readonly Dictionary<ulong, PvPPlayerProfile> _profiles = [];
    private readonly Dictionary<ulong, CoreObject> _playerObjects = [];
    private readonly Dictionary<ulong, List<IMessage>> _queuedMessages = [];
    private readonly PvPQueue _queue = new();

    // ctor
    public PvPDirector()
        => Instance = Self;

    public static Props Props()
        => Akka.Actor.Props.Create(() => new PvPDirector());

    protected override bool AroundReceive(Receive receive, object message) {
        // A restart would rebuild the director empty and silently drop every queued player.
        try {
            return base.AroundReceive(receive, message);
        } catch (Exception ex) {
            Logger.Error("PvPDirector failed to handle {MessageType}: {Exception}",
                Logger.Args(message.GetType().Name, ex));

            return true;
        }
    }

    #region Presence

    [MessageHandler(typeof(PVP_111_PROTOCOL.MSG_PLAYERPRESENT))]
    private void ReceivePlayerPresent(PVP_111_PROTOCOL.MSG_PLAYERPRESENT message) {
        var profile = message.Profile;
        var charId = profile.CharId;

        _sessions[charId] = message.Session;
        _profiles[charId] = profile;
        _playerObjects[charId] = message.PlayerObject;
        Timers.Cancel(DepartureTimerKey(charId));

        ReplayQueuedMessages(charId, message.Session);
    }

    [MessageHandler(typeof(PVP_111_PROTOCOL.MSG_PLAYERDEPARTED))]
    private void ReceivePlayerDeparted(PVP_111_PROTOCOL.MSG_PLAYERDEPARTED message) {
        var charId = message.CharId;

        // A zone transfer can attach the next session before the previous one finishes disposing.
        if (!_sessions.TryGetValue(charId, out var session) || !session.Equals(message.Session)) {
            return;
        }

        _sessions.Remove(charId);

        if (message.IsLogout || !IsQueuedOrInMatch(charId)) {
            Leave(charId);

            return;
        }

        Timers.StartSingleTimer(
            DepartureTimerKey(charId),
            new PVP_111_PROTOCOL.MSG_DEPARTUREGRACEEXPIRED { CharId = charId },
            s_departureGrace
        );
    }

    [MessageHandler(typeof(PVP_111_PROTOCOL.MSG_DEPARTUREGRACEEXPIRED))]
    private void ReceiveDepartureGraceExpired(PVP_111_PROTOCOL.MSG_DEPARTUREGRACEEXPIRED message) {
        var charId = message.CharId;
        if (_sessions.ContainsKey(charId)) {
            return;
        }

        Logger.Debug("Character {CharId} did not return in time and leaves PvP.", Logger.Args(charId));

        Leave(charId);
    }

    #endregion

    #region Queue

    [MessageHandler(typeof(PVP_111_PROTOCOL.MSG_JOINQUEUE))]
    private void ReceiveJoinQueue(PVP_111_PROTOCOL.MSG_JOINQUEUE message) {
        var charId = message.CharId;
        if (!_sessions.ContainsKey(charId) || !_profiles.TryGetValue(charId, out var profile)) {
            return;
        }

        var entry = new PvPQueueEntry(
            CharId: charId,
            RealmName: profile.RealmName,
            TournamentNameId: message.TournamentNameId,
            MatchNameId: message.MatchNameId,
            LeagueId: message.LeagueId,
            SeasonId: message.SeasonId,
            ExpandSearch: message.ExpandSearch,
            EnteredAtUtc: DateTime.UtcNow
        );
        if (!_queue.Add(entry)) {
            return;
        }

        PvPRegistry.SetQueued(charId, true);
    }

    [MessageHandler(typeof(PVP_111_PROTOCOL.MSG_LEAVEQUEUE))]
    private void ReceiveLeaveQueue(PVP_111_PROTOCOL.MSG_LEAVEQUEUE message)
        => RemoveFromQueue(message.CharId);

    #endregion

    #region State

    private bool IsQueuedOrInMatch(ulong charId)
        => _queue.Contains(charId) || PvPRegistry.IsInMatch(charId);

    private void RemoveFromQueue(ulong charId) {
        if (_queue.Remove(charId)) {
            PvPRegistry.SetQueued(charId, false);
        }
    }

    private void Leave(ulong charId) {
        RemoveFromQueue(charId);
        Forget(charId);
    }

    private void Forget(ulong charId) {
        _profiles.Remove(charId);
        _playerObjects.Remove(charId);
        _queuedMessages.Remove(charId);
        Timers.Cancel(DepartureTimerKey(charId));
    }

    private void SendToPlayer(ulong charId, IMessage message) {
        if (_sessions.TryGetValue(charId, out var session)) {
            session.Tell(message);

            return;
        }

        // Only players between sessions are queued; anyone else is simply offline.
        if (!_profiles.ContainsKey(charId)) {
            return;
        }

        if (!_queuedMessages.TryGetValue(charId, out var queue)) {
            queue = [];
            _queuedMessages[charId] = queue;
        }

        if (queue.Count < MaxQueuedMessagesPerPlayer) {
            queue.Add(message);
        }
    }

    private void ReplayQueuedMessages(ulong charId, IActorRef session) {
        if (!_queuedMessages.Remove(charId, out var queue)) {
            return;
        }

        foreach (var message in queue) {
            session.Tell(message);
        }
    }

    #endregion

}
