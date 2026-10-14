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
 * PVP
 * ========================================================================
 *
 * PURPOSE:
 * The zone object of the PvP sigil that every arena zone places: the same properties as a combat sigil object,
 * under the class hash the arena zone data uses.
 *
 * USAGE EXAMPLE:
 * ZoneLoader registers it, so the objects of an arena zone deserialize to it and ZoneSigilSupervisor spawns them.
 *
 * NOTE:
 * Imcodec does not know the class, so the arena sigil of all twelve arena zones deserializes to null without it.
 * It decodes through the generated code of CombatSigilObjectInfo.
 *
 * TODO:
 * Move the class into Imcodec's ServerGeneratedTypeRegistry and delete this file.
 *
 * Created by: Jay
 * Version: KALI 1.0
 * Last Updated: 10/14/2026
 */

using Imcodec.ObjectProperty.TypeCache;

namespace Imlight.CoreLib.Game.Sigils;

internal sealed record PvPSigilObjectInfo : CombatSigilObjectInfo {

    public const uint ClassHash = 744574030;

    public override uint GetHash() => ClassHash;

}
