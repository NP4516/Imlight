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
 * The live state of one house: its placed objects under one lock, persisted on every change, shared
 * by every player who is in the house.
 * 
 * USAGE EXAMPLE:
 * if (state.TryAdd(obj, out var slot, out var version)) {
 *     var gidId = HouseBlobCodec.GidIndex(state.GidBase, slot);
 * }
 * 
 * NOTE:
 * - Every change is written to the database before it returns. A failed write undoes the change
 *   in memory, so the stored house and the live house never differ.
 * - Stored objects are never changed in place; an update replaces the object.
 * - A removed object leaves a slot behind (template id 0), so slot indexes never shift. The slots
 *   are dropped when the first player enters a house nobody else is in.
 * - Use HouseRegistry.TryEnter and HouseRegistry.Release to count viewers.
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
using System.Threading;
using Imcodec.ObjectProperty.TypeCache;
using Imlight.CoreLib.WizardData.Collections;
using Imlight.CoreLib.WizardData.Models.Player;

namespace Imlight.CoreLib.Game.Housing;

/// <summary>
/// A consistent copy of a house for building a blob.
/// </summary>
/// <param name="Version">The blob version.</param>
/// <param name="LiveCount">The number of placed objects, not counting removed slots.</param>
/// <param name="Objects">The blob objects in slot order, removed slots included.</param>
internal readonly record struct HouseSnapshot(uint Version, int LiveCount, IReadOnlyList<HousingBlobObject> Objects);

/// <summary>
/// The live, persisted state of one house.
/// </summary>
internal sealed class HouseState {

    private readonly Lock _lock = new();
    private readonly House _house;
    private readonly HousingZoneInfo _zoneInfo;
    private int _viewers;
    private bool _retired;

    internal HouseState(House house, HousingZoneInfo zoneInfo, uint gidBase) {
        _house = house;
        _zoneInfo = zoneInfo;
        GidBase = gidBase;
    }

    /// <summary>
    /// The base the client numbers this house's blob entries from. Every viewer shares it.
    /// </summary>
    internal uint GidBase { get; }

    internal ulong LotGid => _house.LotGid;

    internal ulong OwnerCharId => _house.OwnerCharId;

    internal string ZoneName => _house.ZoneName;

    internal int LiveCount {
        get {
            lock (_lock) {
                return HouseBlobCodec.CountLive(_house.Objects);
            }
        }
    }

    internal int SlotCount {
        get {
            lock (_lock) {
                return _house.Objects.Count;
            }
        }
    }

    internal int ItemLimit {
        get {
            lock (_lock) {
                return HousingZones.EffectiveItemLimit(_zoneInfo, _house.BonusSlots);
            }
        }
    }

    internal bool IsRetired {
        get {
            lock (_lock) {
                return _retired;
            }
        }
    }

    /// <summary>
    /// Copies the house for building a blob.
    /// </summary>
    /// <returns>The version, live count and objects, taken together.</returns>
    internal HouseSnapshot Snapshot() {
        lock (_lock) {
            return new HouseSnapshot(_house.BlobVersion, HouseBlobCodec.CountLive(_house.Objects), [.. _house.Objects]);
        }
    }

    /// <summary>
    /// Places an object in a new slot at the end of the list.
    /// </summary>
    /// <param name="obj">The object to place; it must be a placed object, not a removed slot.</param>
    /// <param name="slot">The new slot when this returns <c>true</c>.</param>
    /// <param name="version">The new blob version when this returns <c>true</c>.</param>
    /// <returns><c>false</c> at the item limit, when the blob is full, or when the write failed.</returns>
    internal bool TryAdd(HousingBlobObject obj, out int slot, out uint version) {
        slot = -1;
        lock (_lock) {
            version = _house.BlobVersion;
            if (!HouseBlobCodec.IsLive(obj) || HouseBlobCodec.CountLive(_house.Objects) >= ItemLimit
                || _house.Objects.Count >= HousingZones.MaxBlobSlots) {
                return false;
            }

            _house.Objects.Add(Copy(obj));
            _house.BlobVersion = version + 1;
            if (!HouseCollection.Save(_house)) {
                _house.Objects.RemoveAt(_house.Objects.Count - 1);
                _house.BlobVersion = version;

                return false;
            }

            slot = _house.Objects.Count - 1;
            version = _house.BlobVersion;

            return true;
        }
    }

    /// <summary>
    /// Removes a placed object. Its slot stays in the list as a removed slot.
    /// </summary>
    /// <param name="slot">The slot to remove.</param>
    /// <param name="removed">The object that was removed when this returns <c>true</c>.</param>
    /// <param name="version">The new blob version when this returns <c>true</c>.</param>
    /// <returns><c>false</c> when the slot is not a placed object or the write failed.</returns>
    internal bool TryRemove(int slot, out HousingBlobObject removed, out uint version) {
        removed = null;
        lock (_lock) {
            version = _house.BlobVersion;
            if (!IsLiveSlot(slot)) {
                return false;
            }

            var previous = _house.Objects[slot];
            _house.Objects[slot] = Tombstone();
            _house.BlobVersion = version + 1;
            if (!HouseCollection.Save(_house)) {
                _house.Objects[slot] = previous;
                _house.BlobVersion = version;

                return false;
            }

            removed = previous;
            version = _house.BlobVersion;

            return true;
        }
    }

    /// <summary>
    /// Removes several placed objects in one write. The version moves by the number of slots, so a
    /// client can be sent one delete patch per slot with consecutive versions.
    /// </summary>
    /// <param name="slots">The slots to remove; each must be a placed object, and none may repeat.</param>
    /// <param name="version">The new blob version when this returns <c>true</c>: the last patch's version.</param>
    /// <returns><c>false</c> when a slot is not a placed object or the write failed; nothing changes then.</returns>
    internal bool TryRemoveMany(IReadOnlyCollection<int> slots, out uint version) {
        lock (_lock) {
            version = _house.BlobVersion;
            if (slots.Count == 0 || slots.Distinct().Count() != slots.Count || !slots.All(IsLiveSlot)) {
                return false;
            }

            var previousObjects = _house.Objects;
            var updatedObjects = new List<HousingBlobObject>(previousObjects);
            foreach (var slot in slots) {
                updatedObjects[slot] = Tombstone();
            }

            _house.Objects = updatedObjects;
            _house.BlobVersion = version + (uint) slots.Count;
            if (!HouseCollection.Save(_house)) {
                _house.Objects = previousObjects;
                _house.BlobVersion = version;

                return false;
            }

            version = _house.BlobVersion;

            return true;
        }
    }

    /// <summary>
    /// Puts an object back into a removed slot, undoing a <see cref="TryRemove"/> whose follow-up step failed.
    /// The version still moves forward: the removal may already have been seen. The item limit is not checked.
    /// </summary>
    /// <param name="slot">The removed slot.</param>
    /// <param name="obj">The object to put back.</param>
    /// <param name="version">The new blob version when this returns <c>true</c>.</param>
    /// <returns><c>false</c> when the slot is not a removed slot or the write failed.</returns>
    internal bool TryRestore(int slot, HousingBlobObject obj, out uint version) {
        lock (_lock) {
            version = _house.BlobVersion;
            if (!HouseBlobCodec.IsLive(obj) || slot < 0 || slot >= _house.Objects.Count || IsLiveSlot(slot)) {
                return false;
            }

            var previous = _house.Objects[slot];
            _house.Objects[slot] = Copy(obj);
            _house.BlobVersion = version + 1;
            if (!HouseCollection.Save(_house)) {
                _house.Objects[slot] = previous;
                _house.BlobVersion = version;

                return false;
            }

            version = _house.BlobVersion;

            return true;
        }
    }

    /// <summary>
    /// Replaces a placed object with an updated copy of itself.
    /// </summary>
    /// <param name="slot">The slot to update.</param>
    /// <param name="update">Returns the replacement for the stored object; it must not change the stored object in place.</param>
    /// <param name="version">The new blob version when this returns <c>true</c>.</param>
    /// <returns>
    /// <c>false</c> when the slot is not a placed object, <paramref name="update"/> returns <c>null</c> or an object
    /// of another template, or the write failed.
    /// </returns>
    internal bool TryUpdate(int slot, Func<HousingBlobObject, HousingBlobObject> update, out uint version) {
        lock (_lock) {
            version = _house.BlobVersion;
            if (!IsLiveSlot(slot)) {
                return false;
            }

            var previous = _house.Objects[slot];
            var replacement = update(previous);
            if (replacement is null || ReferenceEquals(replacement, previous)
                || HouseBlobCodec.TemplateId(replacement) != HouseBlobCodec.TemplateId(previous)) {
                return false;
            }

            _house.Objects[slot] = replacement;
            _house.BlobVersion = version + 1;
            if (!HouseCollection.Save(_house)) {
                _house.Objects[slot] = previous;
                _house.BlobVersion = version;

                return false;
            }

            version = _house.BlobVersion;

            return true;
        }
    }

    /// <summary>
    /// Counts a player into the house. The first player into a house nobody else is in drops the removed
    /// slots first and moves the version, so every client fetches the new blob.
    /// </summary>
    /// <returns><c>false</c> when the state was retired by its last viewer leaving; get the house from the registry again.</returns>
    internal bool Enter() {
        lock (_lock) {
            if (_retired) {
                return false;
            }

            if (_viewers == 0) {
                DropRemovedSlots();
            }

            _viewers++;

            return true;
        }
    }

    /// <summary>
    /// Counts a player out of the house. The state is retired when the last viewer leaves.
    /// </summary>
    /// <returns><c>true</c> when this was the last viewer and the state is now retired.</returns>
    internal bool Leave() {
        lock (_lock) {
            if (_viewers > 0) {
                _viewers--;
            }

            if (_viewers > 0 || _retired) {
                return false;
            }

            _retired = true;

            return true;
        }
    }

    private bool IsLiveSlot(int slot)
        => slot >= 0 && slot < _house.Objects.Count && HouseBlobCodec.IsLive(_house.Objects[slot]);

    private void DropRemovedSlots() {
        if (HouseBlobCodec.CountLive(_house.Objects) == _house.Objects.Count) {
            return;
        }

        var previousObjects = _house.Objects;
        var previousVersion = _house.BlobVersion;
        _house.Objects = [.. previousObjects.Where(HouseBlobCodec.IsLive)];
        _house.BlobVersion = previousVersion + 1;
        if (!HouseCollection.Save(_house)) {
            _house.Objects = previousObjects;
            _house.BlobVersion = previousVersion;
        }
    }

    private static HousingBlobObject Tombstone() => new();

    private static HousingBlobObject Copy(HousingBlobObject obj) => new() {
        m_gameObjectTemplateID = obj.m_gameObjectTemplateID,
        m_positionXY = obj.m_positionXY,
        m_positionZ = obj.m_positionZ,
        m_yaw = obj.m_yaw,
        m_extraData1 = obj.m_extraData1,
    };

}
