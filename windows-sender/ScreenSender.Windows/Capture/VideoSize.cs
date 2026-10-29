using System.Globalization;
using SIPSorceryMedia.Abstractions;

namespace ScreenSender.Windows.Capture;

public readonly record struct VideoSize(int Width, int Height)
{
    public override string ToString() => $"{Width}x{Height}";

    public static VideoSize Fit(int sourceWidth, int sourceHeight, int maxWidth, int maxHeight)
    {
        if (Math.Min(Math.Min(sourceWidth, sourceHeight), Math.Min(maxWidth, maxHeight)) < 2)
            throw new ArgumentException("Video dimensions must be at least two pixels.");
        double scale = Math.Min(1, Math.Min((double)maxWidth / sourceWidth, (double)maxHeight / sourceHeight));
        return new VideoSize(Math.Max(2, (int)(sourceWidth * scale) / 2 * 2),
            Math.Max(2, (int)(sourceHeight * scale) / 2 * 2));
    }

    public int Macroblocks => ((Width + 15) / 16) * ((Height + 15) / 16);
}

public static class H264Settings
{
    // H.264 Annex A MaxFS and MaxMBPS. Level 4.0 accommodates 1920x1080 at 30 fps.
    private static readonly (int Level, int Frames, int Rate)[] Limits =
    [
        (10, 99, 1485), (11, 396, 3000), (12, 396, 6000), (13, 396, 11880),
        (20, 396, 11880), (21, 792, 19800), (22, 1620, 20250), (30, 1620, 40500),
        (31, 3600, 108000), (32, 5120, 216000), (40, 8192, 245760), (41, 8192, 245760),
        (42, 8704, 522240), (50, 22080, 589824), (51, 36864, 983040), (52, 36864, 2073600),
        (60, 139264, 4177920), (61, 139264, 8355840), (62, 139264, 16711680),
    ];

    public static int LevelFor(VideoSize size, int fps) =>
        Limits.First(limit => size.Macroblocks <= limit.Frames && (long)size.Macroblocks * fps <= limit.Rate).Level;

    public static VideoFormat Format(VideoSize size, int fps) => new(VideoCodecsEnum.H264, 100, 90000,
        $"packetization-mode=1;profile-level-id=42e0{LevelFor(size, fps):x2};level-asymmetry-allowed=1");

    public static VideoSize ConstrainToAnswer(VideoSize size, int fps, string? parameters)
    {
        var profile = Parameter(parameters, "profile-level-id");
        profile ??= "42e01f"; // SDP's absent profile defaults to baseline level 3.1.
        if (profile.Length != 6 || !int.TryParse(profile.AsSpan(4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int level))
            throw new InvalidDataException("The answer contains an invalid H.264 profile-level-id.");
        var limit = Limits.LastOrDefault(entry => entry.Level <= level);
        if (limit.Level == 0) throw new InvalidDataException($"Unsupported H.264 level in answer: {profile}");
        double scale = 1;
        var result = size;
        while (result.Macroblocks > limit.Frames || (long)result.Macroblocks * fps > limit.Rate)
        {
            scale *= 0.98;
            result = VideoSize.Fit(size.Width, size.Height,
                Math.Max(2, (int)(size.Width * scale)), Math.Max(2, (int)(size.Height * scale)));
        }
        return result;
    }

    public static string? Parameter(string? parameters, string key) => parameters?.Split(';')
        .Select(part => part.Trim().Split('=', 2))
        .FirstOrDefault(pair => pair.Length == 2 && pair[0].Equals(key, StringComparison.OrdinalIgnoreCase))?[1];
}
