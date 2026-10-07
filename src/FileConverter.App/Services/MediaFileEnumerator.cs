namespace FileConverter.App.Services;

public static class MediaFileEnumerator
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".avi", ".mov", ".m4v", ".wmv", ".webm", ".ts", ".m2ts", ".mpg", ".mpeg"
    };

    public static bool IsSupported(string path)
        => SupportedExtensions.Contains(Path.GetExtension(path));

    public static IEnumerable<string> FromPaths(IEnumerable<string> paths, bool includeSubfolders)
    {
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(path) && IsSupported(path))
            {
                yield return Path.GetFullPath(path);
                continue;
            }

            if (!Directory.Exists(path))
                continue;

            var option = includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(path, "*.*", option);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                if (IsSupported(file))
                    yield return Path.GetFullPath(file);
            }
        }
    }
}
