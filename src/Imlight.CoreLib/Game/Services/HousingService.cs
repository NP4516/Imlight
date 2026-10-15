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
 *   later edits arrive as small versioned patches. The blob and the patches are built here, from the stored
 *   items, with the generated Imcodec types; the house is in Game/Housing.
 * - The service counts the player into the house at MSG_PRELOGIN and out when the session ends.
 * - Only the owner decorates. A visitor sees the same blob and patches, and every edit they send is refused as forged.
 *
 * TODO:
 *
 * Created by: Jay
 * Version: KALI 1.0
 * Last Updated: 10/15/2026
 */

using System;
using System.Linq;
using Akka.Actor;
using Imcodec.IO;
using Imcodec.MessageLayer.Generated;
using Imcodec.ObjectProperty;
using Imcodec.ObjectProperty.TypeCache;
using Imlight.Common;
using Imlight.CoreLib.Game.Housing;
using Imlight.CoreLib.Game.Housing.Families;
using Imlight.CoreLib.Shared.Items;
using Imlight.CoreLib.Shared.Networking;
using Imlight.CoreLib.Shared.Packets;
using Imlight.CoreLib.Shared.Resources;
using Imlight.CoreLib.WizardData.Collections;
using Imlight.CoreLib.WizardData.Models.Misc;
using Imlight.CoreLib.WizardData.Models.Player;

namespace Imlight.CoreLib.Game.Services;

internal class HousingService(SessionActor sessionActor) : MessageService(sessionActor) {

    private const string BlobType = "Housing";
    private const string ItemSubType = "Item";
    private const int MaxRequestLength = 4096;
    private const int MaxSerializedLength = 65535;
    private const uint SerializeMask = 1;

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

        if (!HouseRegistry.TryEnter(wizard.AttachedLotGid, out var house)) {
            Logger.Warning("Character {CharId} attached to lot {LotGid}, which is not a house.",
                Logger.Args(wizard.CharId, wizard.AttachedLotGid));

            return;
        }

        _house = house;

        // The client compares this with its cached copy and asks for the blob only when they differ.
        var snapshot = house.Snapshot();
        var request = new BlobRequest {
            m_type = BlobType,
            m_associatedGID = house.LotGid,
            m_blobRequestObjectList = [
                new BlobRequestObject {
                    m_subType = ItemSubType,
                    m_versionNumber = snapshot.Revision,
                    m_packageNumber = 0,
                    m_userData = snapshot.GidBase,
                    m_objectCount = (uint) snapshot.LiveCount,
                }
            ],
        };
        SendToSocket(new WIZARDHOUSING_50_PROTOCOL.MSG_REQUEST_BLOBS { Data = Serialize(request) });
    }

    [MessageHandler(typeof(WIZARDHOUSING_50_PROTOCOL.MSG_REQUEST_BLOBS))]
    private void ReceiveRequestBlobs(WIZARDHOUSING_50_PROTOCOL.MSG_REQUEST_BLOBS message) {
        var house = _house;
        if (house is null) {
            Logger.Warning("Character {CharId} requested house blobs outside a house.", Logger.Args(GetActiveWizard().CharId));

            return;
        }

        if (!TryParseRequest(message.Data, out var request)) {
            Logger.Warning("Character {CharId} sent a malformed blob request.", Logger.Args(GetActiveWizard().CharId));

            return;
        }

        // The version, count and user data in the request are the client's cache state, or 0 in a resync. Neither
        // decides what is sent.
        if (!RequestsItemBlob(request, house.LotGid)) {
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
        if (item is null) {
            HandleForgedRequest($"place item {message.ObjectID} that is not in the backpack");

            return;
        }

        var templateId = (uint) Math.Min((ulong) item.m_templateID, uint.MaxValue);
        var placement = HousingItems.Check(templateId, out var template, out var family);
        if (placement == HousingPlacement.Refused) {
            HandleForgedRequest($"place item {message.ObjectID}, template {templateId}, which is not placeable furniture");

            return;
        }

        var switchesBlock = message.SwitchCastleBlock != 0 && !HousingFamilies.CastleBlock.Enabled;
        if (placement == HousingPlacement.NotEnabled || switchesBlock) {
            Logger.Information("Character {CharId} tried to place item {ItemId} of the {Family} family, which is not enabled.",
                Logger.Args(wizard.CharId, message.ObjectID, placement == HousingPlacement.NotEnabled ? family.Id : HousingFamilies.CastleBlock.Id));
            InformGameClient("That kind of object cannot be placed in a house yet.");

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
            || HousingItems.TemplateCopyLimitReached(template, house.CountTemplate(templateId))) {
            InformGameClient("This house cannot hold any more of that.");

            return;
        }

        // The backpack changes in memory only and is written together with the house, so a crash between
        // the two cannot lose the item or leave it in both places.
        var itemId = item.m_globalID;
        if (!wizard.InventoryBehavior.RemoveItem(item)) {
            return;
        }

        var change = house.TryAdd(placed, document => HouseCollection.SaveWithBackpack(document, wizard));
        if (!change.Done) {
            if (!wizard.InventoryBehavior.AddItem(item)) {
                Logger.Error("Could not return item {ItemId} to the backpack of character {CharId} after it failed to be placed.",
                    Logger.Args(itemId, wizard.CharId));
            }

            if (change.Result == HouseEdit.NeedsResync) {
                SendBlob(house);
            }

            InformGameClient("The object could not be placed.");

            return;
        }

        ItemSync.SendRemoved(SessionActor.ActorRef, wizard, itemId);
        ZoneBroadcast(new WIZARDHOUSING_50_PROTOCOL.MSG_PATCHADDHOUSINGOBJECT {
            ObjectID = templateId,
            LocX = change.Item.X,
            LocY = change.Item.Y,
            LocZ = change.Item.Z,
            Yaw = change.Item.Yaw,
            SubType = ItemSubType,
            PackageNumber = 0,
            VersionNumber = change.Revision,
            GIDID = change.GidId,
            ColorBits = (sbyte) change.Item.Tint,
        }, isSelfless: false, ZoneBroadcastTarget.Players);

        DeleteStoredItem(wizard.CharId, itemId);
    }

    [MessageHandler(typeof(WIZARDHOUSING_50_PROTOCOL.MSG_UPDATEHOUSINGOBJECT))]
    private void ReceiveUpdateHousingObject(WIZARDHOUSING_50_PROTOCOL.MSG_UPDATEHOUSINGOBJECT message) {
        if (!TryGetHouse("move an object", out var house)) {
            return;
        }

        if (message.UseExtendedYaw is not (0 or 1) || !house.TryFindByGid(message.ObjectGID, out var current, out _)) {
            HandleForgedRequest($"move object {message.ObjectGID}, which is not an object of this house");

            return;
        }

        if (current is null) {
            // The object is gone. The client moved it already, so give it the real state.
            SendBlob(house);

            return;
        }

        if (message.SwitchCastleBlock != 0 && !HousingFamilies.CastleBlock.Enabled) {
            InformGameClient("That kind of object cannot be changed in a house yet.");
            SendBlob(house);

            return;
        }

        var invalid = false;
        var change = house.TryUpdate(current.Id, stored => {
            var replacement = (stored with {
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
        });
        if (invalid) {
            HandleForgedRequest($"move object {message.ObjectGID} to a position, scale or brightness that is not valid");

            return;
        }

        if (!change.Done) {
            // The object is gone or the write failed. The client moved it already, so give it the real state.
            SendBlob(house);

            return;
        }

        ZoneBroadcast(new WIZARDHOUSING_50_PROTOCOL.MSG_PATCHUPDATEHOUSINGOBJECT {
            LocX = change.Item.X,
            LocY = change.Item.Y,
            LocZ = change.Item.Z,
            Yaw = change.Item.Yaw,
            SubType = ItemSubType,
            PackageNumber = 0,
            VersionNumber = change.Revision,
            GIDID = change.GidId,
            ObjectID = change.Item.TemplateId,
            SwitchTemplateID = 0,
            UseExtendedYaw = (sbyte) (change.Item.ExactYaw ? 1 : 0),
            Scale = change.Item.Scale,
            Brightness = (sbyte) change.Item.Brightness,
        }, isSelfless: false, ZoneBroadcastTarget.Players);
    }

    [MessageHandler(typeof(GAME_5_PROTOCOL.MSG_PICKUPOBJECT))]
    private void ReceivePickUpObject(GAME_5_PROTOCOL.MSG_PICKUPOBJECT message) {
        // Other features use this message too, so a GID that is not one of this house's objects is not ours.
        var house = _house;
        if (house is null || !house.TryFindByGid(message.GameObjectID, out var placedItem, out _)) {
            return;
        }

        var wizard = GetActiveWizard();
        if (house.OwnerCharId != wizard.CharId) {
            HandleForgedRequest("pick up an object in a house that is not theirs");

            return;
        }

        if (placedItem is null
            || CoreObjectFactory.GetCoreTemplate(placedItem.TemplateId) is not WizItemTemplate template
            || wizard.InventoryBehavior.FreeSlots <= 0
            || HousingItems.TemplateCopyLimitReached(template, HousingItems.CountInBackpack(wizard, placedItem.TemplateId))) {
            RefusePickUp(house);

            return;
        }

        var item = HousingItems.CreateItem(wizard, placedItem.TemplateId, placedItem.ItemGid);
        if (item is null || !wizard.InventoryBehavior.AddItem(item)) {
            RefusePickUp(house);

            return;
        }

        // The new item and the emptied slot are written in one transaction, so a crash between them cannot lose the item.
        var change = house.TryRemove(placedItem.Id, document => HouseCollection.SaveWithBackpack(document, wizard, item));
        if (!change.Done) {
            wizard.InventoryBehavior.RemoveItem(item);
            RefusePickUp(house);

            return;
        }

        if (!ItemSync.SendAdded(SessionActor.ActorRef, wizard, item)) {
            Logger.Error("Could not tell the client about picked up item {ItemId} of character {CharId}.",
                Logger.Args(item.m_globalID, wizard.CharId));
        }

        ZoneBroadcast(new WIZARDHOUSING_50_PROTOCOL.MSG_PATCHDELETEHOUSINGOBJECT {
            SubType = ItemSubType,
            PackageNumber = 0,
            VersionNumber = change.Revision,
            GIDID = change.GidId,
        }, isSelfless: false, ZoneBroadcastTarget.Players);
    }

    private bool TryGetHouse(string request, out HouseState house) {
        house = _house;
        if (house is null) {
            HandleForgedRequest($"{request} outside a house");

            return false;
        }

        // Only the owner decorates. The client hides the controls from a visitor, so a request is forged.
        if (house.OwnerCharId != GetActiveWizard().CharId) {
            house = null;
            HandleForgedRequest($"{request} in a house that is not theirs");

            return false;
        }

        return true;
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
            var blob = new Blob {
                m_type = BlobType,
                m_subType = ItemSubType,
                m_versionNumber = snapshot.Revision,
                m_packageNumber = 0,
                m_associatedGID = house.LotGid,
                m_epochDays = EpochDaysNow(),
                m_data = new HousingBlob { m_housingBlobObjectList = [.. snapshot.Slots.Select(item => item?.ToBlobObject() ?? new HousingBlobObject())] },
            };
            SendToSocket(new WIZARDHOUSING_50_PROTOCOL.MSG_SEND_BLOB {
                Data = Serialize(blob),
                UserData = snapshot.GidBase,
            });
        } catch (InvalidOperationException exception) {
            Logger.Error("Could not build the blob of lot {LotGid}: {Reason}", Logger.Args(house.LotGid, exception.Message));
        }
    }

    private static uint EpochDaysNow()
        => unchecked((uint) (DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60 * 1440));

    private static ObjectSerializer NewSerializer()
        => new(Versionable: false, Behaviors: SerializerFlags.None);

    private static ByteString Serialize(PropertyClass value) {
        if (!NewSerializer().Serialize(value, SerializeMask, out var serialized)) {
            throw new InvalidOperationException($"Could not serialize {value.GetType().Name}.");
        }

        if (serialized.Length > MaxSerializedLength) {
            throw new InvalidOperationException(
                $"{value.GetType().Name} is {serialized.Length} bytes, past the {MaxSerializedLength} byte string limit.");
        }

        return serialized;
    }

    private static bool TryParseRequest(ByteString data, out BlobRequest request) {
        // The bytes must be exactly what the serializer writes for the parsed request, so truncated, padded and
        // malformed payloads are rejected.
        request = null;
        byte[] bytes = data;
        if (bytes is null || bytes.Length == 0 || bytes.Length > MaxRequestLength) {
            return false;
        }

        try {
            var serializer = NewSerializer();
            if (!serializer.Deserialize<BlobRequest>(bytes, SerializeMask, out var parsed) || parsed is null
                || !serializer.Serialize(parsed, SerializeMask, out var canonical)
                || !((byte[]) canonical).AsSpan().SequenceEqual(bytes)) {
                return false;
            }

            request = parsed;

            return true;
        } catch (Exception) {
            return false;
        }
    }

    private static bool RequestsItemBlob(BlobRequest request, ulong lotGid)
        => request.m_type == BlobType && request.m_associatedGID == lotGid
            && request.m_blobRequestObjectList?.Any(entry => entry is not null
                && entry.m_subType == ItemSubType && entry.m_packageNumber == 0) == true;

}
