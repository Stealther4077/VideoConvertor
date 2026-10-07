namespace FileConverter.App.Models;

public enum OutputFormat
{
    Avi,
    Mp4,
    Mkv
}

public static class OutputFormatExtensions
{
    public static string ToExtension(this OutputFormat format) => format switch
    {
        OutputFormat.Avi => ".avi",
        OutputFormat.Mp4 => ".mp4",
        OutputFormat.Mkv => ".mkv",
        _ => ".avi"
    };

    public static string ToDisplayName(this OutputFormat format) => format switch
    {
        OutputFormat.Avi => "AVI (H.264 + MP3)",
        OutputFormat.Mp4 => "MP4 (H.264 + AAC)",
        OutputFormat.Mkv => "MKV (H.264 + AAC)",
        _ => format.ToString()
    };

    public static bool MatchesPath(this OutputFormat format, string path)
        => string.Equals(Path.GetExtension(path), format.ToExtension(), StringComparison.OrdinalIgnoreCase);
}
