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
 */

using System;
using System.Collections.Generic;
using System.Linq;

namespace Imlight.CoreLib.Game.Randomization;

/// <summary>
/// Swaps IDs for other IDs in the same bucket. For a given seed the swap is always the same,
/// and no two IDs in a bucket swap to the same ID, so nothing is ever lost or doubled up.
/// </summary>
internal sealed class SeededShuffle {

    // Bucket key -> IDs in that bucket, sorted so the shuffle does not depend on load order.
    private readonly Dictionary<string, ulong[]> _buckets;
    // ID -> (bucket key, index within the bucket).
    private readonly Dictionary<ulong, (string Bucket, int Index)> _positions = [];

    internal SeededShuffle(IEnumerable<(ulong Id, string Bucket)> entries) {
        _buckets = entries
            .GroupBy(e => e.Bucket)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Id).Distinct().OrderBy(id => id).ToArray());

        foreach (var (bucket, ids) in _buckets) {
            for (var i = 0; i < ids.Length; i++) {
                _positions.TryAdd(ids[i], (bucket, i));
            }
        }
    }

    internal int Count => _positions.Count;

    /// <summary>
    /// Returns the ID that <paramref name="id"/> swaps to under <paramref name="seed"/>,
    /// or <paramref name="id"/> itself when it is not in any bucket.
    /// </summary>
    internal ulong Map(ulong id, ulong seed) {
        if (!_positions.TryGetValue(id, out var position)) {
            return id;
        }

        var ids = _buckets[position.Bucket];
        if (ids.Length < 2) {
            return id;
        }

        // Fisher-Yates over a copy, driven by a generator seeded from the seed and the bucket.
        var shuffled = (ulong[]) ids.Clone();
        var state = Mix(seed ^ Fnv1a(position.Bucket));
        for (var i = shuffled.Length - 1; i > 0; i--) {
            state = Mix(state);
            var j = (int) (state % (ulong) (i + 1));
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }

        return shuffled[position.Index];
    }

    /// <summary>
    /// Returns a random ID from the same bucket as <paramref name="id"/>, different every call,
    /// or <paramref name="id"/> itself when it is not in any bucket.
    /// </summary>
    internal ulong Pick(ulong id, Random random) {
        if (!_positions.TryGetValue(id, out var position)) {
            return id;
        }

        var ids = _buckets[position.Bucket];

        return ids[random.Next(ids.Length)];
    }

    /// <summary>
    /// Returns a random ID from any bucket.
    /// </summary>
    internal ulong PickAny(Random random) {
        var index = random.Next(_positions.Count);
        foreach (var ids in _buckets.Values) {
            if (index < ids.Length) {
                return ids[index];
            }

            index -= ids.Length;
        }

        return 0;
    }

    /// <summary>
    /// A stable string hash. string.GetHashCode changes between runs, which would reshuffle everyone.
    /// </summary>
    internal static ulong Fnv1a(string value) {
        var hash = 14695981039346656037UL;
        foreach (var c in value) {
            hash ^= c;
            hash *= 1099511628211UL;
        }

        return hash;
    }

    /// <summary>
    /// SplitMix64 finalizer: spreads every input bit across the output.
    /// </summary>
    internal static ulong Mix(ulong value) {
        value += 0x9E3779B97F4A7C15UL;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;

        return value ^ (value >> 31);
    }

}
