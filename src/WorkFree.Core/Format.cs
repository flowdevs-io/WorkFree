using System.Globalization;
using System.Text.RegularExpressions;

namespace WorkFree.Core;

public static partial class Format
{
    public static string Size(long? bytes)
    {
        if (bytes is null) return "-";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes.Value;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0
            ? $"{bytes} B"
            : string.Create(CultureInfo.InvariantCulture, $"{value:0.0} {units[unit]}");
    }

    public static string Age(DateTimeOffset? when, DateTimeOffset now)
    {
        if (when is null) return "-";
        var span = now - when.Value;
        if (span < TimeSpan.FromMinutes(1)) return "just now";
        if (span < TimeSpan.FromHours(1)) return $"{(int)span.TotalMinutes}m ago";
        if (span < TimeSpan.FromDays(1)) return $"{(int)span.TotalHours}h ago";
        if (span < TimeSpan.FromDays(60)) return $"{(int)span.TotalDays}d ago";
        if (span < TimeSpan.FromDays(730)) return $"{(int)(span.TotalDays / 30)}mo ago";
        return $"{(int)(span.TotalDays / 365)}y ago";
    }

    /// <summary>Parses durations such as 12h, 30d, 2w, 3mo or 1y.</summary>
    public static bool TryParseDuration(string text, out TimeSpan duration)
    {
        duration = default;
        var match = DurationPattern().Match(text.Trim());
        if (!match.Success) return false;
        if (!int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n)) return false;
        duration = match.Groups[2].Value.ToLowerInvariant() switch
        {
            "h" => TimeSpan.FromHours(n),
            "d" => TimeSpan.FromDays(n),
            "w" => TimeSpan.FromDays(7 * n),
            "mo" => TimeSpan.FromDays(30 * n),
            _ => TimeSpan.FromDays(365 * n),
        };
        return true;
    }

    [GeneratedRegex(@"^(\d+)\s*(h|d|w|mo|y)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DurationPattern();
}
