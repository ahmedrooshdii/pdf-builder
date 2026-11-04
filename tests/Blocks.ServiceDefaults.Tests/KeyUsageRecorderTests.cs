using System.Net.Http.Json;
using System.Text.Json;
using Blocks.ServiceDefaults.Authentication;
using Blocks.ServiceDefaults.Clients;
using Blocks.ServiceDefaults.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Blocks.ServiceDefaults.Tests;

/// <summary>Story 6 and FR-012: last use to the minute, off the request path, one write per key per minute.</summary>
public sealed class KeyUsageRecorderTests
{
    private static readonly string[] Generate = ["pdf:generate"];
    private static readonly string[] BothKeys = ["aaaaaaaa", "bbbbbbbb"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static List<string> Drain(KeyUsageRecorder recorder)
    {
        var queued = new List<string>();
        while (recorder.Pending.TryRead(out var keyId))
            queued.Add(keyId);
        return queued;
    }

    [Fact]
    public void A_key_used_a_thousand_times_in_a_minute_is_queued_once_and_again_the_next_minute()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero));
        var recorder = new KeyUsageRecorder(clock);

        for (var i = 0; i < 1000; i++)
        {
            recorder.Record("aaaaaaaa");
            if (i % 100 == 0)
                clock.Advance(TimeSpan.FromSeconds(5));
        }
        recorder.Record("bbbbbbbb");

        Assert.Equal(["aaaaaaaa", "bbbbbbbb"], Drain(recorder));

        clock.Advance(KeyUsageRecorder.Interval);
        recorder.Record("aaaaaaaa");
        Assert.Equal(["aaaaaaaa"], Drain(recorder));
    }

    [Fact]
    public async Task A_flush_writes_each_key_once_at_the_minute()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 11, 4, 9, 41, 37, TimeSpan.Zero));
        var recorder = new KeyUsageRecorder(clock);
        var store = Substitute.For<IClientKeyStore>();
        using var services = new ServiceCollection().AddSingleton(store).BuildServiceProvider();
        var flusher = new KeyUsageFlushService(recorder, services.GetRequiredService<IServiceScopeFactory>(), clock, NullLogger<KeyUsageFlushService>.Instance);

        recorder.Record("aaaaaaaa");
        recorder.Record("bbbbbbbb");
        await flusher.FlushAsync(Ct);
        await flusher.FlushAsync(Ct);       // nothing queued: no write

        await store.Received(1).RecordLastUsedAsync(
            Arg.Is<IReadOnlyCollection<string>>(ids => ids.Order().SequenceEqual(BothKeys)),
            new DateTimeOffset(2026, 11, 4, 9, 41, 0, TimeSpan.Zero),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_failed_write_is_logged_and_the_next_batch_is_still_written()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero));
        var recorder = new KeyUsageRecorder(clock);
        var store = Substitute.For<IClientKeyStore>();
        store.RecordLastUsedAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new StorageUnavailableException()), Task.CompletedTask);
        using var services = new ServiceCollection().AddSingleton(store).BuildServiceProvider();
        var logs = new CapturingLoggerProvider();
        using var factory = LoggerFactory.Create(b => b.AddProvider(logs));
        var flusher = new KeyUsageFlushService(recorder, services.GetRequiredService<IServiceScopeFactory>(), clock, factory.CreateLogger<KeyUsageFlushService>());

        recorder.Record("aaaaaaaa");
        await flusher.FlushAsync(Ct);
        recorder.Record("bbbbbbbb");
        await flusher.FlushAsync(Ct);

        await store.Received(2).RecordLastUsedAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        Assert.Contains(logs.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("could not be recorded", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_request_never_waits_for_the_write()
    {
        // A store whose last-use write never finishes: requests must not notice.
        var gate = new TaskCompletionSource();
        var store = new InMemoryClientKeyStore();
        var key = store.Seed("ecommerce", Generate, new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero));
        var blocking = Substitute.For<IClientKeyStore>();
        blocking.FindKeyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => store.FindKeyAsync(call.Arg<string>(), call.Arg<CancellationToken>()));
        blocking.RecordLastUsedAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(gate.Task);
        await using var slowWrites = await StudHost.StartAsync(customStore: blocking);

        var call = slowWrites.Client(key).GetStringAsync("/generate", Ct);

        Assert.Same(call, await Task.WhenAny(call, Task.Delay(TimeSpan.FromSeconds(5), Ct)));
        Assert.Equal("generated", await call);
        gate.SetResult();
    }

    [Fact]
    public async Task The_key_list_shows_the_last_use_and_null_for_an_unused_key()
    {
        await using var host = await StudHost.StartAsync();
        await host.RegisterAsync("ecommerce", Ct);
        var used = await host.IssueAsync("ecommerce", Generate, Ct);
        var unused = await host.IssueAsync("ecommerce", Generate, Ct);

        await host.Client(used.GetProperty("key").GetString()).GetStringAsync("/generate", Ct);
        var flusher = host.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<KeyUsageFlushService>().Single();
        await flusher.FlushAsync(Ct);

        var keys = (await host.Admin().GetFromJsonAsync<JsonElement>("/api/admin/clients/ecommerce/keys", Ct))
            .GetProperty("items").EnumerateArray().ToDictionary(k => k.GetProperty("keyId").GetString()!);

        Assert.Equal(new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero), keys[used.GetProperty("keyId").GetString()!].GetProperty("lastUsedAt").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, keys[unused.GetProperty("keyId").GetString()!].GetProperty("lastUsedAt").ValueKind);
    }
}
