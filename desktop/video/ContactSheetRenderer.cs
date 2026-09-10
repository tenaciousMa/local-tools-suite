using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace VideoGridDesktop;

public static class ContactSheetRenderer
{
    private const int OuterMargin = 38;
    private const int GridGap = 14;
    private const int TargetContentWidth = 1680;

    public static Task<RenderResult> RenderAsync(
        VideoEntry entry,
        RenderOptions options,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        return RenderPageAsync(
            BuildAutoPicks(entry, options.Settings),
            options,
            progress,
            cancellationToken);
    }

    public static List<FramePick> BuildAutoPicks(VideoEntry entry, SheetSettings settings)
    {
        int frameCount = Math.Max(1, settings.TotalFramesPerVideo > 0
            ? settings.TotalFramesPerVideo
            : settings.FrameCount);
        var picks = new List<FramePick>(frameCount);
        double duration = entry.DurationSeconds;
        if (duration <= 0)
        {
            throw new InvalidOperationException("视频时长尚未读取，无法自动取帧。");
        }

        for (int index = 0; index < frameCount; index++)
        {
            double seconds = duration * (index + 0.5d) / frameCount;
            picks.Add(new FramePick(entry, seconds, index + 1));
        }

        return picks;
    }

    public static List<FramePick> BuildManualPicks(VideoEntry entry)
    {
        var picks = new List<FramePick>();
        List<double> times = entry.ManualTimes
            .Where(value => value >= 0)
            .Distinct()
            .OrderBy(value => value)
            .ToList();
        for (int index = 0; index < times.Count; index++)
        {
            picks.Add(new FramePick(entry, times[index], index + 1));
        }

        return picks;
    }

    public static async Task<RenderResult> RenderPageAsync(
        IReadOnlyList<FramePick> picks,
        RenderOptions options,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (picks.Count == 0)
        {
            throw new InvalidOperationException("这一页没有可选截图。");
        }

        if (!FfmpegRunner.IsAvailable)
        {
            throw new InvalidOperationException("未找到 ffmpeg 文件夹，请保持应用目录完整。");
        }

        string tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "VideoGridDesktop",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var framePaths = picks
                .Select((_, index) => Path.Combine(tempDirectory, $"frame-{index:D3}.jpg"))
                .ToList();
            var groups = Enumerable
                .Range(0, picks.Count)
                .GroupBy(index => picks[index].Video);
            foreach (var group in groups)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report($"正在批量截取 {group.Count()} 帧");
                var requests = group
                    .Select(index => new FrameExtractionRequest(
                        picks[index].Seconds,
                        framePaths[index]))
                    .ToList();
                await FfmpegRunner.ExtractFramesAsync(
                    group.Key.FilePath,
                    group.Key.VideoStreamIndex,
                    group.Key.FrameRate,
                    requests,
                    cancellationToken).ConfigureAwait(false);
            }

            progress?.Report("正在合成拼图");
            return await Task.Run(
                () => Compose(picks, framePaths, options),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
            catch
            {
                // Temporary frames are best effort cleanup.
            }
        }
    }

    private static RenderResult Compose(
        IReadOnlyList<FramePick> picks,
        IReadOnlyList<string> framePaths,
        RenderOptions options)
    {
        SheetSettings settings = options.Settings;
        Bitmap? first = null;
        try
        {
            first = new Bitmap(framePaths[0]);
            GetCellSize(
                first.Width,
                first.Height,
                settings.Aspect,
                settings.Columns,
                out int cellWidth,
                out int cellHeight);

            int columns = Math.Max(1, settings.Columns);
            int rows = (int)Math.Ceiling(picks.Count / (double)columns);
            bool showCaption = settings.CaptionEnabled &&
                !string.IsNullOrWhiteSpace(settings.CaptionTemplate);
            int captionHeight = showCaption
                ? (int)Math.Max(24, settings.CaptionFontSize + 12)
                : 0;

            int baseWidth = OuterMargin * 2 +
                columns * cellWidth +
                GridGap * Math.Max(0, columns - 1);

            double topOffset = OuterMargin;
            double titleHeight = 0;
            if (settings.BigTitleEnabled &&
                !string.IsNullOrWhiteSpace(settings.BigTitleTemplate))
            {
                titleHeight = Math.Max(52, settings.BigTitleFontSize + 22);
                topOffset += titleHeight;
            }

            int gridHeight = (int)Math.Ceiling(
                rows * (cellHeight + captionHeight) +
                (double)GridGap * Math.Max(0, rows - 1));
            int baseHeight = (int)Math.Ceiling(topOffset + gridHeight + OuterMargin);

            bool fixedSize = TryGetPixelSize(settings.OutputSize, out int fixedWidth, out int fixedHeight);
            double scale = fixedSize
                ? Math.Min(fixedWidth / (double)baseWidth, fixedHeight / (double)baseHeight)
                : 1d;
            if (scale <= 0 || double.IsNaN(scale) || double.IsInfinity(scale))
            {
                scale = 1d;
            }

            int width = fixedSize ? fixedWidth : baseWidth;
            int height = fixedSize ? fixedHeight : baseHeight;

            using var output = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using var graphics = Graphics.FromImage(output);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            graphics.Clear(Color.White);

            double offsetX = (width - baseWidth * scale) / 2d;
            double offsetY = (height - baseHeight * scale) / 2d;
            graphics.TranslateTransform((float)offsetX, (float)offsetY);
            graphics.ScaleTransform((float)scale, (float)scale);

            using var panelBrush = new SolidBrush(Color.FromArgb(248, 250, 253));
            using var textBrush = new SolidBrush(Color.FromArgb(23, 32, 43));
            using var mutedBrush = new SolidBrush(Color.FromArgb(90, 110, 130));
            using var borderPen = new Pen(Color.FromArgb(216, 224, 233));

            if (titleHeight > 0)
            {
                string titleText = TokenComposer.Replace(
                    settings.BigTitleTemplate,
                    options.PageTokens);
                using Font titleFont = MakeFont((float)settings.BigTitleFontSize, FontStyle.Bold);
                using var titleFormat = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center,
                    Trimming = StringTrimming.EllipsisCharacter,
                    FormatFlags = StringFormatFlags.LineLimit,
                };
                var titleBounds = new RectangleF(
                    OuterMargin,
                    (float)OuterMargin,
                    baseWidth - OuterMargin * 2,
                    (float)titleHeight);
                graphics.DrawString(titleText, titleFont, textBrush, titleBounds, titleFormat);
            }

            for (int index = 0; index < picks.Count; index++)
            {
                int row = index / columns;
                int column = index % columns;
                int x = OuterMargin + column * (cellWidth + GridGap);
                int y = (int)Math.Ceiling(
                    topOffset + row * (cellHeight + captionHeight + GridGap));
                Bitmap frame = index == 0 ? first : new Bitmap(framePaths[index]);
                try
                {
                    graphics.FillRectangle(panelBrush, x, y, cellWidth, cellHeight);
                    Rectangle destination = FitRectangle(
                        frame.Width,
                        frame.Height,
                        cellWidth,
                        cellHeight,
                        x,
                        y);
                    graphics.DrawImage(
                        frame,
                        destination,
                        new Rectangle(0, 0, frame.Width, frame.Height),
                        GraphicsUnit.Pixel);
                    graphics.DrawRectangle(borderPen, x, y, cellWidth - 1, cellHeight - 1);
                }
                finally
                {
                    if (index > 0)
                    {
                        frame.Dispose();
                    }
                }

                if (showCaption)
                {
                    FramePick pick = picks[index];
                    string timestamp = TokenComposer.FormatTimestamp(
                        pick.Seconds,
                        settings.TimeFormat);
                    var captionTokens = new Dictionary<string, string>(options.PageTokens)
                    {
                        ["time"] = timestamp,
                        ["num"] = (index + 1).ToString(CultureInfo.InvariantCulture),
                        ["seq"] = pick.Sequence.ToString(CultureInfo.InvariantCulture),
                        ["video"] = pick.Video.OriginalBase,
                        ["name"] = pick.Video.OriginalBase,
                    };
                    string captionText = TokenComposer.Replace(
                        settings.CaptionTemplate,
                        captionTokens);
                    using Font captionFont = MakeFont((float)settings.CaptionFontSize);
                    using var captionFormat = new StringFormat
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center,
                        Trimming = StringTrimming.EllipsisCharacter,
                        FormatFlags = StringFormatFlags.LineLimit,
                    };
                    var captionBounds = new RectangleF(
                        x,
                        y + cellHeight + 2,
                        cellWidth,
                        captionHeight);
                    graphics.DrawString(
                        captionText,
                        captionFont,
                        mutedBrush,
                        captionBounds,
                        captionFormat);
                }
            }

            SaveImage(output, options.OutputPath);
            return new RenderResult(options.OutputPath, width, height);
        }
        finally
        {
            first?.Dispose();
        }
    }

    private static void SaveImage(Bitmap bitmap, string outputPath)
    {
        string? directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = new FileStream(
            outputPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None);

        if (Path.GetExtension(outputPath).Equals(".jpg", StringComparison.OrdinalIgnoreCase))
        {
            ImageCodecInfo? codec = ImageCodecInfo.GetImageEncoders()
                .FirstOrDefault(encoder =>
                    encoder.FormatID == ImageFormat.Jpeg.Guid);
            using var parameters = new EncoderParameters(1);
            parameters.Param[0] = new EncoderParameter(Encoder.Quality, 93L);
            if (codec is not null)
            {
                bitmap.Save(stream, codec, parameters);
                return;
            }
        }

        bitmap.Save(stream, ImageFormat.Png);
    }

    private static Font MakeFont(float size, FontStyle style = FontStyle.Regular)
    {
        try
        {
            return new Font("Microsoft YaHei UI", size, style, GraphicsUnit.Pixel);
        }
        catch
        {
            return new Font(FontFamily.GenericSansSerif, size, style, GraphicsUnit.Pixel);
        }
    }

    private static Rectangle FitRectangle(
        int sourceWidth,
        int sourceHeight,
        int targetWidth,
        int targetHeight,
        int left,
        int top)
    {
        double scale = Math.Min(
            targetWidth / (double)sourceWidth,
            targetHeight / (double)sourceHeight);
        int width = Math.Max(1, (int)Math.Round(sourceWidth * scale));
        int height = Math.Max(1, (int)Math.Round(sourceHeight * scale));
        return new Rectangle(
            left + (targetWidth - width) / 2,
            top + (targetHeight - height) / 2,
            width,
            height);
    }

    public static bool TryGetPixelSize(
        OutputSizeKind size,
        out int width,
        out int height)
    {
        (int Width, int Height)? value = size switch
        {
            OutputSizeKind.P1080 => (1920, 1080),
            OutputSizeKind.P2K => (2560, 1440),
            OutputSizeKind.P4K => (3840, 2160),
            OutputSizeKind.P8K => (7680, 4320),
            OutputSizeKind.P16K => (15360, 8640),
            OutputSizeKind.A4Portrait => (2480, 3508),
            OutputSizeKind.A4Landscape => (3508, 2480),
            OutputSizeKind.A3Portrait => (3508, 4961),
            OutputSizeKind.A3Landscape => (4961, 3508),
            _ => null,
        };

        if (value is null)
        {
            width = 0;
            height = 0;
            return false;
        }

        width = value.Value.Width;
        height = value.Value.Height;
        return true;
    }

    private static void GetCellSize(
        int sourceWidth,
        int sourceHeight,
        SheetAspect aspect,
        int columns,
        out int cellWidth,
        out int cellHeight)
    {
        double maxCellWidth = Math.Max(
            240,
            Math.Min(
                720,
                Math.Floor(
                    (double)(TargetContentWidth - GridGap * Math.Max(0, columns - 1)) /
                    Math.Max(1, columns))));

        double targetRatio = aspect switch
        {
            SheetAspect.Ratio16By9 => 16d / 9d,
            SheetAspect.Ratio4By3 => 4d / 3d,
            SheetAspect.Square => 1d,
            _ => sourceWidth > 0 && sourceHeight > 0
                ? sourceWidth / (double)sourceHeight
                : 16d / 9d,
        };

        if (aspect == SheetAspect.Original && targetRatio < 1)
        {
            cellHeight = (int)Math.Min(560, maxCellWidth / Math.Sqrt(targetRatio));
            cellHeight = Math.Max(180, cellHeight);
            cellWidth = Math.Max(120, (int)Math.Round(cellHeight * targetRatio));
            return;
        }

        cellWidth = Math.Max(220, (int)Math.Floor(maxCellWidth));
        cellHeight = Math.Max(120, (int)Math.Round(cellWidth / targetRatio));
    }
}
