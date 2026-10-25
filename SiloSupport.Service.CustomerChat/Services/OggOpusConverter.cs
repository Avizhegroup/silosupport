using Concentus;
using Concentus.Oggfile;

namespace Silo.Bot.Support.Services;

/// <summary>Converts Ogg/Opus voice notes (Bale format) to 16-bit PCM WAV, which the AI core accepts.</summary>
public static class OggOpusConverter
{
    private const int SampleRate = 48000;
    private const int Channels = 1;

    public static byte[] ToWav(byte[] oggOpus)
    {
        var decoder = OpusCodecFactory.CreateDecoder(SampleRate, Channels);

        using var input = new MemoryStream(oggOpus);
        var reader = new OpusOggReadStream(decoder, input);

        using var pcm = new MemoryStream();
        while (reader.HasNextPacket)
        {
            var packet = reader.DecodeNextPacket();
            if (packet is null)
                continue;

            foreach (var sample in packet)
            {
                pcm.WriteByte((byte)(sample & 0xFF));
                pcm.WriteByte((byte)((sample >> 8) & 0xFF));
            }
        }

        using var output = new MemoryStream();
        using (var writer = new BinaryWriter(output, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            var dataLength = (int)pcm.Length;
            writer.Write("RIFF"u8.ToArray());
            writer.Write(36 + dataLength);
            writer.Write("WAVE"u8.ToArray());
            writer.Write("fmt "u8.ToArray());
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)Channels);
            writer.Write(SampleRate);
            writer.Write(SampleRate * Channels * 2);
            writer.Write((short)(Channels * 2));
            writer.Write((short)16);
            writer.Write("data"u8.ToArray());
            writer.Write(dataLength);
            writer.Write(pcm.GetBuffer(), 0, dataLength);
        }

        return output.ToArray();
    }
}
