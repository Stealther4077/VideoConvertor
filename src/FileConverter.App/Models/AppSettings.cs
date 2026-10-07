namespace FileConverter.App.Models;

public sealed class AppSettings
{
    public OutputFormat OutputFormat { get; set; } = OutputFormat.Avi;
    public bool UseSameFolderAsSource { get; set; } = true;
    public string? CustomOutputFolder { get; set; }
    public bool OverwriteExisting { get; set; }
    public bool IncludeSubfolders { get; set; }
    public bool DeleteOriginalAfterSuccess { get; set; }
    public bool SkipCompatibleCodec { get; set; } = true;
    public string? FfmpegDirectory { get; set; }
}
