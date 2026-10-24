using System.Globalization;

namespace ToiletLabels.Labels;

/// <summary>Formát <c>Created</c> kompatibilní se starou Python aplikací: UTC, bez zóny, 6 desetinných míst.</summary>
public static class CreatedTimestamp
{
    private const string Pattern = "yyyy-MM-ddTHH:mm:ss.ffffff";

    public static string Format(DateTime utc)
    {
        if (utc.Kind == DateTimeKind.Local)
            utc = utc.ToUniversalTime();
        return utc.ToString(Pattern, CultureInfo.InvariantCulture);
    }

    public static string Now(TimeProvider time) => Format(time.GetUtcNow().UtcDateTime);
}
