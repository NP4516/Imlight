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
 * The live state of one house: its placed items under one lock, persisted on every change, shared
 * by every player who is in the house.
 * 
 * USAGE EXAMPLE:
 * if (state.TryAdd(item, out var slot, out var stored, out var revision)) {
 *     var gidId = HouseBlobCodec.GidIndex(state.GidBase, slot);
 * }
 * 
 * NOTE:
 * - Every change is written to the database before it returns. A failed write undoes the change
 *   in memory, so the stored house and the live house never differ.
 * - A change that also moves an item to or from a backpack passes its own save, which writes both in one transaction.
 * - Stored items are never changed in place; an update replaces the item.
 * - The slot table numbers the items the way the client numbers its blob entries. A removed item leaves a null
 *   slot behind, so the numbers in later patches stay valid. The table is built, and the revision moved so every
 *   client fetches the new blob, when the state is created.
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
using Imlight.Common;
using Imlight.CoreLib.WizardData.Collections;
using Imlight.CoreLib.WizardData.Models.Player;

namespace Imlight.CoreLib.Game.Housing;

/// <summary>
/// A consistent copy of a house for building a blob.
/// </summary>
/// <param name="Revision">The blob version.</param>
/// <param name="LiveCount">The number of placed items, not counting removed slots.</param>
/// <param name="Slots">The items in slot order; a removed slot is <c>null</c>.</param>
internal readonly record struct HouseSnapshot(uint Revision, int LiveCount, IReadOnlyList<PlacedItem> Slots);

/// <summary>
/// The live, persisted state of one house.
/// </summary>
internal sealed class HouseState {

    private readonly Lock _lock = new();
    private readonly House _house;
    private readonly HouseTemplateInfo _template;
    private readonly List<PlacedItem> _slots;
    private int _liveCount;
    private int _viewers;
    private bool _retired;

    private HouseState(House house, HouseTemplateInfo template, List<PlacedItem> slots, uint gidBase) {
        _house = house;
        _template = template;
        _slots = slots;
        _liveCount = slots.Count;
        GidBase = gidBase;
    }

    /// <summary>
    /// The base the client numbers this house's blob entries from. Every viewer shares it.
    /// </summary>
    internal uint GidBase { get; }

    internal ulong LotGid => _house.LotGid;

    internal ulong OwnerCharId => _house.OwnerCharId;

    internal string ZoneName => _template.LotZone;

    internal int LiveCount {
        get {
            lock (_lock) {
                return _liveCount;
            }
        }
    }

    internal int SlotCount {
        get {
            lock (_lock) {
                return _slots.Count;
            }
        }
    }

    internal int ItemLimit {
        get {
            lock (_lock) {
                return _template.ItemLimit + _house.BonusSlots;
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
    /// Creates the live state of a stored house. The items are numbered by their ids, a new base is drawn,
    /// and the revision moves once and is stored, so a client with an older cache fetches the blob again.
    /// </summary>
    /// <param name="house">The stored house; the state owns it from now on.</param>
    /// <param name="template">The house's template.</param>
    /// <param name="state">The live state when this returns <c>true</c>.</param>
    /// <returns><c>false</c> when the new revision could not be stored.</returns>
    internal static bool TryCreate(House house, HouseTemplateInfo template, out HouseState state) {
        state = null;
        var previousItems = house.Items;
        var previousRevision = house.Revision;
        var ordered = previousItems.OrderBy(item => item.Id).ToList();
        house.Items = [.. ordered];
        house.Revision = previousRevision + 1;
        if (!HouseCollection.Save(house)) {
            house.Items = previousItems;
            house.Revision = previousRevision;

            return false;
        }

        state = new HouseState(house, template, ordered, HouseBlobCodec.NewGidBase());

        return true;
    }

    /// <summary>
    /// Copies the house for building a blob.
    /// </summary>
    /// <returns>The revision, live count and slots, taken together.</returns>
    internal HouseSnapshot Snapshot() {
        lock (_lock) {
            return new HouseSnapshot(_house.Revision, _liveCount, [.. _slots]);
        }
    }

    /// <summary>
    /// Places an item in a new slot at the end of the table.
    /// </summary>
    /// <param name="item">The item to place; its id is replaced by the next free one.</param>
    /// <param name="slot">The new slot when this returns <c>true</c>.</param>
    /// <param name="stored">The item as it is stored when this returns <c>true</c>.</param>
    /// <param name="revision">The new revision when this returns <c>true</c>.</param>
    /// <param name="save">Writes the house; <c>null</c> writes the house alone.</param>
    /// <returns><c>false</c> at the item limit, when the blob is full, or when the write failed.</returns>
    internal bool TryAdd(PlacedItem item, out int slot, out PlacedItem stored, out uint revision, Func<House, bool> save = null) {
        slot = -1;
        stored = null;
        lock (_lock) {
            revision = _house.Revision;
            if (_liveCount >= _template.ItemLimit + _house.BonusSlots || _slots.Count >= HouseBlobCodec.MaxSlotsThatFit) {
                return false;
            }

            var added = item with { Id = _house.NextItemId };
            _house.Items.Add(added);
            _house.NextItemId = added.Id + 1;
            _house.Revision = revision + 1;
            if (!Persist(save)) {
                _house.Items.RemoveAt(_house.Items.Count - 1);
                _house.NextItemId = added.Id;
                _house.Revision = revision;

                return false;
            }

            _slots.Add(added);
            _liveCount++;
            slot = _slots.Count - 1;
            stored = added;
            revision = _house.Revision;

            return true;
        }
    }

    /// <summary>
    /// Removes a placed item. Its slot stays in the table as a null slot.
    /// </summary>
    /// <param name="slot">The slot to remove.</param>
    /// <param name="removed">The item that was removed when this returns <c>true</c>.</param>
    /// <param name="revision">The new revision when this returns <c>true</c>.</param>
    /// <param name="save">Writes the house; <c>null</c> writes the house alone.</param>
    /// <returns><c>false</c> when the slot is not a placed item or the write failed.</returns>
    internal bool TryRemove(int slot, out PlacedItem removed, out uint revision, Func<House, bool> save = null) {
        removed = null;
        lock (_lock) {
            revision = _house.Revision;
            if (!IsLiveSlot(slot)) {
                return false;
            }

            var previous = _slots[slot];
            var index = _house.Items.FindIndex(item => item.Id == previous.Id);
            _house.Items.RemoveAt(index);
            _house.Revision = revision + 1;
            if (!Persist(save)) {
                _house.Items.Insert(index, previous);
                _house.Revision = revision;

                return false;
            }

            _slots[slot] = null;
            _liveCount--;
            removed = previous;
            revision = _house.Revision;

            return true;
        }
    }

    /// <summary>
    /// Removes several placed items in one write. The revision moves by the number of slots, so a
    /// client can be sent one delete patch per slot with consecutive versions.
    /// </summary>
    /// <param name="slots">The slots to remove; each must be a placed item, and none may repeat.</param>
    /// <param name="revision">The new revision when this returns <c>true</c>: the last patch's version.</param>
    /// <returns><c>false</c> when a slot is not a placed item or the write failed; nothing changes then.</returns>
    internal bool TryRemoveMany(IReadOnlyCollection<int> slots, out uint revision) {
        lock (_lock) {
            revision = _house.Revision;
            if (slots.Count == 0 || slots.Distinct().Count() != slots.Count || !slots.All(IsLiveSlot)) {
                return false;
            }

            var previousItems = _house.Items;
            var removedIds = slots.Select(slot => _slots[slot].Id).ToHashSet();
            _house.Items = [.. previousItems.Where(item => !removedIds.Contains(item.Id))];
            _house.Revision = revision + (uint) slots.Count;
            if (!HouseCollection.Save(_house)) {
                _house.Items = previousItems;
                _house.Revision = revision;

                return false;
            }

            foreach (var slot in slots) {
                _slots[slot] = null;
            }

            _liveCount -= slots.Count;
            revision = _house.Revision;

            return true;
        }
    }

    /// <summary>
    /// Replaces a placed item with an updated copy of itself.
    /// </summary>
    /// <param name="slot">The slot to update.</param>
    /// <param name="update">Returns the replacement for the stored item; it must keep the id and the template.</param>
    /// <param name="updated">The item as it is stored when this returns <c>true</c>.</param>
    /// <param name="revision">The new revision when this returns <c>true</c>.</param>
    /// <returns>
    /// <c>false</c> when the slot is not a placed item, <paramref name="update"/> returns <c>null</c> or an item
    /// of another id or template, or the write failed.
    /// </returns>
    internal bool TryUpdate(int slot, Func<PlacedItem, PlacedItem> update, out PlacedItem updated, out uint revision) {
        updated = null;
        lock (_lock) {
            revision = _house.Revision;
            if (!IsLiveSlot(slot)) {
                return false;
            }

            var previous = _slots[slot];
            var replacement = update(previous);
            if (replacement is null || ReferenceEquals(replacement, previous)
                || replacement.Id != previous.Id || replacement.TemplateId != previous.TemplateId) {
                return false;
            }

            var index = _house.Items.FindIndex(item => item.Id == previous.Id);
            _house.Items[index] = replacement;
            _house.Revision = revision + 1;
            if (!HouseCollection.Save(_house)) {
                _house.Items[index] = previous;
                _house.Revision = revision;

                return false;
            }

            _slots[slot] = replacement;
            updated = replacement;
            revision = _house.Revision;

            return true;
        }
    }

    /// <summary>
    /// Counts a player into the house.
    /// </summary>
    /// <returns><c>false</c> when the state was retired by its last viewer leaving; get the house from the registry again.</returns>
    internal bool Enter() {
        lock (_lock) {
            if (_retired) {
                return false;
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

    private bool Persist(Func<House, bool> save) {
        try {
            return (save ?? HouseCollection.Save)(_house);
        } catch (Exception ex) {
            Logger.Error("Failed to write house {LotGid}: {Reason}", Logger.Args(_house.LotGid, ex.Message));

            return false;
        }
    }

    private bool IsLiveSlot(int slot)
        => slot >= 0 && slot < _slots.Count && _slots[slot] is not null;

}
