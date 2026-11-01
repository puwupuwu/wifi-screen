using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

[assembly: System.Reflection.AssemblyTitle("AirSpan Audio Endpoint Monitor")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]

namespace AirSpan.AudioEndpointMonitor {
    // Reads the Windows master mute switch only. It never opens an audio
    // capture stream or calls a volume/mute setter.
    internal static class Program {
        private const int PollMilliseconds = 200;

        [MTAThread]
        private static int Main(string[] args) {
            bool once = false;
            int parentPid = 0;
            for (int i = 0; i < args.Length; i++) {
                if (args[i] == "--once") once = true;
                else if (args[i] == "--parent-pid" && i + 1 < args.Length &&
                    Int32.TryParse(args[++i], out parentPid) && parentPid > 0) { }
                else {
                    Console.Error.WriteLine("Usage: audio-endpoint-monitor.exe [--once] [--parent-pid PID]");
                    return 64;
                }
            }

            Process parent = null;
            try {
                if (parentPid != 0) {
                    try {
                        parent = Process.GetProcessById(parentPid);
                        // Acquire a handle now, so a later PID reuse cannot make
                        // this helper follow a different process.
                        if (parent.HasExited) return 0;
                    } catch (ArgumentException) { return 0; }
                }

                using (var reader = new DefaultRenderMuteReader()) {
                    bool havePrevious = false, previousAvailable = false, previousMuted = false;
                    while (ParentAlive(parent)) {
                        bool muted;
                        bool available = reader.TryRead(out muted);
                        if (!havePrevious || available != previousAvailable || muted != previousMuted) {
                            try {
                                Console.Out.WriteLine("{\"muted\":" + (muted ? "true" : "false") +
                                    ",\"available\":" + (available ? "true" : "false") + "}");
                                Console.Out.Flush();
                            } catch (IOException) { return 0; }
                              catch (ObjectDisposedException) { return 0; }
                            havePrevious = true;
                            previousAvailable = available;
                            previousMuted = muted;
                        }
                        if (once) return available ? 0 : 2;
                        Thread.Sleep(PollMilliseconds);
                    }
                }
                return 0;
            } catch (IOException) { return 0; }
              catch (Exception error) {
                // Diagnostics stay off stdout, whose entire protocol is JSONL.
                Console.Error.WriteLine("Audio endpoint monitor stopped: " + error.GetType().Name);
                return 1;
            } finally {
                if (parent != null) parent.Dispose();
            }
        }

        private static bool ParentAlive(Process parent) {
            if (parent == null) return true;
            try { return !parent.HasExited; }
            catch (InvalidOperationException) { return false; }
        }
    }

    internal sealed class DefaultRenderMuteReader : IDisposable {
        private IMMDeviceEnumerator enumerator;

        public bool TryRead(out bool muted) {
            muted = false;
            IMMDevice endpoint = null;
            object volumeObject = null;
            try {
                if (enumerator == null) enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                // eConsole is the ordinary Windows default playback device,
                // including the master mute switch controlled by media keys.
                // Resolve again every poll so output-device changes are followed.
                Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(
                    EDataFlow.Render, ERole.Console, out endpoint));
                Guid volumeId = typeof(IAudioEndpointVolume).GUID;
                Marshal.ThrowExceptionForHR(endpoint.Activate(ref volumeId,
                    23 /* CLSCTX_ALL */, IntPtr.Zero, out volumeObject));
                var volume = (IAudioEndpointVolume)volumeObject;
                Marshal.ThrowExceptionForHR(volume.GetMute(out muted));
                return true;
            } catch (Exception) {
                // Device removal, service restarts and missing endpoints are
                // transient. Recreate the enumerator and retry at the next poll.
                muted = false;
                Release(enumerator);
                enumerator = null;
                return false;
            } finally {
                Release(volumeObject);
                Release(endpoint);
            }
        }

        private static void Release(object value) {
            try {
                if (value != null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
            } catch (InvalidComObjectException) { }
        }

        public void Dispose() {
            Release(enumerator);
            enumerator = null;
        }
    }

    internal enum EDataFlow { Render = 0, Capture = 1, All = 2 }
    internal enum ERole { Console = 0, Multimedia = 1, Communications = 2 }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    internal class MMDeviceEnumerator { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),
        InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow flow, uint stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow flow, ERole role, out IMMDevice endpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice endpoint);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr callback);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr callback);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"),
        InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice {
        [PreserveSig] int Activate(ref Guid iid, uint classContext, IntPtr activationParameters,
            [MarshalAs(UnmanagedType.IUnknown)] out object result);
        [PreserveSig] int OpenPropertyStore(uint access, out IntPtr properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }

    // Full method order is required by the native COM vtable. Only GetMute is
    // invoked; the setter declarations below are never called by this helper.
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"),
        InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioEndpointVolume {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr callback);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr callback);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float level, IntPtr context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, IntPtr context);
        [PreserveSig] int GetMasterVolumeLevel(out float level);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float level, IntPtr context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, IntPtr context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float level);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, IntPtr context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
        [PreserveSig] int GetVolumeStepInfo(out uint step, out uint count);
        [PreserveSig] int VolumeStepUp(IntPtr context);
        [PreserveSig] int VolumeStepDown(IntPtr context);
        [PreserveSig] int QueryHardwareSupport(out uint mask);
        [PreserveSig] int GetVolumeRange(out float minimum, out float maximum, out float increment);
    }
}
