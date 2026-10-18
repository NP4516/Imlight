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
 * Reads the [PvP] section of Imlight.ini.
 * 
 * USAGE EXAMPLE:
 * if (!PvPSettings.IsEnabled) { }
 * 
 * NOTE:
 * The values are read on every call, so a reloaded configuration takes effect without a restart.
 * 
 * TODO:
 * 
 * 
 * Created by: Jay
 * Version: KALI 1.0
 * Last Updated: 10/18/2026
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Imlight.Common;

namespace Imlight.CoreLib.Game.PvP.Data;

internal static class PvPSettings {

    public static bool IsEnabled
        => ConfigurationManager.GetValue("PvP.Enabled", true);

    public static byte PracticePlanningSeconds
        => (byte) Math.Clamp(ConfigurationManager.GetValue("PvP.Practice.PlanningSeconds", 20), 1, byte.MaxValue);

    public static bool SwapFirstTeamEachRound
        => ConfigurationManager.GetValue("PvP.SwapFirstTeamEachRound", true);

    /// <summary>
    /// Gets how long a finished match waits for the clients to finish their victory scene. Unset means the match
    /// does not wait.
    /// </summary>
    public static TimeSpan ReturnGrace {
        get {
            var seconds = ConfigurationManager.GetValue("PvP.ReturnGraceSeconds", -1d);
            if (seconds < 0) {
                if (!s_warnedReturnGraceUnset) {
                    s_warnedReturnGraceUnset = true;
                    Logger.Warning("PvP.ReturnGraceSeconds is not set; finished matches do not wait for the victory scene.");
                }

                return TimeSpan.Zero;
            }

            return TimeSpan.FromSeconds(seconds);
        }
    }

    /// <summary>
    /// Gets the instant the season checks use instead of the real clock. Null means the real clock.
    /// </summary>
    public static DateTime? SeasonNow {
        get {
            var value = ConfigurationManager.GetValue("PvP.Season.Now", string.Empty);
            if (string.IsNullOrWhiteSpace(value)) {
                return null;
            }

            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)) {
                return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
            }

            if (!s_warnedSeasonNowInvalid) {
                s_warnedSeasonNowInvalid = true;
                Logger.Warning("PvP.Season.Now is not a valid date and is ignored.");
            }

            return null;
        }
    }

    public static double SeasonOffsetDays
        => ConfigurationManager.GetValue("PvP.Season.OffsetDays", 0d);

    /// <summary>
    /// Gets the forced position in the season list of every league. Null means the season is chosen by time.
    /// </summary>
    public static int? SeasonIndex {
        get {
            var index = ConfigurationManager.GetValue("PvP.Season.Index", int.MinValue);

            return index == int.MinValue ? null : index;
        }
    }

    public static bool ShiftClientClock
        => ConfigurationManager.GetValue("PvP.Season.ShiftClientClock", false);

    public static bool RankedEnabled
        => ConfigurationManager.GetValue("PvP.RankedEnabled", true);

    public static bool RankedAwardBadges
        => ConfigurationManager.GetValue("PvP.RankedAwardBadges", true);

    public static double EloScale {
        get {
            var scale = ConfigurationManager.GetValue("PvP.EloScale", 400d);

            return scale > 0 ? scale : 400d;
        }
    }

    public static int KFactorOverride
        => Math.Max(0, ConfigurationManager.GetValue("PvP.KFactorOverride", 0));

    public static string RankedTournament
        => ConfigurationManager.GetValue("PvP.RankedTournament", string.Empty).Trim();

    public static IReadOnlyCollection<string> LeaguesOpen
        => GetList("PvP.Leagues.Open");

    public static IReadOnlyCollection<string> LeaguesClosed
        => GetList("PvP.Leagues.Closed");

    private static string[] GetList(string key)
        => ConfigurationManager.GetValue(key, string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();

    private static bool s_warnedSeasonNowInvalid;

    private static bool s_warnedReturnGraceUnset;

}
