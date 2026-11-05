using System.Net;
using Blocks.ServiceDefaults.Sessions;
using Blocks.ServiceDefaults.Tests.Support;

namespace Blocks.ServiceDefaults.Tests;

/// <summary>FR-008: five failed sign-ins a minute per address, then refusal until the minute ends.</summary>
public sealed class SignInThrottleTests
{
    private static readonly IPAddress Guesser = IPAddress.Parse("203.0.113.7");
    private static readonly IPAddress Operator = IPAddress.Parse("198.51.100.20");

    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Five_failures_are_allowed_and_the_sixth_attempt_is_refused_until_the_window_ends()
    {
        var throttle = new SignInThrottle(_clock);

        for (var i = 0; i < SignInThrottle.MaxFailures; i++)
        {
            Assert.True(throttle.TryEnter(Guesser, out _));
            throttle.RecordFailure(Guesser);
            _clock.Advance(TimeSpan.FromSeconds(5));
        }

        Assert.False(throttle.TryEnter(Guesser, out var retryAfter));
        Assert.Equal(TimeSpan.FromSeconds(35), retryAfter);
    }

    [Fact]
    public void Another_address_is_not_affected()
    {
        var throttle = new SignInThrottle(_clock);
        for (var i = 0; i < SignInThrottle.MaxFailures; i++)
            throttle.RecordFailure(Guesser);

        Assert.True(throttle.TryEnter(Operator, out _));
    }

    [Fact]
    public void After_the_window_the_address_may_try_again()
    {
        var throttle = new SignInThrottle(_clock);
        for (var i = 0; i < SignInThrottle.MaxFailures; i++)
            throttle.RecordFailure(Guesser);

        _clock.Advance(SignInThrottle.Window);

        Assert.True(throttle.TryEnter(Guesser, out _));
        throttle.RecordFailure(Guesser);
        Assert.True(throttle.TryEnter(Guesser, out _));
    }

    [Fact]
    public void Successful_sign_ins_are_not_counted()
    {
        var throttle = new SignInThrottle(_clock);

        // An operator signing in again and again never records a failure, so is never refused.
        for (var i = 0; i < 20; i++)
            Assert.True(throttle.TryEnter(Operator, out _));
    }
}
