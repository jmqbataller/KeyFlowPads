using System.Diagnostics;
using KeyFlowPads.Audio;

internal static partial class Checks
{
    internal static void Render(string directory)
    {
        PadLibrary library = LoadAll(directory);
        Parallel.For(0, PadLibrary.Count, new ParallelOptions { MaxDegreeOfParallelism = 4 },
            key => CheckLoop(library[key]!, PadLibrary.Key(key)));
        CheckTransport(library);
    }

    private static void CheckLoop(PadLibrary.Pad asset, string key)
    {
        const int block = 1024;
        const int window = PadLibrary.SampleRate / 10;
        var voice = new BufferedPad(asset);
        var deadline = Stopwatch.StartNew();
        int afterWrap = window;
        double minimum = double.PositiveInfinity;
        double power = 0;
        int inWindow = 0;
        float finalLeft = 0, finalRight = 0;
        try
        {
            while (voice.Loops < 2 || afterWrap > 0)
            {
                Assert(voice.Error is null, $"Pad {key}: {voice.Error}");
                Assert(deadline.Elapsed < TimeSpan.FromMinutes(3), $"Pad {key}: timed out.");
                int available = Math.Min(voice.AvailableFrames, block);
                if (available == 0)
                {
                    Thread.Sleep(1);
                    continue;
                }

                for (int i = 0; i < available; i++)
                {
                    bool atLast = voice.Position == asset.EndFrame - 1;
                    bool atFirst = voice.Loops > 0 && voice.Position == asset.StartFrame;
                    bool seam = voice.Position >= asset.EndFrame - asset.OverlapFrames ||
                        voice.Loops > 0 && voice.Position < asset.StartFrame + window;
                    Assert(voice.Read(out float left, out float right), $"Pad {key}: buffered frame was missing.");
                    if (atLast)
                    {
                        finalLeft = left;
                        finalRight = right;
                    }
                    if (atFirst)
                    {
                        int start = asset.OverlapFrames * 2;
                        Assert(Math.Abs(finalLeft - asset.Head[start - 2]) < 1e-6 &&
                            Math.Abs(finalRight - asset.Head[start - 1]) < 1e-6, $"Pad {key}: final frame did not match the prepared head.");
                        Assert(Math.Abs(left - asset.Head[start]) < 1e-6 &&
                            Math.Abs(right - asset.Head[start + 1]) < 1e-6, $"Pad {key}: wrap added or skipped samples.");
                    }
                    if (seam)
                    {
                        power += left * left + right * right;
                        if (++inWindow == window)
                        {
                            minimum = Math.Min(minimum, Math.Sqrt(power / (2 * inWindow)));
                            power = 0;
                            inWindow = 0;
                        }
                    }
                    if (voice.Loops == 2 && !atLast && --afterWrap == 0)
                        break;
                }
            }
            Assert(minimum > 0.06, $"Pad {key}: seam fell below {-24.4:F1} dBFS over 100 ms.");
            Console.WriteLine($"{key,-3} real decode and two sample-contiguous wraps | {20 * Math.Log10(minimum):F1} dBFS minimum 100ms seam RMS");
        }
        finally { voice.Cancel(); }
    }

    private static void CheckTransport(PadLibrary library)
    {
        using var mixer = new PadMixer(library) { Volume = 1 };
        var block = new float[PadLibrary.SampleRate / 50 * 2];
        float peak = 0;
        int maximumVoices = 0;

        void RenderFor(int milliseconds, bool continuous = false)
        {
            var time = Stopwatch.StartNew();
            for (int cycle = 0; cycle < milliseconds / 20; cycle++)
            {
                Assert(mixer.Read(block, 0, block.Length) == block.Length, "Render count changed.");
                Assert(mixer.Status.Error is null, mixer.Status.Error ?? "");
                maximumVoices = Math.Max(maximumVoices, mixer.Status.Voices);
                double power = 0;
                foreach (float value in block)
                {
                    Assert(float.IsFinite(value), "Non-finite audio sample.");
                    peak = Math.Max(peak, Math.Abs(value));
                    power += value * value;
                }
                if (continuous)
                    Assert(Math.Sqrt(power / block.Length) > 0.01, "Detected 20ms of dead air.");
                int wait = (cycle + 1) * 20 - (int)time.ElapsedMilliseconds;
                if (wait > 0)
                    Thread.Sleep(wait);
            }
        }

        Assert(mixer.Play(0), "C failed to start.");
        RenderFor(400);
        Assert(mixer.Status is { Transport: Transport.Playing, Target: 0, Voices: 1 }, "C did not settle.");
        long position = mixer.Status.Position;
        mixer.Play(0);
        RenderFor(100, true);
        Assert(mixer.Status.Position - position == PadLibrary.SampleRate / 10, "Reselecting C reset playback.");

        (int Out, int In)[] transitions = [(0, 7), (7, 2), (2, 9), (9, 4), (4, 11),
            (6, 1), (1, 6), (10, 3), (3, 8), (5, 0)];
        foreach (var (outgoing, incoming) in transitions)
        {
            if (mixer.Status.Target != outgoing)
            {
                mixer.Play(outgoing);
                RenderFor(2040, true);
            }
            mixer.Play(incoming);
            RenderFor(1000, true);
            Assert(mixer.Status.Target == incoming && mixer.Status.Switching && mixer.Status.Voices == 2,
                $"{PadLibrary.Key(outgoing)} → {PadLibrary.Key(incoming)} did not overlap.");
            RenderFor(1020, true);
            Assert(!mixer.Status.Switching && mixer.Status.Voices == 1, "Outgoing voice survived its fade.");
            Console.WriteLine($"{PadLibrary.Key(outgoing)} → {PadLibrary.Key(incoming)} | two-second overlap and continuous output");
        }

        foreach (int key in new[] { 7, 2, 9, 6, 1, 11, 4, 10, 3, 8, 5, 9 })
        {
            mixer.Play(key);
            RenderFor(60, true);
        }
        mixer.Volume = 0.7f;
        mixer.CrossfadeSeconds = 0.5;
        mixer.Play(7);
        RenderFor(540, true);
        Assert(mixer.Status is { Target: 7, Voices: 1, Switching: false }, "Rapid switching did not resolve to G.");
        Assert(maximumVoices <= PadLibrary.Count, "Rapid input created too many voices.");
        mixer.Pause();
        RenderFor(260);
        position = mixer.Status.Position;
        Assert(mixer.Status.Transport == Transport.Paused && block.All(v => v == 0), "Pause did not end in silence.");
        RenderFor(220);
        Assert(mixer.Status.Position == position, "Pause advanced the playback position.");
        mixer.Play(7);
        RenderFor(260);
        Assert(mixer.Status.Position - position == 13 * block.Length / 2, "Resume did not continue the paused pad.");
        mixer.Stop();
        RenderFor(260);
        Assert(mixer.Status is { Transport: Transport.Stopped, Voices: 0, Position: 0 } && block.All(v => v == 0),
            "Stop did not clear and silence playback.");
        Assert(mixer.Status.UnderrunFrames == 0, "The render checks encountered decoder starvation.");
        Assert(peak < 1, "Playback clipped.");
        Console.WriteLine($"Rapid input, volume, pause/resume, stop | maximum {maximumVoices} voices | output peak {20 * Math.Log10(peak):F2} dBFS | zero decoder underruns");
    }
}
