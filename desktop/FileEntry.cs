using System;
using System.IO;

namespace RenamerDesktop;

public sealed class FileEntry
{
    public string Id { get; } = Guid.NewGuid().ToString("N");

    public required string CurrentPath { get; set; }

    public required string OriginalName { get; init; }

    public required string DirectoryPath { get; init; }

    public required DateTime LastModified { get; init; }

    public int Order { get; set; }

    public bool Renamed { get; set; }

    public string CurrentName => Path.GetFileName(CurrentPath);

    public string OriginalBase =>
        Path.GetFileNameWithoutExtension(OriginalName);

    public string Extension =>
        Path.GetExtension(OriginalName);

    public string TargetName { get; set; } = "";

    public string StatusText { get; set; } = "";

    public string TimeText => LastModified.ToString("yyyy-MM-dd HH:mm");
}
