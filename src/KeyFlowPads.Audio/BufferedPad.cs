using NLayer;

namespace KeyFlowPads.Audio;

// A single producer decodes ahead. Only the render thread consumes the ring and moves Position.
internal sealed class BufferedPad : IPlaybackVoice
{
    private const int Block = 4096;
    private readonly PadLibrary.Pad pad;
    private readonly AudioRing ring = new();
    private int primeOffset;
    private string? error;

    public long Position { get; private set; }
    public long Loops { get; private set; }
    public long Remaining => long.MaxValue;
    public string? Error => Volatile.Read(ref error);

    internal BufferedPad(PadLibrary.Pad pad)
    {
        this.pad = pad;
        Position = pad.StartFrame;
        primeOffset = pad.OverlapFrames * 2;
        _ = Task.Factory.StartNew(Produce, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public void Cancel() => ring.Cancel();
    internal int AvailableFrames => (ring.Available + pad.Head.Length - primeOffset) / 2;

    public bool Read(out float left, out float right)
    {
        if (primeOffset < pad.Head.Length)
        {
            left = pad.Head[primeOffset++];
            right = pad.Head[primeOffset++];
        }
        else if (!ring.Read(out left, out right))
            return false;

        if (++Position == pad.EndFrame)
        {
            Position = pad.StartFrame;
            Loops++;
        }
        return true;
    }

    private void Produce()
    {
        var scratch = new float[Block];
        bool first = true;
        try
        {
            using var stream = File.OpenRead(pad.Path);
            using var decoder = new MpegFile(stream);
            while (!ring.Cancelled)
            {
                if (!first)
                    decoder.Position = 0;
                int skip = (pad.HeadFrame + (first ? pad.CachedFrames : pad.OverlapFrames)) * 2;
                first = false;
                while (skip > 0 && !ring.Cancelled)
                {
                    int count = Math.Min(skip, Block);
                    DecodeExactly(decoder, scratch, count);
                    skip -= count;
                }

                int frame = checked((int)(decoder.Position / 8));
                while (frame < pad.EndFrame)
                {
                    int count = Math.Min(Block, (pad.EndFrame - frame) * 2);
                    if (!ring.WaitForSpace(count))
                        return;

                    DecodeExactly(decoder, scratch, count);
                    BlendTail(scratch, frame, count);
                    ring.Write(scratch, count);
                    frame += count / 2;
                }
            }
        }
        catch (Exception ex)
        {
            Volatile.Write(ref error, ex.Message);
        }
    }

    private void BlendTail(float[] samples, int frame, int count)
    {
        int overlapStart = pad.EndFrame - pad.OverlapFrames;
        for (int i = Math.Max(0, overlapStart - frame) * 2; i < count; i += 2)
        {
            int offset = frame + i / 2 - overlapStart;
            float mix = 0.5f - 0.5f * MathF.Cos(MathF.PI * offset / (pad.OverlapFrames - 1));
            samples[i] += (pad.Head[offset * 2] - samples[i]) * mix;
            samples[i + 1] += (pad.Head[offset * 2 + 1] - samples[i + 1]) * mix;
        }
    }

    private static void DecodeExactly(MpegFile decoder, float[] scratch, int count)
    {
        if (decoder.ReadSamples(scratch, 0, count) != count)
            throw new EndOfStreamException("The recording ended before its prepared loop point.");
    }
}
