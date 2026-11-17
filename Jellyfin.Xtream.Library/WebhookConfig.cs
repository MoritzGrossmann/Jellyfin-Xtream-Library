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

namespace Jellyfin.Xtream.Library;

/// <summary>
/// A destination that receives an HTTP request for the events it subscribes to. Modelled on
/// the official Jellyfin webhook plugin: a URL, headers and a template for the body.
/// </summary>
public class WebhookConfig
{
    /// <summary>Gets or sets the name shown in the settings page.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether this webhook is sent.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the service this webhook was set up for (Generic, Discord, Slack, Gotify,
    /// Pushover, Telegram or Ntfy). The settings page uses it to fill in a template; the
    /// request itself is built from the fields below.
    /// </summary>
    public string Service { get; set; } = "Generic";

    /// <summary>Gets or sets the URL the request is sent to.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Gets or sets the Content-Type of the request body.</summary>
    public string ContentType { get; set; } = "application/json";

    /// <summary>Gets or sets the HTTP method, POST or PUT.</summary>
    public string Method { get; set; } = "POST";

    /// <summary>Gets or sets the template the body is rendered from.</summary>
    public string Template { get; set; } = string.Empty;

    /// <summary>Gets or sets the extra request headers.</summary>
    public List<WebhookHeader> Headers { get; set; } = new();

    /// <summary>Gets or sets the names of the events this webhook receives.</summary>
    public List<string> Events { get; set; } = new();

    /// <summary>
    /// Gets a value indicating whether this webhook can be sent: enabled, with an http(s) URL.
    /// </summary>
    public bool IsUsable
        => IsEnabled
        && Uri.TryCreate((Url ?? string.Empty).Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    /// <summary>
    /// Gets a value indicating whether the body is JSON, which decides how values are escaped
    /// when a template inserts them.
    /// </summary>
    public bool IsJson
        => (ContentType ?? string.Empty).Contains("json", StringComparison.OrdinalIgnoreCase);

    /// <summary>Gets how a value inserted with a template is escaped for this body.</summary>
    public WebhookEscape Escape
        => IsJson ? WebhookEscape.Json
        : (ContentType ?? string.Empty).Contains("x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase) ? WebhookEscape.Url
        : WebhookEscape.None;
}
