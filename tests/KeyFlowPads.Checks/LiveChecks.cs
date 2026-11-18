using System.Diagnostics;
using System.Runtime.InteropServices;
using KeyFlowPads.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;

internal static partial class Checks
{
    internal static void Live(string directory, PreparedMusic? firstSong = null, PreparedMusic? nextSong = null)
    {
        PadLibrary library = LoadAll(directory);
        using var mixer = new PadMixer(library) { Volume = 0.15f };
        using var output = new PadOutput(mixer, "");
        using var ready = new AutoResetEvent(false);
        using var client = ProcessLoopback.Open();
        var format = new WaveFormat(PadLibrary.SampleRate, 16, 2);
        client.Initialize(AudioClientShareMode.Shared, AudioClientStreamFlags.Loopback |
            AudioClientStreamFlags.EventCallback | AudioClientStreamFlags.AutoConvertPcm, 0, 0, format, Guid.Empty);
        client.SetEventHandle(ready.SafeWaitHandle.DangerousGetHandle());
        using AudioCaptureClient capture = client.AudioCaptureClient;
        client.Start();
        var samples = new short[client.BufferSize * format.Channels];
        int discontinuities = 0;
        int nonzeroPackets = 0;
        int packets = 0;
        double quietPeak = 0;
        var latencies = new List<double>();

        void CheckFor(int milliseconds, bool? shouldBeAudible = null)
        {
            var watch = Stopwatch.StartNew();
            int packetStart = packets;
            while (watch.ElapsedMilliseconds < milliseconds)
            {
                while (capture.GetNextPacketSize() != 0)
                {
                    IntPtr data = capture.GetBuffer(out int frames, out var flags, out _, out _);
                    try
                    {
                        int count = frames * format.Channels;
                        if (shouldBeAudible == true && (flags & AudioClientBufferFlags.DataDiscontinuity) != 0)
                            discontinuities++;
                        double power = 0;
                        if ((flags & AudioClientBufferFlags.Silent) == 0)
                        {
                            Marshal.Copy(data, samples, 0, count);
                            for (int i = 0; i < count; i++)
                            {
                                double value = samples[i] / 32768.0;
                                power += value * value;
                            }
                        }
                        double rms = Math.Sqrt(power / count);
                        packets++;
                        if (rms > 0.0001)
                            nonzeroPackets++;
                        if (shouldBeAudible == false)
                            quietPeak = Math.Max(quietPeak, rms);
                        if (shouldBeAudible is { } expected)
                            Assert(expected ? rms > 0.001 : rms < 0.0001,
                                $"Unexpected process-loopback RMS of {rms:F6} (expected audible {expected}).");
                    }
                    finally { capture.ReleaseBuffer(frames); }
                }
                Assert(output.Error is null, output.Error ?? "");
                Assert(mixer.Status.Error is null, mixer.Status.Error ?? "");
                ready.WaitOne(5);
            }
            if (shouldBeAudible == true)
                Assert(packets > packetStart, "No endpoint audio packets during active playback.");
        }

        void Select(int key)
        {
            var watch = Stopwatch.StartNew();
            Assert(mixer.Play(key), $"Could not select {PadLibrary.Key(key)}.");
            while (mixer.Status.Target != key || mixer.Status.Transport != Transport.Playing)
            {
                Assert(watch.ElapsedMilliseconds < 150, "The output took too long to process a key command.");
                Thread.Sleep(1);
            }
            latencies.Add(watch.Elapsed.TotalMilliseconds);
        }

        CheckFor(250);
        Select(0);
        CheckFor(350);
        CheckFor(700, true);
        Select(7);
        CheckFor(840, true);
        Assert(mixer.Status is { Voices: 2, Switching: true }, "C → G had no live overlap.");
        CheckFor(2200, true);
        Assert(mixer.Status is { Target: 7, Voices: 1, Switching: false }, "G did not sustain after the transition.");
        Select(2);
        CheckFor(300, true);
        Assert(mixer.Status is { Voices: 2, Switching: true }, "G → D had no live overlap.");
        foreach (int next in new[] { 0, 7, 2, 9 })
        {
            Select(next);
            CheckFor(75, true);
        }
        CheckFor(2250, true);
        Assert(mixer.Status is { Target: 9, Voices: 1 }, "Rapid rerouting failed on the device.");
        mixer.Pause();
        CheckFor(350);
        long position = mixer.Status.Position;
        CheckFor(450, false);
        Assert(mixer.Status.Transport == Transport.Paused && mixer.Status.Position == position, "Pause failed on the device.");
        Select(9);
        CheckFor(450);
        CheckFor(650, true);
        Assert(mixer.Status.Position > position, "Resume failed on the device.");
        mixer.Stop();
        CheckFor(350);
        CheckFor(400, false);
        Assert(mixer.Status is { Voices: 0, Position: 0, Transport: Transport.Stopped }, "Stop failed on the device.");

        if (firstSong is not null && nextSong is not null)
        {
            Select(0);
            CheckFor(400);
            CheckFor(240, true);
            mixer.Play(firstSong);
            CheckFor(800, true);
            Assert(mixer.Status is { Voices: 2, Switching: true } && mixer.Status.MusicId == firstSong.Track.Id,
                "The pad and imported song did not overlap on the device.");
            CheckFor(1250, true);
            mixer.Play(nextSong);
            CheckFor(800, true);
            Assert(mixer.Status is { Voices: 2, Switching: true } && mixer.Status.MusicId == nextSong.Track.Id,
                "The two imported songs did not overlap on the device.");
            CheckFor(1250, true);
            Select(7);
            CheckFor(2050, true);
            Assert(mixer.Status is { Target: 7, Voices: 1, Switching: false }, "The mixer did not return to the G pad.");
            Console.WriteLine("Process-isolated Windows output passed pad → music → music → pad.");
        }
        Assert(mixer.Status.UnderrunFrames == 0 && discontinuities == 0,
            $"Live check reported {discontinuities} capture discontinuities during active audio and {mixer.Status.UnderrunFrames} decoder underrun frames.");
        client.Stop();
        Console.WriteLine($"WASAPI {output.DeviceName}, process-isolated capture | C → G → D, rapid rerouting, pause/resume, stop passed.");
        Console.WriteLine($"{nonzeroPackets} audible packets. Zero capture discontinuity flags during active audio and zero decoder underrun frames.");
        Console.WriteLine($"Quiet-window packet RMS peak {20 * Math.Log10(quietPeak):F1} dBFS.");
        Console.WriteLine($"Command to first rendered block: median {latencies.Order().ElementAt(latencies.Count / 2):F1} ms, max {latencies.Max():F1} ms over {latencies.Count} key commands.");
    }
}
