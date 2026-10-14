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
 * HOUSING SERVICE
 * ========================================================================
 *
 * PURPOSE:
 * Sends a house's furniture to the client when the player enters it, and keeps the client's copy in step
 * with the house while the player decorates.
 *
 * USAGE EXAMPLE:
 * Internal service handling housing messages within the game server session.
 *
 * NOTE:
 * - Furniture is not a zone entity. The client builds every placed object from a blob the server sends, and
 *   later edits arrive as small versioned patches. The blob and the patches are built in Game/Housing.
 * - The service counts the player into the house at MSG_PRELOGIN and out when the session ends.
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
using Imlight.Common;
using Imlight.CoreLib.Game.Housing;
using Imlight.CoreLib.Shared.Networking;
using Imlight.CoreLib.Shared.Packets;

namespace Imlight.CoreLib.Game.Services;

internal class HousingService(SessionActor sessionActor) : MessageService(sessionActor) {

    private HouseState _house;

    protected static Props Props(SessionActor parentActor)
        => Akka.Actor.Props.Create(() => new HousingService(parentActor));

    protected override void OnPreDispose() {
        LeaveHouse();
        base.OnPreDispose();
    }

    [MessageHandler(typeof(ZONE_102_PROTOCOL.MSG_PRELOGIN))]
    private void ReceivePreLogin(ZONE_102_PROTOCOL.MSG_PRELOGIN message) {
        LeaveHouse();

        var wizard = GetActiveWizard();
        if (wizard.AttachedLotGid == 0) {
            return;
        }

        if (!HouseRegistry.TryEnter(wizard.CharId, wizard.AttachedLotGid, out var house)) {
            Logger.Warning("Character {CharId} attached to lot {LotGid}, which is not their house.",
                Logger.Args(wizard.CharId, wizard.AttachedLotGid));

            return;
        }

        _house = house;

        // The client compares this with its cached copy and asks for the blob only when they differ.
        var snapshot = house.Snapshot();
        SendToSocket(new WIZARDHOUSING_50_PROTOCOL.MSG_REQUEST_BLOBS {
            Data = HouseBlobCodec.BuildBlobRequest(house.LotGid, snapshot.Version, house.GidBase, snapshot.LiveCount),
        });
    }

    [MessageHandler(typeof(WIZARDHOUSING_50_PROTOCOL.MSG_REQUEST_BLOBS))]
    private void ReceiveRequestBlobs(WIZARDHOUSING_50_PROTOCOL.MSG_REQUEST_BLOBS message) {
        var house = _house;
        if (house is null) {
            Logger.Warning("Character {CharId} requested house blobs outside a house.", Logger.Args(GetActiveWizard().CharId));

            return;
        }

        if (!HouseBlobCodec.ParseBlobRequest(message.Data, out var request)) {
            Logger.Warning("Character {CharId} sent a malformed blob request.", Logger.Args(GetActiveWizard().CharId));

            return;
        }

        // The version, count and user data in the request are the client's cache state, or 0 in a resync. Neither
        // decides what is sent.
        if (HouseBlobCodec.RequestedItemBlobs(request, house.LotGid) == 0) {
            Logger.Warning("Character {CharId} requested blobs that are not the item blob of lot {LotGid}.",
                Logger.Args(GetActiveWizard().CharId, house.LotGid));

            return;
        }

        SendBlob(house);
    }

    private void LeaveHouse() {
        if (_house is null) {
            return;
        }

        HouseRegistry.Release(_house);
        _house = null;
    }

    private void SendBlob(HouseState house) {
        var snapshot = house.Snapshot();
        try {
            SendToSocket(new WIZARDHOUSING_50_PROTOCOL.MSG_SEND_BLOB {
                Data = HouseBlobCodec.BuildBlob(house.LotGid, snapshot.Version, snapshot.Objects, HouseBlobCodec.EpochDaysNow()),
                UserData = house.GidBase,
            });
        } catch (InvalidOperationException exception) {
            Logger.Error("Could not build the blob of lot {LotGid}: {Reason}", Logger.Args(house.LotGid, exception.Message));
        }
    }

}
