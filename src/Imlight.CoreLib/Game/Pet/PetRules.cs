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
 * PET RULES
 * ========================================================================
 *
 * PURPOSE:
 * Holds the pet rules the client files do not define. Each one has a default and can be
 * overridden in the [Pets] section of Imlight.ini.
 *
 * USAGE EXAMPLE:
 * pet.m_maximumHappiness = (uint) PetRules.MaximumHappiness;
 *
 * NOTE:
 * Rules that exist in a client file are read from that file, never from here.
 *
 * TODO:
 *
 * Created by: Jay
 * Version: KALI 1.0
 * Last Updated: 10/14/2026
 */

using System;
using Imlight.Common;

namespace Imlight.CoreLib.Game.Pet;

/// <summary>
/// The server-side pet rules, with their defaults.
/// </summary>
internal static class PetRules {

    private const int DefaultStartingHappiness = 50;
    private const int DefaultMaximumHappiness = 100;

    /// <summary>
    /// The happiness a pet starts with.
    /// </summary>
    internal static int StartingHappiness { get; } = Math.Max(0, ConfigurationManager.GetValue("Pets.StartingHappiness", DefaultStartingHappiness));

    /// <summary>
    /// The most happiness a pet can have.
    /// </summary>
    internal static int MaximumHappiness { get; } = Math.Max(1, ConfigurationManager.GetValue("Pets.MaximumHappiness", DefaultMaximumHappiness));

}
