using FFmpeg.AutoGen;
using SIPSorceryMedia.Abstractions;

namespace ScreenSender.Windows.Capture;

/// <summary>Validated packed 24-bit frame metadata; validation never copies pixel data.</summary>
public readonly record struct RawFrameLayout(
    int Width, int Height, int Stride, VideoPixelFormatsEnum PixelFormat,
    AVPixelFormat FFmpegPixelFormat, int BufferSize)
{
    public static RawFrameLayout Validate(VideoPixelFormatsEnum format, int width, int height,
        int? stride, long bufferLength)
    {
        AVPixelFormat nativeFormat = format switch
        {
            VideoPixelFormatsEnum.Bgr => AVPixelFormat.AV_PIX_FMT_BGR24,
            VideoPixelFormatsEnum.Rgb => AVPixelFormat.AV_PIX_FMT_RGB24,
            _ => throw new NotSupportedException($"Unsupported raw frame: {Describe(format, width, height, stride, bufferLength)}"),
        };
        if (width <= 0 || height <= 0)
            throw new InvalidDataException($"Invalid raw frame dimensions: {Describe(format, width, height, stride, bufferLength)}");

        int rowBytes;
        int actualStride;
        int requiredBytes;
        try
        {
            // Bgr is B,G,R, three bytes per pixel. RawImage always supplies a stride;
            // only callers with no stride metadata may use the packed-row fallback.
            rowBytes = checked(width * 3);
            actualStride = stride ?? rowBytes;
            requiredBytes = checked(actualStride * height);
        }
        catch (OverflowException error)
        {
            throw new InvalidDataException($"Raw frame layout exceeds supported buffer size: {Describe(format, width, height, stride, bufferLength)}", error);
        }
        if (actualStride < rowBytes)
            throw new InvalidDataException($"Raw frame stride is smaller than a packed row: {Describe(format, width, height, stride, bufferLength)}");
        if (bufferLength < requiredBytes)
            throw new InvalidDataException($"Truncated raw frame; requires {requiredBytes} bytes: {Describe(format, width, height, stride, bufferLength)}");

        return new(width, height, actualStride, format, nativeFormat, requiredBytes);
    }

    public static RawFrameLayout FromRawImage(RawImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        // In 10.0.17 BufferSize is computed from Stride * Height, not an independent
        // allocation length. Validate arithmetic/geometry before native access; the
        // capture source owns the allocation and keeps it alive during this callback.
        var layout = Validate(image.PixelFormat, image.Width, image.Height, image.Stride, image.BufferSize);
        if (image.Sample == IntPtr.Zero)
            throw new InvalidDataException($"Raw frame has a null sample pointer: {Describe(image.PixelFormat, image.Width, image.Height, image.Stride, image.BufferSize)}");
        return layout;
    }

    public static string Describe(VideoPixelFormatsEnum format, int width, int height, int? stride, long bufferLength) =>
        $"format={format} ({(int)format}), width={width}, height={height}, stride={stride?.ToString() ?? "not supplied"}, sample buffer length={bufferLength}";
}
