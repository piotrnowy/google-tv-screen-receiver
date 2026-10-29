using FFmpeg.AutoGen;
using ScreenSender.Windows.Capture;

internal static class EncoderFrameChecks
{
    private static readonly VideoSize Output = new(1280, 720);
    private unsafe delegate void FrameCheck(AVFrame* frame);

    public static unsafe Task ReusedFrame()
    {
        WithFrame(1280, 640, frame =>
        {
            // Reproduce what SIPSorcery 10.0.17 leaves on the reused AVFrame after Encode(keyFrame: true).
            frame->pict_type = AVPictureType.AV_PICTURE_TYPE_I;
            frame->flags = ffmpeg.AV_FRAME_FLAG_KEY | ffmpeg.AV_FRAME_FLAG_CORRUPT;
            for (int index = 0; index < 3; index++)
            {
                H264Encoding.PrepareFrame(frame, Output);
                Assert(frame->pict_type == AVPictureType.AV_PICTURE_TYPE_NONE, "Stale I-frame state would force every frame to intra-code");
                Assert((frame->flags & ffmpeg.AV_FRAME_FLAG_KEY) == 0, "Stale keyframe flag was retained");
                Assert((frame->flags & ffmpeg.AV_FRAME_FLAG_CORRUPT) != 0, "Unrelated frame flags were changed");
                Assert(frame->width == 1280 && frame->height == 720, "Keyframe reset changed frame dimensions");
                // A later explicit keyframe request must be reset on the following frame too.
                if (index == 1) { frame->pict_type = AVPictureType.AV_PICTURE_TYPE_I; frame->flags |= ffmpeg.AV_FRAME_FLAG_KEY; }
            }
        });
        return Task.CompletedTask;
    }

    public static unsafe Task PlaneValidation()
    {
        WithFrame(1280, 640, frame =>
        {
            var layout = H264Encoding.PrepareFrame(frame, Output);
            Assert(layout.Width == 1280 && layout.Height == 720 && layout.PlaneBytes == 1_382_400, "Unexpected 720p YUV420P geometry");
            Assert(layout.AllocationBytes == 1_382_400, "Encoder allocation size must match the actual owning buffer");
            Rejected(frame, new VideoSize(2560, 1440));
            frame->format = (int)AVPixelFormat.AV_PIX_FMT_BGR24;
            Rejected(frame, Output);
            frame->format = (int)AVPixelFormat.AV_PIX_FMT_YUV420P;
            frame->linesize[0] = 1279;
            Rejected(frame, Output);
            frame->linesize[0] = 1280;
            frame->buf[0]->size--;
            Rejected(frame, Output); // Last chroma byte would be out of bounds.
            frame->buf[0]->size++;
            frame->data[2] = null;
            Rejected(frame, Output);
        });
        WithFrame(1312, 672, frame =>
        {
            var layout = H264Encoding.PrepareFrame(frame, Output);
            Assert(layout.YStride == 1312 && layout.UStride == 672 && layout.VStride == 672, "Padded YUV strides were flattened");
            Assert(layout.PlaneBytes == 1_428_480 && layout.AllocationBytes == 1_428_480, "Padded YUV buffer size is incorrect");
        });
        return Task.CompletedTask;
    }

    public static Task QualitySettings()
    {
        Assert(H264Encoding.Bitrate == 6_000_000, "Quality milestone requires 6 Mbps, not a low default bitrate");
        var options = H264Encoding.Options(Output, 30);
        Assert(options["preset"] == "veryfast" && options["tune"] == "zerolatency", "Screen-content latency/quality tuning changed");
        Assert(options["profile"] == "baseline", "Android H264 profile compatibility changed");
        foreach (string setting in new[] { "bitrate=6000", "vbv-maxrate=6000", "vbv-bufsize=6000", "nal-hrd=cbr", "keyint=60", "min-keyint=30", "bframes=0" })
            Assert(options["x264-params"].Split(':').Contains(setting), $"Missing stable quality setting {setting}");
        return Task.CompletedTask;
    }

    private static unsafe void WithFrame(int yStride, int uvStride, FrameCheck check)
    {
        int yBytes = yStride * 720, uvBytes = uvStride * 360;
        byte[] storage = new byte[yBytes + uvBytes * 2];
        fixed (byte* data = storage)
        {
            // Pure metadata/native-layout tests; do not load FFmpeg shared DLLs.
            AVBufferRef buffer = new() { data = data, size = (ulong)storage.Length };
            AVFrame frame = new() { width = 1280, height = 720, format = (int)AVPixelFormat.AV_PIX_FMT_YUV420P };
            frame.buf[0] = &buffer;
            frame.data[0] = data;
            frame.data[1] = data + yBytes;
            frame.data[2] = data + yBytes + uvBytes;
            frame.linesize[0] = yStride;
            frame.linesize[1] = uvStride;
            frame.linesize[2] = uvStride;
            check(&frame);
        }
    }

    private static unsafe void Rejected(AVFrame* frame, VideoSize expected)
    {
        try { H264Encoding.PrepareFrame(frame, expected); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException("Invalid encoder frame was accepted");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
