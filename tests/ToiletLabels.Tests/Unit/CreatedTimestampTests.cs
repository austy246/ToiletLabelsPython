using ToiletLabels.Labels;

namespace ToiletLabels.Tests.Unit;

public class CreatedTimestampTests
{
    [Fact]
    public void Format_UsesSixFractionDigitsWithoutZone()
    {
        var utc = new DateTime(2026, 6, 3, 12, 34, 56, DateTimeKind.Utc).AddTicks(1234560);

        Assert.Equal("2026-06-03T12:34:56.123456", CreatedTimestamp.Format(utc));
    }

    [Fact]
    public void Format_IsCultureInvariant()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("cs-CZ");
        try
        {
            var utc = new DateTime(2026, 6, 3, 12, 34, 56, DateTimeKind.Utc);

            Assert.Equal("2026-06-03T12:34:56.000000", CreatedTimestamp.Format(utc));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Now_UsesTimeProviderInUtc()
    {
        var time = new FixedTimeProvider(new DateTimeOffset(2026, 6, 3, 14, 0, 0, TimeSpan.FromHours(2)));

        Assert.Equal("2026-06-03T12:00:00.000000", CreatedTimestamp.Now(time));
    }
}

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
}
