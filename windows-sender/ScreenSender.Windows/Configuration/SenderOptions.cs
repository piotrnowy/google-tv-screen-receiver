using System.Globalization;
using System.Text.Json;
using ScreenSender.Windows.Audio;

namespace ScreenSender.Windows.Configuration;

public sealed record SenderOptions(string Host = "", int Port = 53530, string Monitor = "primary",
    int Width = 1920, int Height = 1080, int Fps = 30, bool Audio = true, bool AudioDebug = false, AudioOutputMode AudioOutput = AudioOutputMode.Projector, bool RemoteVolume = true)
{
    public const string Usage = """
        Screen Sender
        Usage: dotnet run -- --host <TV-IP> [--port 53530] [--monitor primary|1|2...]
                          [--width 1920] [--height 1080] [--fps 30] [--audio true|false]
                          [--audio-debug true|false] [--audio-output mirror|projector] [--remote-volume true|false]

        --monitor uses a 1-based list with the primary monitor first.
        Resolution is a bounding box: the full desktop is scaled without stretching.
        --audio defaults to true and captures system playback, never the microphone.
        Audio capture/PCM/Opus/RTP diagnostics log once per second.
        --audio-debug defaults to false; true also logs capture-buffer depth.
        --audio-output defaults to projector; projector mutes local output while streaming and restores it on exit.
        --remote-volume defaults to true; Windows media keys control Google TV only in projector mode.
        appsettings.json beside the executable can override host, port, audio, audioOutput and remoteVolume.
        Set host in that local file or supply --host; no TV address is built in.
        CLI arguments override appsettings.json. FFMPEG_ROOT may point to FFmpeg 8.1 x64 shared bin.
        Ctrl+C stops capture and disconnects.
        """;

    public static SenderOptions Parse(string[] args, string? configPath = null)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        configPath ??= Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (File.Exists(configPath))
        {
            AppSettings config;
            try { config = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(configPath), JsonOptions) ?? throw new JsonException("The root value must be a JSON object."); }
            catch (JsonException error)
            {
                string location = error.LineNumber is long line ? $"line {line + 1}, byte {error.BytePositionInLine}" : "unknown location";
                throw new InvalidDataException($"Could not read configuration '{configPath}' ({location}): {error.Message}", error);
            }
            catch (IOException error) { throw new InvalidDataException($"Could not read configuration '{configPath}': {error.Message}", error); }
            Add("--host", config.Host);
            Add("--port", config.Port?.ToString(CultureInfo.InvariantCulture));
            Add("--audio", config.Audio?.ToString());
            Add("--audio-output", config.AudioOutput);
            Add("--remote-volume", config.RemoteVolume?.ToString());
        }
        var commandLineValues = new Dictionary<string, string>(StringComparer.Ordinal);
        var allowed = new HashSet<string> { "--host", "--port", "--monitor", "--width", "--height", "--fps", "--audio", "--audio-debug", "--audio-output", "--remote-volume" };
        for (int i = 0; i < args.Length; i += 2)
        {
            if (!allowed.Contains(args[i])) throw new ArgumentException($"Unknown option: {args[i]}");
            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Missing value for {args[i]}.");
            if (!commandLineValues.TryAdd(args[i], args[i + 1])) throw new ArgumentException($"Duplicate option: {args[i]}");
        }
        foreach (var (key, value) in commandLineValues) values[key] = value;

        var host = values.GetValueOrDefault("--host", "");
        if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("Set --host or host in appsettings.json to the TV's IP address or local hostname.");
        if (Uri.CheckHostName(host) == UriHostNameType.Unknown)
            throw new ArgumentException("--host must be an IP address or local hostname, without a scheme or port.");
        int port = Number("--port", 53530, 1, 65535);
        int width = Number("--width", 1920, 2, 7680);
        int height = Number("--height", 1080, 2, 4320);
        int fps = Number("--fps", 30, 1, 60);
        if (width % 2 != 0 || height % 2 != 0) throw new ArgumentException("Width and height must be even for H.264 YUV420 video.");
        var monitor = values.GetValueOrDefault("--monitor", "primary");
        if (!monitor.Equals("primary", StringComparison.OrdinalIgnoreCase) &&
            (!int.TryParse(monitor, out int index) || index < 1))
            throw new ArgumentException("--monitor must be primary or a positive 1-based monitor number.");
        var output = values.GetValueOrDefault("--audio-output", "projector") switch
        {
            "mirror" => AudioOutputMode.Mirror,
            "projector" => AudioOutputMode.Projector,
            _ => throw new ArgumentException("--audio-output must be mirror or projector."),
        };
        return new SenderOptions(host, port, monitor, width, height, fps, Boolean("--audio", true), Boolean("--audio-debug", false), output, Boolean("--remote-volume", true));

        void Add(string key, string? value)
        {
            if (value is not null) values[key] = value;
        }

        bool Boolean(string key, bool fallback)
        {
            if (!values.TryGetValue(key, out var text)) return fallback;
            if (!bool.TryParse(text, out bool value)) throw new ArgumentException($"{key} must be true or false.");
            return value;
        }

        int Number(string key, int fallback, int minimum, int maximum)
        {
            if (!values.TryGetValue(key, out var text)) return fallback;
            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) ||
                value < minimum || value > maximum)
                throw new ArgumentException($"{key} must be between {minimum} and {maximum}.");
            return value;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed class AppSettings
    {
        public string? Host { get; init; }
        public int? Port { get; init; }
        public bool? Audio { get; init; }
        public string? AudioOutput { get; init; }
        public bool? RemoteVolume { get; init; }
    }
}
