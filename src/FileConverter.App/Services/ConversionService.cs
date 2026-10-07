using System.Diagnostics;
using System.Globalization;
using System.Text;
using FileConverter.App.Models;

namespace FileConverter.App.Services;

public sealed class ConversionProgress
{
    public required double Percent { get; init; }
    public required TimeSpan OutTime { get; init; }
    public string? Speed { get; init; }
    public double? SpeedFactor { get; init; }
    public string Detail { get; init; } = string.Empty;
}

public sealed class ConversionService
{
    private readonly FfmpegLocator _locator;
    private readonly MediaProbeService _probe;

    public ConversionService(FfmpegLocator locator, MediaProbeService probe)
    {
        _locator = locator;
        _probe = probe;
    }

    /// <summary>
    /// Probe and apply skip rules without encoding.
    /// Returns true when the job was marked Skipped.
    /// </summary>
    public async Task<bool> PrepareAsync(
        ConversionJob job,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        job.OutputPath = BuildOutputPath(job.SourcePath, settings);

        if (PathsEqual(job.SourcePath, job.OutputPath) ||
            (settings.OutputFormat.MatchesPath(job.SourcePath) && settings.UseSameFolderAsSource))
        {
            MarkSkipped(job, "Already in selected format");
            return true;
        }

        if (File.Exists(job.OutputPath) && !settings.OverwriteExisting)
        {
            MarkSkipped(job, "Output already exists");
            return true;
        }

        var media = await _probe.ProbeAsync(job.SourcePath, cancellationToken);
        job.Duration = media.Duration;

        if (settings.SkipCompatibleCodec && media.HasCompatibleVideoCodec)
        {
            MarkSkipped(job, $"Already H.264 ({media.VideoCodec})");
            return true;
        }

        job.CountsTowardEncodeProgress = true;
        if (job.Status != JobStatus.Running)
            job.StatusDetail = "Queued for encode";
        job.ProgressPercent = job.Status == JobStatus.Running ? job.ProgressPercent : 0;
        return false;
    }

    public async Task ConvertAsync(
        ConversionJob job,
        AppSettings settings,
        IProgress<ConversionProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (_locator.FfmpegPath is null)
            throw new InvalidOperationException("ffmpeg is not available.");

        // Already scanned for encode — avoid a second ffprobe unless needed.
        if (!job.CountsTowardEncodeProgress || string.IsNullOrWhiteSpace(job.OutputPath))
        {
            if (await PrepareAsync(job, settings, cancellationToken))
                return;
        }

        var outputPath = job.OutputPath
            ?? throw new InvalidOperationException("Output path was not prepared.");

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var args = BuildArguments(job.SourcePath, outputPath, settings.OutputFormat);
        var psi = new ProcessStartInfo
        {
            FileName = _locator.FfmpegPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start ffmpeg.");

        var stderrBuilder = new StringBuilder();
        var lastSpeed = "—";
        double? lastSpeedFactor = null;

        var stderrTask = Task.Run(async () =>
        {
            while (!process.StandardError.EndOfStream)
            {
                var line = await process.StandardError.ReadLineAsync(cancellationToken);
                if (line is null) break;
                if (line.Length < 500)
                    stderrBuilder.AppendLine(line);

                var speedIdx = line.LastIndexOf("speed=", StringComparison.Ordinal);
                if (speedIdx >= 0)
                {
                    var speedPart = line[(speedIdx + 6)..].Trim();
                    var end = speedPart.IndexOf(' ');
                    lastSpeed = end > 0 ? speedPart[..end] : speedPart;
                    lastSpeedFactor = ParseSpeedFactor(lastSpeed);
                }
            }
        }, cancellationToken);

        var stdoutTask = Task.Run(async () =>
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            while (!process.StandardOutput.EndOfStream)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = await process.StandardOutput.ReadLineAsync(cancellationToken);
                if (line is null) break;

                var eq = line.IndexOf('=');
                if (eq <= 0) continue;
                var key = line[..eq].Trim();
                var value = line[(eq + 1)..].Trim();
                values[key] = value;

                if (!key.Equals("progress", StringComparison.OrdinalIgnoreCase))
                    continue;

                ReportFromProgress(values, job.Duration, lastSpeed, lastSpeedFactor, progress);
                if (value.Equals("end", StringComparison.OrdinalIgnoreCase))
                    break;
            }
        }, cancellationToken);

        try
        {
            await using (cancellationToken.Register(() =>
                         {
                             try
                             {
                                 if (!process.HasExited)
                                     process.Kill(entireProcessTree: true);
                             }
                             catch
                             {
                                 // ignored
                             }
                         }))
            {
                await Task.WhenAll(stdoutTask, stderrTask);
                await process.WaitForExitAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
                // ignored
            }

            TryDeletePartial(outputPath);
            throw;
        }

        if (process.ExitCode != 0)
        {
            TryDeletePartial(outputPath);
            var err = stderrBuilder.ToString().Trim();
            var lastLines = string.Join('\n', err.Split('\n').TakeLast(8));
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(lastLines)
                ? $"ffmpeg exited with code {process.ExitCode}."
                : lastLines);
        }

        if (!File.Exists(outputPath))
            throw new InvalidOperationException("Conversion finished but output file was not created.");

        job.ProgressPercent = 100;
        job.StatusDetail = "Completed";
        job.Status = JobStatus.Done;
    }

    private static void MarkSkipped(ConversionJob job, string reason)
    {
        job.Status = JobStatus.Skipped;
        job.StatusDetail = reason;
        job.ProgressPercent = 100;
        job.CountsTowardEncodeProgress = false;
    }

    private static void ReportFromProgress(
        IReadOnlyDictionary<string, string> values,
        TimeSpan? duration,
        string lastSpeed,
        double? lastSpeedFactor,
        IProgress<ConversionProgress>? progress)
    {
        if (progress is null) return;

        TimeSpan outTime = TimeSpan.Zero;
        if (values.TryGetValue("out_time_us", out var usRaw) &&
            long.TryParse(usRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var us) &&
            us >= 0)
        {
            outTime = TimeSpan.FromTicks(us * 10);
        }
        else if (values.TryGetValue("out_time_ms", out var msRaw) &&
                 long.TryParse(msRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var micro) &&
                 micro >= 0)
        {
            outTime = TimeSpan.FromTicks(micro * 10);
        }
        else if (values.TryGetValue("out_time", out var timeRaw) &&
                 TimeSpan.TryParse(timeRaw, CultureInfo.InvariantCulture, out var parsed))
        {
            outTime = parsed;
        }

        var speed = values.TryGetValue("speed", out var speedRaw) && !string.IsNullOrWhiteSpace(speedRaw)
            ? speedRaw
            : lastSpeed;
        var speedFactor = ParseSpeedFactor(speed) ?? lastSpeedFactor;

        double percent = 0;
        if (duration is { TotalSeconds: > 0 })
            percent = Math.Clamp(outTime.TotalSeconds / duration.Value.TotalSeconds * 100.0, 0, 99.9);

        progress.Report(new ConversionProgress
        {
            Percent = percent,
            OutTime = outTime,
            Speed = speed,
            SpeedFactor = speedFactor,
            Detail = duration is null
                ? outTime.ToString(@"hh\:mm\:ss")
                : $"{outTime:hh\\:mm\\:ss} / {duration.Value:hh\\:mm\\:ss}"
        });
    }

    public static double? ParseSpeedFactor(string? speed)
    {
        if (string.IsNullOrWhiteSpace(speed))
            return null;

        var trimmed = speed.Trim().TrimEnd('x', 'X');
        if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
            value > 0)
        {
            return value;
        }

        return null;
    }

    private static IEnumerable<string> BuildArguments(string input, string output, OutputFormat format)
    {
        yield return "-y";
        yield return "-hide_banner";
        yield return "-i";
        yield return input;
        yield return "-map";
        yield return "0:v:0";
        yield return "-map";
        yield return "0:a:0?";
        yield return "-c:v";
        yield return "libx264";
        yield return "-crf";
        yield return "20";
        yield return "-preset";
        yield return "medium";
        yield return "-pix_fmt";
        yield return "yuv420p";

        switch (format)
        {
            case OutputFormat.Avi:
                yield return "-c:a";
                yield return "mp3";
                yield return "-b:a";
                yield return "192k";
                break;
            default:
                yield return "-c:a";
                yield return "aac";
                yield return "-b:a";
                yield return "192k";
                break;
        }

        yield return "-progress";
        yield return "pipe:1";
        yield return "-nostats";
        yield return output;
    }

    public static string BuildOutputPath(string sourcePath, AppSettings settings)
    {
        var fileName = Path.GetFileNameWithoutExtension(sourcePath) + settings.OutputFormat.ToExtension();
        if (settings.UseSameFolderAsSource || string.IsNullOrWhiteSpace(settings.CustomOutputFolder))
            return Path.Combine(Path.GetDirectoryName(sourcePath)!, fileName);

        return Path.Combine(settings.CustomOutputFolder, fileName);
    }

    private static bool PathsEqual(string a, string b)
        => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private static void TryDeletePartial(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // ignored
        }
    }
}
