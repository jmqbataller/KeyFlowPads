using NAudio.Wave;

namespace KeyFlowPads.Audio;

internal sealed class BufferedMusic : IPlaybackVoice
{
    private readonly PreparedMusic music;
    private readonly AudioRing ring = new();
    private readonly int endFade;
    private int primeOffset;
    private string? error;

    internal BufferedMusic(PreparedMusic music)
    {
        this.music = music;
        endFade = (int)Math.Min(PadLibrary.SampleRate / 10, (music.Track.EndFrame - music.Track.FirstFrame) / 4);
        if (music.Head.Length / 2 < Remaining)
            _ = Task.Factory.StartNew(Produce, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public long Position { get; private set; }
    public long Loops => 0;
    public long Remaining => music.Track.EndFrame - music.Track.FirstFrame - Position;
    public string? Error => Volatile.Read(ref error);
    public void Cancel() => ring.Cancel();

    public bool Read(out float left, out float right)
    {
        if (primeOffset < music.Head.Length)
        {
            left = music.Head[primeOffset++];
            right = music.Head[primeOffset++];
        }
        else if (!ring.Read(out left, out right))
            return false;

        if (Remaining <= endFade)
        {
            float progress = (Remaining - 1) / (float)endFade;
            float gain = progress * progress * (3 - 2 * progress);
            left *= gain;
            right *= gain;
        }
        Position++;
        return true;
    }

    private void Produce()
    {
        try
        {
            using var reader = MusicLibrary.OpenAudio(music.Path);
            long frame = music.Track.FirstFrame + music.Head.Length / 2;
            reader.Position = frame * reader.WaveFormat.BlockAlign;
            var samples = reader.ToSampleProvider();
            var buffer = new float[4096];
            while (frame < music.Track.EndFrame)
            {
                int count = (int)Math.Min(buffer.Length, (music.Track.EndFrame - frame) * 2);
                if (!ring.WaitForSpace(count))
                    return;
                MusicLibrary.ReadExactly(samples, buffer, count);
                ring.Write(buffer, count);
                frame += count / 2;
            }
        }
        catch (Exception ex) { Volatile.Write(ref error, ex.Message); }
    }
}
