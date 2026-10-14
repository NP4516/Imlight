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
using System.Linq;
using Akka.Actor;
using Imcodec.MessageLayer.Generated;
using Imcodec.ObjectProperty.TypeCache;
using Imlight.Common;
using Imlight.CoreLib.Game.Housing;
using Imlight.CoreLib.Shared.Items;
using Imlight.CoreLib.Shared.Networking;
using Imlight.CoreLib.Shared.Packets;
using Imlight.CoreLib.Shared.Resources;
using Imlight.CoreLib.WizardData.Collections;
using Imlight.CoreLib.WizardData.Models.Misc;
using Imlight.CoreLib.WizardData.Models.Player;

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

    [MessageHandler(typeof(WIZARDHOUSING_50_PROTOCOL.MSG_PLACEHOUSINGOBJECT))]
    private void ReceivePlaceHousingObject(WIZARDHOUSING_50_PROTOCOL.MSG_PLACEHOUSINGOBJECT message) {
        if (!TryGetHouse("place an object", out var house)) {
            return;
        }

        // The item and its template come from the server's own inventory, never from the client.
        var wizard = GetActiveWizard();
        var item = wizard.InventoryBehavior.GetItem(message.ObjectID);
        if (item is null || message.SwitchCastleBlock != 0) {
            HandleForgedRequest($"place item {message.ObjectID} that is not in the backpack or is a castle block");

            return;
        }

        var templateId = (uint) Math.Min((ulong) item.m_templateID, uint.MaxValue);
        if (!HousingRules.TryGetPlaceable(templateId, out var template)) {
            HandleForgedRequest($"place item {message.ObjectID}, template {templateId}, which is not placeable furniture");

            return;
        }

        var placed = HouseBlobCodec.FromPlacement(templateId, message.LocX, message.LocY, message.LocZ, message.Yaw);
        if (placed is null) {
            HandleForgedRequest($"place item {message.ObjectID} at a position that is not valid");

            return;
        }

        if (house.LiveCount >= house.ItemLimit
            || HousingRules.TemplateCopyLimitReached(template, HousingRules.CountPlaced(house.Snapshot().Objects, templateId))) {
            InformGameClient("This house cannot hold any more of that.");

            return;
        }

        // The backpack changes in memory only and is written together with the house, so a crash between
        // the two cannot lose the item or leave it in both places.
        var itemId = item.m_globalID;
        if (!wizard.InventoryBehavior.RemoveItem(item)) {
            return;
        }

        if (!house.TryAdd(placed, out var slot, out var version, stored => HouseCollection.SaveWithBackpack(stored, wizard))) {
            if (!wizard.InventoryBehavior.AddItem(item)) {
                Logger.Error("Could not return item {ItemId} to the backpack of character {CharId} after it failed to be placed.",
                    Logger.Args(itemId, wizard.CharId));
            }

            InformGameClient("The object could not be placed.");

            return;
        }

        ItemSync.SendRemoved(SessionActor.ActorRef, wizard, itemId);
        var (x, y) = HouseBlobCodec.UnpackLocation(placed.m_positionXY);
        ZoneBroadcast(new WIZARDHOUSING_50_PROTOCOL.MSG_PATCHADDHOUSINGOBJECT {
            ObjectID = templateId,
            LocX = x,
            LocY = y,
            LocZ = placed.m_positionZ,
            Yaw = HouseBlobCodec.UnpackYaw(placed),
            SubType = HouseBlobCodec.ItemSubType,
            PackageNumber = 0,
            VersionNumber = version,
            GIDID = HouseBlobCodec.GidIndex(house.GidBase, slot),
            ColorBits = 0,
        }, isSelfless: false, ZoneBroadcastTarget.Players);

        DeleteStoredItem(wizard.CharId, itemId);
    }

    [MessageHandler(typeof(WIZARDHOUSING_50_PROTOCOL.MSG_UPDATEHOUSINGOBJECT))]
    private void ReceiveUpdateHousingObject(WIZARDHOUSING_50_PROTOCOL.MSG_UPDATEHOUSINGOBJECT message) {
        if (!TryGetHouse("move an object", out var house)) {
            return;
        }

        if (message.SwitchCastleBlock != 0 || message.UseExtendedYaw is not (0 or 1)
            || !HouseBlobCodec.TrySlotFromGid(house.GidBase, message.ObjectGID, house.SlotCount, out var slot)) {
            HandleForgedRequest($"move object {message.ObjectGID}, which is not an object of this house");

            return;
        }

        HousingBlobObject updated = null;
        var invalid = false;
        var done = house.TryUpdate(slot, current => {
            updated = HouseBlobCodec.ApplyUpdate(current, message.LocX, message.LocY, message.LocZ, message.Yaw,
                                                 message.UseExtendedYaw == 1, message.Scale, message.Brightness);
            invalid = updated is null;

            return updated;
        }, out var version);
        if (invalid) {
            HandleForgedRequest($"move object {message.ObjectGID} to a position, scale or brightness that is not valid");

            return;
        }

        if (!done) {
            // The object is gone or the write failed. The client moved it already, so give it the real state.
            SendBlob(house);

            return;
        }

        var (x, y) = HouseBlobCodec.UnpackLocation(updated.m_positionXY);
        ZoneBroadcast(new WIZARDHOUSING_50_PROTOCOL.MSG_PATCHUPDATEHOUSINGOBJECT {
            LocX = x,
            LocY = y,
            LocZ = updated.m_positionZ,
            Yaw = HouseBlobCodec.UnpackYaw(updated),
            SubType = HouseBlobCodec.ItemSubType,
            PackageNumber = 0,
            VersionNumber = version,
            GIDID = HouseBlobCodec.GidIndex(house.GidBase, slot),
            ObjectID = HouseBlobCodec.TemplateId(updated),
            SwitchTemplateID = 0,
            UseExtendedYaw = (sbyte) (HouseBlobCodec.UsesExtendedYaw(updated) ? 1 : 0),
            Scale = HouseBlobCodec.GetScale(updated),
            Brightness = (sbyte) HouseBlobCodec.GetBrightness(updated),
        }, isSelfless: false, ZoneBroadcastTarget.Players);
    }

    [MessageHandler(typeof(GAME_5_PROTOCOL.MSG_PICKUPOBJECT))]
    private void ReceivePickUpObject(GAME_5_PROTOCOL.MSG_PICKUPOBJECT message) {
        // Other features use this message too, so a GID that is not one of this house's objects is not ours.
        var house = _house;
        if (house is null || !HouseBlobCodec.TrySlotFromGid(house.GidBase, message.GameObjectID, house.SlotCount, out var slot)) {
            return;
        }

        var wizard = GetActiveWizard();
        var snapshot = house.Snapshot();
        if (slot >= snapshot.Objects.Count) {
            return;
        }

        var templateId = HouseBlobCodec.TemplateId(snapshot.Objects[slot]);
        if (templateId == 0
            || CoreObjectFactory.GetCoreTemplate(templateId) is not WizItemTemplate template
            || wizard.InventoryBehavior.FreeSlots <= 0
            || HousingRules.TemplateCopyLimitReached(template, wizard.InventoryBehavior.Items.Count(i => i.m_templateID == templateId))) {
            RefusePickUp(house);

            return;
        }

        // A crash between the two writes loses the item instead of duplicating it.
        if (!house.TryRemove(slot, out var removed, out var version)) {
            RefusePickUp(house);

            return;
        }

        if (!wizard.AddItemToInventory(templateId, out var item)) {
            house.TryRestore(slot, removed, out _);
            RefusePickUp(house);

            return;
        }

        if (!ItemSync.SendAdded(SessionActor.ActorRef, wizard, item)) {
            wizard.RemoveItemFromInventory(item.m_globalID);
            WizardItemCollection.RemoveItem(wizard.CharId, item.m_globalID);
            house.TryRestore(slot, removed, out _);
            RefusePickUp(house);

            return;
        }

        ZoneBroadcast(new WIZARDHOUSING_50_PROTOCOL.MSG_PATCHDELETEHOUSINGOBJECT {
            SubType = HouseBlobCodec.ItemSubType,
            PackageNumber = 0,
            VersionNumber = version,
            GIDID = HouseBlobCodec.GidIndex(house.GidBase, slot),
        }, isSelfless: false, ZoneBroadcastTarget.Players);
    }

    private bool TryGetHouse(string request, out HouseState house) {
        house = _house;
        if (house is not null) {
            return true;
        }

        HandleForgedRequest($"{request} outside a house");

        return false;
    }

    private void HandleForgedRequest(string request) {
        var wizard = GetActiveWizard();
        wizard.Account?.AddInfraction(InfractionType.SuspiciousBehavior, $"Tried to {request}.");
        Logger.Warning("Character {CharId} tried to {Request}. This has been logged as suspicious behavior.",
            Logger.Args(wizard.CharId, request));
        InformGameClient("That request was refused.");
    }

    private static void DeleteStoredItem(ulong charId, ulong itemId) {
        // The item already left the backpack, so only an unused document is left to delete.
        try {
            if (!WizardItemCollection.RemoveItem(charId, itemId)) {
                Logger.Warning("Placed item {ItemId} of character {CharId} has no stored item to delete.", Logger.Args(itemId, charId));
            }
        } catch (Exception exception) {
            Logger.Warning("Could not delete the stored item {ItemId} of character {CharId}: {Reason}",
                Logger.Args(itemId, charId, exception.Message));
        }
    }

    private void RefusePickUp(HouseState house) {
        // The client has faded the object already and the refusal does not bring it back, so the blob rebuilds it.
        SendToSocket(new WIZARDHOUSING_50_PROTOCOL.MSG_HOUSINGOBJECTNOPICKUP());
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
