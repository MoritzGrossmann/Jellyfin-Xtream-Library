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
using System.Net.Mime;
using Jellyfin.Xtream.Library.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Xtream.Library.Api;

/// <summary>
/// Shows who is streaming through each provider.
/// </summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("XtreamLibrary")]
[Produces(MediaTypeNames.Application.Json)]
public class StreamLimitController : ControllerBase
{
    private readonly ActiveStreamTracker _tracker;

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamLimitController"/> class.
    /// </summary>
    /// <param name="tracker">The tracker holding the playing streams.</param>
    public StreamLimitController(ActiveStreamTracker tracker)
    {
        _tracker = tracker;
    }

    /// <summary>
    /// Gets the streams playing through each provider, with the provider's limit.
    /// </summary>
    /// <returns>One entry per provider.</returns>
    [HttpGet("ActiveStreams")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult GetActiveStreams()
    {
        IReadOnlyList<ProviderConfig> providers;
        try
        {
            providers = Plugin.Instance.Configuration.Providers;
        }
        catch (Exception)
        {
            return Ok(Array.Empty<object>());
        }

        var streams = _tracker.Registry.Snapshot();
        var result = providers
            .Select((p, i) => new
            {
                ProviderIndex = i,
                ProviderName = p.Name,
                MaxConnections = p.MaxConnections,
                Streams = streams
                    .Where(s => s.ProviderIndex == i)
                    .Select(s => new
                    {
                        s.UserName,
                        s.DeviceName,
                        s.ClientName,
                        s.ItemName,
                        s.StartedUtc,
                    })
                    .ToList(),
            })
            .ToList();

        return Ok(result);
    }
}
