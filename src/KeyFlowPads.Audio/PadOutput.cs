using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace KeyFlowPads.Audio;

public sealed record AudioDevice(string Id, string Name)
{
    public override string ToString() => Name;
}

public sealed class PadOutput : IDisposable
{
    private readonly MMDeviceEnumerator enumerator = new();
    private readonly MMDevice device;
    private readonly WasapiOut output;
    private string? error;

    public const int BufferMilliseconds = 45;
    public string? Error => Volatile.Read(ref error);
    public string DeviceName => device.FriendlyName;

    public PadOutput(PadMixer mixer, string deviceId)
    {
        MMDevice? opened = null;
        WasapiOut? player = null;
        try
        {
            opened = OpenDevice(enumerator, deviceId);
            if (opened.State != DeviceState.Active)
                throw new InvalidOperationException("The selected output is unavailable. Reconnect it or choose another output.");
            player = new WasapiOut(opened, AudioClientShareMode.Shared, true, BufferMilliseconds);
            player.Init(mixer.ToWaveProvider());
            player.PlaybackStopped += OnStopped;
            player.Play();
            device = opened;
            output = player;
        }
        catch
        {
            player?.Dispose();
            opened?.Dispose();
            enumerator.Dispose();
            throw;
        }
    }

    public static AudioDevice[] ListDevices()
    {
        using var devices = new MMDeviceEnumerator();
        var results = new List<AudioDevice> { new("", "System default") };
        foreach (var device in devices.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            using (device)
                results.Add(new AudioDevice(device.ID, device.FriendlyName));
        }
        return results.ToArray();
    }

    public static MMDevice OpenDevice(MMDeviceEnumerator devices, string deviceId) =>
        deviceId.Length == 0 ? devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia) : devices.GetDevice(deviceId);

    private void OnStopped(object? sender, StoppedEventArgs args) =>
        Volatile.Write(ref error, args.Exception?.Message ?? "Windows stopped the selected audio output.");

    public void Dispose()
    {
        output.PlaybackStopped -= OnStopped;
        output.Dispose();
        device.Dispose();
        enumerator.Dispose();
    }
}
