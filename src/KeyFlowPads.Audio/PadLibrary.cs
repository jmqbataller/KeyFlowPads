using System.Runtime.InteropServices;
using NLayer;

namespace KeyFlowPads.Audio;

public sealed class PadLibrary
{
    public const int SampleRate = 44100;
    public const int Count = 12;
    public static string Key(int index) => Keys[index];

    private static readonly string[] Keys = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
    private readonly Pad?[] pads = new Pad[Count];
    private readonly string?[] errors = new string[Count];

    internal Pad? this[int key] => pads[key];
    public string? Error(int key) => errors[key];
    public static string RecordingName(int key) => Keys[key] + (key == 5 ? "" : " ") + "- WARM - CHURCHFRONT PADS.mp3";

    public PadLibrary(string directory)
    {
        for (int key = 0; key < Count; key++)
        {
            try { pads[key] = Load(directory, key); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                errors[key] = $"Pad {Key(key)} is unavailable. {ex.Message}";
            }
        }
    }

    private static Pad Load(string directory, int key)
    {
        string recording = Path.Combine(directory, RecordingName(key));
        using var mp3 = new MpegFile(recording);
        using var file = File.OpenRead(Path.Combine(directory, Key(key) + ".loop"));
        using var header = new BinaryReader(file);
        if (header.ReadInt32() != 0x3150464B || mp3.SampleRate != SampleRate || mp3.Channels != 2)
            throw new InvalidDataException("The recording or loop data uses an unsupported format.");

        int start = header.ReadInt32();
        int end = header.ReadInt32();
        int overlap = header.ReadInt32();
        int prime = header.ReadInt32();
        var bounds = Mp3Bounds.Detect(recording, checked((int)(mp3.Length / 8)));
        if (start < bounds.FirstFrame || end <= start || end > bounds.EndFrame ||
            overlap < 2 || overlap > 3 * SampleRate || prime != 2 * SampleRate || end - start <= 2 * overlap + prime)
            throw new InvalidDataException("The loop data contains invalid frame offsets.");

        var head = new float[(overlap + prime) * 2];
        file.ReadExactly(MemoryMarshal.AsBytes(head.AsSpan()));
        if (head.Any(sample => !float.IsFinite(sample) || Math.Abs(sample) > 4f))
            throw new InvalidDataException("The loop data contains invalid samples.");
        return new Pad(recording, start, end, overlap, head);
    }

    internal sealed record Pad(string Path, int HeadFrame, int EndFrame, int OverlapFrames, float[] Head)
    {
        internal int StartFrame => HeadFrame + OverlapFrames;
        internal int CachedFrames => Head.Length / 2;
    }
}
