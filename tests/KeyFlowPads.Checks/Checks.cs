using System.Diagnostics;
using System.Runtime.InteropServices;
using KeyFlowPads.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;

internal static partial class Checks
{
    internal static void Soak(string directory, int seconds)
    {
        var stopwatch = Stopwatch.StartNew();
        PadLibrary library = LoadAll(directory);
        Console.WriteLine($"Loaded twelve pads in {stopwatch.Elapsed.TotalMilliseconds:F0}ms.");
        using var mixer = new PadMixer(library) { Volume = 0.10f };
        using var output = new PadOutput(mixer, "");
        using var devices = new MMDeviceEnumerator();
        using var endpoint = PadOutput.OpenDevice(devices, "");
        using var client = endpoint.AudioClient;
        WaveFormat format = client.MixFormat;
        if (format.BitsPerSample != 32)
            throw new InvalidOperationException($"This monitor expects a 32-bit IEEE-float endpoint. Found {format}.");
        client.Initialize(AudioClientShareMode.Shared, AudioClientStreamFlags.Loopback, 10_000_000, 0, format, Guid.Empty);
        using AudioCaptureClient capture = client.AudioCaptureClient;
        client.Start();
        var buffer = new float[format.SampleRate * format.Channels];
        using var process = Process.GetCurrentProcess();
        DateTime started = DateTime.Now;
        Console.WriteLine($"SOAK START {started:O} | {seconds}s | {output.DeviceName} | {format.SampleRate}Hz {format.Channels}ch");
        Assert(mixer.Play(0), "C must play.");
        stopwatch.Restart();
        int nextReport = 60;
        long captured = 0;
        int discontinuities = 0;
        long silentRun = 0;
        long longestSilence = 0;
        long firstSound = -1;
        long previousCaptureEnd = -1;
        double lowestLevel = double.PositiveInfinity;
        float peak = 0;
        double energy = 0;
        int levelsCount = 0;
        while (stopwatch.Elapsed.TotalSeconds < seconds)
        {
            while (capture.GetNextPacketSize() != 0)
            {
                IntPtr data = capture.GetBuffer(out int frames, out var flags, out long position, out _);
                try
                {
                    if (stopwatch.Elapsed.TotalSeconds > 0.5 &&
                        ((flags & AudioClientBufferFlags.DataDiscontinuity) != 0 ||
                         (previousCaptureEnd >= 0 && position != previousCaptureEnd)))
                        discontinuities++;
                    previousCaptureEnd = position + frames;

                    int samples = frames * format.Channels;
                    if ((flags & AudioClientBufferFlags.Silent) != 0)
                        Array.Clear(buffer, 0, samples);
                    else
                        Marshal.Copy(data, buffer, 0, samples);

                    if (firstSound < 0 && buffer.AsSpan(0, samples).ContainsAnyExcept(0f))
                        firstSound = stopwatch.ElapsedMilliseconds;
                    if (stopwatch.Elapsed.TotalSeconds < 0.5)
                        continue;
                    captured += frames;
                    for (int i = 0; i < samples; i += format.Channels)
                    {
                        float framePeak = 0;
                        for (int channel = 0; channel < format.Channels; channel++)
                        {
                            float value = buffer[i + channel];
                            framePeak = Math.Max(framePeak, Math.Abs(value));
                            energy += value * value;
                        }
                        peak = Math.Max(peak, framePeak);
                        silentRun = framePeak <= 0.00001 ? silentRun + 1 : 0;
                        longestSilence = Math.Max(longestSilence, silentRun);
                        if (++levelsCount != format.SampleRate / 10)
                            continue;
                        lowestLevel = Math.Min(lowestLevel, Math.Sqrt(energy / (levelsCount * format.Channels)));
                        energy = 0;
                        levelsCount = 0;
                    }
                }
                finally { capture.ReleaseBuffer(frames); }
            }

            if (output.Error is { } failure)
                throw new InvalidOperationException(failure);
            if (mixer.Status.Error is { } decodeFailure)
                throw new InvalidOperationException(decodeFailure);
            if (stopwatch.Elapsed.TotalSeconds >= nextReport)
            {
                process.Refresh();
                Console.WriteLine($"{nextReport}s | wraps {mixer.Status.Loops} | decoder underrun frames {mixer.Status.UnderrunFrames} | capture discontinuities {discontinuities} | working set {process.WorkingSet64 / 1048576.0:F1} MiB | CPU {process.TotalProcessorTime.TotalSeconds:F1}s");
                nextReport += 60;
            }
            Thread.Sleep(4);
        }
        client.Stop();
        Console.WriteLine($"SOAK END {DateTime.Now:O} | C {stopwatch.Elapsed.TotalSeconds:F2}s | captured {captured / (double)format.SampleRate:F2}s | first nonzero packet {firstSound}ms | longest near-zero run {longestSilence * 1000.0 / format.SampleRate:F3}ms | minimum 100ms RMS {20 * Math.Log10(lowestLevel):F1} dBFS | peak {20 * Math.Log10(peak):F1} dBFS | wraps {mixer.Status.Loops} | underrun frames {mixer.Status.UnderrunFrames} | capture discontinuities {discontinuities}");
        Assert(mixer.Status.Transport == Transport.Playing && mixer.Status.Target == 0, "C stopped during the soak.");
        if (seconds >= 1800)
            Assert(mixer.Status.Loops >= 3, "C did not wrap three times.");
        Assert(mixer.Status.UnderrunFrames == 0, "The decoder starved during playback.");
        Assert(discontinuities == 0, "The endpoint capture reported discontinuities.");
        Assert(firstSound >= 0 && longestSilence < format.SampleRate / 50 && lowestLevel > 0.0001,
            "WASAPI loopback detected silence.");
        Assert(captured / (double)format.SampleRate > seconds - 2, "The endpoint returned too little captured audio.");
    }

    private static PadLibrary LoadAll(string directory)
    {
        var library = new PadLibrary(directory);
        for (int key = 0; key < PadLibrary.Count; key++)
            Assert(library.Error(key) is null, library.Error(key) ?? "");
        return library;
    }

    private static void Assert(bool passed, string reason)
    {
        if (!passed)
            throw new InvalidOperationException(reason);
    }
}
