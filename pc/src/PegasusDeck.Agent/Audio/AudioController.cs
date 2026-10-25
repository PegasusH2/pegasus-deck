using System.Runtime.InteropServices;
using PegasusDeck.Core.Contract;

namespace PegasusDeck.Agent.Audio;

/// <summary>Volumen y silencio de la salida de audio predeterminada y silencio del micrófono predeterminado.</summary>
internal interface IAudioController
{
    AudioInfo Read();
    void SetMute(bool mute);
    void SetVolume(int percent);
    MicInfo ReadMic();
    void SetMicMute(bool mute);
}

/// <summary>Implementación con Core Audio (IAudioEndpointVolume) de Windows.</summary>
internal sealed class CoreAudioController : IAudioController
{
    private static Guid _context = Guid.Empty;

    public AudioInfo Read()
    {
        try
        {
            return WithEndpoint(ep =>
            {
                Marshal.ThrowExceptionForHR(ep.GetMute(out var muted));
                Marshal.ThrowExceptionForHR(ep.GetMasterVolumeLevelScalar(out var level));
                return new AudioInfo("ready", muted, (int)Math.Round(level * 100));
            });
        }
#pragma warning disable CA1031 // Cualquier fallo (sin dispositivo, acceso denegado…) = audio no disponible, nunca un bucle roto.
        catch (Exception)
#pragma warning restore CA1031
        {
            return AudioInfo.Unavailable;
        }
    }

    public void SetMute(bool mute) => WithEndpoint(ep => { Marshal.ThrowExceptionForHR(ep.SetMute(mute, ref _context)); return 0; });

    public void SetVolume(int percent) =>
        WithEndpoint(ep => { Marshal.ThrowExceptionForHR(ep.SetMasterVolumeLevelScalar(Math.Clamp(percent, 0, 100) / 100f, ref _context)); return 0; });

    public MicInfo ReadMic()
    {
        try
        {
            return WithEndpoint(ep =>
            {
                Marshal.ThrowExceptionForHR(ep.GetMute(out var muted));
                return new MicInfo("ready", muted);
            }, Capture);
        }
#pragma warning disable CA1031 // Sin micrófono o acceso denegado = no disponible.
        catch (Exception)
#pragma warning restore CA1031
        {
            return MicInfo.Unavailable;
        }
    }

    public void SetMicMute(bool mute) => WithEndpoint(ep => { Marshal.ThrowExceptionForHR(ep.SetMute(mute, ref _context)); return 0; }, Capture);

    private const int Render = 0;  // eRender
    private const int Capture = 1; // eCapture

    /// <summary>Resuelve el dispositivo predeterminado en cada uso (sigue los cambios de dispositivo) y libera los objetos COM.</summary>
    private static T WithEndpoint<T>(Func<IAudioEndpointVolume, T> use, int dataFlow = Render)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
        IMMDevice? device = null;
        object? volume = null;
        try
        {
            Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(dataFlow, 0 /* eConsole */, out device));
            var iid = typeof(IAudioEndpointVolume).GUID;
            Marshal.ThrowExceptionForHR(device.Activate(ref iid, 1 /* CLSCTX_INPROC_SERVER */, IntPtr.Zero, out volume));
            return use((IAudioEndpointVolume)volume);
        }
        finally
        {
            if (volume is not null) Marshal.ReleaseComObject(volume);
            if (device is not null) Marshal.ReleaseComObject(device);
            Marshal.ReleaseComObject(enumerator);
        }
    }

#pragma warning disable SYSLIB1096 // Interop COM clásico: suficiente para tres llamadas.
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorCom;

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object endpoint);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
#pragma warning restore SYSLIB1096
}
