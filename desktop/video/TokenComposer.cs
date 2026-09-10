using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace VideoGridDesktop;

public static class TokenComposer
{
    private const string EscapedOpen = "\uE000";
    private const string EscapedClose = "\uE001";

    public static string Replace(string template, IReadOnlyDictionary<string, string> tokens)
    {
        string protectedText = template
            .Replace("{{", EscapedOpen, StringComparison.Ordinal)
            .Replace("}}", EscapedClose, StringComparison.Ordinal);

        var output = new StringBuilder(protectedText.Length);
        int index = 0;
        while (index < protectedText.Length)
        {
            char current = protectedText[index];
            if (current == '{')
            {
                int end = protectedText.IndexOf('}', index + 1);
                if (end >= 0)
                {
                    string key = protectedText.Substring(index + 1, end - index - 1);
                    output.Append(tokens.TryGetValue(key, out string? value) ? value : $"{{{key}}}");
                    index = end + 1;
                    continue;
                }
            }

            output.Append(current);
            index++;
        }

        return output
            .ToString()
            .Replace(EscapedOpen, "{", StringComparison.Ordinal)
            .Replace(EscapedClose, "}", StringComparison.Ordinal);
    }

    public static string FormatTimestamp(double seconds, string format)
    {
        if (seconds < 0)
        {
            seconds = 0;
        }

        int hours = (int)Math.Floor(seconds / 3600d);
        int minutes = (int)Math.Floor(seconds / 60d);
        int wholeSeconds = (int)Math.Floor(seconds) % 60;
        int milliseconds = (int)Math.Round((seconds - Math.Floor(seconds)) * 1000d) % 1000;
        if (milliseconds > 999)
        {
            milliseconds = 0;
            wholeSeconds++;
            if (wholeSeconds == 60)
            {
                wholeSeconds = 0;
                minutes++;
            }
        }

        string ss = wholeSeconds.ToString("D2", CultureInfo.InvariantCulture);
        string mm = minutes.ToString("D2", CultureInfo.InvariantCulture);
        string hh = hours.ToString("D2", CultureInfo.InvariantCulture);
        string ms = milliseconds.ToString("D3", CultureInfo.InvariantCulture);

        return format switch
        {
            "HH:mm:ss" => $"{hh}:{mm}:{ss}",
            "mm:ss.mmm" => $"{mm}:{ss}.{ms}",
            "HH:mm:ss.mmm" => $"{hh}:{mm}:{ss}.{ms}",
            "mm分ss秒" => $"{minutes}分{ss}秒",
            "HH时mm分ss秒" => $"{hours}时{minutes % 60:D2}分{ss}秒",
            _ => $"{mm}:{ss}",
        };
    }

    public static string SanitizeBase(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (char character in value)
        {
            builder.Append(Array.IndexOf(invalid, character) >= 0 ? '-' : character);
        }

        string result = builder.ToString().Trim().Trim('.', ' ');
        if (result.Length == 0)
        {
            return "video";
        }

        string upper = result.ToUpperInvariant();
        if (upper is "CON" or "PRN" or "AUX" or "NUL" ||
            upper.StartsWith("COM", StringComparison.Ordinal) ||
            upper.StartsWith("LPT", StringComparison.Ordinal))
        {
            return "_" + result;
        }

        return result;
    }
}
