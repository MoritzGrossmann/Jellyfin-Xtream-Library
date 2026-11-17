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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Xtream.Library.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Xtream.Library.Service;

/// <summary>
/// Sends the configured webhooks when something happens in the plugin.
/// <para>
/// Reached through <see cref="Publish"/>, a static entry point, because the places that raise
/// events (the HTTP client, the sync, the playback tracker) are built by hand in many tests and
/// by Jellyfin's container elsewhere, and none should need a new constructor argument for what
/// is an optional side channel. Publishing never throws and never waits for the webhook.
/// </para>
/// </summary>
public sealed class WebhookService : IHostedService
{
    /// <summary>The shortest time between two auth-failure webhooks for one provider.</summary>
    internal static readonly TimeSpan AuthFailureInterval = TimeSpan.FromMinutes(15);

    private static readonly HttpClient Http = CreateHttpClient();

    private readonly ILogger<WebhookService> _logger;
    private readonly ConcurrentDictionary<string, DateTime> _lastAuthFailure = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="WebhookService"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public WebhookService(ILogger<WebhookService> logger)
    {
        _logger = logger;
        Current = this;
    }

    /// <summary>Gets the running instance, or null before the plugin has started.</summary>
    internal static WebhookService? Current { get; private set; }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Sends an event to every webhook that subscribes to it. Returns at once.
    /// </summary>
    /// <param name="eventName">One of <see cref="WebhookEvents"/>.</param>
    /// <param name="data">The values a template can use.</param>
    public static void Publish(string eventName, Dictionary<string, object?> data)
        => Current?.PublishInternal(eventName, data);

    /// <summary>
    /// Sends the auth-failure event for the provider a connection belongs to, unless it was
    /// already sent for that provider a short while ago. A failing account is retried by every
    /// request of a sync, and one message per request would flood the channel.
    /// </summary>
    /// <param name="connection">The connection that was refused.</param>
    /// <param name="reason">What the provider answered.</param>
    /// <param name="statusCode">The HTTP status, or 0 when the provider answered 200 with auth 0.</param>
    public static void PublishAuthFailure(ConnectionInfo connection, string reason, int statusCode)
        => Current?.PublishAuthFailureInternal(connection, reason, statusCode);

    /// <summary>
    /// Builds the template values describing a stream.
    /// </summary>
    /// <param name="stream">The stream.</param>
    /// <param name="provider">The provider it plays through.</param>
    /// <param name="activeStreams">How many streams the provider has in use.</param>
    /// <returns>The values.</returns>
    internal static Dictionary<string, object?> StreamData(ActivePlayback stream, ProviderConfig provider, int activeStreams)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(provider);
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["ProviderName"] = provider.Name,
            ["ProviderIndex"] = stream.ProviderIndex,
            ["UserName"] = stream.UserName,
            ["DeviceName"] = stream.DeviceName,
            ["ClientName"] = stream.ClientName,
            ["ItemName"] = stream.ItemName,
            ["ItemId"] = stream.ItemId,
            ["ActiveStreams"] = activeStreams,
            ["MaxConnections"] = provider.MaxConnections,
        };
    }

    /// <summary>
    /// Sends a webhook with sample values, to check the URL and template.
    /// </summary>
    /// <param name="webhook">The webhook, as it is in the settings page, saved or not.</param>
    /// <param name="eventName">The event to imitate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What happened.</returns>
    public async Task<WebhookSendResult> SendTestAsync(WebhookConfig webhook, string eventName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(webhook);
        return await SendAsync(webhook, eventName, SampleData(eventName), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets example values for an event, which is also what the settings page documents.
    /// </summary>
    /// <param name="eventName">The event.</param>
    /// <returns>Values for every name a template can use for that event.</returns>
    internal static Dictionary<string, object?> SampleData(string eventName)
    {
        var data = new Dictionary<string, object?>(StringComparer.Ordinal);
        switch (eventName)
        {
            case WebhookEvents.StreamStarted:
            case WebhookEvents.StreamStopped:
            case WebhookEvents.StreamLimitReached:
                data["ProviderName"] = "Provider 1";
                data["ProviderIndex"] = 0;
                data["UserName"] = "alice";
                data["DeviceName"] = "Living room TV";
                data["ClientName"] = "Jellyfin Web";
                data["ItemName"] = "The Matrix (1999)";
                data["ItemId"] = "0123456789abcdef0123456789abcdef";
                data["ActiveStreams"] = 2;
                data["MaxConnections"] = 2;
                break;
            case WebhookEvents.SyncStarted:
                data["ProviderCount"] = 1;
                break;
            case WebhookEvents.SyncCompleted:
                data["Success"] = true;
                data["Error"] = string.Empty;
                data["DurationSeconds"] = 312;
                data["MoviesCreated"] = 12;
                data["MoviesUpdated"] = 3;
                data["MoviesDeleted"] = 1;
                data["SeriesCreated"] = 2;
                data["EpisodesCreated"] = 40;
                data["EpisodesUpdated"] = 5;
                data["EpisodesDeleted"] = 0;
                data["ChannelsCreated"] = 0;
                data["ChannelsUpdated"] = 0;
                data["ChannelsDeleted"] = 0;
                data["Errors"] = 0;
                break;
            case WebhookEvents.ProviderAuthFailed:
                data["ProviderName"] = "Provider 1";
                data["ProviderIndex"] = 0;
                data["Host"] = "provider.example.com";
                data["Reason"] = "HTTP 401 Unauthorized";
                data["StatusCode"] = 401;
                break;
        }

        return data;
    }

    /// <summary>
    /// Fills in the values every event carries.
    /// </summary>
    /// <param name="eventName">The event.</param>
    /// <param name="data">The event's own values.</param>
    /// <returns>A copy with the shared values added.</returns>
    internal static Dictionary<string, object?> WithCommon(string eventName, Dictionary<string, object?> data)
    {
        var all = new Dictionary<string, object?>(data, StringComparer.Ordinal)
        {
            ["NotificationType"] = eventName,
            ["UtcTimestamp"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
        };
        return all;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Jellyfin-Xtream-Library-Webhook");
        return client;
    }

    private void PublishInternal(string eventName, Dictionary<string, object?> data)
    {
        try
        {
            PluginConfiguration config;
            try
            {
                config = Plugin.Instance.Configuration;
            }
            catch (Exception)
            {
                return;
            }

            var targets = config.Webhooks
                .Where(w => w.IsUsable && w.Events.Contains(eventName, StringComparer.Ordinal))
                .ToList();
            if (targets.Count == 0)
            {
                return;
            }

            var values = WithCommon(eventName, data);
            foreach (var webhook in targets)
            {
                _ = Task.Run(async () =>
                {
                    var result = await SendAsync(webhook, eventName, values, CancellationToken.None).ConfigureAwait(false);
                    if (!result.Success)
                    {
                        _logger.LogWarning("Webhook '{Name}' for {Event} failed: {Message}", webhook.Name, eventName, result.Message);
                    }
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not publish the {Event} webhook", eventName);
        }
    }

    private void PublishAuthFailureInternal(ConnectionInfo connection, string reason, int statusCode)
    {
        try
        {
            string key = connection.BaseUrl + "|" + connection.UserName;
            var now = DateTime.UtcNow;
            if (_lastAuthFailure.TryGetValue(key, out var last) && now - last < AuthFailureInterval)
            {
                return;
            }

            _lastAuthFailure[key] = now;

            string providerName = string.Empty;
            int providerIndex = -1;
            try
            {
                var providers = Plugin.Instance.Configuration.Providers;
                for (int i = 0; i < providers.Count; i++)
                {
                    if (string.Equals(providers[i].BaseUrl?.Trim().TrimEnd('/'), connection.BaseUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)
                        && string.Equals(providers[i].Username?.Trim(), connection.UserName, StringComparison.Ordinal))
                    {
                        providerName = providers[i].Name;
                        providerIndex = i;
                        break;
                    }
                }
            }
            catch (Exception)
            {
                // The plugin is not initialised; the event still goes out without a provider name.
            }

            string host = Uri.TryCreate(connection.BaseUrl, UriKind.Absolute, out var uri) ? uri.Host : string.Empty;
            PublishInternal(WebhookEvents.ProviderAuthFailed, new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["ProviderName"] = providerName,
                ["ProviderIndex"] = providerIndex,
                ["Host"] = host,
                ["Reason"] = reason,
                ["StatusCode"] = statusCode,
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not publish the auth failure webhook");
        }
    }

    private async Task<WebhookSendResult> SendAsync(
        WebhookConfig webhook,
        string eventName,
        Dictionary<string, object?> data,
        CancellationToken cancellationToken)
    {
        string body;
        try
        {
            var values = data.ContainsKey("NotificationType") ? data : WithCommon(eventName, data);
            body = WebhookTemplate.Render(webhook.Template, new Dictionary<string, object?>(values), webhook.IsJson);
        }
        catch (FormatException ex)
        {
            return new WebhookSendResult(false, 0, "Template error: " + ex.Message);
        }

        try
        {
            var method = string.Equals(webhook.Method, "PUT", StringComparison.OrdinalIgnoreCase) ? HttpMethod.Put : HttpMethod.Post;
            using var request = new HttpRequestMessage(method, webhook.Url.Trim());
            string contentType = string.IsNullOrWhiteSpace(webhook.ContentType) ? "application/json" : webhook.ContentType.Trim();
            request.Content = new StringContent(body, Encoding.UTF8, contentType.Split(';')[0].Trim());
            foreach (var header in webhook.Headers.Where(h => !string.IsNullOrWhiteSpace(h.Key)))
            {
                if (!request.Headers.TryAddWithoutValidation(header.Key.Trim(), header.Value))
                {
                    request.Content.Headers.TryAddWithoutValidation(header.Key.Trim(), header.Value);
                }
            }

            using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            int status = (int)response.StatusCode;
            return response.IsSuccessStatusCode
                ? new WebhookSendResult(true, status, "Sent")
                : new WebhookSendResult(false, status, $"The server answered HTTP {status}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or UriFormatException)
        {
            return new WebhookSendResult(false, 0, ex.Message);
        }
    }
}

/// <summary>
/// What came of sending a webhook.
/// </summary>
/// <param name="Success">Whether the receiver accepted it.</param>
/// <param name="StatusCode">The HTTP status, or 0 when there was no answer.</param>
/// <param name="Message">A short description.</param>
public sealed record WebhookSendResult(bool Success, int StatusCode, string Message);
