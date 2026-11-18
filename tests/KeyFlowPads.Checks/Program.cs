using System.Runtime.InteropServices;
using KeyFlowPads.Audio;
using NLayer;

try
{
    if (args.Length < 2)
        throw new ArgumentException("Usage: KeyFlowPads.Checks prepare|render|live|soak|music <audio directory> [seconds]");
    string audioDir = Path.GetFullPath(args[1]);
    switch (args[0])
    {
        case "prepare":
            Parallel.For(0, PadLibrary.Count, new ParallelOptions { MaxDegreeOfParallelism = 4 },
                key => Prepare(audioDir, key));
            break;
        case "render":
            Checks.Render(audioDir);
            break;
        case "live":
            Checks.Live(audioDir);
            break;
        case "soak":
            Checks.Soak(audioDir, args.Length > 2 ? int.Parse(args[2]) : 1830);
            break;
        case "music":
            Checks.Music(audioDir);
            break;
        default:
            throw new ArgumentException("Unknown command.");
    }
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error);
    return 1;
}

static void Prepare(string directory, int key)
{
    const int rate = PadLibrary.SampleRate;
    const int blockFrames = rate / 10;
    const int edgeFrames = 60 * rate;
    const int overlap = 2 * rate;
    const int prime = 2 * rate;
    string name = PadLibrary.Key(key);
    string recording = Path.Combine(directory, PadLibrary.RecordingName(key));
    using var mp3 = new MpegFile(recording);
    if (mp3.SampleRate != rate || mp3.Channels != 2)
        throw new InvalidDataException($"{name}: expected 44.1 kHz stereo.");
    int advertisedFrames = checked((int)(mp3.Length / 8));
    var bounds = Mp3Bounds.Detect(recording, advertisedFrames);
    var head = new float[edgeFrames * 2];
    var tail = new float[edgeFrames * 2];
    var block = new float[blockFrames * 2];
    var levels = new List<double>();
    float peak = 0;
    int frames = 0;
    int count;
    while ((count = mp3.ReadSamples(block, 0, block.Length)) != 0)
    {
        double energy = 0;
        for (int i = 0; i < count; i++)
        {
            float sample = block[i];
            if (!float.IsFinite(sample))
                throw new InvalidDataException($"{name}: invalid PCM.");
            peak = Math.Max(peak, Math.Abs(sample));
            energy += sample * sample;
        }
        levels.Add(Math.Sqrt(energy / count));
        if (frames < edgeFrames)
            Array.Copy(block, 0, head, frames * 2, Math.Min(count, (edgeFrames - frames) * 2));
        int tailIndex = frames % edgeFrames * 2;
        int part = Math.Min(count, tail.Length - tailIndex);
        Array.Copy(block, 0, tail, tailIndex, part);
        Array.Copy(block, part, tail, 0, count - part);
        frames += count / 2;
    }
    if (frames != advertisedFrames || frames < edgeFrames * 2)
        throw new InvalidDataException($"{name}: unexpected decoded duration ({frames}/{advertisedFrames} frames).");

    double median = levels.Order().ElementAt(levels.Count / 2);
    double threshold = median * 0.60;
    int firstBlock = (bounds.FirstFrame + blockFrames - 1) / blockFrames;
    int finalBlock = bounds.EndFrame / blockFrames;
    int startBlock = Enumerable.Range(firstBlock, 50 * 10 - firstBlock).First(i =>
        levels.Skip(i).Take(40).All(level => level >= threshold));
    int endBlock = Enumerable.Range(finalBlock - 51 * 10, 51 * 10).Reverse().First(i =>
        levels.Skip(i - 30).Take(30).All(level => level >= threshold));

    int start = startBlock * blockFrames;
    int end = endBlock * blockFrames;
    // Test several sub-second offsets and prefer the overlap with the strongest quietest 100 ms.
    double[] mix = Enumerable.Range(0, overlap).Select(i => 0.5 - 0.5 * Math.Cos(Math.PI * i / (overlap - 1))).ToArray();
    double best = -1;
    int bestEnd = end;
    for (int delta = 0; delta <= rate; delta += 221)
    {
        int candidate = end - delta;
        double minimum = double.PositiveInfinity;
        for (int window = 0; window < overlap; window += blockFrames)
        {
            double energy = 0;
            for (int i = window; i < window + blockFrames; i += 8)
            {
                int a = ((candidate - overlap + i) % edgeFrames) * 2;
                int b = (start + i) * 2;
                double left = tail[a] + (head[b] - tail[a]) * mix[i];
                double right = tail[a + 1] + (head[b + 1] - tail[a + 1]) * mix[i];
                energy += left * left + right * right;
            }
            minimum = Math.Min(minimum, Math.Sqrt(energy / (2 * Math.Ceiling(blockFrames / 8.0))));
        }
        if (minimum > best)
        {
            best = minimum;
            bestEnd = candidate;
        }
    }
    end = bestEnd;

    using var file = File.Create(Path.Combine(directory, name + ".loop"));
    using var header = new BinaryWriter(file);
    header.Write(0x3150464B); // KFP1, followed by frame offsets and a float stereo cache.
    header.Write(start);
    header.Write(end);
    header.Write(overlap);
    header.Write(prime);
    header.Write(MemoryMarshal.AsBytes(head.AsSpan(start * 2, (overlap + prime) * 2)));

    static double Db(double x) => 20 * Math.Log10(x);
    Console.WriteLine($"{name,-3} source {(bounds.EndFrame - bounds.FirstFrame) / (double)rate:F3}s after gapless trim | head {start / (double)rate:F3}s | end {end / (double)rate:F3}s | loop {(end - start - overlap) / (double)rate:F3}s | peak {Db(peak):F1} dBFS | median {Db(median):F1} dBFS | seam RMS floor {Db(best):F1} dBFS");
}
