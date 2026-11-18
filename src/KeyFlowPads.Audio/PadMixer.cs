using NAudio.Wave;

namespace KeyFlowPads.Audio;

public enum Transport { Stopped, Playing, Pausing, Paused, Stopping }

public sealed record MixerStatus(Transport Transport, int Target, bool Switching, int Voices,
    long Position, long Loops, long UnderrunFrames, string? Error,
    Guid? MusicId = null, long CompletedMusicRequest = 0);

public sealed class PadMixer : ISampleProvider, IDisposable
{
    private readonly PadLibrary library;
    private const int SlotCount = PadLibrary.Count + 8;
    private readonly IPlaybackVoice?[] voices = new IPlaybackVoice?[SlotCount];
    private readonly MusicTrack?[] musicTracks = new MusicTrack?[SlotCount];
    private readonly float[] gains = new float[SlotCount];
    private readonly float[] startingGains = new float[SlotCount];
    private readonly float[] tangent = new float[SlotCount];
    private Command command = new(0, Transport.Stopped);
    private Command? applied;
    private float requestedVolume = 0.65f;
    private int requestedCrossfade = 2 * PadLibrary.SampleRate;
    private int requestedTransportFade = PadLibrary.SampleRate / 5;
    private MixerStatus status = new(Transport.Stopped, -1, false, 0, 0, 0, 0, null);
    private Transport transport;
    private int target = -1;
    private int switchFrame;
    private int switchLength;
    private float angle;
    private float switchMagnitude = 1;
    private float master;
    private float masterTarget;
    private float masterStep;
    private int masterRemaining;
    private float gate;
    private float gateStart;
    private float gateTarget;
    private int gateFrame;
    private int gateLength;
    private long underruns;
    private long musicRequest;
    private long activeMusicRequest;
    private long completedMusicRequest;
    private string? error;

    public PadMixer(PadLibrary library) => this.library = library;
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(PadLibrary.SampleRate, 2);
    public MixerStatus Status => Volatile.Read(ref status);
    public float Volume { set => Volatile.Write(ref requestedVolume, Math.Clamp(value, 0, 1)); }
    public double CrossfadeSeconds
    {
        set => Volatile.Write(ref requestedCrossfade, (int)(Math.Clamp(value, 0.5, 5) * PadLibrary.SampleRate));
    }
    public double TransportFadeSeconds
    {
        set => Volatile.Write(ref requestedTransportFade, (int)(Math.Clamp(value, 0.05, 1) * PadLibrary.SampleRate));
    }

    public bool Play(int key)
    {
        if ((uint)key >= PadLibrary.Count || library[key] is null)
            return false;
        Volatile.Write(ref command, new Command(key, Transport.Playing));
        return true;
    }

    public long Play(PreparedMusic music)
    {
        long request = Interlocked.Increment(ref musicRequest);
        Volatile.Write(ref command, new Command(-1, Transport.Playing, music, request));
        return request;
    }

    public void Pause() => Volatile.Write(ref command, new Command(-1, Transport.Paused));
    public void Stop() => Volatile.Write(ref command, new Command(-1, Transport.Stopped));

    public int Read(float[] buffer, int offset, int count)
    {
        ApplyCommand();
        float volume = Volatile.Read(ref requestedVolume);
        if (volume != masterTarget)
        {
            masterTarget = volume;
            masterRemaining = PadLibrary.SampleRate / 25;
            masterStep = (volume - master) / masterRemaining;
        }

        int end = offset + count;
        for (int i = offset; i < end; i += 2)
        {
            if (transport is Transport.Stopped or Transport.Paused)
            {
                Array.Clear(buffer, i, end - i);
                break;
            }

            // The render callback never waits for a decoder or performs disk I/O.
            float cosine = 1;
            float sine = 0;
            if (switchFrame < switchLength)
            {
                float progress = Ease(switchFrame / (float)switchLength);
                float phase = angle * progress;
                (sine, cosine) = MathF.SinCos(phase);
                float magnitude = switchMagnitude + (1 - switchMagnitude) * progress;
                sine *= magnitude;
                cosine *= magnitude;
            }

            float left = 0;
            float right = 0;
            bool missedFrame = false;
            for (int key = 0; key < voices.Length; key++)
            {
                if (voices[key] is not { } voice)
                    continue;
                if (switchFrame < switchLength)
                    gains[key] = Math.Max(0, startingGains[key] * cosine + tangent[key] * sine);
                if (voice.Remaining == 0)
                    continue;
                if (voice.Read(out float a, out float b))
                {
                    left += gains[key] * a;
                    right += gains[key] * b;
                }
                else
                    missedFrame = true;
            }
            if (missedFrame)
                underruns++;

            if (masterRemaining > 0)
                master = --masterRemaining == 0 ? masterTarget : master + masterStep;

            // Equal-power mixes can sum above full scale for correlated audio.
            buffer[i] = Limit(left * master * gate);
            buffer[i + 1] = Limit(right * master * gate);
            AdvanceEnvelopes();
            if (target >= 0 && voices[target]?.Remaining == 0)
            {
                completedMusicRequest = activeMusicRequest;
                ClearVoices();
                transport = Transport.Stopped;
                gate = 0;
                gateFrame = gateLength;
            }
        }
        PublishStatus();
        return count;
    }

    private void ApplyCommand()
    {
        Command pending = Volatile.Read(ref command);
        if (ReferenceEquals(applied, pending))
            return;
        switch (pending.Transport)
        {
            case Transport.Playing:
                int slot = pending.Music is { } music ? FindMusicSlot(music.Track.Id) : pending.Key;
                // A rapid ninth music request stays pending until the current fade frees a slot.
                if (slot < 0)
                    return;
                if (slot != target)
                {
                    if (transport is Transport.Stopped or Transport.Paused)
                        ClearVoices();
                    if (voices[slot] is null)
                    {
                        musicTracks[slot] = pending.Music?.Track;
                        voices[slot] = pending.Music is null
                            ? new BufferedPad(library[slot]!)
                            : new BufferedMusic(pending.Music);
                    }
                    BeginSwitch(slot);
                }
                activeMusicRequest = pending.Request;
                completedMusicRequest = 0;
                if (transport != Transport.Playing)
                {
                    transport = Transport.Playing;
                    MoveGate(1);
                }
                error = null;
                break;
            case Transport.Paused when transport == Transport.Playing:
                transport = Transport.Pausing;
                MoveGate(0);
                break;
            case Transport.Stopped when transport == Transport.Paused:
                ClearVoices();
                transport = Transport.Stopped;
                break;
            case Transport.Stopped when transport is Transport.Playing or Transport.Pausing:
                transport = Transport.Stopping;
                MoveGate(0);
                break;
        }
        applied = pending;
    }

    private int FindMusicSlot(Guid id)
    {
        int free = -1;
        for (int i = PadLibrary.Count; i < SlotCount; i++)
        {
            if (musicTracks[i]?.Id == id && voices[i]?.Remaining > 0)
                return i;
            if (free < 0 && (voices[i] is null || (i != target && voices[i]!.Remaining == 0)))
                free = i;
        }
        if (free < 0 && transport == Transport.Paused)
            return PadLibrary.Count;
        if (free >= 0 && voices[free] is not null)
            CancelVoice(free);
        return free;
    }

    private void BeginSwitch(int key)
    {
        target = key;
        double power = 0;
        int strongest = -1;
        for (int i = 0; i < gains.Length; i++)
        {
            if (voices[i]?.Remaining == 0)
                gains[i] = 0;
            power += gains[i] * gains[i];
            if (i != key && gains[i] > 0 && (strongest < 0 || gains[i] > gains[strongest]))
                strongest = i;
        }
        if (power < 1e-10)
        {
            FinishSwitch();
            return;
        }

        // Rotate from the current gain vector to the newest key on the unit sphere.
        // Retargeting keeps the instantaneous mixture and never creates a second voice for a key.
        float magnitude = (float)Math.Sqrt(power);
        switchMagnitude = magnitude;
        float dot = Math.Clamp(gains[key] / magnitude, 0, 1);
        angle = MathF.Acos(dot);
        if (angle < 0.0001f)
        {
            FinishSwitch();
            return;
        }
        float inverseSine = 1 / MathF.Sin(angle);
        for (int i = 0; i < gains.Length; i++)
        {
            startingGains[i] = gains[i] / magnitude;
            tangent[i] = ((i == key ? 1 : 0) - dot * startingGains[i]) * inverseSine;
        }
        switchFrame = 0;
        long requested = Volatile.Read(ref requestedCrossfade);
        long outgoing = strongest >= 0 ? voices[strongest]!.Remaining : long.MaxValue;
        switchLength = (int)Math.Max(1, Math.Min(requested, Math.Min(outgoing, voices[key]!.Remaining)));
    }

    private void FinishSwitch()
    {
        switchFrame = switchLength = 0;
        for (int i = 0; i < voices.Length; i++)
        {
            if (i != target)
                CancelVoice(i);
            gains[i] = i == target ? 1 : 0;
        }
    }

    private void MoveGate(float destination)
    {
        gateStart = gate;
        gateTarget = destination;
        gateFrame = 0;
        gateLength = Volatile.Read(ref requestedTransportFade);
    }

    private void AdvanceEnvelopes()
    {
        if (switchFrame < switchLength && ++switchFrame == switchLength)
            FinishSwitch();
        if (gateFrame >= gateLength)
            return;
        gate = gateStart + (gateTarget - gateStart) * Ease(++gateFrame / (float)gateLength);
        if (gateFrame != gateLength)
            return;
        if (transport == Transport.Pausing)
            transport = Transport.Paused;
        else if (transport == Transport.Stopping)
        {
            ClearVoices();
            transport = Transport.Stopped;
        }
    }

    private void PublishStatus()
    {
        int active = 0;
        for (int i = 0; i < voices.Length; i++)
        {
            if (voices[i] is not { } voice)
                continue;
            active++;
            if (voice.Error is { } message)
                error ??= $"{musicTracks[i]?.Title ?? (i < PadLibrary.Count ? PadLibrary.Key(i) : "Music")}: {message}";
        }
        IPlaybackVoice? current = target < 0 ? null : voices[target];
        Volatile.Write(ref status, new MixerStatus(transport, target < PadLibrary.Count ? target : -1,
            switchFrame < switchLength, active, current?.Position ?? 0, current?.Loops ?? 0, underruns, error,
            target < 0 ? null : musicTracks[target]?.Id, completedMusicRequest));
    }

    private void CancelVoice(int key)
    {
        voices[key]?.Cancel();
        voices[key] = null;
        musicTracks[key] = null;
        gains[key] = 0;
    }

    private void ClearVoices()
    {
        for (int i = 0; i < voices.Length; i++)
            CancelVoice(i);
        Array.Clear(gains);
        target = -1;
        switchFrame = switchLength = 0;
    }

    public void Dispose() => ClearVoices();

    private static float Ease(float value) => value * value * (3 - 2 * value);

    // A soft knee only affects rare peaks. The usual pad range remains linear.
    private static float Limit(float sample)
    {
        float absolute = Math.Abs(sample);
        if (absolute <= 0.90f)
            return sample;
        float excess = absolute - 0.90f;
        return MathF.CopySign(0.90f + 0.10f * excess / (excess + 0.10f), sample);
    }

    private sealed record Command(int Key, Transport Transport, PreparedMusic? Music = null, long Request = 0);
}
