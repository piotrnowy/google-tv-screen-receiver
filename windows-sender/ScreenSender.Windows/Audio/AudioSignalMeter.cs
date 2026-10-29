using System.Buffers.Binary;
using System.Runtime.InteropServices;
using NAudio.Wave;

namespace ScreenSender.Windows.Audio;

public readonly record struct AudioLevel(long Samples, double Squares, double Peak)
{
    public double RmsDb => Samples > 0 && Squares > 0 ? 10 * Math.Log10(Squares / Samples) : -120;
    public double PeakDb => Peak > 0 ? 20 * Math.Log10(Peak) : -120;
    public bool Silent => Peak < 0.00001;
}

/// <summary>Measures original numeric PCM values, independently of the conversion/encoder.</summary>
public sealed class AudioSignalMeter
{
    private readonly object _gate = new();
    private AudioLevel _level;

    public AudioLevel Add(ReadOnlySpan<byte> data, WaveFormat format, bool silent = false)
    {
        var standard = format.AsStandardWaveFormat();
        int bytes = standard.BitsPerSample / 8;
        if (bytes == 0 || data.Length % bytes != 0) throw new InvalidDataException("Misaligned audio meter input.");
        double squares = 0, peak = 0;
        if (!silent) for (int offset = 0; offset < data.Length; offset += bytes)
        {
            var sample = data.Slice(offset, bytes);
            double value = (standard.Encoding, standard.BitsPerSample) switch
            {
                (WaveFormatEncoding.IeeeFloat, 32) => MemoryMarshal.Read<float>(sample),
                (WaveFormatEncoding.IeeeFloat, 64) => MemoryMarshal.Read<double>(sample),
                (WaveFormatEncoding.Pcm, 16) => BinaryPrimitives.ReadInt16LittleEndian(sample) / 32768.0,
                (WaveFormatEncoding.Pcm, 24) => ((sample[0] | sample[1] << 8 | sample[2] << 16) << 8 >> 8) / 8388608.0,
                (WaveFormatEncoding.Pcm, 32) => BinaryPrimitives.ReadInt32LittleEndian(sample) / 2147483648.0,
                (WaveFormatEncoding.Pcm, 8) => (sample[0] - 128) / 128.0,
                _ => throw new NotSupportedException($"Unsupported diagnostic PCM format {format}"),
            };
            value = double.IsFinite(value) ? Math.Clamp(value, -1, 1) : 0;
            squares += value * value;
            peak = Math.Max(peak, Math.Abs(value));
        }
        var measured = new AudioLevel(data.Length / bytes, squares, peak);
        lock (_gate) _level = new(_level.Samples + measured.Samples, _level.Squares + squares, Math.Max(_level.Peak, peak));
        return measured;
    }

    public AudioLevel Take()
    {
        lock (_gate) { var result = _level; _level = default; return result; }
    }
}
