using System;
using System.Collections.Generic;
using System.IO;

namespace VideoGridDesktop;

public enum SheetAspect
{
    Original,
    Ratio16By9,
    Ratio4By3,
    Square,
}

public sealed class VideoEntry
{
    public string Id { get; } = Guid.NewGuid().ToString("N");

    public required string FilePath { get; init; }

    public required string OriginalName { get; init; }

    public required string DirectoryPath { get; init; }

    public required DateTime LastModified { get; init; }

    public int Order { get; set; }

    public double DurationSeconds { get; set; }

    public int VideoWidth { get; set; }

    public int VideoHeight { get; set; }

    public int VideoStreamIndex { get; set; }

    public double FrameRate { get; set; }

    public bool ProbeSucceeded { get; set; }

    public bool ProbeFailed { get; set; }

    public bool Exported { get; set; }

    public bool ExportFailed { get; set; }

    public bool ManualCapture { get; set; }

    public List<double> ManualTimes { get; } = new();

    public string TargetName { get; set; } = "";

    public string StatusText { get; set; } = "等待读取";

    public string OriginalBase =>
        Path.GetFileNameWithoutExtension(OriginalName);

    public string DurationText =>
        DurationSeconds > 0
            ? TokenComposer.FormatTimestamp(DurationSeconds, "HH:mm:ss")
            : "";

    public string FrameSourceText => ManualCapture
        ? $"手动 {ManualTimes.Count} 帧"
        : "自动等分";

    public string ResolutionText =>
        VideoWidth > 0 && VideoHeight > 0
            ? $"{VideoWidth} x {VideoHeight}"
            : "";
}
