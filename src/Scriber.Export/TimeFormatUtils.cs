using System.Globalization;

namespace Scriber.Export;

internal static class TimeFormatUtils
{
    public static string FormatSrt(TimeSpan ts)
    {
        int hours = (int)ts.TotalHours;
        int minutes = ts.Minutes;
        int seconds = ts.Seconds;
        int millis = ts.Milliseconds;
        return string.Create(CultureInfo.InvariantCulture, $"{hours:D2}:{minutes:D2}:{seconds:D2},{millis:D3}");
    }

    public static string FormatVtt(TimeSpan ts)
    {
        int hours = (int)ts.TotalHours;
        int minutes = ts.Minutes;
        int seconds = ts.Seconds;
        int millis = ts.Milliseconds;
        return string.Create(CultureInfo.InvariantCulture, $"{hours:D2}:{minutes:D2}:{seconds:D2}.{millis:D3}");
    }

    public static string FormatHuman(TimeSpan ts)
    {
        int hours = (int)ts.TotalHours;
        int minutes = ts.Minutes;
        int seconds = ts.Seconds;
        if (hours > 0)
            return string.Create(CultureInfo.InvariantCulture, $"{hours:D2}:{minutes:D2}:{seconds:D2}");
        return string.Create(CultureInfo.InvariantCulture, $"{minutes:D2}:{seconds:D2}");
    }

    public static TimeSpan ParseSrtTime(string s)
    {
        // 00:00:01,000
        var parts = s.Trim().Split(',', ':');
        if (parts.Length != 4)
            throw new FormatException($"Invalid SRT time format: '{s}'");

        int h = int.Parse(parts[0], CultureInfo.InvariantCulture);
        int m = int.Parse(parts[1], CultureInfo.InvariantCulture);
        int sec = int.Parse(parts[2], CultureInfo.InvariantCulture);
        int ms = int.Parse(parts[3], CultureInfo.InvariantCulture);

        return new TimeSpan(0, h, m, sec, ms);
    }

    public static TimeSpan ParseVttTime(string s)
    {
        // 00:00:01.000 or 00:01.000
        var dotSplit = s.Trim().Split('.');
        if (dotSplit.Length != 2)
            throw new FormatException($"Invalid VTT time format: '{s}'");

        int ms = int.Parse(dotSplit[1].PadRight(3, '0')[..3], CultureInfo.InvariantCulture);
        var colonParts = dotSplit[0].Split(':');

        int h = 0;
        int m;
        int sec;

        if (colonParts.Length == 3)
        {
            h = int.Parse(colonParts[0], CultureInfo.InvariantCulture);
            m = int.Parse(colonParts[1], CultureInfo.InvariantCulture);
            sec = int.Parse(colonParts[2], CultureInfo.InvariantCulture);
        }
        else if (colonParts.Length == 2)
        {
            m = int.Parse(colonParts[0], CultureInfo.InvariantCulture);
            sec = int.Parse(colonParts[1], CultureInfo.InvariantCulture);
        }
        else
        {
            throw new FormatException($"Invalid VTT time format: '{s}'");
        }

        return new TimeSpan(0, h, m, sec, ms);
    }
}
