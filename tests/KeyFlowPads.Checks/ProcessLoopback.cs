using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wasapi.CoreAudioApi.Interfaces;

public static class ProcessLoopback
{
    public static AudioClient Open()
    {
        var worker = new Completion();
        int size = Marshal.SizeOf<ActivationParameters>();
        IntPtr parameters = Marshal.AllocHGlobal(size);
        IntPtr completion = IntPtr.Zero;
        try
        {
            completion = Marshal.GetComInterfaceForObject(worker, typeof(IActivateAudioInterfaceCompletionHandler));
            Marshal.StructureToPtr(new ActivationParameters(1, Environment.ProcessId, 0), parameters, false);
            var variant = new BlobVariant { Type = 65, Size = size, Data = parameters }; // VT_BLOB
            Guid iid = typeof(IAudioClient).GUID;
            Marshal.ThrowExceptionForHR(ActivateAudioInterfaceAsync(
                "VAD\\Process_Loopback", ref iid, ref variant, completion, out var operation));
            try
            {
                return new AudioClient(worker.Result.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult());
            }
            finally { Marshal.Release(operation); }
        }
        finally
        {
            if (completion != IntPtr.Zero)
                Marshal.Release(completion);
            Marshal.FreeHGlobal(parameters);
        }
    }

    [DllImport("Mmdevapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int ActivateAudioInterfaceAsync(string device, ref Guid iid, ref BlobVariant activation,
        IntPtr completion, out IntPtr operation);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct ActivationParameters(int Type, int Process, int Mode);

    [StructLayout(LayoutKind.Explicit)]
    private struct BlobVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public int Size;
        [FieldOffset(16)] public IntPtr Data;
    }

    [ComImport, Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAgileObject;

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class Completion : IActivateAudioInterfaceCompletionHandler, IAgileObject
    {
        public TaskCompletionSource<IAudioClient> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation)
        {
            try
            {
                operation.GetActivateResult(out int error, out object instance);
                Marshal.ThrowExceptionForHR(error);
                Result.TrySetResult((IAudioClient)instance);
            }
            catch (Exception error) { Result.TrySetException(error); }
        }
    }
}
