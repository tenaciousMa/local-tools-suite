using System;
using System.Collections.Generic;

namespace VideoGridDesktop;

public sealed record SheetSettings(
    SheetAspect Aspect,
    int FrameCount,
    int Columns,
    bool BigTitleEnabled,
    string BigTitleTemplate,
    double BigTitleFontSize,
    bool CaptionEnabled,
    string CaptionTemplate,
    string TimeFormat,
    double CaptionFontSize,
    int TotalFramesPerVideo,
    OutputSizeKind OutputSize);

public sealed record RenderOptions(
    string OutputPath,
    SheetSettings Settings,
    IReadOnlyDictionary<string, string> PageTokens);

public sealed record RenderResult(string OutputPath, int Width, int Height);

public sealed record FramePick(VideoEntry Video, double Seconds, int Sequence);

public enum OutputSizeKind
{
    Auto,
    P1080,
    P2K,
    P4K,
    P8K,
    P16K,
    A4Portrait,
    A4Landscape,
    A3Portrait,
    A3Landscape,
}

public enum ExportMode
{
    Separate,
    Merged,
    Both,
}

public sealed class NameRule
{
    public string Formula { get; init; } = "{date}_{num}";

    public string DateFormat { get; init; } = "yyyy-MM-dd_HH-mm-ss";

    public bool UseModifiedTime { get; init; } = true;

    public int StartNumber { get; init; } = 1;

    public int PadDigits { get; init; } = 2;

    public bool StripMarkers { get; init; }

    public bool LowercaseName { get; init; }

    public bool AutoSuffix { get; init; } = true;

    public string OutputExtension { get; init; } = "png";
}
