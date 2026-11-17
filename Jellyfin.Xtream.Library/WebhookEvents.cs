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

using System.Collections.Generic;

namespace Jellyfin.Xtream.Library;

/// <summary>
/// The events a webhook can be sent for.
/// </summary>
public static class WebhookEvents
{
    /// <summary>A user started a stream from a provider.</summary>
    public const string StreamStarted = "StreamStarted";

    /// <summary>A user's stream from a provider ended.</summary>
    public const string StreamStopped = "StreamStopped";

    /// <summary>A user tried to play something while every stream of the provider was in use.</summary>
    public const string StreamLimitReached = "StreamLimitReached";

    /// <summary>A library sync started.</summary>
    public const string SyncStarted = "SyncStarted";

    /// <summary>A library sync ended, successfully or not.</summary>
    public const string SyncCompleted = "SyncCompleted";

    /// <summary>A provider refused the configured credentials.</summary>
    public const string ProviderAuthFailed = "ProviderAuthFailed";

    /// <summary>Gets every event, in the order the settings page lists them.</summary>
    public static IReadOnlyList<string> All { get; } = new[]
    {
        StreamStarted,
        StreamStopped,
        StreamLimitReached,
        SyncStarted,
        SyncCompleted,
        ProviderAuthFailed,
    };
}
