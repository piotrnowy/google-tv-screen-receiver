using FFmpeg.AutoGen;
using SIPSorceryMedia.FFmpeg;

namespace ScreenSender.Windows.Capture;

public static class H264Encoding
{
    public const long Bitrate = 6_000_000;

    public static Dictionary<string, string> Options(VideoSize size, int fps)
    {
        int level = H264Settings.LevelFor(size, fps);
        return new()
        {
            ["profile"] = "baseline", ["level"] = $"{level / 10}.{level % 10}",
            ["preset"] = "veryfast", ["tune"] = "zerolatency",
            // libx264 uses kbps here, while FFmpegVideoEncoder.SetBitrate uses bits/sec.
            ["x264-params"] = $"bitrate=6000:vbv-maxrate=6000:vbv-bufsize=6000:nal-hrd=cbr:filler=1:" +
                $"keyint={fps * 2}:min-keyint={fps}:scenecut=0:bframes=0:repeat-headers=1",
        };
    }

    public readonly record struct FrameLayout(int Width, int Height, int YStride, int UStride, int VStride,
        long PlaneBytes, long AllocationBytes);

    /// <summary>Validate the actual native planes, then clear state left by encoding a previous frame.</summary>
    public static unsafe FrameLayout PrepareFrame(AVFrame* frame, VideoSize expected)
    {
        if (frame == null) throw new InvalidDataException("Encoder input frame is null.");
        if (frame->width != expected.Width || frame->height != expected.Height)
            throw new InvalidDataException($"Encoder input {frame->width}x{frame->height} does not match selected output {expected}.");
        if (frame->format != (int)AVPixelFormat.AV_PIX_FMT_YUV420P)
            throw new InvalidDataException($"Encoder requires YUV420P, received {(AVPixelFormat)frame->format}.");
        if (frame->width <= 0 || frame->height <= 0 || frame->width % 2 != 0 || frame->height % 2 != 0)
            throw new InvalidDataException("YUV420P encoder dimensions must be positive and even.");

        long planeBytes = 0, allocationBytes = 0;
        for (uint index = 0; index < 8; index++)
            if (frame->buf[index] != null) allocationBytes = checked(allocationBytes + (long)frame->buf[index]->size);
        for (uint plane = 0; plane < 3; plane++)
        {
            int width = plane == 0 ? frame->width : frame->width / 2;
            int height = plane == 0 ? frame->height : frame->height / 2;
            int stride = frame->linesize[plane];
            if (frame->data[plane] == null || stride < width)
                throw new InvalidDataException($"Invalid encoder plane {plane}: width={width}, height={height}, stride={stride}.");
            long span = checked((long)stride * (height - 1) + width);
            bool inBuffer = false;
            for (uint index = 0; index < 8; index++)
            {
                var buffer = frame->buf[index];
                if (buffer == null) continue;
                long offset = frame->data[plane] - buffer->data;
                long capacity = checked((long)buffer->size);
                if (offset >= 0 && offset <= capacity && span <= capacity - offset) { inBuffer = true; break; }
            }
            if (!inBuffer) throw new InvalidDataException($"Encoder plane {plane} exceeds its native buffer allocation.");
            planeBytes = checked(planeBytes + (long)stride * height);
        }

        // SIPSorcery 10.0.17 mutates AVFrame on forced keyframes and does not reset
        // it for later frames. swscale reuses this frame, so stale I/KEY state would
        // otherwise force EVERY subsequent frame to be intra-coded at the target bitrate.
        frame->pict_type = AVPictureType.AV_PICTURE_TYPE_NONE;
        frame->flags &= ~ffmpeg.AV_FRAME_FLAG_KEY;
        return new(frame->width, frame->height, frame->linesize[0], frame->linesize[1], frame->linesize[2], planeBytes, allocationBytes);
    }

    /// <summary>Decode only the first encoded access unit to verify real bitstream dimensions.</summary>
    public static unsafe VideoSize ReadEncodedSize(byte[] sample)
    {
        AVCodecContext* decoder = null;
        AVPacket* packet = null;
        AVFrame* decoded = null;
        try
        {
            var codec = ffmpeg.avcodec_find_decoder(AVCodecID.AV_CODEC_ID_H264);
            if (codec == null) throw new InvalidOperationException("FFmpeg H264 decoder is required for the startup resolution check.");
            decoder = ffmpeg.avcodec_alloc_context3(codec);
            if (decoder == null) throw new OutOfMemoryException("Could not allocate H264 diagnostic decoder.");
            decoder->thread_count = 1; // No frame-thread delay on the first IDR.
            Check(ffmpeg.avcodec_open2(decoder, codec, null));
            packet = ffmpeg.av_packet_alloc();
            decoded = ffmpeg.av_frame_alloc();
            if (packet == null || decoded == null) throw new OutOfMemoryException("Could not allocate H264 diagnostic frame.");
            Check(ffmpeg.av_new_packet(packet, sample.Length));
            // One startup-only copy into an FFmpeg-owned, correctly padded packet.
            sample.AsSpan().CopyTo(new Span<byte>(packet->data, sample.Length));
            Check(ffmpeg.avcodec_send_packet(decoder, packet));
            Check(ffmpeg.avcodec_receive_frame(decoder, decoded));
            return new(decoded->width, decoded->height);
        }
        finally
        {
            ffmpeg.av_frame_free(&decoded);
            ffmpeg.av_packet_free(&packet);
            ffmpeg.avcodec_free_context(&decoder);
        }
    }

    private static void Check(int result)
    {
        if (result < 0) throw new InvalidDataException($"H264 startup bitstream check failed: {FFmpegInit.av_strerror(result)}");
    }
}
