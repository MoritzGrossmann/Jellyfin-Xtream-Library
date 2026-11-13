// Copyright (C) 2024  Roland Breitschaft

// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

// Limit on parallel streams per provider: the slot counting and the match of a STRM URL to a
// provider. The Jellyfin event wiring around them needs a running server and is not covered.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Jellyfin.Xtream.Library;
using Jellyfin.Xtream.Library.Service;
using Xunit;

namespace Jellyfin.Xtream.Library.Tests.Service;

public class StreamLimitTests
{
    private static ActivePlayback Stream(string key, int provider = 0, string item = "i1")
        => new(key, provider, "user-" + key, "device", "client", "Title", DateTime.UtcNow, item);

    [Fact]
    public void ZeroMeansNoLimit()
    {
        var registry = new StreamSlotRegistry();

        for (int i = 0; i < 50; i++)
        {
            registry.TryAcquire(Stream("s" + i), 0).Should().BeTrue();
        }

        registry.Count(0).Should().Be(50);
    }

    [Fact]
    public void TheStreamBeyondTheLimitIsRefusedAndNotRegistered()
    {
        var registry = new StreamSlotRegistry();

        registry.TryAcquire(Stream("a"), 2).Should().BeTrue();
        registry.TryAcquire(Stream("b"), 2).Should().BeTrue();
        registry.TryAcquire(Stream("c"), 2).Should().BeFalse();

        registry.Count(0).Should().Be(2);
        registry.Snapshot().Should().NotContain(s => s.Key == "c");
    }

    [Fact]
    public void AFreedSlotCanBeTakenAgain()
    {
        var registry = new StreamSlotRegistry();
        registry.TryAcquire(Stream("a"), 1);

        registry.TryAcquire(Stream("b"), 1).Should().BeFalse();
        registry.Release("a").Should().BeTrue();
        registry.TryAcquire(Stream("b"), 1).Should().BeTrue();
    }

    [Fact]
    public void LimitsAreCountedPerProvider()
    {
        var registry = new StreamSlotRegistry();
        registry.TryAcquire(Stream("a", provider: 0), 1);

        registry.TryAcquire(Stream("b", provider: 1), 1).Should().BeTrue();
        registry.Count(0).Should().Be(1);
        registry.Count(1).Should().Be(1);
    }

    [Fact]
    public void ASessionThatReportsAgainDoesNotCountTwice()
    {
        var registry = new StreamSlotRegistry();

        registry.TryAcquire(Stream("a", item: "ep1"), 1).Should().BeTrue();
        registry.TryAcquire(Stream("a", item: "ep2"), 1).Should().BeTrue();

        registry.Count(0).Should().Be(1);
    }

    [Fact]
    public void ARefusedSessionLosesTheSlotItHeldForAnEarlierItem()
    {
        // Session a holds a slot on provider 0, then starts something on provider 1 that is full.
        // Its old stream is no longer playing, so provider 0 must not keep counting it.
        var registry = new StreamSlotRegistry();
        registry.TryAcquire(Stream("a", provider: 0), 1);
        registry.TryAcquire(Stream("b", provider: 1), 1);

        registry.TryAcquire(Stream("a", provider: 1), 1).Should().BeFalse();

        registry.Count(0).Should().Be(0);
    }

    [Fact]
    public void ALateStopOfThePreviousItemKeepsTheNewSlot()
    {
        var registry = new StreamSlotRegistry();
        registry.TryAcquire(Stream("a", item: "ep2"), 1);

        registry.Release("a", "ep1").Should().BeFalse();

        registry.Count(0).Should().Be(1);
        registry.Release("a", "ep2").Should().BeTrue();
    }

    [Fact]
    public void ReleasingAnUnknownKeyIsHarmless()
    {
        new StreamSlotRegistry().Release("nobody").Should().BeFalse();
    }

    private static List<ProviderConfig> Providers() => new()
    {
        new ProviderConfig { BaseUrl = "http://host-a:8080", Username = "alice" },
        new ProviderConfig { BaseUrl = "http://host-a:8080/", Username = "bob" },
        new ProviderConfig
        {
            BaseUrl = "http://xtream.example",
            Username = "carol",
            EnableDispatcharrMode = true,
            DispatcharrBaseUrl = "http://dispatcharr:9191",
        },
    };

    [Theory]
    [InlineData("http://host-a:8080/movie/alice/pw/123.mp4", 0)]
    [InlineData("http://host-a:8080/series/alice/pw/9.mkv", 0)]
    [InlineData("http://host-a:8080/movie/bob/pw/123.mp4", 1)]
    [InlineData("  HTTP://HOST-A:8080/movie/bob/pw/123.mp4\n", 1)]
    [InlineData("http://dispatcharr:9191/proxy/vod/movie/uuid?stream_id=5", 2)]
    [InlineData("http://host-a:8080/movie/mallory/pw/123.mp4", -1)]
    [InlineData("http://elsewhere/movie/alice/pw/123.mp4", -1)]
    [InlineData("", -1)]
    public void AStrmUrlIsMatchedToItsProvider(string url, int expected)
    {
        ActiveStreamTracker.ResolveProviderIndex(url, Providers()).Should().Be(expected);
    }

    [Fact]
    public void ADispatcharrUrlMatchesOnlyWhenDispatcharrModeIsOn()
    {
        var providers = Providers();
        providers[2].EnableDispatcharrMode = false;

        ActiveStreamTracker.ResolveProviderIndex("http://dispatcharr:9191/proxy/vod/movie/uuid", providers)
            .Should().Be(-1);
    }

    [Fact]
    public void ABlankMessageFallsBackToTheDefault()
    {
        new ProviderConfig { MaxConnectionsMessage = "  " }.EffectiveMaxConnectionsMessage
            .Should().Be(ProviderConfig.DefaultMaxConnectionsMessage);
        new ProviderConfig { MaxConnectionsMessage = " Full up " }.EffectiveMaxConnectionsMessage
            .Should().Be("Full up");
    }

    [Fact]
    public void ValidateClampsTheLimit()
    {
        var provider = new ProviderConfig { MaxConnections = -3 };
        provider.Validate();
        provider.MaxConnections.Should().Be(0);

        provider.MaxConnections = 100000;
        provider.Validate();
        provider.MaxConnections.Should().Be(1000);
    }
}
