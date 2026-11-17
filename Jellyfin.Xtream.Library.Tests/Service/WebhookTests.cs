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

public class WebhookDescriptionTests
{
    [Fact]
    public void EveryEvent_GetsTitleMessageAndColor()
    {
        foreach (var name in WebhookEvents.All)
        {
            var all = WebhookService.WithCommon(name, WebhookService.SampleData(name));
            all["Title"].Should().BeOfType<string>().Which.Should().NotBeNullOrEmpty(name);
            all["Message"].Should().BeOfType<string>().Which.Should().NotBeNullOrEmpty(name);
            all["Color"].Should().BeOfType<int>();
        }
    }

    [Fact]
    public void LimitMessage_NamesUserItemAndProvider()
    {
        var all = WebhookService.WithCommon(WebhookEvents.StreamLimitReached, WebhookService.SampleData(WebhookEvents.StreamLimitReached));
        string message = (string)all["Message"]!;
        message.Should().Contain("alice").And.Contain("The Matrix (1999)").And.Contain("Provider 1");
    }

    [Fact]
    public void FailedSync_TitleSaysFailed_AndKeepsError()
    {
        var data = WebhookService.SampleData(WebhookEvents.SyncCompleted);
        data["Success"] = false;
        data["Error"] = "boom";
        var all = WebhookService.WithCommon(WebhookEvents.SyncCompleted, data);
        all["Title"].Should().Be("Library sync failed");
        ((string)all["Message"]!).Should().StartWith("boom");
    }

    [Fact]
    public void FormEscape_PercentEncodes()
        => WebhookTemplate.Render("m={{X}}&r={{{X}}}", new Dictionary<string, object?> { ["X"] = "a b&c" }, WebhookEscape.Url)
            .Should().Be("m=a%20b%26c&r=a b&c");

    [Theory]
    [InlineData("application/json", WebhookEscape.Json)]
    [InlineData("application/x-www-form-urlencoded", WebhookEscape.Url)]
    [InlineData("text/plain", WebhookEscape.None)]
    public void ContentType_DecidesEscaping(string contentType, WebhookEscape expected)
        => new WebhookConfig { ContentType = contentType }.Escape.Should().Be(expected);
}

public class WebhookIfEqualsTests
{
    private static Dictionary<string, object?> D(string type, string other = "")
        => new() { ["NotificationType"] = type, ["Other"] = other };

    [Theory]
    [InlineData("StreamStarted", "start")]
    [InlineData("StreamStopped", "stop")]
    [InlineData("SyncStarted", "other")]
    public void IfEquals_PicksTheMatchingBranch(string type, string expected)
        => WebhookTemplate.Render(
                "{{#if_equals NotificationType 'StreamStarted'}}start{{else}}{{#if_equals NotificationType \"StreamStopped\"}}stop{{else}}other{{/if_equals}}{{/if_equals}}",
                D(type),
                false)
            .Should().Be(expected);

    [Fact]
    public void IfEquals_ComparesTwoValues()
    {
        const string t = "{{#if_equals NotificationType Other}}same{{else}}diff{{/if_equals}}";
        WebhookTemplate.Render(t, D("A", "A"), false).Should().Be("same");
        WebhookTemplate.Render(t, D("A", "B"), false).Should().Be("diff");
    }

    [Fact]
    public void IfNotEquals_IsTheInverse()
    {
        const string t = "{{#if_not_equals NotificationType 'X'}}not x{{else}}x{{/if_not_equals}}";
        WebhookTemplate.Render(t, D("A"), false).Should().Be("not x");
        WebhookTemplate.Render(t, D("X"), false).Should().Be("x");
    }

    [Fact]
    public void QuotedLiteral_IsNotTakenForAName()
        => WebhookTemplate.Render("{{#if_equals 'Other' Other}}y{{else}}n{{/if_equals}}", D("A", "Other"), false).Should().Be("y");

    [Fact]
    public void LiteralMayContainSpaces()
        => WebhookTemplate.Render("{{#if_equals Other 'a b'}}y{{else}}n{{/if_equals}}", D("A", "a b"), false).Should().Be("y");

    [Theory]
    [InlineData("{{#if_equals A}}x{{/if_equals}}")]
    [InlineData("{{#if_equals A 'B' C}}x{{/if_equals}}")]
    [InlineData("{{#if_equals A 'B}}x{{/if_equals}}")]
    [InlineData("{{#if_equals A 'B'}}x")]
    [InlineData("{{#if_equals A 'B'}}x{{/if_not_equals}}")]
    public void Malformed_IsReported(string template)
        => WebhookTemplate.Validate(template).Should().NotBeNull();
}
