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
 * Persists houses. The document id is derived from the owner and the deed template, so creating
 * a house is idempotent however many logins race.
 * 
 * USAGE EXAMPLE:
 * var house = HouseCollection.Load(wizard.CharId, templateId);
 * if (house is null) { HouseCollection.TryCreate(newHouse, out house); }
 * 
 * NOTE:
 * - Reads are direct loads by document id, never queries, because a query can be stale.
 * - An item that moves between a house and a backpack is written with SaveWithBackpack: one
 *   transaction, so a crash between the two cannot lose or duplicate it.
 * 
 * TODO:
 * 
 * Created by: Jay
 * Version: KALI 1.0
 * Last Updated: 10/14/2026
 */

using System;
using Imcodec.ObjectProperty.TypeCache;
using Imlight.Common;
using Imlight.CoreLib.WizardData.Databases;
using Imlight.CoreLib.WizardData.Models.Player;
using Raven.Client.Documents;
using Raven.Client.Exceptions;

namespace Imlight.CoreLib.WizardData.Collections;

public static class HouseCollection {

    public const string CollectionName = "Houses";
    private static readonly IDocumentStore s_store;

    static HouseCollection() => s_store = PlayerDatabase.Instance.Store;

    /// <summary>
    /// Loads a character's house.
    /// </summary>
    /// <param name="ownerCharId">The owning character.</param>
    /// <param name="deedTemplateId">The deed or structure template of the house.</param>
    /// <returns>The house, or <c>null</c> when none is stored.</returns>
    public static House Load(ulong ownerCharId, uint deedTemplateId) {
        using var session = s_store.OpenSession();

        return session.Load<House>(House.GetDocumentId(ownerCharId, deedTemplateId));
    }

    /// <summary>
    /// Stores a new house unless one already exists under the same id.
    /// </summary>
    /// <param name="house">The house to store; its <see cref="House.Id"/> must be set.</param>
    /// <param name="winner">
    /// <paramref name="house"/> when it was stored, otherwise the house that already existed.
    /// <c>null</c> when the store failed for another reason.
    /// </param>
    /// <returns><c>true</c> when <paramref name="house"/> was stored.</returns>
    public static bool TryCreate(House house, out House winner) {
        try {
            using var session = s_store.OpenSession();

            session.Store(house, changeVector: string.Empty, house.Id);
            var metadata = session.Advanced.GetMetadataFor(house);
            metadata[Raven.Client.Constants.Documents.Metadata.Collection] = CollectionName;

            session.SaveChanges();
            winner = house;

            return true;
        } catch (ConcurrencyException) {
            winner = Load(house.OwnerCharId, house.DeedTemplateId);

            return false;
        } catch (Exception ex) {
            Logger.Error("Failed to create house {HouseId}: {Reason}", Logger.Args(house.Id, ex.Message));
            winner = null;

            return false;
        }
    }

    /// <summary>
    /// Writes the changing parts of a house: its blob version, bonus slots and objects.
    /// </summary>
    /// <param name="house">The house to save.</param>
    /// <returns><c>true</c> when the stored house now matches <paramref name="house"/>.</returns>
    public static bool Save(House house) {
        try {
            using var session = s_store.OpenSession();

            var stored = session.Load<House>(house.Id);
            if (stored is null) {
                return false;
            }

            CopyChanging(house, stored);
            session.SaveChanges();

            return true;
        } catch (Exception ex) {
            Logger.Error("Failed to save house {HouseId}: {Reason}", Logger.Args(house.Id, ex.Message));

            return false;
        }
    }

    /// <summary>
    /// Writes a house together with its owner's backpack in one transaction, so an item that moves between
    /// them is either in the house or in the backpack, never lost and never in both.
    /// </summary>
    /// <param name="house">The house to save.</param>
    /// <param name="owner">The owner, whose backpack is written as it is in memory.</param>
    /// <param name="addedItem">An item that entered the backpack and is not stored yet, or <c>null</c>.</param>
    /// <returns><c>true</c> when both are stored; <c>false</c> when neither changed.</returns>
    public static bool SaveWithBackpack(House house, Wizard owner, WizClientObjectItem addedItem = null) {
        try {
            return WizardCollection.UpdateCharacterWithSession(owner.CharId, (session, storedOwner) => {
                var stored = session.Load<House>(house.Id);
                if (stored is null) {
                    return false;
                }

                CopyChanging(house, stored);
                storedOwner.InventoryBehavior = owner.InventoryBehavior;
                if (addedItem is not null) {
                    session.Store(addedItem);
                    var metadata = session.Advanced.GetMetadataFor(addedItem);
                    metadata[Raven.Client.Constants.Documents.Metadata.Collection] = WizardItemCollection.CollectionName;
                }

                return true;
            });
        } catch (Exception ex) {
            Logger.Error("Failed to save house {HouseId} with the backpack of character {CharId}: {Reason}",
                Logger.Args(house.Id, owner.CharId, ex.Message));

            return false;
        }
    }

    private static void CopyChanging(House from, House to) {
        to.BlobVersion = from.BlobVersion;
        to.BonusSlots = from.BonusSlots;
        to.Objects = [.. from.Objects];
    }

}
