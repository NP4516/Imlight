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
            Data = HouseBlobCodec.BuildBlobRequest(house.LotGid, snapshot.Revision, house.GidBase, snapshot.LiveCount),
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

        var placed = new PlacedItem {
            ItemGid = item.m_globalID,
            TemplateId = templateId,
            X = message.LocX,
            Y = message.LocY,
            Z = message.LocZ,
            Yaw = message.Yaw,
        }.Normalize();
        if (placed is null) {
            HandleForgedRequest($"place item {message.ObjectID} at a position that is not valid");

            return;
        }

        if (house.LiveCount >= house.ItemLimit
            || HousingRules.TemplateCopyLimitReached(template, HousingRules.CountPlaced(house.Snapshot().Slots, templateId))) {
            InformGameClient("This house cannot hold any more of that.");

            return;
        }

        // The backpack changes in memory only and is written together with the house, so a crash between
        // the two cannot lose the item or leave it in both places.
        var itemId = item.m_globalID;
        if (!wizard.InventoryBehavior.RemoveItem(item)) {
            return;
        }

        if (!house.TryAdd(placed, out var slot, out var stored, out var version, document => HouseCollection.SaveWithBackpack(document, wizard))) {
            if (!wizard.InventoryBehavior.AddItem(item)) {
                Logger.Error("Could not return item {ItemId} to the backpack of character {CharId} after it failed to be placed.",
                    Logger.Args(itemId, wizard.CharId));
            }

            InformGameClient("The object could not be placed.");

            return;
        }

        ItemSync.SendRemoved(SessionActor.ActorRef, wizard, itemId);
        ZoneBroadcast(new WIZARDHOUSING_50_PROTOCOL.MSG_PATCHADDHOUSINGOBJECT {
            ObjectID = templateId,
            LocX = stored.X,
            LocY = stored.Y,
            LocZ = stored.Z,
            Yaw = stored.Yaw,
            SubType = HouseBlobCodec.ItemSubType,
            PackageNumber = 0,
            VersionNumber = version,
            GIDID = HouseBlobCodec.GidIndex(house.GidBase, slot),
            ColorBits = (sbyte) stored.Tint,
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

        var invalid = false;
        var done = house.TryUpdate(slot, current => {
            var replacement = (current with {
                X = message.LocX,
                Y = message.LocY,
                Z = message.LocZ,
                Yaw = message.Yaw,
                ExactYaw = message.UseExtendedYaw == 1,
                Scale = message.Scale,
                Brightness = message.Brightness,
            }).Normalize();
            invalid = replacement is null;

            return replacement;
        }, out var updated, out var version);
        if (invalid) {
            HandleForgedRequest($"move object {message.ObjectGID} to a position, scale or brightness that is not valid");

            return;
        }

        if (!done) {
            // The object is gone or the write failed. The client moved it already, so give it the real state.
            SendBlob(house);

            return;
        }

        ZoneBroadcast(new WIZARDHOUSING_50_PROTOCOL.MSG_PATCHUPDATEHOUSINGOBJECT {
            LocX = updated.X,
            LocY = updated.Y,
            LocZ = updated.Z,
            Yaw = updated.Yaw,
            SubType = HouseBlobCodec.ItemSubType,
            PackageNumber = 0,
            VersionNumber = version,
            GIDID = HouseBlobCodec.GidIndex(house.GidBase, slot),
            ObjectID = updated.TemplateId,
            SwitchTemplateID = 0,
            UseExtendedYaw = (sbyte) (updated.ExactYaw ? 1 : 0),
            Scale = updated.Scale,
            Brightness = (sbyte) updated.Brightness,
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
        if (slot >= snapshot.Slots.Count) {
            return;
        }

        var templateId = snapshot.Slots[slot]?.TemplateId ?? 0;
        if (templateId == 0
            || CoreObjectFactory.GetCoreTemplate(templateId) is not WizItemTemplate template
            || wizard.InventoryBehavior.FreeSlots <= 0
            || HousingRules.TemplateCopyLimitReached(template, wizard.InventoryBehavior.Items.Count(i => i.m_templateID == templateId))) {
            RefusePickUp(house);

            return;
        }

        var item = HousingRules.CreateItem(wizard, templateId);
        if (item is null || !wizard.InventoryBehavior.AddItem(item)) {
            RefusePickUp(house);

            return;
        }

        // The new item and the emptied slot are written in one transaction, so a crash between them cannot lose the item.
        if (!house.TryRemove(slot, out _, out var version, document => HouseCollection.SaveWithBackpack(document, wizard, item))) {
            wizard.InventoryBehavior.RemoveItem(item);
            RefusePickUp(house);

            return;
        }

        if (!ItemSync.SendAdded(SessionActor.ActorRef, wizard, item)) {
            Logger.Error("Could not tell the client about picked up item {ItemId} of character {CharId}.",
                Logger.Args(item.m_globalID, wizard.CharId));
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
            // A removed slot is an empty entry, which the client numbers and does not count.
            var entries = snapshot.Slots.Select(item => item?.ToBlobObject() ?? new HousingBlobObject()).ToList();
            SendToSocket(new WIZARDHOUSING_50_PROTOCOL.MSG_SEND_BLOB {
                Data = HouseBlobCodec.BuildBlob(house.LotGid, snapshot.Revision, entries, HouseBlobCodec.EpochDaysNow()),
                UserData = house.GidBase,
            });
        } catch (InvalidOperationException exception) {
            Logger.Error("Could not build the blob of lot {LotGid}: {Reason}", Logger.Args(house.LotGid, exception.Message));
        }
    }

}
