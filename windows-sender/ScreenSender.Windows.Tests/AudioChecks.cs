using System.Runtime.InteropServices;
using Concentus;
using NAudio.Wave;
using ScreenSender.Windows.Audio;
using ScreenSender.Windows.Configuration;
using ScreenSender.Windows.Signaling;

internal static class AudioChecks
{
    public static Task FutureVolumeMessages()
    {
        foreach (SignalingMessage message in new SignalingMessage[] { new VolumeMessage(0), new VolumeMessage(0.5), new VolumeMessage(1), new MuteMessage(true), new MuteMessage(false) })
        {
            string json = SignalingCodec.Encode(message);
            Assert(SignalingCodec.TryParse(json, out var parsed) && parsed == message, "Future volume/mute wire message did not round-trip");
        }
        foreach (string json in new[] { "{\"type\":\"volume\",\"value\":-1}", "{\"type\":\"volume\",\"value\":1.1}", "{\"type\":\"volume\",\"value\":\"0.5\"}", "{\"type\":\"volume\"}", "{\"type\":\"mute\",\"value\":1}", "{\"type\":\"mute\",\"value\":\"true\"}" })
            Assert(!SignalingCodec.TryParse(json, out _), "Invalid volume/mute field was accepted");
        return Task.CompletedTask;
    }

    public static Task SdpValidation()
    {
        static string Audio(string attributes) => "v=0\r\nm=video 9 UDP/TLS/RTP/SAVPF 100\r\na=sendonly\r\n" +
            "m=audio 9 UDP/TLS/RTP/SAVPF 111\r\na=mid:1\r\na=rtpmap:111 opus/48000/2\r\n" + attributes + "\r\n";
        var logs = new List<string>();
        AudioSdpDiagnostics.LogAndValidate(Audio("a=sendonly"), true, logs.Add);
        AudioSdpDiagnostics.LogAndValidate(Audio("a=recvonly"), false, logs.Add);
        AudioSdpDiagnostics.LogAndValidate(Audio("a=sendrecv"), false, logs.Add);
        foreach (string rejected in new[] {
            "v=0\r\nm=video 9 UDP/TLS/RTP/SAVPF 100\r\n",
            Audio("a=recvonly").Replace("m=audio 9", "m=audio 0"),
            Audio("a=inactive"), Audio("a=sendonly"),
            Audio("a=recvonly").Replace("opus/48000/2", "PCMU/8000"),
            Audio("a=recvonly").Replace("SAVPF 111", "SAVPF 112"),
        })
        {
            bool failed = false;
            try { AudioSdpDiagnostics.LogAndValidate(rejected, false, logs.Add); } catch (InvalidDataException) { failed = true; }
            Assert(failed, "Rejected/inactive/incompatible audio must not pass codec-only negotiation checks");
        }
        Assert(logs.Any(line => line.Contains("inactive")) && !logs.Any(line => line.StartsWith("m=video")), "Audio SDP diagnostics must isolate audio and report rejection");
        return Task.CompletedTask;
    }

    public static Task RawLevels()
    {
        var meter = new AudioSignalMeter();
        byte[] raw = new byte[16];
        var samples = MemoryMarshal.Cast<byte, float>(raw.AsSpan());
        samples[0] = 0.5f; samples[1] = -0.5f; samples[2] = 0.5f; samples[3] = -0.5f;
        var format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        meter.Add(raw, format);
        var level = meter.Take();
        Assert(Math.Abs(level.RmsDb + 6.0206) < 0.001 && Math.Abs(level.PeakDb + 6.0206) < 0.001, "WASAPI float32 must be numerically measured, not read as PCM16");
        Assert(meter.Take().Samples == 0, "Meter reporting windows must reset");
        meter.Add(raw, format, silent: true);
        Assert(meter.Take().Silent, "WASAPI SILENT flag must override undefined raw buffer content");
        samples[0] = 2; samples[1] = -2; samples[2] = float.NaN; samples[3] = float.PositiveInfinity;
        level = meter.Add(raw, format);
        Assert(level.Peak == 1 && double.IsFinite(level.RmsDb), "Meter must clamp and ignore non-finite samples safely");
        return Task.CompletedTask;
    }

    public static Task Options()
    {
        Assert(SenderOptions.Parse(["--host", "tv"]).Audio, "System audio must be enabled by default");
        Assert(SenderOptions.Parse(["--host", "tv"]).RemoteVolume, "Remote volume must default to enabled");
        Assert(!SenderOptions.Parse(["--host", "tv", "--remote-volume", "false"]).RemoteVolume, "Remote volume disable option was ignored");
        Assert(!SenderOptions.Parse(["--host", "tv", "--audio", "false"]).Audio, "Video-only mode was not parsed");
        Assert(SenderOptions.Parse(["--host", "tv", "--audio", "true", "--audio-debug", "true"]).AudioDebug, "Audio debugging was not parsed");
        foreach (string invalid in new[] { "1", "yes", "no", "" })
        {
            bool rejected = false;
            try { SenderOptions.Parse(["--host", "tv", "--audio", invalid]); } catch (ArgumentException) { rejected = true; }
            Assert(rejected, "Invalid audio boolean was accepted");
        }
        return Task.CompletedTask;
    }

    public static Task Framing()
    {
        Assert(Pcm20MsFramer.SamplesPerChannel == 960, "20 ms at 48 kHz must have 960 samples per channel");
        Assert(Pcm20MsFramer.TotalSamples == 1920 && Pcm20MsFramer.FrameBytes == 3840, "Stereo PCM16 frame must occupy 3840 bytes");
        byte[] pcm = Enumerable.Range(0, 3840 * 3 + 17).Select(index => (byte)(index % 251)).ToArray();
        var frames = new List<byte[]>();
        var framer = new Pcm20MsFramer();
        int offset = 0;
        foreach (int chunk in new[] { 1, 1919, 17, 6000, pcm.Length - 7937 })
        {
            framer.Append(pcm.AsSpan(offset, chunk), frame => frames.Add(frame.ToArray()));
            offset += chunk;
        }
        Assert(offset == pcm.Length && frames.Count == 3, "Partial converter reads were lost or sent as arbitrary-sized packets");
        for (int index = 0; index < frames.Count; index++)
            Assert(frames[index].AsSpan().SequenceEqual(pcm.AsSpan(index * 3840, 3840)), "PCM order or stereo interleaving changed");
        framer.Append(new byte[3840 - 17], frame => frames.Add(frame.ToArray()));
        Assert(frames.Count == 4 && frames[3].AsSpan(0, 17).SequenceEqual(pcm.AsSpan(3840 * 3)), "Partial packet tail was not preserved");
        return Task.CompletedTask;
    }

    public static Task StereoOpus()
    {
        using var encoder = new OpusStereoEncoder();
        var decoder = OpusCodecFactory.CreateDecoder(48000, 2);
        try
        {
            byte[] pcm = new byte[3840];
            short[] decoded = new short[1920];
            double leftEnergy = 0, rightEnergy = 0;
            for (int packetIndex = 0; packetIndex < 6; packetIndex++)
            {
                var samples = MemoryMarshal.Cast<byte, short>(pcm.AsSpan());
                for (int index = 0; index < 960; index++)
                {
                    samples[index * 2] = (short)(Math.Sin((packetIndex * 960 + index) * Math.PI * 2 * 440 / 48000) * 10000);
                    samples[index * 2 + 1] = 0;
                }
                byte[] packet = encoder.Encode(pcm);
                Assert(packet.Length > 0 && packet.Length <= 1275, "Invalid Opus packet length");
                int samplesPerChannel = decoder.Decode(packet, decoded, 960);
                Assert(samplesPerChannel == 960, "Opus packet is not exactly 20 ms stereo");
                if (packetIndex == 5) for (int index = 0; index < 960; index++)
                {
                    leftEnergy += (double)decoded[index * 2] * decoded[index * 2];
                    rightEnergy += (double)decoded[index * 2 + 1] * decoded[index * 2 + 1];
                }
            }
            Assert(leftEnergy > 1000000 && rightEnergy < leftEnergy * 0.02, "Independent stereo channels were lost");
            Assert(OpusStereoEncoder.Format.ChannelCount == 2 && OpusStereoEncoder.Format.RtpClockRate == 48000, "Incorrect Opus SDP format");
        }
        finally { if (decoder is IDisposable disposable) disposable.Dispose(); }
        return Task.CompletedTask;
    }

    public static Task Conversion()
    {
        // Media Foundation is built into Windows 11; these checks do not open an audio device.
        foreach (int rate in new[] { 44100, 48000, 96000 })
        {
            var format = WaveFormat.CreateIeeeFloatWaveFormat(rate, 2);
            using var converter = new LoopbackPcmConverter(format);
            byte[] input = new byte[rate / 10 * format.BlockAlign];
            var floats = MemoryMarshal.Cast<byte, float>(input.AsSpan());
            for (int index = 0; index < floats.Length; index += 2) { floats[index] = 0.25f; floats[index + 1] = -0.5f; }
            converter.AddSamples(input);
            byte[] output = new byte[3840];
            Assert(converter.Read(output) == 3840 && converter.Read(output) == 3840, "Resampler did not produce complete 48 kHz frames");
            var samples = MemoryMarshal.Cast<byte, short>(output);
            Assert(Math.Abs(samples[1000] - 8192) < 500 && Math.Abs(samples[1001] + 16384) < 500, $"Float PCM was reinterpreted, clipped or channels changed at {rate} Hz");
            bool rejected = false;
            try { converter.AddSamples(new byte[format.BlockAlign - 1]); } catch (InvalidDataException) { rejected = true; }
            Assert(rejected, "Misaligned WASAPI sample frames must be rejected");
        }
        using (var converter = new LoopbackPcmConverter(new WaveFormat(48000, 16, 2)))
        {
            converter.AddSamples(new byte[3840 * 2], silent: true);
            var silence = new byte[3840];
            Assert(converter.Read(silence) == 3840 && silence.All(value => value == 0), "PCM16 or silent loopback buffers were handled incorrectly");
            converter.AddSamples(new byte[48000 * 4]);
            Assert(converter.BufferedDuration <= TimeSpan.FromMilliseconds(200) && converter.DroppedInputBytes > 0,
                "Capture overruns must remain bounded and discard stale samples instead of terminating audio");
        }
        using (var converter = new LoopbackPcmConverter(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)))
        {
            byte[] raw = new byte[4800 * 2 * sizeof(float)];
            var samples = MemoryMarshal.Cast<byte, float>(raw.AsSpan());
            for (int i = 0; i < samples.Length; i += 2) { samples[i] = 1.25f; samples[i + 1] = -1.25f; }
            converter.AddSamples(raw);
            byte[] pcm = new byte[3840];
            converter.Read(pcm); converter.Read(pcm);
            var converted = MemoryMarshal.Cast<byte, short>(pcm);
            Assert(converted[1000] > 32000 && converted[1001] < -32000, "Float-to-PCM16 conversion must clamp rather than wrap");
        }
        return Task.CompletedTask;
    }

    public static Task CaptureOverrun()
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        using var converter = new LoopbackPcmConverter(format);
        byte[] old = new byte[format.AverageBytesPerSecond / 10];
        var oldSamples = MemoryMarshal.Cast<byte, float>(old.AsSpan());
        oldSamples.Fill(-0.25f);
        converter.AddSamples(old);
        byte[] latest = new byte[format.AverageBytesPerSecond];
        MemoryMarshal.Cast<byte, float>(latest.AsSpan()).Fill(0.25f);
        converter.AddSamples(latest); // Simulate a delayed one-second callback; preserve the newest 200 ms.
        Assert(converter.BufferedDuration <= TimeSpan.FromMilliseconds(200) && converter.DroppedInputBytes > 0, "Oversized callback must not expand or fail the capture queue");
        byte[] pcm = new byte[3840];
        converter.Read(pcm); converter.Read(pcm);
        var samples = MemoryMarshal.Cast<byte, short>(pcm);
        Assert(samples[1000] > 7500, "Overrun handling retained old audio instead of the latest samples");
        return Task.CompletedTask;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
