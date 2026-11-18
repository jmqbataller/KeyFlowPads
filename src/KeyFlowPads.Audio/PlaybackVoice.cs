namespace KeyFlowPads.Audio;

internal interface IPlaybackVoice
{
    long Position { get; }
    long Loops { get; }
    long Remaining { get; }
    string? Error { get; }
    bool Read(out float left, out float right);
    void Cancel();
}

// Single producer and single consumer. Only the output callback advances playback.
internal sealed class AudioRing
{
    private const int Capacity = 1 << 17;
    private readonly float[] samples = new float[Capacity];
    private long read;
    private long written;
    private int cancelled;

    internal int Available => (int)(Volatile.Read(ref written) - read);
    internal bool Cancelled => Volatile.Read(ref cancelled) != 0;
    internal void Cancel() => Volatile.Write(ref cancelled, 1);

    internal bool Read(out float left, out float right)
    {
        if (read == Volatile.Read(ref written))
        {
            left = right = 0;
            return false;
        }
        left = samples[(int)(read & (Capacity - 1))];
        right = samples[(int)((read + 1) & (Capacity - 1))];
        Volatile.Write(ref read, read + 2);
        return true;
    }

    internal bool WaitForSpace(int count)
    {
        while (Capacity - (written - Volatile.Read(ref read)) < count && !Cancelled)
            Thread.Sleep(8);
        return !Cancelled;
    }

    internal void Write(float[] source, int count)
    {
        int index = (int)(written & (Capacity - 1));
        int beforeWrap = Math.Min(count, Capacity - index);
        Array.Copy(source, 0, samples, index, beforeWrap);
        Array.Copy(source, beforeWrap, samples, 0, count - beforeWrap);
        Volatile.Write(ref written, written + count);
    }
}
