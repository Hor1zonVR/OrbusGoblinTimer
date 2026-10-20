using System;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace OrbusGoblinTimer;

/// <summary>Pure clock math and conservative announcement matching; no Unity/BepInEx dependencies.</summary>
public static class SpawnClock
{
    private static readonly Regex TagRegex = new("<[^>]*>", RegexOptions.Compiled);
    private static readonly Regex SpaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex SpawnRegex = new(
        @"\btrickster\s+goblin\b.{0,110}\b(?:(?:has\s+(?:been\s+)?)?spawned|(?:has\s+)?appeared)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsSpawnAnnouncement(string? html)
    {
        if (string.IsNullOrWhiteSpace(html) || html.Length > 8192) return false;
        if (html.IndexOf("trickster", StringComparison.OrdinalIgnoreCase) < 0 ||
            html.IndexOf("goblin", StringComparison.OrdinalIgnoreCase) < 0) return false;

        string plain = SpaceRegex.Replace(WebUtility.HtmlDecode(TagRegex.Replace(html, " ")), " ");
        if (plain.IndexOf("will spawn", StringComparison.OrdinalIgnoreCase) >= 0 ||
            plain.IndexOf("until the", StringComparison.OrdinalIgnoreCase) >= 0)
            return false;
        return SpawnRegex.IsMatch(plain);
    }

    public static bool TryParseUtc(string? value, out DateTimeOffset utc)
    {
        utc = default;
        return !string.IsNullOrWhiteSpace(value) &&
               DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                   DateTimeStyles.AssumeUniversal, out utc) &&
               utc.Year >= 2020 && utc <= DateTimeOffset.UtcNow.AddMinutes(2);
    }

    public static DateTimeOffset NextSpawn(DateTimeOffset lastSpawn, DateTimeOffset now, int intervalHours)
    {
        if (intervalHours < 1 || intervalHours > 24)
            throw new ArgumentOutOfRangeException(nameof(intervalHours));
        long period = TimeSpan.FromHours(intervalHours).Ticks;
        long elapsed = now.UtcDateTime.Ticks - lastSpawn.UtcDateTime.Ticks;
        if (elapsed < 0) return lastSpawn;
        long nextIndex = elapsed / period + 1;
        return lastSpawn.AddTicks(nextIndex * period);
    }

    public static string FormatRemaining(TimeSpan remaining)
    {
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        int hours = (int)remaining.TotalHours;
        return hours.ToString("00", CultureInfo.InvariantCulture) + ":" +
               remaining.Minutes.ToString("00", CultureInfo.InvariantCulture) + ":" +
               remaining.Seconds.ToString("00", CultureInfo.InvariantCulture);
    }
}
