using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace VideoGridDesktop;

public partial class App : Application
{
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VideoGridDesktop");

    private static readonly string LogPath = Path.Combine(LogDirectory, "startup-error.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        _ = VlcRuntime.GetLibVlcAsync();

        try
        {
            if (e.Args.Length >= 3 &&
                string.Equals(e.Args[0], "--render", StringComparison.OrdinalIgnoreCase))
            {
                int exitCode = RunCommandLineRender(e.Args);
                Shutdown(exitCode);
                return;
            }

            if (e.Args.Length >= 4 &&
                string.Equals(e.Args[0], "--pdf", StringComparison.OrdinalIgnoreCase))
            {
                int exitCode = RunCommandLinePdf(e.Args);
                Shutdown(exitCode);
                return;
            }

            if (e.Args.Length >= 2 &&
                string.Equals(e.Args[0], "--player-smoke", StringComparison.OrdinalIgnoreCase))
            {
                RunPlayerSmoke(e.Args);
                return;
            }

            if (e.Args.Length >= 2 &&
                string.Equals(e.Args[0], "--screenshot-main", StringComparison.OrdinalIgnoreCase))
            {
                RunMainWindowScreenshot(e.Args[1]);
                return;
            }

            var window = new MainWindow();
            window.Show();
        }
        catch (Exception exception)
        {
            WriteLog(exception);
            MessageBox.Show(
                exception.Message,
                "视频截图拼图台启动失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void RunMainWindowScreenshot(string outputPath)
    {
        try
        {
            var window = new MainWindow
            {
                Width = 1360,
                Height = 860,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10000,
                Top = -10000,
                ShowInTaskbar = false,
            };
            window.ContentRendered += (_, _) => Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    window.UpdateLayout();
                    var content = (Visual)window.Content;
                    int width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
                    int height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
                    var bitmap = new RenderTargetBitmap(
                        width,
                        height,
                        96,
                        96,
                        PixelFormats.Pbgra32);
                    bitmap.Render(content);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    string fullPath = Path.GetFullPath(outputPath);
                    Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? ".");
                    using (var stream = File.Create(fullPath))
                    {
                        encoder.Save(stream);
                    }

                    window.Close();
                    Shutdown(0);
                }
                catch (Exception exception)
                {
                    WriteLog(exception);
                    window.Close();
                    Shutdown(1);
                }
            }, DispatcherPriority.ContextIdle);
            window.Show();
        }
        catch (Exception exception)
        {
            WriteLog(exception);
            Shutdown(1);
        }
    }

    private void RunPlayerSmoke(string[] args)
    {
        string statusPath = args.Length > 2
            ? Path.GetFullPath(args[2])
            : Path.Combine(Path.GetTempPath(), "VideoGridDesktop-player-smoke.txt");
        try
        {
            string inputPath = Path.GetFullPath(args[1]);
            VideoProbeResult probe = FfmpegRunner.ProbeVideoAsync(
                inputPath,
                CancellationToken.None).GetAwaiter().GetResult();
            var entry = new VideoEntry
            {
                FilePath = inputPath,
                OriginalName = Path.GetFileName(inputPath),
                DirectoryPath = Path.GetDirectoryName(inputPath) ?? string.Empty,
                LastModified = File.GetLastWriteTime(inputPath),
                DurationSeconds = probe.DurationSeconds,
                VideoWidth = probe.Width,
                VideoHeight = probe.Height,
                VideoStreamIndex = probe.VideoStreamIndex,
                FrameRate = probe.FrameRate,
                ProbeSucceeded = true,
            };

            var player = new FramePlayerWindow(entry)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10000,
                Top = -10000,
                ShowInTaskbar = false,
                Opacity = 0,
                Width = 640,
                Height = 360,
            };
            var timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500),
            };
            var startedAt = DateTime.UtcNow;
            timer.Tick += (_, _) =>
            {
                bool active = player.IsPlaybackActive && player.CurrentTimeMs > 0;
                if (!active && DateTime.UtcNow - startedAt < TimeSpan.FromSeconds(25))
                {
                    return;
                }

                int capturedFrames = active ? player.AddCurrentFrameForSmoke() : 0;
                active = active && capturedFrames > 0;
                timer.Stop();
                File.WriteAllText(
                    statusPath,
                    $"active={active}; time={player.CurrentTimeMs}; length={player.MediaLengthMs}; frames={capturedFrames}");
                try
                {
                    player.Close();
                }
                catch
                {
                    // Smoke-test cleanup is best effort.
                }

                Shutdown(active ? 0 : 1);
            };
            player.Show();
            timer.Start();
        }
        catch (Exception exception)
        {
            File.WriteAllText(statusPath, exception.ToString());
            Shutdown(1);
        }
    }

    private static int RunCommandLinePdf(string[] args)
    {
        try
        {
            string inputPath = Path.GetFullPath(args[1]);
            string outputPath = Path.GetFullPath(args[2]);
            int total = int.TryParse(args[3], out int parsedTotal) ? parsedTotal : 9;
            int pageCapacity = args.Length > 4 && int.TryParse(args[4], out int parsedCapacity)
                ? parsedCapacity
                : 9;
            if (total < 1 || pageCapacity < 1)
            {
                return 1;
            }

            VideoProbeResult probe = FfmpegRunner.ProbeVideoAsync(
                inputPath,
                CancellationToken.None).GetAwaiter().GetResult();
            var entry = new VideoEntry
            {
                FilePath = inputPath,
                OriginalName = Path.GetFileName(inputPath),
                DirectoryPath = Path.GetDirectoryName(inputPath) ?? string.Empty,
                LastModified = File.GetLastWriteTime(inputPath),
                DurationSeconds = probe.DurationSeconds,
                VideoWidth = probe.Width,
                VideoHeight = probe.Height,
                VideoStreamIndex = probe.VideoStreamIndex,
                FrameRate = probe.FrameRate,
                ProbeSucceeded = true,
            };

            var settings = new SheetSettings(
                SheetAspect.Original,
                pageCapacity,
                pageCapacity <= 4 ? 2 : 3,
                true,
                "{name}",
                36,
                true,
                "{time}",
                "mm:ss",
                18,
                total,
                OutputSizeKind.Auto);
            var picks = new List<FramePick>();
            for (int index = 0; index < total; index++)
            {
                picks.Add(new FramePick(
                    entry,
                    entry.DurationSeconds * (index + 0.5d) / total,
                    index + 1));
            }

            string tempRoot = Path.Combine(
                Path.GetTempPath(),
                "VideoGridDesktop",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            var pages = new List<PdfPageImage>();
            try
            {
                for (int page = 0; page < picks.Count; page += pageCapacity)
                {
                    int count = Math.Min(pageCapacity, picks.Count - page);
                    List<FramePick> pagePicks = picks.GetRange(page, count);
                    string tempJpeg = Path.Combine(tempRoot, $"{page}.jpg");
                    var tokens = new Dictionary<string, string>
                    {
                        ["name"] = entry.OriginalBase,
                        ["date"] = "2026-09-10",
                        ["time"] = "00-00-00",
                        ["num"] = "1",
                        ["page"] = (page / pageCapacity + 1).ToString(),
                        ["pageTotal"] = ((picks.Count + pageCapacity - 1) / pageCapacity).ToString(),
                    };
                    RenderResult result = ContactSheetRenderer.RenderPageAsync(
                        pagePicks,
                        new RenderOptions(tempJpeg, settings, tokens),
                        null,
                        CancellationToken.None).GetAwaiter().GetResult();
                    pages.Add(new PdfPageImage(File.ReadAllBytes(tempJpeg), result.Width, result.Height));
                }

                PdfPageWriter.Write(outputPath, pages);
                File.WriteAllText(outputPath + ".txt", $"{pages.Count} pages");
                return 0;
            }
            finally
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
        catch (Exception exception)
        {
            WriteLog(exception);
            return 1;
        }
    }

    private static int RunCommandLineRender(string[] args)
    {
        try
        {
            string inputPath = Path.GetFullPath(args[1]);
            string outputPath = Path.GetFullPath(args[2]);
            int frameCount = args.Length > 3 && int.TryParse(args[3], out int parsed)
                ? parsed
                : 9;
            OutputSizeKind outputSize = args.Length > 4
                ? args[4].ToLowerInvariant() switch
                {
                    "4k" => OutputSizeKind.P4K,
                    "8k" => OutputSizeKind.P8K,
                    "16k" => OutputSizeKind.P16K,
                    "a4v" => OutputSizeKind.A4Portrait,
                    "a4h" => OutputSizeKind.A4Landscape,
                    "a3v" => OutputSizeKind.A3Portrait,
                    "a3h" => OutputSizeKind.A3Landscape,
                    _ => OutputSizeKind.P2K,
                }
                : OutputSizeKind.Auto;
            int columns = frameCount switch
            {
                2 => 2,
                4 => 2,
                6 => 3,
                8 => 4,
                9 => 3,
                12 => 4,
                16 => 4,
                20 => 5,
                24 => 6,
                _ => Math.Max(1, (int)Math.Ceiling(Math.Sqrt(frameCount))),
            };

            VideoProbeResult probe = FfmpegRunner.ProbeVideoAsync(
                inputPath,
                CancellationToken.None).GetAwaiter().GetResult();
            var entry = new VideoEntry
            {
                FilePath = inputPath,
                OriginalName = Path.GetFileName(inputPath),
                DirectoryPath = Path.GetDirectoryName(inputPath) ?? string.Empty,
                LastModified = File.GetLastWriteTime(inputPath),
                DurationSeconds = probe.DurationSeconds,
                VideoWidth = probe.Width,
                VideoHeight = probe.Height,
                VideoStreamIndex = probe.VideoStreamIndex,
                FrameRate = probe.FrameRate,
                ProbeSucceeded = true,
            };

            var tokens = new Dictionary<string, string>
            {
                ["name"] = entry.OriginalBase,
                ["date"] = "2026-09-10",
                ["num"] = "1",
                ["duration"] = entry.DurationText,
            };
            var settings = new SheetSettings(
                SheetAspect.Original,
                frameCount,
                columns,
                true,
                "{name}",
                44,
                true,
                "{time}",
                "mm:ss",
                22,
                frameCount,
                outputSize);
            RenderResult result = ContactSheetRenderer.RenderAsync(
                entry,
                new RenderOptions(outputPath, settings, tokens),
                null,
                CancellationToken.None).GetAwaiter().GetResult();

            File.WriteAllText(
                outputPath + ".txt",
                $"{result.Width}x{result.Height}");
            return 0;
        }
        catch (Exception exception)
        {
            WriteLog(exception);
            File.WriteAllText(
                Path.Combine(Path.GetTempPath(), "VideoGridDesktop-selftest-error.log"),
                exception.ToString());
            return 1;
        }
    }

    private static void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        WriteLog(e.Exception);
        MessageBox.Show(
            e.Exception.Message,
            "视频截图拼图台遇到错误",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void WriteLog(Exception exception)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            File.AppendAllText(
                LogPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Logging is best effort and must not hide the original failure.
        }
    }
}
