// Copyright (C) 2024  Roland Breitschaft

// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.

// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Xtream.Library.Service;

/// <summary>
/// Keeps the streams playing per provider and hands out slots against a limit.
/// Thread safe; knows nothing about Jellyfin, so the counting rules can be tested alone.
/// </summary>
public sealed class StreamSlotRegistry
{
    private readonly object _lock = new();
    private readonly Dictionary<string, ActivePlayback> _streams = new(StringComparer.Ordinal);

    /// <summary>
    /// Registers a stream unless the provider is already at its limit.
    /// <para>
    /// A key that is already registered is replaced, never counted twice: a session that reports
    /// the start of playback again, or switches to the next episode, still holds one connection.
    /// The replaced entry is not counted against the limit for the same reason.
    /// </para>
    /// </summary>
    /// <param name="stream">The stream asking for a slot.</param>
    /// <param name="maxConnections">The provider's limit; 0 or less means no limit.</param>
    /// <returns>True when the stream was registered, false when the limit is reached.</returns>
    public bool TryAcquire(ActivePlayback stream, int maxConnections)
    {
        ArgumentNullException.ThrowIfNull(stream);

        lock (_lock)
        {
            if (maxConnections > 0)
            {
                int others = _streams.Values.Count(s => s.ProviderIndex == stream.ProviderIndex
                    && !string.Equals(s.Key, stream.Key, StringComparison.Ordinal));
                if (others >= maxConnections)
                {
                    // The same key may hold a slot from an earlier item; it is gone now.
                    _streams.Remove(stream.Key);
                    return false;
                }
            }

            _streams[stream.Key] = stream;
            return true;
        }
    }

    /// <summary>
    /// Frees the slot a key holds.
    /// </summary>
    /// <param name="key">The key given to <see cref="TryAcquire"/>.</param>
    /// <param name="itemId">When given, the slot is only freed if it is held for this item. A
    /// client that reports the next episode before the stop of the previous one must not lose
    /// the slot it just got.</param>
    /// <returns>True when a slot was freed.</returns>
    public bool Release(string key, string? itemId = null)
    {
        lock (_lock)
        {
            if (itemId is not null
                && _streams.TryGetValue(key, out var held)
                && !string.Equals(held.ItemId, itemId, StringComparison.Ordinal))
            {
                return false;
            }

            return _streams.Remove(key);
        }
    }

    /// <summary>
    /// Counts the streams playing through a provider.
    /// </summary>
    /// <param name="providerIndex">The provider's index.</param>
    /// <returns>The number of registered streams.</returns>
    public int Count(int providerIndex)
    {
        lock (_lock)
        {
            return _streams.Values.Count(s => s.ProviderIndex == providerIndex);
        }
    }

    /// <summary>
    /// Copies the registered streams, oldest first.
    /// </summary>
    /// <returns>A snapshot that later changes do not touch.</returns>
    public IReadOnlyList<ActivePlayback> Snapshot()
    {
        lock (_lock)
        {
            return _streams.Values.OrderBy(s => s.StartedUtc).ToList();
        }
    }
}
