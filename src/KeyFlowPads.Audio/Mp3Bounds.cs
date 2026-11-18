using System.Globalization;
using System.Text;

namespace KeyFlowPads.Audio;

internal readonly record struct Mp3Bounds(int FirstFrame, int EndFrame)
{
    internal static Mp3Bounds Detect(string fileName, int decodedFrames)
    {
        using var file = File.OpenRead(fileName);
        Span<byte> header = stackalloc byte[10];
        file.ReadExactly(header);
        // NLayer handles Xing/LAME and newer ID3 text tags. These recordings use
        // Apple's ID3v2.2 COM frame for iTunSMPB, which the decoder leaves intact.
        if (!header[..4].SequenceEqual("ID3\u0002"u8) || header[5] != 0)
            return new(0, decodedFrames);
        int remaining = header[6] << 21 | header[7] << 14 | header[8] << 7 | header[9];
        Span<byte> frame = stackalloc byte[6];
        Span<byte> comment = stackalloc byte[256];
        while (remaining >= frame.Length)
        {
            file.ReadExactly(frame);
            remaining -= frame.Length;
            int size = frame[3] << 16 | frame[4] << 8 | frame[5];
            if (size == 0)
                break;
            if (size > remaining || size > file.Length - file.Position)
                throw new InvalidDataException("MP3 metadata contains an invalid frame.");
            remaining -= size;
            if (frame[..3].SequenceEqual("COM"u8) && size is > 12 and <= 256)
            {
                file.ReadExactly(comment[..size]);
                if (comment[0] == 0 && ReadGaplessComment(comment[4..size], decodedFrames) is { } bounds)
                    return bounds;
            }
            else
                file.Position += size;
        }
        return new(0, decodedFrames);
    }

    private static Mp3Bounds? ReadGaplessComment(ReadOnlySpan<byte> comment, int decodedFrames)
    {
        const int synthesisDelay = 529;
        if (!comment.StartsWith("iTunSMPB\0"u8))
            return null;
        string text = Encoding.ASCII.GetString(comment[9..]);
        string[] fields = text.TrimEnd('\0').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 4 || !ParseHex(fields[1], out int lead) ||
            !ParseHex(fields[2], out int padding) || !ParseHex(fields[3], out int expected) ||
            lead < 0 || padding < 0 || expected <= 0)
            throw new InvalidDataException("MP3 gapless metadata is invalid.");
        if (decodedFrames == expected)
            return new(0, decodedFrames);
        if ((long)lead + padding + expected != decodedFrames)
            throw new InvalidDataException("MP3 gapless metadata does not match the decoded duration.");
        int decoderDelay = Math.Min(synthesisDelay, padding);
        return new(lead + decoderDelay, decodedFrames - (padding - decoderDelay));
    }

    private static bool ParseHex(string text, out int value) =>
        int.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
}
