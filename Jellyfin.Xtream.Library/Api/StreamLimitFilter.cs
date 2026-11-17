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
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Xtream.Library.Service;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.MediaInfo;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Xtream.Library.Api;

/// <summary>
/// Refuses a playback request when the item's provider has no free stream left.
/// <para>
/// A client asks Jellyfin for the playback info before it opens the stream. Refusing there
/// works even when the provider itself rejects a connection over its limit, in which case the
/// client sits on a loading animation and never reports playback for the tracker to see.
/// </para>
/// </summary>
public sealed class StreamLimitFilter : IAsyncActionFilter
{
    private readonly ActiveStreamTracker _tracker;
    private readonly ISessionManager _sessionManager;
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<StreamLimitFilter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamLimitFilter"/> class.
    /// </summary>
    /// <param name="tracker">The tracker holding the playing streams.</param>
    /// <param name="sessionManager">Jellyfin's session manager.</param>
    /// <param name="libraryManager">Jellyfin's library manager.</param>
    /// <param name="logger">The logger.</param>
    public StreamLimitFilter(
        ActiveStreamTracker tracker,
        ISessionManager sessionManager,
        ILibraryManager libraryManager,
        ILogger<StreamLimitFilter> logger)
    {
        _tracker = tracker;
        _sessionManager = sessionManager;
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        try
        {
            var refusal = await CheckAsync(context).ConfigureAwait(false);
            if (refusal is not null)
            {
                context.Result = refusal;
                return;
            }
        }
        catch (Exception ex)
        {
            // Never let a counting problem reach the playback path.
            _logger.LogWarning(ex, "Could not check the stream limit for a playback request");
        }

        await next().ConfigureAwait(false);
    }

    private async Task<IActionResult?> CheckAsync(ActionExecutingContext context)
    {
        var request = context.HttpContext.Request;
        if (!request.Path.Value?.EndsWith("/PlaybackInfo", StringComparison.OrdinalIgnoreCase) ?? true)
        {
            return null;
        }

        if (!context.RouteData.Values.TryGetValue("itemId", out var raw)
            || !Guid.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), out var itemId))
        {
            return null;
        }

        var item = _libraryManager.GetItemById(itemId);
        if (item is null)
        {
            return null;
        }

        var session = FindSession(context);
        var provider = _tracker.FindFullProvider(item, session?.Id);
        if (provider is null)
        {
            return null;
        }

        _logger.LogWarning(
            "Provider {Provider} is at its limit of {Max} streams; refusing playback of {Item} for {User}",
            provider.Name,
            provider.MaxConnections,
            item.Name,
            session?.UserName ?? "?");

        _tracker.PublishLimitReached(item, session, provider);

        if (session is not null)
        {
            await _tracker.NotifyAsync(session.Id, provider).ConfigureAwait(false);
        }

        return new OkObjectResult(new PlaybackInfoResponse { ErrorCode = PlaybackErrorCode.NotAllowed });
    }

    private SessionInfo? FindSession(ActionExecutingContext context)
    {
        var user = context.HttpContext.User;
        string? deviceId = user.FindFirst("Jellyfin-DeviceId")?.Value;
        if (string.IsNullOrEmpty(deviceId))
        {
            return null;
        }

        var userId = Guid.TryParse(user.FindFirst("Jellyfin-UserId")?.Value, out var parsed) ? parsed : Guid.Empty;
        var matches = _sessionManager.Sessions
            .Where(s => string.Equals(s.DeviceId, deviceId, StringComparison.Ordinal))
            .ToList();
        return matches.FirstOrDefault(s => s.UserId == userId) ?? matches.FirstOrDefault();
    }
}
