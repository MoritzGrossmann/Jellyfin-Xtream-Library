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

namespace Jellyfin.Xtream.Library.Service;

/// <summary>
/// One stream that is playing through a provider right now.
/// </summary>
/// <param name="Key">What identifies the playback, normally the Jellyfin session id.</param>
/// <param name="ProviderIndex">Index of the provider the stream comes from.</param>
/// <param name="UserName">The Jellyfin user watching.</param>
/// <param name="DeviceName">The device the user watches on.</param>
/// <param name="ClientName">The app the user watches in.</param>
/// <param name="ItemName">Title of the movie, episode or channel.</param>
/// <param name="StartedUtc">When the stream was registered.</param>
/// <param name="ItemId">Id of the item playing, so a late stop report for the previous item
/// does not free the slot of the next one.</param>
public sealed record ActivePlayback(
    string Key,
    int ProviderIndex,
    string UserName,
    string DeviceName,
    string ClientName,
    string ItemName,
    DateTime StartedUtc,
    string ItemId = "");
