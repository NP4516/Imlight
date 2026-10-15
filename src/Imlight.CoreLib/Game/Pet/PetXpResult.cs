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
 * PET XP RESULT
 * ========================================================================
 *
 * PURPOSE:
 * Describes what a grant of pet XP did: how much XP the pet received and how many levels it gained.
 *
 * USAGE EXAMPLE:
 * var result = PetProgression.AddXp(pet, xp, PetXpSource.Game);
 * if (result.Applied) { ... }
 *
 * NOTE:
 *
 * TODO:
 *
 * Created by: Jay
 * Version: KALI 1.0
 * Last Updated: 10/14/2026
 */

namespace Imlight.CoreLib.Game.Pet;

/// <summary>
/// The outcome of a grant of pet XP.
/// </summary>
/// <param name="Granted">The XP the pet received, with any bonus for the source. 0 if the pet gained nothing.</param>
/// <param name="LevelsGained">How many levels the XP carried the pet up.</param>
internal sealed record PetXpResult(uint Granted, int LevelsGained) {

    /// <summary>
    /// The result of a pet that cannot gain XP.
    /// </summary>
    internal static PetXpResult None { get; } = new(0, 0);

    /// <summary>
    /// True if the pet received XP.
    /// </summary>
    internal bool Applied => Granted > 0;

}
