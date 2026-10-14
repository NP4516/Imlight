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
 * SILVER CHEST COMPONENT
 * ========================================================================
 *
 * PURPOSE:
 * Makes a silver chest an interactable that sends the player into a lock puzzle minigame (Shock A Lock or
 * Catch A Key). The chest is used up when the game is played to the end, won or lost, and the spawner
 * brings a new one later.
 *
 * USAGE EXAMPLE:
 * Attached by the component registry to every chest template named "Silver Chest" whose template id has a
 * loot table (an NpcDropTable in SpiralDB). A chest without a loot table stays inert.
 *
 * NOTE:
 * One player at a time holds the chest: Idle, Pending (sent to the game, lease running), InGame, Consumed.
 * The state is world state and is not persisted. The minigame process reports STARTED, RESULT and ABORTED;
 * a STARTED for a chest that another player holds is answered with a refusal and that process pays nothing.
 *
 * TODO:
 * - How long does a chest transfer take on the live server? ReservationLease is a guess.
 *
 * Created by: Jay
 * Version: KALI 1.0
 * Last Updated: 10/14/2026
 */

using System;
using System.Collections.Generic;
using Akka.Actor;
using Imcodec.ObjectProperty.TypeCache;
using Imlight.Common;
using Imlight.CoreLib.Game.DropTables;
using Imlight.CoreLib.Game.Minigames;
using Imlight.CoreLib.Game.WizBang;
using Imlight.CoreLib.Game.Zone.Core;
using Imlight.CoreLib.Shared.Networking;
using Imlight.CoreLib.Shared.Packets;
using Imlight.CoreLib.Shared.Resources;
using Imlight.CoreLib.WizardData.Models.Player;

namespace Imlight.CoreLib.Game.Zone.Components;

internal sealed class SilverChestComponent(ZoneEntity entity)
    : ZoneEntityComponent(entity), IServiceComponent, IComponentFactory, IWithTimers {

    // Locale WizardGameObjects_00000055 is "Silver Chest", on the chest template of every world.
    private const string SilverChestNameKey = "WizardGameObjects_00000055";
    private const string ChestTextKey = "GUI_ChestInteract";
    private const string LeaseTimerKey = "chest-lease";

    // How long a chest waits for the game to start after its player was sent.
    private static readonly TimeSpan ReservationLease = PendingChestGames.ArrivalWindow;
    private static readonly string[] s_minigameNames = ["Shockalock", "CatchAKey"];

    private enum ChestState {
        Idle,
        Pending,
        InGame,
        Consumed,
    }

    private ChestState _state = ChestState.Idle;
    private ulong _playerCharId;

    public string ServiceName => "Interact";
    public string NpcIcon => (Entity.Template as GameObjectTemplate)?.m_sIcon;
    public string NpcNameKey => (Entity.Template as GameObjectTemplate)?.m_displayName;
    public string NpcTextKey => ChestTextKey;
    public WizBangs WizBang => WizBangs.None;
    public string StateName => "";
    public string InteractWizBang => "";
    public string DisplayKey => "";

    public ITimerScheduler Timers { get; set; }

    public static bool ShouldAttachToEntity(CoreTemplate template)
        => template is GameObjectTemplate go
            && go.m_displayName == SilverChestNameKey
            && ChestLootRoller.HasLootTable(go.m_templateID);

    public IEnumerable<ServiceOptionBase> GetServiceOptions(Wizard _)
        => [
            new ServiceOptionBase() {
                m_displayKey = DisplayKey,
                m_forceInteract = false,
                m_iconKey = NpcIcon,
                m_serviceName = ServiceName,
                m_serviceIndex = 0
            }
        ];

    public void OnServiceInteraction(IActorRef playerActor, Wizard playerCharacter, CoreObject playerObject, uint serviceOptionIndex) {
        if (_state != ChestState.Idle) {
            Logger.Debug("{Wizard} clicked a silver chest that is {State}",
                Logger.Args(playerCharacter.PlayerNameBehavior.GetWizardName(), _state));

            return;
        }

        var radius = ((IServiceComponent) this).DEFAULT_INTERACTION_RADIUS;
        if (playerCharacter.IsInDuel || !IsInHorizontalRadius(playerObject, radius)) {
            Logger.Debug("{Wizard} clicked a silver chest while in a duel or out of reach",
                Logger.Args(playerCharacter.PlayerNameBehavior.GetWizardName()));

            return;
        }

        var templateId = ((GameObjectTemplate) Entity.Template).m_templateID;
        if (!ChestLootRoller.HasLootTable(templateId)) {
            return;
        }

        var minigameName = s_minigameNames[Random.Shared.Next(s_minigameNames.Length)];
        if (!Imlight.CoreLib.Game.Minigames.MinigameConfig.TryGetMinigameByName(minigameName, out _)) {
            Logger.Error("The silver chest game {Minigame} is not in the minigame configuration",
                Logger.Args(minigameName));

            return;
        }

        _state = ChestState.Pending;
        _playerCharId = playerCharacter.CharId;
        Timers.StartSingleTimer(LeaseTimerKey, new ZONE_102_PROTOCOL.MSG_CHEST_LEASE_EXPIRED(), ReservationLease);

        var context = new ChestGameContext(
            ChestRef: Self,
            ChestTemplateId: templateId,
            CharId: playerCharacter.CharId,
            MinigameName: minigameName,
            ReturnZone: playerCharacter.Zone,
            ReturnLocation: Util.GetCompactStringFromVector(playerObject.m_location, playerObject.m_orientation),
            ReturnPrivate: Entity.Zone.IsInstance);
        playerActor.Tell(new SERVICE_101_PROTOCOL.MSG_START_CHEST_MINIGAME { Context = context }, Self);
    }

    [MessageHandler(typeof(ZONE_102_PROTOCOL.MSG_CHEST_GAME_STARTED))]
    private void ReceiveChestGameStarted(ZONE_102_PROTOCOL.MSG_CHEST_GAME_STARTED message) {
        // A start that outlived its lease still holds the chest if nobody else does.
        var heldByPlayer = (_state is ChestState.Pending or ChestState.InGame) && _playerCharId == message.CharId;
        if (!heldByPlayer && _state != ChestState.Idle) {
            Logger.Warning("A silver chest game of {CharId} started on a chest that is {State}",
                Logger.Args(message.CharId, _state));
            Sender.Tell(new PROCESS_107_PROTOCOL.MSG_CHEST_GAME_REFUSED(), Self);

            return;
        }

        _state = ChestState.InGame;
        _playerCharId = message.CharId;
        Timers.Cancel(LeaseTimerKey);
    }

    [MessageHandler(typeof(ZONE_102_PROTOCOL.MSG_CHEST_GAME_RESULT))]
    private void ReceiveChestGameResult(ZONE_102_PROTOCOL.MSG_CHEST_GAME_RESULT message) {
        if (_state != ChestState.InGame || _playerCharId != message.CharId) {
            return;
        }

        _state = ChestState.Consumed;
        Entity.DeleteObject();
    }

    [MessageHandler(typeof(ZONE_102_PROTOCOL.MSG_CHEST_GAME_ABORTED))]
    private void ReceiveChestGameAborted(ZONE_102_PROTOCOL.MSG_CHEST_GAME_ABORTED message) {
        if ((_state is ChestState.Pending or ChestState.InGame) && _playerCharId == message.CharId) {
            Release();
        }
    }

    [MessageHandler(typeof(ZONE_102_PROTOCOL.MSG_CHEST_LEASE_EXPIRED))]
    private void ReceiveChestLeaseExpired(ZONE_102_PROTOCOL.MSG_CHEST_LEASE_EXPIRED _) {
        if (_state != ChestState.Pending) {
            return;
        }

        Logger.Information("A silver chest was released, {CharId} never reached the game", Logger.Args(_playerCharId));
        Release();
    }

    private void Release() {
        _state = ChestState.Idle;
        _playerCharId = 0;
        Timers.Cancel(LeaseTimerKey);
    }

}
