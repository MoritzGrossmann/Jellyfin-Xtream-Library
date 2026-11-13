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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Xtream.Library.Service;

/// <summary>
/// Counts the streams playing through each provider and stops playback that would exceed the
/// provider's <see cref="ProviderConfig.MaxConnections"/>.
/// <para>
/// The STRM files hold the provider's own URL, so playback never passes through the plugin and
/// the plugin cannot refuse the connection itself. It learns about playback from Jellyfin's
/// session events instead. When a start would exceed the limit it shows the configured message
/// on the client and stops that playback. The client has already begun to play for a moment by
/// then, and a client that never reports playback to Jellyfin is not counted.
/// </para>
/// </summary>
public sealed class ActiveStreamTracker : IHostedService, IDisposable
{
    private readonly ISessionManager _sessionManager;
    private readonly IEnumerable<ITunerHost> _tunerHosts;
    private readonly ILogger<ActiveStreamTracker> _logger;
    private bool _subscribed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ActiveStreamTracker"/> class.
    /// </summary>
    /// <param name="sessionManager">Jellyfin's session manager.</param>
    /// <param name="tunerHosts">The registered tuner hosts, to place live channels.</param>
    /// <param name="logger">The logger.</param>
    public ActiveStreamTracker(
        ISessionManager sessionManager,
        IEnumerable<ITunerHost> tunerHosts,
        ILogger<ActiveStreamTracker> logger)
    {
        _sessionManager = sessionManager;
        _tunerHosts = tunerHosts;
        _logger = logger;
    }

    /// <summary>Gets the streams currently playing.</summary>
    public StreamSlotRegistry Registry { get; } = new();

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_subscribed)
        {
            _sessionManager.PlaybackStart += OnPlaybackStart;
            _sessionManager.PlaybackStopped += OnPlaybackStopped;
            _sessionManager.SessionEnded += OnSessionEnded;
            _subscribed = true;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        Unsubscribe();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose() => Unsubscribe();

    /// <summary>
    /// The provider a STRM URL points at, or -1. Matches the base URL and, for the Xtream
    /// routes, the account name, so two providers on one host stay apart. A Dispatcharr VOD
    /// proxy URL carries no account and matches on the Dispatcharr base URL alone.
    /// </summary>
    /// <param name="url">The URL read from a STRM file.</param>
    /// <param name="providers">The configured providers.</param>
    /// <returns>The provider's index, or -1 when none matches.</returns>
    internal static int ResolveProviderIndex(string url, IReadOnlyList<ProviderConfig> providers)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return -1;
        }

        url = url.Trim();
        for (int i = 0; i < providers.Count; i++)
        {
            var p = providers[i];
            string baseUrl = (p.BaseUrl ?? string.Empty).Trim().TrimEnd('/');
            if (baseUrl.Length > 0 && !string.IsNullOrEmpty(p.Username))
            {
                string prefixMovie = $"{baseUrl}/movie/{p.Username}/";
                string prefixSeries = $"{baseUrl}/series/{p.Username}/";
                if (url.StartsWith(prefixMovie, StringComparison.OrdinalIgnoreCase)
                    || url.StartsWith(prefixSeries, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            string dispatcharr = p.EffectiveDispatcharrBaseUrl;
            if (p.EnableDispatcharrMode
                && dispatcharr.Length > 0
                && url.StartsWith(dispatcharr + "/proxy/vod/", StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private void Unsubscribe()
    {
        if (_subscribed)
        {
            _sessionManager.PlaybackStart -= OnPlaybackStart;
            _sessionManager.PlaybackStopped -= OnPlaybackStopped;
            _sessionManager.SessionEnded -= OnSessionEnded;
            _subscribed = false;
        }
    }

    private void OnSessionEnded(object? sender, SessionEventArgs e)
    {
        if (e.SessionInfo?.Id is { } id)
        {
            Registry.Release(id);
        }
    }

    private void OnPlaybackStopped(object? sender, PlaybackStopEventArgs e)
    {
        string? key = KeyOf(e);
        if (key is not null)
        {
            Registry.Release(key, e.Item?.Id.ToString("N", CultureInfo.InvariantCulture));
        }
    }

    private void OnPlaybackStart(object? sender, PlaybackProgressEventArgs e)
    {
        try
        {
            HandlePlaybackStart(e);
        }
        catch (Exception ex)
        {
            // Never let a counting problem reach the playback path.
            _logger.LogWarning(ex, "Could not check the stream limit for {Item}", e.Item?.Name);
        }
    }

    private void HandlePlaybackStart(PlaybackProgressEventArgs e)
    {
        string? key = KeyOf(e);
        var item = e.Item;
        if (key is null || item is null)
        {
            return;
        }

        PluginConfiguration config;
        try
        {
            config = Plugin.Instance.Configuration;
        }
        catch (Exception)
        {
            return;
        }

        int providerIndex = ResolveProvider(item, config.Providers);
        if (providerIndex < 0 || providerIndex >= config.Providers.Count)
        {
            // Not ours. A slot this session held for an earlier item of ours is free now.
            Registry.Release(key);
            return;
        }

        var provider = config.Providers[providerIndex];
        var stream = new ActivePlayback(
            key,
            providerIndex,
            e.Session?.UserName ?? e.Users?.FirstOrDefault()?.Username ?? "?",
            e.DeviceName ?? e.Session?.DeviceName ?? string.Empty,
            e.ClientName ?? e.Session?.Client ?? string.Empty,
            item.Name ?? string.Empty,
            DateTime.UtcNow,
            item.Id.ToString("N", CultureInfo.InvariantCulture));

        if (Registry.TryAcquire(stream, provider.MaxConnections))
        {
            return;
        }

        _logger.LogWarning(
            "Provider {Provider} is at its limit of {Max} streams; stopping {User}'s playback of {Item}",
            provider.Name,
            provider.MaxConnections,
            stream.UserName,
            stream.ItemName);

        if (e.Session?.Id is { } sessionId)
        {
            _ = RefuseAsync(sessionId, provider.Name, provider.EffectiveMaxConnectionsMessage);
        }
    }

    private int ResolveProvider(BaseItem item, IReadOnlyList<ProviderConfig> providers)
    {
        if (item is LiveTvChannel channel)
        {
            string? externalId = channel.ExternalId;
            if (!string.IsNullOrEmpty(externalId))
            {
                foreach (var host in _tunerHosts.OfType<XtreamTunerHost>())
                {
                    if (host.TryGetProviderIndex(externalId, out int index))
                    {
                        return index;
                    }
                }
            }

            return -1;
        }

        string? path = item.Path;
        if (string.IsNullOrEmpty(path) || !path.EndsWith(".strm", StringComparison.OrdinalIgnoreCase))
        {
            return -1;
        }

        try
        {
            using var reader = new StreamReader(path);
            return ResolveProviderIndex(reader.ReadLine() ?? string.Empty, providers);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Could not read STRM file {Path}", path);
            return -1;
        }
    }

    private async Task RefuseAsync(string sessionId, string providerName, string message)
    {
        // Message first: some clients drop it once playback has ended. Either command can fail
        // on a client that cannot be remote controlled; the limit is still counted correctly.
        try
        {
            await _sessionManager.SendMessageCommand(
                sessionId,
                sessionId,
                new MessageCommand { Header = providerName, Text = message, TimeoutMs = 15000 },
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not show the stream limit message to session {Session}", sessionId);
        }

        try
        {
            await _sessionManager.SendPlaystateCommand(
                sessionId,
                sessionId,
                new PlaystateRequest { Command = PlaystateCommand.Stop },
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not stop playback of session {Session}", sessionId);
        }
    }

    private static string? KeyOf(PlaybackProgressEventArgs e)
        => e.Session?.Id ?? e.PlaySessionId ?? e.DeviceId;
}
