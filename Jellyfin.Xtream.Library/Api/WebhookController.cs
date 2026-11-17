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
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Xtream.Library.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Xtream.Library.Api;

/// <summary>
/// Lets the settings page try a webhook before it is saved.
/// </summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("XtreamLibrary/Webhooks")]
[Produces(MediaTypeNames.Application.Json)]
public class WebhookController : ControllerBase
{
    /// <summary>
    /// Sends a webhook with sample values.
    /// </summary>
    /// <param name="request">The webhook as it stands in the page, and the event to imitate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Whether the receiver accepted the request.</returns>
    [HttpPost("Test")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<WebhookTestResult>> Test(
        [FromBody] WebhookTestRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var webhook = request.Webhook;
        if (webhook is null || !Uri.TryCreate((webhook.Url ?? string.Empty).Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return BadRequest(new WebhookTestResult { Success = false, Message = "Enter an http:// or https:// URL." });
        }

        string eventName = request.Event ?? string.Empty;
        if (!WebhookEvents.All.Contains(eventName, StringComparer.Ordinal))
        {
            return BadRequest(new WebhookTestResult { Success = false, Message = "Unknown event." });
        }

        string? templateError = WebhookTemplate.Validate(webhook.Template ?? string.Empty);
        if (templateError is not null)
        {
            return Ok(new WebhookTestResult { Success = false, Message = "Template error: " + templateError });
        }

        var service = WebhookService.Current;
        if (service is null)
        {
            return Ok(new WebhookTestResult { Success = false, Message = "The webhook service is not running yet." });
        }

        var result = await service.SendTestAsync(webhook, eventName, cancellationToken).ConfigureAwait(false);
        return Ok(new WebhookTestResult { Success = result.Success, StatusCode = result.StatusCode, Message = result.Message });
    }
}

/// <summary>
/// Request to send a test webhook.
/// </summary>
public class WebhookTestRequest
{
    /// <summary>Gets or sets the webhook to send.</summary>
    public WebhookConfig? Webhook { get; set; }

    /// <summary>Gets or sets the event whose sample values are used.</summary>
    public string? Event { get; set; }
}

/// <summary>
/// Outcome of a test webhook.
/// </summary>
public class WebhookTestResult
{
    /// <summary>Gets or sets a value indicating whether the receiver accepted the request.</summary>
    public bool Success { get; set; }

    /// <summary>Gets or sets the HTTP status the receiver answered with, or 0.</summary>
    public int StatusCode { get; set; }

    /// <summary>Gets or sets a short description.</summary>
    public string Message { get; set; } = string.Empty;
}
