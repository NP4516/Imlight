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
 * The stored state of one player house: its lot, its blob version and the placed objects.
 * 
 * USAGE EXAMPLE:
 * var id = House.GetDocumentId(wizard.CharId, HousingZones.DormInfo.TemplateId);
 * 
 * NOTE:
 * Objects are stored in the client's own wire form, so the stored state and the blob are the
 * same data. A removed object stays in the list with template id 0 so slot indexes never shift.
 * 
 * TODO:
 * 
 * Created by: Jay
 * Version: KALI 1.0
 * Last Updated: 10/14/2026
 */

using System;
using System.Collections.Generic;
using Imcodec.ObjectProperty.TypeCache;

namespace Imlight.CoreLib.WizardData.Models.Player;

/// <summary>
/// A house owned by one character.
/// </summary>
public class House {

    public string Id { get; set; }
    public ulong LotGid { get; set; }
    public ulong OwnerCharId { get; set; }
    public uint DeedTemplateId { get; set; }
    public string ZoneName { get; set; }
    public uint BlobVersion { get; set; }
    public int BonusSlots { get; set; }
    public List<HousingBlobObject> Objects { get; set; } = [];
    public DateTime CreatedUtc { get; set; }

    /// <summary>
    /// Gets the document id of a character's house. One character owns one house per deed template.
    /// </summary>
    /// <param name="ownerCharId">The owning character.</param>
    /// <param name="deedTemplateId">The deed or structure template of the house.</param>
    /// <returns>The document id.</returns>
    public static string GetDocumentId(ulong ownerCharId, uint deedTemplateId)
        => $"Houses/{ownerCharId}/{deedTemplateId}";

}
