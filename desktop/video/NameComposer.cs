using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace VideoGridDesktop;

public static class NameComposer
{
    public static string BuildTargetName(VideoEntry entry, NameRule rule, int index)
    {
        return BuildPageTargetName(entry, rule, index, 1, 1);
    }

    public static string BuildPageTargetName(
        VideoEntry entry,
        NameRule rule,
        int outputIndex,
        int page,
        int pageTotal)
    {
        string cleanBase = CleanOriginalName(entry, rule);
        DateTime date = rule.UseModifiedTime ? entry.LastModified : DateTime.Now;
        Dictionary<string, string> tokens = CreatePageTokens(entry, cleanBase, date, rule, outputIndex);
        tokens["page"] = page.ToString();
        tokens["pageTotal"] = pageTotal.ToString();
        string composed = TokenComposer.Replace(rule.Formula, tokens);
        string sanitized = TokenComposer.SanitizeBase(composed);
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = cleanBase;
        }

        if (pageTotal > 1 &&
            !rule.Formula.Contains("{page}", StringComparison.Ordinal))
        {
            sanitized = $"{sanitized}_p{page:D2}";
        }

        string extension = rule.OutputExtension.StartsWith(".", StringComparison.Ordinal)
            ? rule.OutputExtension
            : "." + rule.OutputExtension;
        return sanitized + extension;
    }

    public static string BuildMergedTargetName(
        NameRule rule,
        int outputIndex,
        int page,
        int pageTotal)
    {
        DateTime date = DateTime.Now;
        int sequence = rule.StartNumber + outputIndex;
        string paddedNumber = sequence.ToString().PadLeft(rule.PadDigits, '0');
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["date"] = date.ToString(rule.DateFormat),
            ["time"] = date.ToString("HH-mm-ss"),
            ["num"] = paddedNumber,
            ["name"] = "合并",
            ["page"] = page.ToString(),
            ["pageTotal"] = pageTotal.ToString(),
            ["YYYY"] = date.Year.ToString("D4"),
            ["M"] = date.Month.ToString(),
            ["MM"] = date.Month.ToString("D2"),
            ["D"] = date.Day.ToString(),
            ["DD"] = date.Day.ToString("D2"),
            ["H"] = date.Hour.ToString(),
            ["HH"] = date.Hour.ToString("D2"),
            ["m"] = date.Minute.ToString(),
            ["mm"] = date.Minute.ToString("D2"),
            ["s"] = date.Second.ToString(),
            ["ss"] = date.Second.ToString("D2"),
        };
        string composed = TokenComposer.Replace(rule.Formula, tokens);
        string sanitized = TokenComposer.SanitizeBase(composed);
        if (sanitized.Length == 0)
        {
            sanitized = "合并";
        }

        if (pageTotal > 1 &&
            !rule.Formula.Contains("{page}", StringComparison.Ordinal))
        {
            sanitized = $"{sanitized}_p{page:D2}";
        }

        string extension = rule.OutputExtension.StartsWith(".", StringComparison.Ordinal)
            ? rule.OutputExtension
            : "." + rule.OutputExtension;
        return sanitized + extension;
    }

    public static Dictionary<string, string> CreatePageTokens(
        VideoEntry entry,
        NameRule rule,
        int index,
        int page = 1,
        int pageTotal = 1)
    {
        string cleanBase = CleanOriginalName(entry, rule);
        DateTime date = rule.UseModifiedTime ? entry.LastModified : DateTime.Now;
        Dictionary<string, string> tokens = CreatePageTokens(entry, cleanBase, date, rule, index);
        tokens["page"] = page.ToString();
        tokens["pageTotal"] = pageTotal.ToString();
        return tokens;
    }

    public static Dictionary<string, string> CreateMergedPageTokens(
        NameRule rule,
        int outputIndex,
        int page,
        int pageTotal)
    {
        DateTime date = DateTime.Now;
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["date"] = date.ToString(rule.DateFormat),
            ["time"] = date.ToString("HH-mm-ss"),
            ["num"] = (rule.StartNumber + outputIndex).ToString()
                .PadLeft(rule.PadDigits, '0'),
            ["name"] = "合并",
            ["page"] = page.ToString(),
            ["pageTotal"] = pageTotal.ToString(),
            ["duration"] = "",
            ["width"] = "",
            ["height"] = "",
        };
        return tokens;
    }

    private static Dictionary<string, string> CreatePageTokens(
        VideoEntry entry,
        string cleanBase,
        DateTime date,
        NameRule rule,
        int index)
    {
        int sequence = rule.StartNumber + index;
        string paddedNumber = sequence.ToString().PadLeft(rule.PadDigits, '0');
        string formattedDate = date.ToString(rule.DateFormat);

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["date"] = formattedDate,
            ["time"] = date.ToString("HH-mm-ss"),
            ["num"] = paddedNumber,
            ["name"] = cleanBase,
            ["duration"] = entry.DurationSeconds > 0
                ? TokenComposer.FormatTimestamp(entry.DurationSeconds, "HH:mm:ss")
                : "",
            ["width"] = entry.VideoWidth > 0 ? entry.VideoWidth.ToString() : "",
            ["height"] = entry.VideoHeight > 0 ? entry.VideoHeight.ToString() : "",
            ["YYYY"] = date.Year.ToString("D4"),
            ["M"] = date.Month.ToString(),
            ["MM"] = date.Month.ToString("D2"),
            ["D"] = date.Day.ToString(),
            ["DD"] = date.Day.ToString("D2"),
            ["H"] = date.Hour.ToString(),
            ["HH"] = date.Hour.ToString("D2"),
            ["m"] = date.Minute.ToString(),
            ["mm"] = date.Minute.ToString("D2"),
            ["s"] = date.Second.ToString(),
            ["ss"] = date.Second.ToString("D2"),
        };
    }

    public static string CleanOriginalName(VideoEntry entry, NameRule rule)
    {
        string value = entry.OriginalBase;
        if (rule.StripMarkers)
        {
            value = Regex.Replace(value, @"\s*\(\d+\)\s*$", "");
            value = Regex.Replace(value, @"\s*\[\d+\]\s*$", "");
            value = Regex.Replace(value, @"\s*-\s*副本\s*$", "");
            value = Regex.Replace(value, @"\s*副本\s*$", "");
            value = Regex.Replace(value, @"\s*\(副本\)\s*$", "");
        }

        if (rule.LowercaseName)
        {
            value = value.ToLowerInvariant();
        }

        return value.Length == 0 ? "video" : value;
    }
}
