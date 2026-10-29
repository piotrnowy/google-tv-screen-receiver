using ScreenSender.Windows.Audio;
using ScreenSender.Windows.Capture;
using ScreenSender.Windows.Configuration;

internal static class ConfigurationChecks
{
    public static Task AppSettings()
    {
        string root = Path.Combine(Path.GetTempPath(), "ScreenSender-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string configPath = Path.Combine(root, "appsettings.json");
            string missingPath = Path.Combine(root, "missing.json");
            bool missingHost = false;
            try { SenderOptions.Parse([], missingPath); }
            catch (ArgumentException error) { missingHost = error.Message.Contains("--host", StringComparison.Ordinal); }
            Check(missingHost, "A missing configuration must require an explicit TV address");
            var builtIn = SenderOptions.Parse(["--host", "tv"], missingPath);
            Check(builtIn.Host == "tv" && builtIn.Port == 53530 && builtIn.Audio &&
                builtIn.AudioOutput == AudioOutputMode.Projector && builtIn.RemoteVolume,
                "Built-in defaults with an explicit host are incorrect");

            File.WriteAllText(configPath, """
                { "host": "192.0.2.9", "port": 1234, "audio": false, "audioOutput": "mirror", "remoteVolume": false }
                """);
            var configured = SenderOptions.Parse([], configPath);
            Check(configured.Host == "192.0.2.9" && configured.Port == 1234 && !configured.Audio &&
                configured.AudioOutput == AudioOutputMode.Mirror && !configured.RemoteVolume,
                "appsettings.json values were not loaded");
            var overridden = SenderOptions.Parse(["--host", "192.0.2.10", "--audio", "true", "--audio-output", "projector"], configPath);
            Check(overridden.Host == "192.0.2.10" && overridden.Audio && overridden.AudioOutput == AudioOutputMode.Projector && !overridden.RemoteVolume,
                "CLI did not override only supplied JSON values");

            File.WriteAllText(configPath, """{ "host": "" }""");
            bool emptyHost = false;
            try { SenderOptions.Parse([], configPath); }
            catch (ArgumentException) { emptyHost = true; }
            Check(emptyHost, "An unconfigured example must not connect to a built-in address");
            Check(SenderOptions.Parse(["--host", "tv"], configPath).Host == "tv", "CLI host must override an empty example host");

            File.WriteAllText(configPath, "{ broken");
            bool malformed = false;
            try { SenderOptions.Parse([], configPath); }
            catch (InvalidDataException error) { malformed = error.Message.Contains(configPath, StringComparison.Ordinal) && error.Message.Contains("line", StringComparison.Ordinal); }
            Check(malformed, "Malformed JSON did not produce a useful path/line error");
        }
        finally { Directory.Delete(root, recursive: true); }
        return Task.CompletedTask;
    }

    public static Task FFmpegDiscovery()
    {
        string root = Path.Combine(Path.GetTempPath(), "ScreenSender-ffmpeg-" + Guid.NewGuid().ToString("N"));
        string exeDir = Path.Combine(root, "publish");
        string adjacent = Path.Combine(exeDir, "ffmpeg", "bin");
        string pathDir = Path.Combine(root, "path");
        string wingetRoot = Path.Combine(root, "local", "Microsoft", "WinGet", "Packages");
        string winget = Path.Combine(wingetRoot, "Gyan.FFmpeg.Shared_Source", "ffmpeg-8.1.2-full_build-shared", "bin");
        string explicitDir = Path.Combine(root, "explicit");
        try
        {
            MakeComplete(adjacent); MakeComplete(pathDir); MakeComplete(winget); MakeComplete(explicitDir);
            Check(FFmpegRuntime.ResolveSharedLibraryDirectory(exeDir, path: pathDir, localAppData: Path.Combine(root, "local")) == Path.GetFullPath(adjacent),
                "Executable-adjacent shared libraries must take precedence over WinGet and PATH");
            Check(FFmpegRuntime.ResolveSharedLibraryDirectory(exeDir, explicitDir, pathDir, Path.Combine(root, "local")) == Path.GetFullPath(explicitDir),
                "FFMPEG_ROOT must take precedence");
            Directory.Delete(adjacent, recursive: true);
            Check(FFmpegRuntime.ResolveSharedLibraryDirectory(exeDir, path: pathDir, localAppData: Path.Combine(root, "local")) == Path.GetFullPath(winget),
                "Known WinGet shared installation was not found before PATH");
            Directory.Delete(winget, recursive: true);
            Check(FFmpegRuntime.ResolveSharedLibraryDirectory(exeDir, path: pathDir, localAppData: Path.Combine(root, "local")) == Path.GetFullPath(pathDir),
                "PATH shared libraries were not found");
            var incomplete = Path.Combine(root, "incomplete");
            Directory.CreateDirectory(incomplete);
            bool clearFailure = false;
            try { FFmpegRuntime.ResolveSharedLibraryDirectory(exeDir, incomplete, pathDir, Path.Combine(root, "local")); }
            catch (InvalidOperationException error) { clearFailure = error.Message.Contains("avcodec-62.dll", StringComparison.Ordinal); }
            Check(clearFailure, "Incomplete explicit FFmpeg root must list missing shared DLLs");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
        return Task.CompletedTask;
    }

    private static void MakeComplete(string directory)
    {
        Directory.CreateDirectory(directory);
        foreach (var dll in new[] { "avcodec-62.dll", "avformat-62.dll", "avutil-60.dll", "avdevice-62.dll", "swscale-9.dll", "swresample-6.dll" })
            File.WriteAllBytes(Path.Combine(directory, dll), []);
    }

    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
