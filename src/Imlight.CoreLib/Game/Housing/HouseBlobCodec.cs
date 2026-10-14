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
 * HOUSING SYSTEM
 * ========================================================================
 *
 * PURPOSE:
 * Packs and unpacks the objects of a house blob and builds the blob and blob request the client
 * expects. The client builds every placed object from the blob and patches its own cached copy
 * with the same bit math, so the packing here must match it exactly.
 *
 * USAGE EXAMPLE:
 * var obj = HouseBlobCodec.FromPlacement(templateId, x, y, z, yawRadians);
 * var blob = HouseBlobCodec.BuildBlob(lotGid, version, objects, HouseBlobCodec.EpochDaysNow());
 *
 * NOTE:
 * - An object whose low 28 template bits are 0 is a removed slot. It stays in the list so slot
 *   indexes never shift, and the client does not count it.
 * - Which bits the client clears when it switches an object between the eight-way and the
 *   extended yaw form is not observable by the server.
 * - The packed layout is documented in the housing plan, "Buffers and serialized objects".
 *
 * TODO:
 *
 * Created by: Jay
 * Version: KALI 1.0
 * Last Updated: 10/14/2026
 */

using System;
using System.Collections.Generic;
using System.Linq;
using Imcodec.IO;
using Imcodec.ObjectProperty;
using Imcodec.ObjectProperty.TypeCache;
using Imlight.Common;

namespace Imlight.CoreLib.Game.Housing;

/// <summary>
/// Builds and reads the housing blob and its packed objects.
/// </summary>
internal static class HouseBlobCodec {

    internal const string BlobType = "Housing";
    internal const string ItemSubType = "Item";
    internal const int MaxCoordinateMagnitude = 0x7FFF;
    internal const float MinScale = 0.10f;
    internal const float MaxScale = 4.0f;
    internal const int MaxBrightness = 100;
    internal const int MaxSerializedLength = 65535;
    internal const int MaxSlotsThatFit = (MaxSerializedLength - BlobHeaderLength) / BlobObjectLength;

    private const int BlobHeaderLength = 47;
    private const int BlobObjectLength = 21;
    private const int MaxRequestLength = 4096;
    private const int HousingGidType = 9;
    private const uint SerializeMask = 1;
    private const uint TemplateIdMask = 0x0FFFFFFF;
    private const uint RemapFlag = 0x20000000;
    private const uint ExtendedYawFlag = 0x80000000;
    private const uint ExtendedYawOver255Flag = 0x40000000;
    private const uint SectorMask = 0x07;
    private const byte ColorBitsMask = 0xF8;
    private const uint ScaleShift = 8;
    private const uint ScaleMask = 0xFF;
    private const uint ExtendedYawShift = 16;
    private const uint ExtendedYawMask = 0xFF;
    private const uint BrightnessShift = 24;
    private const uint BrightnessMask = 0x7F;
    private const byte FirstFineScaleCode = 0x1A;
    private const byte FirstQuarterScaleCode = 0x1D;
    private const byte LastQuarterScaleCode = 0x2C;
    private const int DegreesPerSector = 45;

    // The sector codes are not in compass order: 4 is 45 degrees, 1 is 90, and so on.
    private static readonly byte[] s_sectorByDirection = [0, 4, 1, 5, 2, 6, 3, 7];
    private static readonly float[] s_sectorThresholds = [
        0.3927f, 1.1781f, 1.9635f, 2.7489f, 3.5343f, 4.3197f, 5.1051f, 5.8905f
    ];
    private static readonly (byte Code, float Value)[] s_scaleSteps = BuildScaleSteps();

    /// <summary>
    /// Gets the template id of a blob object, without the flag bits.
    /// </summary>
    /// <param name="obj">The blob object.</param>
    /// <returns>The template id, or 0 for a removed slot.</returns>
    internal static uint TemplateId(HousingBlobObject obj)
        => obj.m_gameObjectTemplateID & TemplateIdMask;

    /// <summary>
    /// Checks whether a blob object is placed. A removed slot has template id 0.
    /// </summary>
    /// <param name="obj">The blob object.</param>
    /// <returns><c>true</c> for a placed object.</returns>
    internal static bool IsLive(HousingBlobObject obj)
        => obj is not null && TemplateId(obj) != 0;

    /// <summary>
    /// Counts the placed objects. The client compares this count, not the list length, with its cache.
    /// </summary>
    /// <param name="objects">The blob objects, removed slots included.</param>
    /// <returns>The number of placed objects.</returns>
    internal static int CountLive(IEnumerable<HousingBlobObject> objects)
        => objects.Count(IsLive);

    /// <summary>
    /// Packs a position into the layout of <c>m_positionXY</c>: X in the high 16 bits, Y in the low 16,
    /// each as a sign and a 15-bit magnitude.
    /// </summary>
    /// <param name="x">The X coordinate.</param>
    /// <param name="y">The Y coordinate.</param>
    /// <param name="packed">The packed position when this returns <c>true</c>.</param>
    /// <returns><c>false</c> when a coordinate is not finite or its magnitude does not fit.</returns>
    internal static bool PackLocation(float x, float y, out uint packed) {
        packed = 0;
        if (!TryPackCoordinate(x, out var packedX) || !TryPackCoordinate(y, out var packedY)) {
            return false;
        }

        packed = (packedX << 16) | packedY;

        return true;
    }

    /// <summary>
    /// Unpacks <c>m_positionXY</c>.
    /// </summary>
    /// <param name="packed">The packed position.</param>
    /// <returns>The X and Y coordinates.</returns>
    internal static (float X, float Y) UnpackLocation(uint packed)
        => (UnpackCoordinate(packed >> 16), UnpackCoordinate(packed & 0xFFFF));

    /// <summary>
    /// Picks the eight-way sector code of a yaw.
    /// </summary>
    /// <param name="yawRadians">The yaw in radians.</param>
    /// <returns>The sector code, 0 to 7.</returns>
    internal static byte PackYawEightWay(float yawRadians) {
        var reduced = ReduceYaw(yawRadians);
        for (var direction = 0; direction < s_sectorThresholds.Length; direction++) {
            if (reduced < s_sectorThresholds[direction]) {
                return s_sectorByDirection[direction];
            }
        }

        return 0;
    }

    /// <summary>
    /// Converts a yaw to the whole degrees the extended form stores.
    /// </summary>
    /// <param name="yawRadians">The yaw in radians.</param>
    /// <returns>The degrees, 0 to 359.</returns>
    internal static int PackYawExtended(float yawRadians)
        => (int) (ReduceYaw(yawRadians) * 180 / MathF.PI) % 360;

    /// <summary>
    /// Sets the yaw of a blob object to the nearest of the eight directions.
    /// </summary>
    /// <param name="obj">The object to change.</param>
    /// <param name="yawRadians">The yaw in radians.</param>
    internal static void SetYawEightWay(HousingBlobObject obj, float yawRadians) {
        obj.m_yaw = (byte) ((obj.m_yaw & ColorBitsMask) | PackYawEightWay(yawRadians));
        obj.m_gameObjectTemplateID &= ~(ExtendedYawFlag | ExtendedYawOver255Flag);
        obj.m_extraData1 &= ~(ExtendedYawMask << (int) ExtendedYawShift);
    }

    /// <summary>
    /// Sets the yaw of a blob object to whole degrees. A multiple of 45 degrees uses the eight-way form.
    /// </summary>
    /// <param name="obj">The object to change.</param>
    /// <param name="yawRadians">The yaw in radians.</param>
    internal static void SetYawExtended(HousingBlobObject obj, float yawRadians) {
        var degrees = PackYawExtended(yawRadians);
        if (degrees % DegreesPerSector == 0) {
            SetYawEightWay(obj, yawRadians);

            return;
        }

        obj.m_gameObjectTemplateID |= ExtendedYawFlag;
        obj.m_gameObjectTemplateID = degrees > 255
            ? obj.m_gameObjectTemplateID | ExtendedYawOver255Flag
            : obj.m_gameObjectTemplateID & ~ExtendedYawOver255Flag;
        obj.m_extraData1 = (obj.m_extraData1 & ~(ExtendedYawMask << (int) ExtendedYawShift))
            | (((uint) degrees & ExtendedYawMask) << (int) ExtendedYawShift);
    }

    /// <summary>
    /// Checks whether a blob object stores its yaw in whole degrees instead of eight directions.
    /// </summary>
    /// <param name="obj">The blob object.</param>
    /// <returns><c>true</c> for the extended form.</returns>
    internal static bool UsesExtendedYaw(HousingBlobObject obj)
        => (obj.m_gameObjectTemplateID & ExtendedYawFlag) != 0;

    /// <summary>
    /// Reads the yaw of a blob object.
    /// </summary>
    /// <param name="obj">The blob object.</param>
    /// <returns>The yaw in radians.</returns>
    internal static float UnpackYaw(HousingBlobObject obj) {
        int degrees;
        if ((obj.m_gameObjectTemplateID & ExtendedYawFlag) != 0) {
            degrees = (int) ((obj.m_extraData1 >> (int) ExtendedYawShift) & ExtendedYawMask);
            if ((obj.m_gameObjectTemplateID & ExtendedYawOver255Flag) != 0) {
                degrees += 256;
            }
        } else {
            degrees = Array.IndexOf(s_sectorByDirection, (byte) (obj.m_yaw & SectorMask)) * DegreesPerSector;
        }

        return degrees * MathF.PI / 180;
    }

    /// <summary>
    /// Picks the scale code nearest to a scale. Code 0 means 1.0.
    /// </summary>
    /// <param name="scale">The scale, from <see cref="MinScale"/> to <see cref="MaxScale"/>.</param>
    /// <returns>The scale code.</returns>
    internal static byte PackScale(float scale) {
        var best = s_scaleSteps[0];
        foreach (var step in s_scaleSteps) {
            if (MathF.Abs(step.Value - scale) < MathF.Abs(best.Value - scale)) {
                best = step;
            }
        }

        return best.Code;
    }

    /// <summary>
    /// Converts a scale code to its scale.
    /// </summary>
    /// <param name="code">The scale code.</param>
    /// <returns>The scale, capped at <see cref="MaxScale"/>.</returns>
    internal static float UnpackScale(byte code) => code switch {
        0 => 1.0f,
        FirstFineScaleCode => 0.10f,
        FirstFineScaleCode + 1 => 0.15f,
        FirstFineScaleCode + 2 => 0.20f,
        >= FirstQuarterScaleCode => MathF.Min(0.25f * (code - FirstQuarterScaleCode) + 0.25f, MaxScale),
        _ => 1.0f
    };

    /// <summary>
    /// Gets the scale of a blob object.
    /// </summary>
    /// <param name="obj">The blob object.</param>
    /// <returns>The scale.</returns>
    internal static float GetScale(HousingBlobObject obj)
        => UnpackScale((byte) ((obj.m_extraData1 >> (int) ScaleShift) & ScaleMask));

    /// <summary>
    /// Packs a brightness into the 7-bit field of <c>m_extraData1</c>, which stores one more than the
    /// brightness so that 0 can mean unset.
    /// </summary>
    /// <param name="brightness">The brightness, 0 to <see cref="MaxBrightness"/>.</param>
    /// <returns>The field value.</returns>
    internal static uint PackBrightness(int brightness)
        => (uint) Math.Clamp(brightness, 0, MaxBrightness) + 1;

    /// <summary>
    /// Gets the brightness of a blob object. An unset field is full brightness.
    /// </summary>
    /// <param name="obj">The blob object.</param>
    /// <returns>The brightness, 0 to <see cref="MaxBrightness"/>.</returns>
    internal static int GetBrightness(HousingBlobObject obj) {
        var field = (obj.m_extraData1 >> (int) BrightnessShift) & BrightnessMask;

        return field == 0 ? MaxBrightness : (int) field - 1;
    }

    /// <summary>
    /// Creates the blob object for a freshly placed item. Placement always uses the eight-way yaw.
    /// </summary>
    /// <param name="templateId">The item's template id.</param>
    /// <param name="x">The X coordinate.</param>
    /// <param name="y">The Y coordinate.</param>
    /// <param name="z">The Z coordinate.</param>
    /// <param name="yawRadians">The yaw in radians.</param>
    /// <returns>The object, or <c>null</c> when the template id is 0 or too large, or a value is not finite or does not fit.</returns>
    internal static HousingBlobObject FromPlacement(uint templateId, float x, float y, float z, float yawRadians) {
        if (templateId == 0 || templateId > TemplateIdMask || !float.IsFinite(z) || !float.IsFinite(yawRadians)
            || !PackLocation(x, y, out var packedLocation)) {
            return null;
        }

        var obj = new HousingBlobObject {
            m_gameObjectTemplateID = templateId,
            m_positionXY = packedLocation,
            m_positionZ = z,
            m_yaw = 0,
            m_extraData1 = 0,
        };
        SetYawEightWay(obj, yawRadians);

        return obj;
    }

    /// <summary>
    /// Creates the blob object for a moved, turned, scaled or relit object. The result is a copy:
    /// the template, color and remap bits of <paramref name="current"/> are kept.
    /// </summary>
    /// <param name="current">The object as it is stored.</param>
    /// <param name="x">The new X coordinate.</param>
    /// <param name="y">The new Y coordinate.</param>
    /// <param name="z">The new Z coordinate.</param>
    /// <param name="yawRadians">The new yaw in radians.</param>
    /// <param name="useExtendedYaw">Whether the yaw is stored in whole degrees instead of eight directions.</param>
    /// <param name="scale">The new scale.</param>
    /// <param name="brightness">The new brightness.</param>
    /// <returns>The updated copy, or <c>null</c> when a value is not finite or is out of its range.</returns>
    internal static HousingBlobObject ApplyUpdate(HousingBlobObject current,
                                                  float x,
                                                  float y,
                                                  float z,
                                                  float yawRadians,
                                                  bool useExtendedYaw,
                                                  float scale,
                                                  int brightness) {
        if (!float.IsFinite(z) || !float.IsFinite(yawRadians) || !float.IsFinite(scale)
            || scale < MinScale || scale > MaxScale || brightness < 0 || brightness > MaxBrightness
            || !PackLocation(x, y, out var packedLocation)) {
            return null;
        }

        var updated = new HousingBlobObject {
            m_gameObjectTemplateID = current.m_gameObjectTemplateID,
            m_positionXY = packedLocation,
            m_positionZ = z,
            m_yaw = current.m_yaw,
            m_extraData1 = current.m_extraData1,
        };
        if (useExtendedYaw) {
            SetYawExtended(updated, yawRadians);
        } else {
            SetYawEightWay(updated, yawRadians);
        }

        updated.m_extraData1 = (updated.m_extraData1 & ~((ScaleMask << (int) ScaleShift) | (BrightnessMask << (int) BrightnessShift)))
            | ((uint) PackScale(scale) << (int) ScaleShift)
            | (PackBrightness(brightness) << (int) BrightnessShift);

        return updated;
    }

    /// <summary>
    /// Creates the blob objects of a house layout file.
    /// </summary>
    /// <param name="layout">The layout, such as the Dorm Room's.</param>
    /// <returns>The blob objects in layout order. An object that cannot be packed is logged and left out.</returns>
    internal static List<HousingBlobObject> FromLayout(HousingLayout layout) {
        var objects = new List<HousingBlobObject>(layout.m_housingLayoutObjectList?.Count ?? 0);
        foreach (var layoutObject in layout.m_housingLayoutObjectList ?? []) {
            var obj = layoutObject is null
                ? null
                : FromPlacement(layoutObject.m_templateID,
                    layoutObject.m_positionX,
                    layoutObject.m_positionY,
                    layoutObject.m_positionZ,
                    layoutObject.m_yaw);
            if (obj is null) {
                Logger.Error("A housing layout object cannot be packed: {Template}",
                    Logger.Args(layoutObject?.m_templateID ?? 0));

                continue;
            }

            objects.Add(obj);
        }

        return objects;
    }

    /// <summary>
    /// Draws the base the client numbers a house's blob entries from. The range leaves room for
    /// <see cref="MaxSlotsThatFit"/> entries above any base without wrapping past 2^32.
    /// </summary>
    /// <returns>A new GID base.</returns>
    internal static uint NewGidBase()
        => (uint) Random.Shared.NextInt64(0x80000000L, 0xFFFF0000L);

    /// <summary>
    /// Gets the <c>GIDID</c> of a slot: the base plus the slot, wrapping like the client's 32-bit sum.
    /// </summary>
    /// <param name="gidBase">The house's GID base.</param>
    /// <param name="slot">The slot, counting removed slots.</param>
    /// <returns>The <c>GIDID</c> of the slot's patches.</returns>
    internal static uint GidIndex(uint gidBase, int slot)
        => unchecked(gidBase + (uint) slot);

    /// <summary>
    /// Finds the slot a housing object GID names. The client builds the GID as
    /// <c>(u16 &lt;&lt; 48) | (9 &lt;&lt; 40) | GIDID</c>.
    /// </summary>
    /// <param name="gidBase">The house's GID base.</param>
    /// <param name="gid">The GID the client sent.</param>
    /// <param name="slotCount">The number of slots, counting removed slots.</param>
    /// <param name="slot">The slot when this returns <c>true</c>.</param>
    /// <returns><c>true</c> when the GID is a housing object GID inside the house.</returns>
    internal static bool TrySlotFromGid(uint gidBase, ulong gid, int slotCount, out int slot) {
        slot = -1;
        if (((gid >> 40) & 0xFF) != HousingGidType || (gid & 0x000000FF00000000UL) != 0 || slotCount <= 0) {
            return false;
        }

        var offset = unchecked((uint) gid - gidBase);
        if (offset >= (uint) slotCount) {
            return false;
        }

        slot = (int) offset;

        return true;
    }

    /// <summary>
    /// Gets the epoch days value the client expects in a blob definition.
    /// </summary>
    /// <param name="when">The time of the blob.</param>
    /// <returns>Minutes since the Unix epoch times 1440, wrapped to 32 bits as the client does.</returns>
    internal static uint EpochDays(DateTimeOffset when)
        => unchecked((uint) (when.ToUnixTimeSeconds() / 60 * 1440));

    /// <summary>
    /// Gets the epoch days value for the current time.
    /// </summary>
    /// <returns>The epoch days value.</returns>
    internal static uint EpochDaysNow() => EpochDays(DateTimeOffset.UtcNow);

    /// <summary>
    /// Builds the <c>Blob</c> that holds a house's placed objects.
    /// </summary>
    /// <param name="lotGid">The house's lot.</param>
    /// <param name="version">The blob version.</param>
    /// <param name="objects">The blob objects, removed slots included.</param>
    /// <param name="epochDays">The blob definition's epoch days.</param>
    /// <returns>The serialized blob, as <c>MSG_SEND_BLOB.Data</c>.</returns>
    /// <exception cref="InvalidOperationException">The blob cannot be serialized or does not fit a string field.</exception>
    internal static ByteString BuildBlob(ulong lotGid,
                                         uint version,
                                         IReadOnlyList<HousingBlobObject> objects,
                                         uint epochDays) {
        var blob = new Blob {
            m_type = BlobType,
            m_subType = ItemSubType,
            m_versionNumber = version,
            m_packageNumber = 0,
            m_associatedGID = lotGid,
            m_epochDays = epochDays,
            m_data = new HousingBlob { m_housingBlobObjectList = [.. objects] },
        };

        return SerializeBounded(blob);
    }

    /// <summary>
    /// Builds the blob request the server sends before login completes.
    /// </summary>
    /// <param name="lotGid">The house's lot.</param>
    /// <param name="version">The blob version.</param>
    /// <param name="gidBase">The house's GID base, sent as the entry's user data.</param>
    /// <param name="liveCount">The number of placed objects, not the list length.</param>
    /// <returns>The serialized request, as <c>MSG_REQUEST_BLOBS.Data</c>.</returns>
    /// <exception cref="InvalidOperationException">The request cannot be serialized.</exception>
    internal static ByteString BuildBlobRequest(ulong lotGid, uint version, uint gidBase, int liveCount) {
        var request = new BlobRequest {
            m_type = BlobType,
            m_associatedGID = lotGid,
            m_blobRequestObjectList = [
                new BlobRequestObject {
                    m_subType = ItemSubType,
                    m_versionNumber = version,
                    m_packageNumber = 0,
                    m_userData = gidBase,
                    m_objectCount = (uint) liveCount,
                }
            ],
        };

        return SerializeBounded(request);
    }

    /// <summary>
    /// Parses the blob request a client sent. The bytes must be exactly what the serializer writes for the
    /// parsed request, so truncated, padded and malformed payloads are rejected.
    /// </summary>
    /// <param name="data">The client's <c>MSG_REQUEST_BLOBS.Data</c>.</param>
    /// <param name="request">The request when this returns <c>true</c>.</param>
    /// <returns><c>true</c> when the payload is a well-formed <c>BlobRequest</c>.</returns>
    internal static bool ParseBlobRequest(ByteString data, out BlobRequest request) {
        request = null;
        byte[] bytes = data;
        if (bytes is null || bytes.Length == 0 || bytes.Length > MaxRequestLength) {
            return false;
        }

        try {
            var serializer = NewSerializer();
            if (!serializer.Deserialize<BlobRequest>(bytes, SerializeMask, out var parsed) || parsed is null) {
                return false;
            }

            if (!serializer.Serialize(parsed, SerializeMask, out var canonical)
                || !((byte[]) canonical).AsSpan().SequenceEqual(bytes)) {
                return false;
            }

            request = parsed;

            return true;
        } catch (Exception) {
            return false;
        }
    }

    /// <summary>
    /// Counts the item blobs a request asks for. The client's version, object count and user data are
    /// ignored: a resync request carries the version of the patch it could not apply and no user data.
    /// </summary>
    /// <param name="request">The parsed request.</param>
    /// <param name="lotGid">The lot the player is attached to.</param>
    /// <returns>1 when the request names the lot's item blob, otherwise 0.</returns>
    internal static int RequestedItemBlobs(BlobRequest request, ulong lotGid) {
        if (request is null || request.m_type != BlobType || request.m_associatedGID != lotGid) {
            return 0;
        }

        return request.m_blobRequestObjectList?.Any(entry => entry is not null
            && entry.m_subType == ItemSubType
            && entry.m_packageNumber == 0) == true ? 1 : 0;
    }

    private static bool TryPackCoordinate(float value, out uint packed) {
        packed = 0;
        if (!float.IsFinite(value)) {
            return false;
        }

        if (MathF.Abs(value) > MaxCoordinateMagnitude + 0.5f) {
            return false;
        }

        var magnitude = (uint) (value >= 0 ? value + 0.5f : 0.5f - value);
        if (magnitude > MaxCoordinateMagnitude) {
            return false;
        }

        packed = value >= 0 ? magnitude : 0x8000 + magnitude;

        return true;
    }

    private static float UnpackCoordinate(uint packed)
        => packed < 0x8000 ? packed : -(int) (packed - 0x8000);

    private static float ReduceYaw(float yawRadians) {
        const float TwoPi = MathF.PI * 2;

        var reduced = yawRadians % TwoPi;

        return reduced < 0 ? reduced + TwoPi : reduced;
    }

    private static (byte Code, float Value)[] BuildScaleSteps() {
        var steps = new List<(byte, float)> { (0, 1.0f) };
        for (byte code = FirstFineScaleCode; code <= LastQuarterScaleCode; code++) {
            steps.Add((code, UnpackScale(code)));
        }

        return [.. steps];
    }

    private static ObjectSerializer NewSerializer()
        => new(Versionable: false, Behaviors: SerializerFlags.None);

    private static ByteString SerializeBounded(PropertyClass value) {
        if (!NewSerializer().Serialize(value, SerializeMask, out var serialized)) {
            throw new InvalidOperationException($"Could not serialize {value.GetType().Name}.");
        }

        if (serialized.Length > MaxSerializedLength) {
            throw new InvalidOperationException(
                $"{value.GetType().Name} is {serialized.Length} bytes, past the {MaxSerializedLength} byte string limit.");
        }

        return serialized;
    }

}
