using System;
using System.Threading;

namespace EventManager.Tests.TestInfrastructure;

internal sealed class FakeTimeProvider(DateTimeOffset? fixedDate) : TimeProvider
{
    public DateTimeOffset? FixedDate { get; set; } = fixedDate;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        => throw new NotSupportedException("Do not use");

    public override long GetTimestamp()
        => FixedDate?.Ticks ?? throw new NotSupportedException("Disabled!");

    public override TimeZoneInfo LocalTimeZone
        => throw new NotSupportedException("Do not use");

    public override long TimestampFrequency
        => throw new NotSupportedException("Do not use");

    public override DateTimeOffset GetUtcNow()
        => FixedDate ?? throw new NotSupportedException("Disabled!");
}