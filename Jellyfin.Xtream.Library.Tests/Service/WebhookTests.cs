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
using FluentAssertions;
using Jellyfin.Xtream.Library.Service;
using Xunit;

namespace Jellyfin.Xtream.Library.Tests.Service;

public class WebhookTemplateTests
{
    private static Dictionary<string, object?> Data(params (string, object?)[] pairs)
    {
        var d = new Dictionary<string, object?>();
        foreach (var (k, v) in pairs)
        {
            d[k] = v;
        }

        return d;
    }

    [Fact]
    public void Variable_IsInserted()
        => WebhookTemplate.Render("{{UserName}} plays {{ItemName}}", Data(("UserName", "alice"), ("ItemName", "Heat")), false)
            .Should().Be("alice plays Heat");

    [Fact]
    public void Variable_IsEscapedForJson_ButTripleStashIsNot()
    {
        var data = Data(("ItemName", "The \"Matrix\"\n"));
        WebhookTemplate.Render("{\"t\":\"{{ItemName}}\"}", data, true).Should().Be("{\"t\":\"The \\\"Matrix\\\"\\n\"}");
        WebhookTemplate.Render("{{{ItemName}}}", data, true).Should().Be("The \"Matrix\"\n");
        WebhookTemplate.Render("{{ItemName}}", data, false).Should().Be("The \"Matrix\"\n");
    }

    [Fact]
    public void UnknownVariable_RendersNothing()
        => WebhookTemplate.Render("a{{Nope}}b", Data(), false).Should().Be("ab");

    [Fact]
    public void Numbers_UseInvariantCulture()
        => WebhookTemplate.Render("{{N}}", Data(("N", 1234.5)), false).Should().Be("1234.5");

    [Theory]
    [InlineData(true, "yes")]
    [InlineData(false, "no")]
    public void If_PicksBranchByTruthiness(bool success, string expected)
        => WebhookTemplate.Render("{{#if Success}}yes{{else}}no{{/if}}", Data(("Success", success)), false).Should().Be(expected);

    [Fact]
    public void If_TreatsEmptyStringAndZeroAndMissingAsFalse()
    {
        const string t = "{{#if X}}T{{else}}F{{/if}}";
        WebhookTemplate.Render(t, Data(("X", string.Empty)), false).Should().Be("F");
        WebhookTemplate.Render(t, Data(("X", 0)), false).Should().Be("F");
        WebhookTemplate.Render(t, Data(), false).Should().Be("F");
        WebhookTemplate.Render(t, Data(("X", 3)), false).Should().Be("T");
    }

    [Fact]
    public void Unless_And_Nesting_Work()
        => WebhookTemplate.Render(
                "{{#if A}}a{{#unless B}}-nob{{/unless}}{{#if B}}-b{{/if}}{{/if}}",
                Data(("A", true), ("B", false)),
                false)
            .Should().Be("a-nob");

    [Fact]
    public void Comment_IsDropped()
        => WebhookTemplate.Render("a{{! note }}b", Data(), false).Should().Be("ab");

    [Theory]
    [InlineData("{{#if A}}open")]
    [InlineData("{{/if}}")]
    [InlineData("{{#if A}}x{{/unless}}")]
    [InlineData("{{Unclosed")]
    public void Malformed_IsReported(string template)
        => WebhookTemplate.Validate(template).Should().NotBeNull();

    [Fact]
    public void Valid_TemplateHasNoError()
        => WebhookTemplate.Validate("{{#if A}}x{{else}}y{{/if}}{{B}}").Should().BeNull();
}

public class WebhookServiceTests
{
    [Fact]
    public void EveryEventHasSampleValues()
    {
        foreach (var name in WebhookEvents.All)
        {
            WebhookService.SampleData(name).Should().NotBeEmpty(name);
        }
    }

    [Fact]
    public void WithCommon_AddsTypeAndTimestamp_WithoutChangingInput()
    {
        var input = new Dictionary<string, object?> { ["A"] = 1 };
        var all = WebhookService.WithCommon(WebhookEvents.SyncStarted, input);
        all["NotificationType"].Should().Be(WebhookEvents.SyncStarted);
        all.Should().ContainKey("UtcTimestamp");
        input.Should().HaveCount(1);
    }

    [Fact]
    public void StreamData_CarriesWhatTheTemplatesDocument()
    {
        var provider = new ProviderConfig { Name = "P1", MaxConnections = 2 };
        var stream = new ActivePlayback("k", 0, "alice", "TV", "Web", "Heat", System.DateTime.UtcNow, "abc");
        var data = WebhookService.StreamData(stream, provider, 1);
        data["UserName"].Should().Be("alice");
        data["ItemName"].Should().Be("Heat");
        data["ProviderName"].Should().Be("P1");
        data["ActiveStreams"].Should().Be(1);
        data["MaxConnections"].Should().Be(2);
    }

    [Fact]
    public void Webhook_IsUsable_NeedsEnabledAndHttpUrl()
    {
        new WebhookConfig { Url = "https://example.com/x" }.IsUsable.Should().BeTrue();
        new WebhookConfig { Url = "ftp://example.com/x" }.IsUsable.Should().BeFalse();
        new WebhookConfig { Url = "nonsense" }.IsUsable.Should().BeFalse();
        new WebhookConfig { Url = "https://example.com/x", IsEnabled = false }.IsUsable.Should().BeFalse();
    }

    [Fact]
    public void Publish_WithoutService_DoesNothing()
    {
        System.Action act = () => WebhookService.Publish(WebhookEvents.SyncStarted, new Dictionary<string, object?>());
        act.Should().NotThrow();
    }
}
