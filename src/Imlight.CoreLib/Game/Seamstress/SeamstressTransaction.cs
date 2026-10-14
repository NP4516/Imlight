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
 * SEAMSTRESS TRANSACTION
 * ========================================================================
 *
 * PURPOSE:
 * Swaps the inputs of a stitch or an unstitch for their results and charges the Crowns, in an order
 * that a crash or a failure can never turn into a loss for the player.
 *
 * USAGE EXAMPLE:
 * var transaction = new SeamstressTransaction(wizard, session, unequip);
 * if (!transaction.TryApply([displayItem, statsItem], [stitchedItem], stitchCrowns, out var failure)) { deny(failure); }
 *
 * NOTE:
 * Order: unequip the equipped inputs, add the results, spend the Crowns, remove the inputs. Every step
 * is its own commit, so a crash leaves the player with extra items at worst. A failure before the Crowns
 * are spent takes the results back. Once the Crowns are spent the transaction succeeds even if an input
 * cannot be removed, and that is logged as an error. The client must see every item message before the
 * stitch confirm, so the caller sends the confirm after this returns.
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
using Akka.Actor;
using Imcodec.MessageLayer.Generated;
using Imcodec.ObjectProperty.TypeCache;
using Imlight.Common;
using Imlight.CoreLib.Shared.Items;
using Imlight.CoreLib.WizardData.Collections;
using Imlight.CoreLib.WizardData.Models.Player;

namespace Imlight.CoreLib.Game.Seamstress;

/// <summary>
/// One stitch or unstitch for one wizard. See the header for the order of its steps.
/// </summary>
/// <param name="wizard">The wizard that stitches.</param>
/// <param name="session">The SessionActor reference of the wizard's session.</param>
/// <param name="unequip">
/// Takes an equipped item off the wizard and returns whether it is no longer equipped.
/// </param>
internal sealed class SeamstressTransaction(Wizard wizard, IActorRef session, Func<ulong, bool> unequip) {

    /// <summary>
    /// Replaces the inputs with the results and charges the Crowns.
    /// </summary>
    /// <param name="inputs">The items the player gives up. They may be in the backpack or equipped.</param>
    /// <param name="results">The new items. They are added before anything is charged or removed.</param>
    /// <param name="crownsCost">The Crowns to charge. Zero charges nothing.</param>
    /// <param name="failure">Why nothing happened. Empty on success.</param>
    /// <returns>True when the player paid for the results and holds them. False when nothing changed for good.</returns>
    internal bool TryApply(IReadOnlyList<WizClientObjectItem> inputs, IReadOnlyList<WizClientObjectItem> results,
                           int crownsCost, out string failure) {
        var account = wizard.Account;
        var equippedInputs = inputs.Where(input => wizard.EquipmentBehavior.HasItemEquipped(input.m_globalID)).ToList();

        // An equipped input takes a backpack slot when it comes off, and the results are added before the inputs go.
        var slotsNeeded = results.Count + equippedInputs.Count;
        if (wizard.InventoryBehavior.FreeSlots < slotsNeeded) {
            failure = $"{slotsNeeded} free inventory slots are needed and {wizard.InventoryBehavior.FreeSlots} are free";

            return false;
        }

        if (account.Crowns < crownsCost) {
            failure = $"{crownsCost} Crowns are needed and the account has {account.Crowns}";

            return false;
        }

        var added = new List<WizClientObjectItem>();
        var announced = new HashSet<ulong>();
        try {
            foreach (var input in equippedInputs) {
                if (!unequip(input.m_globalID)) {
                    failure = $"item {input.m_globalID.Full} could not be unequipped";

                    return false;
                }
            }

            foreach (var result in results) {
                // Listed first so that an exception half way through the add is taken back too.
                added.Add(result);
                if (!wizard.AddItemToInventory(result)) {
                    failure = $"item {result.m_globalID.Full} could not be added to the inventory";
                    TakeBack(added, announced);

                    return false;
                }

                if (!ItemSync.SendAdded(session, wizard, result)) {
                    failure = $"item {result.m_globalID.Full} could not be sent to the client";
                    TakeBack(added, announced);

                    return false;
                }

                announced.Add(result.m_globalID);
            }
        }
        catch (Exception ex) {
            Logger.Error("[Seamstress] Error while giving {0} the results of a seamstress request: {1} {2}",
                Logger.Args(wizard.CharId, ex.Message, ex.StackTrace));
            failure = "an error occurred while adding the results";
            TakeBack(added, announced);

            return false;
        }

        if (crownsCost > 0 && !TrySpendCrowns(account, crownsCost)) {
            failure = "the Crowns could not be taken";
            TakeBack(added, announced);

            return false;
        }

        var inputIds = string.Join(", ", inputs.Select(input => input.m_globalID.Full));
        foreach (var input in inputs) {
            RemoveInput(input.m_globalID, inputIds);
        }

        failure = string.Empty;

        return true;
    }

    private bool TrySpendCrowns(Account account, int cost) {
        var before = account.Crowns;
        if (before < cost) {
            return false;
        }

        var saved = false;
        try {
            saved = account.SetCrowns(before - cost);
        }
        catch (Exception ex) {
            Logger.Error("[Seamstress] Error while saving the Crowns of account {0}: {1} {2}",
                Logger.Args(account.AccountId, ex.Message, ex.StackTrace));
        }

        if (!saved) {
            account.Crowns = before;

            return false;
        }

        session.Tell(new WIZARD_12_PROTOCOL.MSG_CROWNBALANCE {
            Failure = 0,
            TotalCrowns = account.Crowns,
            CharacterID = wizard.CharId,
            CacheBalanceForCSSegmentation = 1
        });

        return true;
    }

    private void RemoveInput(ulong itemId, string inputIds) {
        try {
            if (!wizard.RemoveItemFromInventory(itemId)) {
                Logger.Error("[Seamstress] Character {0} paid for a seamstress request but input item {1} is not in the backpack (inputs: {2}).",
                    Logger.Args(wizard.CharId, itemId, inputIds));

                return;
            }

            ItemSync.SendRemoved(session, wizard, itemId);
            WizardItemCollection.RemoveItem(wizard.CharId, itemId);
        }
        catch (Exception ex) {
            Logger.Error("[Seamstress] Character {0} paid for a seamstress request but input item {1} was not removed cleanly (inputs: {2}): {3} {4}",
                Logger.Args(wizard.CharId, itemId, inputIds, ex.Message, ex.StackTrace));
        }
    }

    private void TakeBack(IReadOnlyList<WizClientObjectItem> added, IReadOnlySet<ulong> announced) {
        foreach (var item in added) {
            try {
                wizard.RemoveItemFromInventory(item.m_globalID);
                WizardItemCollection.RemoveItem(wizard.CharId, item.m_globalID);
                if (announced.Contains(item.m_globalID)) {
                    ItemSync.SendRemoved(session, wizard, item.m_globalID);
                }
            }
            catch (Exception ex) {
                Logger.Error("[Seamstress] Could not take back result item {0} of character {1}: {2} {3}",
                    Logger.Args(item.m_globalID.Full, wizard.CharId, ex.Message, ex.StackTrace));
            }
        }
    }

}
