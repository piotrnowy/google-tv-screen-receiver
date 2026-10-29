using System.Runtime.InteropServices;
using Concentus;
using Concentus.Enums;
using SIPSorceryMedia.Abstractions;

namespace ScreenSender.Windows.Audio;

public sealed class OpusStereoEncoder : IDisposable
{
    // Concentus is already supplied by SIPSorcery 10.0.17. Its AudioEncoder wrapper
    // hardcodes mono; passing 1920 stereo samples to that wrapper would encode 40 ms mono.
    private readonly IOpusEncoder _encoder = OpusCodecFactory.CreateEncoder(48000, 2, OpusApplication.OPUS_APPLICATION_AUDIO);
    private readonly byte[] _packet = new byte[1275];
    public static AudioFormat Format => new(AudioCodecsEnum.OPUS, 111, 48000, 2,
        "minptime=10;useinbandfec=1;stereo=1;sprop-stereo=1");

    public OpusStereoEncoder()
    {
        _encoder.Bitrate = 128000;
        _encoder.Complexity = 5;
        _encoder.UseVBR = true;
        _encoder.UseConstrainedVBR = true;
        _encoder.UseInbandFEC = true;
        _encoder.PacketLossPercent = 5;
    }

    public byte[] Encode(ReadOnlySpan<byte> pcm16)
    {
        if (pcm16.Length != Pcm20MsFramer.FrameBytes)
            throw new ArgumentException("Opus requires exactly 3840 bytes of 20 ms PCM16 stereo.", nameof(pcm16));
        int length = _encoder.Encode(MemoryMarshal.Cast<byte, short>(pcm16), Pcm20MsFramer.SamplesPerChannel, _packet, _packet.Length);
        return _packet.AsSpan(0, length).ToArray();
    }

    public void Dispose() { if (_encoder is IDisposable disposable) disposable.Dispose(); }
}
