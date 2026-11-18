using System.Text.Json;
using System.Text.Json.Serialization;
using NAudio.Utils;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace KeyFlowPads.Audio;

public sealed record MusicTrack(Guid Id, string Title, long FirstFrame, long EndFrame)
{
    [JsonIgnore]
    public TimeSpan Duration => TimeSpan.FromSeconds((EndFrame - FirstFrame) / (double)PadLibrary.SampleRate);
}

public sealed class PreparedMusic
{
    internal PreparedMusic(MusicTrack track, string path, float[] head) => (Track, Path, Head) = (track, path, head);
    public MusicTrack Track { get; }
    internal string Path { get; }
    internal float[] Head { get; }
}

public sealed class MusicLibrary(string directory)
{
    // Ignore near-silence at the edges so a silent file intro does not delay a crossfade.
    private const float Audible = 0.00032f;
    private const int SampleRate = PadLibrary.SampleRate;
    private const string MetadataFile = "track.json";
    private const string AudioFile = "audio.wav";
    private static readonly WaveFormat Format = new(SampleRate, 16, 2);
    private readonly string directory = Path.GetFullPath(directory);

    public IReadOnlyList<MusicTrack> List()
    {
        var tracks = new List<MusicTrack>();
        if (!Directory.Exists(directory))
            return tracks;
        foreach (string path in Directory.EnumerateDirectories(directory))
        {
            if (!Guid.TryParseExact(Path.GetFileName(path), "N", out Guid id))
                continue;
            try { tracks.Add(ReadMetadata(id)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                // The damaged entry stays on disk for recovery; other tracks remain usable.
            }
        }
        return tracks.OrderBy(track => track.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(track => track.Id).ToArray();
    }

    public MusicTrack Import(string fileName, CancellationToken cancellation = default)
    {
        string source = Path.GetFullPath(fileName);
        Guid id = Guid.NewGuid();
        string staging = Path.Combine(directory, ".import-" + id.ToString("N"));
        Directory.CreateDirectory(staging);
        bool committed = false;
        try
        {
            MusicTrack track = Convert(source, Path.Combine(staging, AudioFile), id, cancellation);
            using (var metadata = new FileStream(Path.Combine(staging, MetadataFile), FileMode.CreateNew))
            {
                JsonSerializer.Serialize(metadata, track);
                metadata.Flush(flushToDisk: true);
            }

            cancellation.ThrowIfCancellationRequested();
            // Publish audio and metadata together. A failed or interrupted import never becomes a library row.
            Directory.Move(staging, TrackDirectory(id));
            committed = true;
            return track;
        }
        finally
        {
            if (!committed)
            {
                try { Directory.Delete(staging, recursive: true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    public PreparedMusic Prepare(Guid id)
    {
        MusicTrack track = ReadMetadata(id);
        string path = Path.Combine(TrackDirectory(id), AudioFile);
        using var reader = OpenAudio(path);
        if (track.EndFrame > reader.Length / Format.BlockAlign)
            throw new InvalidDataException("The saved track is incomplete.");
        reader.Position = track.FirstFrame * Format.BlockAlign;
        var head = new float[checked((int)Math.Min(2 * SampleRate, track.EndFrame - track.FirstFrame)) * 2];
        ReadExactly(reader.ToSampleProvider(), head, head.Length);
        return new PreparedMusic(track, path, head);
    }

    internal static WaveFileReader OpenAudio(string path)
    {
        var reader = new WaveFileReader(path);
        if (!reader.WaveFormat.Equals(Format))
        {
            reader.Dispose();
            throw new InvalidDataException("The saved music file has an invalid format.");
        }
        return reader;
    }

    internal static void ReadExactly(ISampleProvider source, float[] buffer, int count)
    {
        int filled = 0;
        while (filled < count)
        {
            int read = source.Read(buffer, filled, count - filled);
            if (read == 0)
                throw new EndOfStreamException("The saved track ended before its expected length.");
            filled += read;
        }
    }

    private static MusicTrack Convert(string source, string destination, Guid id, CancellationToken cancellation)
    {
        using WaveStream reader = Path.GetExtension(source).Equals(".wav", StringComparison.OrdinalIgnoreCase)
            ? new WaveFileReader(source)
            : new MediaFoundationReader(source, new() { RequestFloatOutput = true, SingleReaderObject = true });
        ISampleProvider audio = reader.ToSampleProvider();
        if (audio.WaveFormat.Channels == 1)
            audio = new MonoToStereoSampleProvider(audio);
        if (audio.WaveFormat.Channels != 2)
            throw new InvalidDataException("Import a mono or stereo audio file.");
        if (audio.WaveFormat.SampleRate != SampleRate)
            audio = new WdlResamplingSampleProvider(audio, SampleRate);

        long frame = 0;
        long first = -1;
        long last = -1;
        var buffer = new float[8192];
        var pcm = new short[buffer.Length];
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.ReadWrite);
        using (var wave = new WaveFileWriter(new IgnoreDisposeStream(output), Format))
        {
            int count;
            while ((count = audio.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellation.ThrowIfCancellationRequested();
                if ((count & 1) != 0 || (frame + count / 2) * Format.BlockAlign > uint.MaxValue - 4096)
                    throw new InvalidDataException("The track exceeds the supported WAV size or has invalid stereo frames.");
                for (int index = 0; index < count; index += 2, frame++)
                {
                    float left = buffer[index];
                    float right = buffer[index + 1];
                    if (!float.IsFinite(left) || !float.IsFinite(right))
                        throw new InvalidDataException("The track contains invalid audio samples.");
                    if (Math.Max(Math.Abs(left), Math.Abs(right)) >= Audible)
                    {
                        if (first < 0)
                            first = frame;
                        last = frame;
                    }
                    pcm[index] = ToPcm(left);
                    pcm[index + 1] = ToPcm(right);
                }
                wave.WriteSamples(pcm, 0, count);
            }
        }
        output.Flush(flushToDisk: true);
        if (first < 0 || last - first < SampleRate / 10)
            throw new InvalidDataException("The track needs at least a tenth of a second of audible music.");
        return new MusicTrack(id, Path.GetFileNameWithoutExtension(source), first, last + 1);
    }

    private static short ToPcm(float value) => (short)Math.Clamp(MathF.Round(value * 32768), short.MinValue, short.MaxValue);

    private string TrackDirectory(Guid id) => Path.Combine(directory, id.ToString("N"));

    private MusicTrack ReadMetadata(Guid id)
    {
        using var metadata = File.OpenRead(Path.Combine(TrackDirectory(id), MetadataFile));
        var track = JsonSerializer.Deserialize<MusicTrack>(metadata);
        if (track is null || track.Id != id || string.IsNullOrWhiteSpace(track.Title) ||
            track.FirstFrame < 0 || track.EndFrame <= track.FirstFrame ||
            track.EndFrame > uint.MaxValue / Format.BlockAlign)
            throw new InvalidDataException("The saved music details are invalid.");
        return track;
    }
}
