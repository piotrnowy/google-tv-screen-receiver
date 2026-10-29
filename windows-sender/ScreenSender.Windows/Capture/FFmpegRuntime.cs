namespace ScreenSender.Windows.Capture;

/// <summary>Finds the FFmpeg 8.1 shared DLL directory; a static ffmpeg.exe is not sufficient.</summary>
public static class FFmpegRuntime
{
    private static readonly string[] RequiredDlls =
    [
        "avcodec-62.dll", "avformat-62.dll", "avutil-60.dll", "avdevice-62.dll", "swscale-9.dll", "swresample-6.dll",
    ];

    public static string ResolveSharedLibraryDirectory(string? baseDirectory = null, string? ffmpegRoot = null, string? path = null, string? localAppData = null)
    {
        baseDirectory ??= AppContext.BaseDirectory;
        ffmpegRoot ??= Environment.GetEnvironmentVariable("FFMPEG_ROOT");
        path ??= Environment.GetEnvironmentVariable("PATH");
        localAppData ??= Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        if (!string.IsNullOrWhiteSpace(ffmpegRoot))
        {
            var explicitPath = Normalize(ffmpegRoot);
            var missing = MissingLibraries(explicitPath);
            if (missing.Length > 0)
                throw new InvalidOperationException($"FFMPEG_ROOT points to '{explicitPath}', but the FFmpeg 8.1 shared build is incomplete. Missing: {string.Join(", ", missing)}.");
            return explicitPath;
        }

        var candidates = new List<string>
        {
            Path.Combine(baseDirectory, "ffmpeg", "bin"),
        };

        string wingetRoot = Path.Combine(localAppData, "Microsoft", "WinGet", "Packages");
        if (Directory.Exists(wingetRoot))
        {
            try
            {
                foreach (string package in Directory.EnumerateDirectories(wingetRoot, "Gyan.FFmpeg.Shared_*", SearchOption.TopDirectoryOnly))
                    foreach (string build in Directory.EnumerateDirectories(package, "ffmpeg-8.1*-full_build-shared", SearchOption.TopDirectoryOnly))
                        candidates.Add(Path.Combine(build, "bin"));
            }
            catch (IOException) { /* A package update may race discovery; PATH is checked next. */ }
            catch (UnauthorizedAccessException) { }
        }

        candidates.AddRange((path ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).Select(Normalize));
        foreach (string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            if (Directory.Exists(candidate) && MissingLibraries(candidate).Length == 0)
                return Path.GetFullPath(candidate);

        throw new InvalidOperationException(
            "FFmpeg 8.1 x64 shared libraries were not found. Required DLLs: " + string.Join(", ", RequiredDlls) + ". " +
            "Install an FFmpeg 8.1 shared build, set FFMPEG_ROOT to its bin folder, or place the DLLs in " +
            $"'{Path.Combine(baseDirectory, "ffmpeg", "bin")}'. A static ffmpeg.exe does not include the required shared libraries.");
    }

    private static string[] MissingLibraries(string directory) => RequiredDlls
        .Where(name => !File.Exists(Path.Combine(directory, name))).ToArray();

    private static string Normalize(string path) => Path.GetFullPath(path.Trim().Trim('"'));
}
