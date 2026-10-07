using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace FileConverter.App.Services;

public sealed class MediaInfo
{
    public TimeSpan? Duration { get; init; }
    public string? VideoCodec { get; init; }

    /// <summary>
    /// Codecs known to play on TCL Google TV and Nebula Apollo from this project's testing.
    /// </summary>
    public bool HasCompatibleVideoCodec =>
        string.Equals(VideoCodec, "h264", StringComparison.OrdinalIgnoreCase);
}

public sealed class MediaProbeService
{
    private readonly FfmpegLocator _locator;

    public MediaProbeService(FfmpegLocator locator) => _locator = locator;

    public async Task<TimeSpan?> GetDurationAsync(string inputPath, CancellationToken cancellationToken)
    {
        var info = await ProbeAsync(inputPath, cancellationToken);
        return info.Duration;
    }

    public async Task<MediaInfo> ProbeAsync(string inputPath, CancellationToken cancellationToken)
    {
        if (_locator.FfprobePath is null)
            throw new InvalidOperationException("ffprobe is not available.");

        var psi = new ProcessStartInfo
        {
            FileName = _locator.FfprobePath,
            ArgumentList =
            {
                "-v", "error",
                "-show_entries", "format=duration:stream=index,codec_type,codec_name",
                "-of", "json",
                inputPath
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start ffprobe.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var stdout = await stdoutTask;

        if (process.ExitCode != 0)
        {
            var stderr = await stderrTask;
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr)
                ? "ffprobe failed."
                : stderr.Trim());
        }

        using var doc = JsonDocument.Parse(stdout);

        TimeSpan? duration = null;
        if (doc.RootElement.TryGetProperty("format", out var format) &&
            format.TryGetProperty("duration", out var durationElement))
        {
            var raw = durationElement.GetString();
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
                duration = TimeSpan.FromSeconds(seconds);
        }

        string? videoCodec = null;
        if (doc.RootElement.TryGetProperty("streams", out var streams) &&
            streams.ValueKind == JsonValueKind.Array)
        {
            foreach (var stream in streams.EnumerateArray())
            {
                if (!stream.TryGetProperty("codec_type", out var type) ||
                    !string.Equals(type.GetString(), "video", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Prefer the first real video stream (skip attached cover images when possible).
                if (stream.TryGetProperty("codec_name", out var name))
                {
                    var codec = name.GetString();
                    if (string.Equals(codec, "mjpeg", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(codec, "png", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    videoCodec = codec;
                    break;
                }
            }

            // Fallback: first video stream even if cover-like.
            if (videoCodec is null)
            {
                foreach (var stream in streams.EnumerateArray())
                {
                    if (stream.TryGetProperty("codec_type", out var type) &&
                        string.Equals(type.GetString(), "video", StringComparison.OrdinalIgnoreCase) &&
                        stream.TryGetProperty("codec_name", out var name))
                    {
                        videoCodec = name.GetString();
                        break;
                    }
                }
            }
        }

        return new MediaInfo
        {
            Duration = duration,
            VideoCodec = videoCodec
        };
    }
}
