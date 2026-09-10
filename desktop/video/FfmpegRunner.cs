using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VideoGridDesktop;

public sealed record VideoProbeResult(
    double DurationSeconds,
    int Width,
    int Height,
    int VideoStreamIndex,
    double FrameRate);

public sealed record FrameExtractionRequest(double Seconds, string OutputPath);

public static class FfmpegRunner
{
    private const double SequentialDecodeLimitSeconds = 60d;

    private static string? _toolDirectory;

    public static bool IsAvailable => FfmpegExePath is not null;

    public static string? FfmpegExePath => LocateTool("ffmpeg.exe");

    public static string? FfprobeExePath => LocateTool("ffprobe.exe");

    public static async Task<VideoProbeResult> ProbeVideoAsync(
        string videoPath,
        CancellationToken cancellationToken)
    {
        string probePath = FfprobeExePath
            ?? throw new InvalidOperationException("未找到内置 ffprobe.exe，无法读取视频信息。");

        var arguments = new List<string>
        {
            "-v",
            "error",
            "-show_entries",
            "format=duration:stream=index,codec_name,codec_type,width,height,duration,avg_frame_rate,r_frame_rate:stream_disposition=attached_pic,default",
            "-of",
            "json",
            videoPath,
        };

        string output = await RunToolAsync(
            probePath,
            arguments,
            cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(output);
        JsonElement root = document.RootElement;
        double duration = 0;
        int width = 0;
        int height = 0;
        int videoStreamIndex = 0;
        double frameRate = 0;

        if (root.TryGetProperty("format", out JsonElement format) &&
            format.TryGetProperty("duration", out JsonElement formatDuration))
        {
            TryGetDouble(formatDuration, out duration);
        }

        if (root.TryGetProperty("streams", out JsonElement streams) &&
            streams.ValueKind == JsonValueKind.Array)
        {
            int bestScore = int.MinValue;
            foreach (JsonElement stream in streams.EnumerateArray())
            {
                if (!stream.TryGetProperty("codec_type", out JsonElement type) ||
                    type.GetString() != "video")
                {
                    continue;
                }

                int streamIndex = stream.TryGetProperty("index", out JsonElement indexElement)
                    ? indexElement.GetInt32()
                    : 0;
                int streamWidth = stream.TryGetProperty("width", out JsonElement widthElement)
                    ? widthElement.GetInt32()
                    : 0;
                int streamHeight = stream.TryGetProperty("height", out JsonElement heightElement)
                    ? heightElement.GetInt32()
                    : 0;
                double streamDurationValue = 0;
                if (stream.TryGetProperty("duration", out JsonElement streamDuration))
                {
                    TryGetDouble(streamDuration, out streamDurationValue);
                }

                double streamFrameRate = ParseFrameRate(stream, "avg_frame_rate");
                if (streamFrameRate <= 0)
                {
                    streamFrameRate = ParseFrameRate(stream, "r_frame_rate");
                }

                if (streamWidth <= 0 || streamHeight <= 0)
                {
                    continue;
                }

                string codecName = stream.TryGetProperty("codec_name", out JsonElement codecElement)
                    ? codecElement.GetString() ?? ""
                    : "";
                bool imageCodec = codecName is "mjpeg" or "png" or "bmp" or "gif" or "webp" or "tiff";
                bool attachedPicture =
                    stream.TryGetProperty("disposition", out JsonElement disposition) &&
                    disposition.TryGetProperty("attached_pic", out JsonElement attached) &&
                    attached.GetInt32() == 1;
                bool isDefault =
                    stream.TryGetProperty("disposition", out JsonElement defaultDisposition) &&
                    defaultDisposition.TryGetProperty("default", out JsonElement defaultElement) &&
                    defaultElement.GetInt32() == 1;

                int score = 0;
                if (!attachedPicture) score += 1000;
                if (!imageCodec) score += 300;
                if (isDefault) score += 100;
                if (streamDurationValue > 0) score += 200;
                score += Math.Min(199, streamWidth * streamHeight / 100_000);

                if (score <= bestScore)
                {
                    continue;
                }

                bestScore = score;
                videoStreamIndex = streamIndex;
                width = streamWidth;
                height = streamHeight;
                frameRate = streamFrameRate;
                if (duration <= 0 && streamDurationValue > 0)
                {
                    duration = streamDurationValue;
                }
            }
        }

        if (duration <= 0 || width <= 0 || height <= 0)
        {
            throw new InvalidOperationException("视频没有可读取的画面流或时长信息。");
        }

        return new VideoProbeResult(duration, width, height, videoStreamIndex, frameRate);
    }

    private static double ParseFrameRate(JsonElement stream, string propertyName)
    {
        if (!stream.TryGetProperty(propertyName, out JsonElement element))
        {
            return 0;
        }

        string value = element.GetString() ?? "";
        string[] parts = value.Split('/');
        if (parts.Length == 2 &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double numerator) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double denominator) &&
            denominator > 0)
        {
            return numerator / denominator;
        }

        return double.TryParse(
            value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out double parsed)
            ? parsed
            : 0;
    }

    private static bool TryGetDouble(JsonElement element, out double value)
    {
        value = 0;
        if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out double number))
        {
            value = number;
            return true;
        }

        if (element.ValueKind == JsonValueKind.String &&
            double.TryParse(
                element.GetString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double parsed))
        {
            value = parsed;
            return true;
        }

        return false;
    }

    public static async Task ExtractFramesAsync(
        string videoPath,
        int videoStreamIndex,
        double frameRate,
        IReadOnlyList<FrameExtractionRequest> requests,
        CancellationToken cancellationToken)
    {
        if (requests.Count == 0)
        {
            return;
        }

        double furthestFrame = requests.Max(request => request.Seconds);
        bool canUseSequentialBatch =
            frameRate > 0 &&
            frameRate <= 240 &&
            furthestFrame <= SequentialDecodeLimitSeconds;
        if (!canUseSequentialBatch)
        {
            await ExtractFramesIndividuallyAsync(
                videoPath,
                videoStreamIndex,
                requests,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        string ffmpegPath = FfmpegExePath
            ?? throw new InvalidOperationException("未找到内置 ffmpeg.exe，无法批量截取视频画面。");
        string tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "VideoGridDesktop",
            $"batch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            List<long> frameNumbers = requests
                .Select(request => Math.Max(0, (long)Math.Round(request.Seconds * frameRate)))
                .ToList();
            string selection = string.Join(
                "+",
                frameNumbers.Select(number => $"eq(n\\,{number})"));
            string outputPattern = Path.Combine(tempDirectory, "frame-%04d.jpg");
            var arguments = new List<string>
            {
                "-hide_banner",
                "-loglevel",
                "error",
                "-y",
                "-i",
                videoPath,
                "-map",
                $"0:{videoStreamIndex}",
                "-an",
                "-sn",
                "-dn",
                "-vf",
                $"select='{selection}',scale='min(1600,iw)':-2,format=yuvj420p",
                "-vsync",
                "0",
                "-q:v",
                "2",
                outputPattern,
            };

            try
            {
                await RunToolAsync(ffmpegPath, arguments, cancellationToken)
                    .ConfigureAwait(false);
                string[] files = Directory
                    .GetFiles(tempDirectory, "frame-*.jpg")
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (files.Length == requests.Count)
                {
                    for (int index = 0; index < requests.Count; index++)
                    {
                        string destination = requests[index].OutputPath;
                        string? directory = Path.GetDirectoryName(destination);
                        if (!string.IsNullOrWhiteSpace(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }

                        File.Move(files[index], destination, overwrite: true);
                    }

                    return;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Fall through to the per-frame compatibility path.
            }

            await ExtractFramesIndividuallyAsync(
                videoPath,
                videoStreamIndex,
                requests,
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
                // Temporary batch frames are best effort cleanup.
            }
        }
    }

    private static async Task ExtractFramesIndividuallyAsync(
        string videoPath,
        int videoStreamIndex,
        IReadOnlyList<FrameExtractionRequest> requests,
        CancellationToken cancellationToken)
    {
        int concurrency = Math.Clamp(Environment.ProcessorCount / 2, 2, 4);
        using var gate = new SemaphoreSlim(Math.Min(concurrency, requests.Count));
        Task[] tasks = requests.Select(async request =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await ExtractFrameAsync(
                    videoPath,
                    videoStreamIndex,
                    request.Seconds,
                    request.OutputPath,
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }).ToArray();

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    public static async Task ExtractFrameAsync(
        string videoPath,
        int videoStreamIndex,
        double seconds,
        string outputPath,
        CancellationToken cancellationToken)
    {
        string ffmpegPath = FfmpegExePath
            ?? throw new InvalidOperationException("未找到内置 ffmpeg.exe，无法截取视频画面。");

        string timestamp = seconds.ToString("0.######", CultureInfo.InvariantCulture);
        string streamMap = $"0:{videoStreamIndex}";
        var attempts = new List<string[]>
        {
            new[]
            {
                "-hide_banner", "-loglevel", "error", "-y",
                "-ss", timestamp,
                "-i", videoPath,
                "-map", streamMap,
                "-an", "-sn", "-dn",
                "-frames:v", "1",
                "-vf", "scale='min(1600,iw)':-2,format=rgb24",
                outputPath,
            },
            new[]
            {
                "-hide_banner", "-loglevel", "error", "-y",
                "-i", videoPath,
                "-ss", timestamp,
                "-map", streamMap,
                "-an", "-sn", "-dn",
                "-frames:v", "1",
                "-vf", "scale='min(1600,iw)':-2,format=rgb24",
                outputPath,
            },
            new[]
            {
                "-hide_banner", "-loglevel", "error", "-y",
                "-ss", timestamp,
                "-i", videoPath,
                "-map", streamMap,
                "-an", "-sn", "-dn",
                "-frames:v", "1",
                "-vf", "scale='min(1600,iw)':-2,format=rgb24",
                outputPath,
            },
            new[]
            {
                "-hide_banner", "-loglevel", "error", "-y",
                "-i", videoPath,
                "-ss", timestamp,
                "-map", streamMap,
                "-an", "-sn", "-dn",
                "-frames:v", "1",
                "-pix_fmt", "rgb24",
                outputPath,
            },
            new[]
            {
                "-hide_banner", "-loglevel", "error", "-y",
                "-i", videoPath,
                "-ss", timestamp,
                "-map", "0:v:0",
                "-an", "-sn", "-dn",
                "-frames:v", "1",
                "-pix_fmt", "rgb24",
                outputPath,
            },
        };

        foreach (double offset in new[] { 0.05d, 0.12d, 0.25d, 0.5d, 1d })
        {
            double adjustedSeconds = Math.Max(0, seconds - offset);
            if (adjustedSeconds >= seconds)
            {
                continue;
            }

            string adjustedTimestamp = adjustedSeconds.ToString(
                "0.######",
                CultureInfo.InvariantCulture);
            attempts.Add(new[]
            {
                "-hide_banner", "-loglevel", "error", "-y",
                "-ss", adjustedTimestamp,
                "-i", videoPath,
                "-map", streamMap,
                "-an", "-sn", "-dn",
                "-frames:v", "1",
                "-vf", "scale='min(1600,iw)':-2,format=rgb24",
                outputPath,
            });
            attempts.Add(new[]
            {
                "-hide_banner", "-loglevel", "error", "-y",
                "-i", videoPath,
                "-ss", adjustedTimestamp,
                "-map", streamMap,
                "-an", "-sn", "-dn",
                "-frames:v", "1",
                "-pix_fmt", "rgb24",
                outputPath,
            });

            if (adjustedSeconds <= 0)
            {
                break;
            }
        }

        attempts.Add(new[]
        {
            "-hide_banner", "-loglevel", "error", "-y",
            "-ss", "0",
            "-i", videoPath,
            "-map", streamMap,
            "-an", "-sn", "-dn",
            "-frames:v", "1",
            "-vf", "scale='min(1600,iw)':-2,format=rgb24",
            outputPath,
        });
        attempts.Add(new[]
        {
            "-hide_banner", "-loglevel", "error", "-y",
            "-i", videoPath,
            "-ss", "0",
            "-map", streamMap,
            "-an", "-sn", "-dn",
            "-frames:v", "1",
            "-pix_fmt", "rgb24",
            outputPath,
        });

        Exception? lastError = null;
        foreach (string[] arguments in attempts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }

                await RunToolAsync(ffmpegPath, arguments, cancellationToken)
                    .ConfigureAwait(false);
                if (File.Exists(outputPath) && new FileInfo(outputPath).Length > 0)
                {
                    return;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                lastError = exception;
            }
        }

        throw lastError ?? new InvalidOperationException("FFmpeg 未生成截帧文件。");
    }

    private static string? LocateTool(string fileName)
    {
        if (_toolDirectory is not null)
        {
            string cached = Path.Combine(_toolDirectory, fileName);
            if (File.Exists(cached))
            {
                return cached;
            }
        }

        string[] roots =
        {
            AppContext.BaseDirectory,
            Path.Combine(AppContext.BaseDirectory, "ffmpeg"),
            Path.Combine(AppContext.BaseDirectory, "..", "ffmpeg"),
            Environment.CurrentDirectory,
        };

        foreach (string root in roots)
        {
            string candidate = Path.Combine(Path.GetFullPath(root), fileName);
            if (File.Exists(candidate))
            {
                _toolDirectory = Path.GetDirectoryName(candidate);
                return candidate;
            }
        }

        string? pathValue = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathValue))
        {
            foreach (string directory in pathValue.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(directory))
                {
                    continue;
                }

                string candidate = Path.Combine(directory.Trim(), fileName);
                if (File.Exists(candidate))
                {
                    _toolDirectory = Path.GetDirectoryName(candidate);
                    return candidate;
                }
            }
        }

        return null;
    }

    private static async Task<string> RunToolAsync(
        string toolPath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = toolPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardErrorEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException($"无法启动 {Path.GetFileName(toolPath)}。");
        }

        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        string output = await standardOutput.ConfigureAwait(false);
        string error = await standardError.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            string detail = string.IsNullOrWhiteSpace(error) ? output : error;
            throw new InvalidOperationException(
                detail.Length > 0 ? detail.Trim() : "FFmpeg 处理失败。");
        }

        return output;
    }
}
