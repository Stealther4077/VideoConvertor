namespace FileConverter.App.Services;

public sealed class FfmpegLocator
{
    public string? FfmpegPath { get; private set; }
    public string? FfprobePath { get; private set; }
    public string? DiscoveryMessage { get; private set; }

    public bool TryLocate(string? preferredDirectory = null)
    {
        FfmpegPath = null;
        FfprobePath = null;
        DiscoveryMessage = null;

        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(preferredDirectory))
            candidates.Add(preferredDirectory);

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        candidates.AddRange(pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));

        candidates.AddRange(new[]
        {
            @"C:\ffmpeg\bin",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Links"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ffmpeg", "bin"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "ffmpeg", "bin"),
        });

        foreach (var dir in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var ffmpeg = FindExe(dir, "ffmpeg.exe");
            var ffprobe = FindExe(dir, "ffprobe.exe");
            if (ffmpeg is null || ffprobe is null)
                continue;

            FfmpegPath = ffmpeg;
            FfprobePath = ffprobe;
            DiscoveryMessage = $"Using ffmpeg at {ffmpeg}";
            return true;
        }

        // WinGet may put shims/aliases elsewhere; try bare names via where.exe
        var fromWhereFfmpeg = ResolveViaWhere("ffmpeg.exe");
        var fromWhereFfprobe = ResolveViaWhere("ffprobe.exe");
        if (fromWhereFfmpeg is not null && fromWhereFfprobe is not null)
        {
            FfmpegPath = fromWhereFfmpeg;
            FfprobePath = fromWhereFfprobe;
            DiscoveryMessage = $"Using ffmpeg at {fromWhereFfmpeg}";
            return true;
        }

        DiscoveryMessage = "ffmpeg/ffprobe not found. Install ffmpeg or set the folder in Settings.";
        return false;
    }

    private static string? FindExe(string directory, string fileName)
    {
        try
        {
            if (!Directory.Exists(directory))
                return null;
            var path = Path.Combine(directory, fileName);
            return File.Exists(path) ? path : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveViaWhere(string exeName)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "where.exe",
                Arguments = exeName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = System.Diagnostics.Process.Start(psi);
            if (process is null) return null;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(3000);
            var first = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return !string.IsNullOrWhiteSpace(first) && File.Exists(first) ? first : null;
        }
        catch
        {
            return null;
        }
    }
}
