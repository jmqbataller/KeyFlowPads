using System.Diagnostics;
using KeyFlowPads.Audio;
using NAudio.Wave;

internal static partial class Checks
{
    internal static void Music(string directory)
    {
        DirectoryInfo work = Directory.CreateTempSubdirectory("KeyFlowPads-music-test-");
        try
        {
            string catalogPath = Path.Combine(work.FullName, "library");
            var library = new MusicLibrary(catalogPath);
            string original = Path.Combine(work.FullName, "First.wav");
            string second = Path.Combine(work.FullName, "Second.wav");
            MakeSong(original, 48000, 1, 262);
            MakeSong(second, 44100, 2, 441);
            MusicTrack first = library.Import(original);
            MusicTrack next = library.Import(second);
            MusicTrack mp3 = library.Import(Path.Combine(directory, PadLibrary.RecordingName(0)));
            Assert(mp3.Duration.TotalMinutes > 9, "MP3 import found an unexpectedly short track.");
            File.Move(original, original + ".moved");
            File.Move(second, second + ".moved");

            var reopened = new MusicLibrary(catalogPath);
            Assert(reopened.List().Count == 3, "The library did not persist the imports.");
            PreparedMusic firstSong = reopened.Prepare(first.Id);
            PreparedMusic nextSong = reopened.Prepare(next.Id);
            Assert(firstSong.Head.Any(x => Math.Abs(x) > 0.05f), "Missing stereo PCM after resampling a mono source.");
            Assert(first.FirstFrame > PadLibrary.SampleRate / 2 && next.FirstFrame > PadLibrary.SampleRate / 2,
                "Silent intros were not skipped.");
            Assert(first.Duration.TotalSeconds is > 7.9 and < 8.1, "Unexpected audible duration of the first song.");
            try
            {
                reopened.Import(original);
                throw new InvalidOperationException("Missing input was accepted.");
            }
            catch (IOException) { }
            Assert(reopened.List().Count == 3 && !Directory.EnumerateDirectories(catalogPath, ".import-*").Any(),
                "A failed import left a visible or staged library entry.");

            using var mixer = new PadMixer(LoadAll(directory)) { Volume = 0.7f };
            var block = new float[PadLibrary.SampleRate / 100 * 2];
            void ReadFor(int ms, bool audible)
            {
                var clock = Stopwatch.StartNew();
                for (int frame = 0; frame < ms / 10; frame++)
                {
                    mixer.Read(block, 0, block.Length);
                    Assert(mixer.Status.Error is null, mixer.Status.Error ?? string.Empty);
                    if (audible)
                        Assert(Math.Sqrt(block.Sum(value => value * value) / block.Length) > 0.008,
                            "The mixer returned a low-signal block during a music transition.");
                    int wait = (frame + 1) * 10 - (int)clock.ElapsedMilliseconds;
                    if (wait > 0)
                        Thread.Sleep(wait);
                }
            }
            Assert(mixer.Play(0), "The original C pad could not play.");
            ReadFor(400, false);
            mixer.Play(firstSong);
            ReadFor(500, true);
            Assert(mixer.Status is { MusicId: not null, Switching: true, Voices: 2 }, "Pad to music did not crossfade.");
            ReadFor(1550, true);
            mixer.Play(nextSong);
            ReadFor(700, true);
            Assert(mixer.Status is { Switching: true, Voices: 2 } && mixer.Status.MusicId == next.Id,
                "The songs did not overlap.");
            ReadFor(1350, true);
            mixer.Pause();
            ReadFor(250, false);
            long pausedAt = mixer.Status.Position;
            ReadFor(100, false);
            Assert(mixer.Status.Transport == Transport.Paused && mixer.Status.Position == pausedAt, "Music pause moved the playhead.");
            mixer.Play(nextSong);
            ReadFor(300, false);
            Assert(mixer.Status.Position - pausedAt == 300 * PadLibrary.SampleRate / 1000, "Music resume restarted the song.");
            ReadFor(5360, true);
            mixer.Play(firstSong);
            ReadFor(360, true);
            Assert(mixer.Status is { Switching: false, Voices: 1 } && mixer.Status.MusicId == first.Id,
                "A late change did not finish before the outgoing song ended.");
            Assert(mixer.Play(7), "The original G pad could not play.");
            ReadFor(2050, true);
            Assert(mixer.Status is { Target: 7, Voices: 1 }, "Music to pad did not settle.");
            long completion = mixer.Play(nextSong);
            ReadFor(8150, false);
            Assert(mixer.Status is { Transport: Transport.Stopped, Voices: 0 } &&
                mixer.Status.CompletedMusicRequest == completion && block.All(x => x == 0),
                "An imported song did not end cleanly.");
            Assert(mixer.Status.UnderrunFrames == 0, "A producer missed its deadline.");
            Console.WriteLine("Music checks passed: WAV/MP3 import, saved library, missing source recovery, quiet intro trimming, music and pad transitions, late switching, pause/resume, single play, zero underruns.");
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348))
                Live(directory, firstSong, nextSong);
        }
        finally { work.Delete(recursive: true); }
    }

    private static void MakeSong(string path, int rate, int channels, int hz)
    {
        using var wave = new WaveFileWriter(path, new WaveFormat(rate, 16, channels));
        var second = new short[rate * channels];
        for (int frame = 0; frame < rate; frame++)
        {
            short value = (short)(8000 * Math.Sin(2 * Math.PI * hz * frame / rate));
            for (int channel = 0; channel < channels; channel++)
                second[frame * channels + channel] = value;
        }
        var silence = new short[rate * channels];
        wave.WriteSamples(silence, 0, silence.Length);
        for (int i = 0; i < 8; i++)
            wave.WriteSamples(second, 0, second.Length);
        wave.WriteSamples(silence, 0, silence.Length);
    }
}
