using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using PegasusDeck.Core.Contract;

namespace PegasusDeck.Agent.Audio;

/// <summary>Volumen y silencio de la salida de audio predeterminada, silencio del micrófono y elección de la salida (ADR-035).</summary>
internal interface IAudioController
{
    AudioInfo Read();
    void SetMute(bool mute);
    void SetVolume(int percent);
    MicInfo ReadMic();
    void SetMicMute(bool mute);
    OutputsInfo ReadOutputs();

    /// <summary>Hace predeterminada la salida con ese id (de ReadOutputs). Null si Windows lo acepta; si no, el motivo.</summary>
    string? SetDefaultOutput(string id);
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

    /// <summary>Salidas activas con nombre; el id es un hash corto del identificador de Windows (no se envía tal cual).</summary>
    public OutputsInfo ReadOutputs()
    {
        try
        {
            var defaultId = DefaultEndpointId();
            var devices = ActiveOutputs().Select(o => new OutputDevice(o.ShortId, o.Name, o.EndpointId == defaultId)).ToList();
            return new OutputsInfo("ready", devices);
        }
#pragma warning disable CA1031 // Sin dispositivos o acceso denegado = no disponible.
        catch (Exception)
#pragma warning restore CA1031
        {
            return OutputsInfo.Unavailable;
        }
    }

    public string? SetDefaultOutput(string id)
    {
        var target = ActiveOutputs().FirstOrDefault(o => o.ShortId == id);
        if (target is null) return "Esa salida de audio ya no está conectada.";
        // Windows no tiene API pública para esto: IPolicyConfig es la que usa el propio panel de Sonido (Windows 7 a 11).
        var policy = (IPolicyConfig)new PolicyConfigClientCom();
        try
        {
            foreach (var role in new[] { 0, 1, 2 }) // eConsole, eMultimedia, eCommunications
            {
                var hr = policy.SetDefaultEndpoint(target.EndpointId, role);
                if (hr < 0) return $"Windows no ha cambiado la salida (0x{hr:X8}).";
            }
        }
        finally
        {
            Marshal.ReleaseComObject(policy);
        }
        // Se confirma leyendo la predeterminada real.
        return DefaultEndpointId() == target.EndpointId ? null : "Windows no ha cambiado la salida.";
    }

    private sealed record Output(string EndpointId, string ShortId, string Name);

    private static string ShortId(string endpointId) =>
        "out-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(endpointId.ToLowerInvariant())))[..12].ToLowerInvariant();

    private static string? DefaultEndpointId()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
        IMMDevice? device = null;
        try
        {
            if (enumerator.GetDefaultAudioEndpoint(Render, 0, out device) < 0) return null;
            return device.GetId(out var id) < 0 ? null : id;
        }
        finally
        {
            if (device is not null) Marshal.ReleaseComObject(device);
            Marshal.ReleaseComObject(enumerator);
        }
    }

    private static List<Output> ActiveOutputs()
    {
        var list = new List<Output>();
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
        IMMDeviceCollection? collection = null;
        try
        {
            Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(Render, 1 /* DEVICE_STATE_ACTIVE */, out collection));
            Marshal.ThrowExceptionForHR(collection.GetCount(out var count));
            for (uint i = 0; i < count; i++)
            {
                IMMDevice? device = null;
                IPropertyStore? store = null;
                try
                {
                    if (collection.Item(i, out device) < 0 || device.GetId(out var endpointId) < 0) continue;
                    var name = endpointId;
                    if (device.OpenPropertyStore(0 /* STGM_READ */, out store) >= 0)
                    {
                        var key = FriendlyName;
                        if (store.GetValue(ref key, out var value) >= 0)
                        {
                            try
                            {
                                if (value.Type == 31 /* VT_LPWSTR */ && Marshal.PtrToStringUni(value.Pointer) is { Length: > 0 } text) name = text;
                            }
                            finally
                            {
                                _ = PropVariantClear(ref value);
                            }
                        }
                    }
                    list.Add(new Output(endpointId, ShortId(endpointId), name));
                }
                finally
                {
                    if (store is not null) Marshal.ReleaseComObject(store);
                    if (device is not null) Marshal.ReleaseComObject(device);
                }
            }
        }
        finally
        {
            if (collection is not null) Marshal.ReleaseComObject(collection);
            Marshal.ReleaseComObject(enumerator);
        }
        return [.. list.OrderBy(o => o.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>PKEY_Device_FriendlyName: el nombre que muestra Windows, p. ej. «Altavoces (Realtek)».</summary>
    private static readonly PropertyKey FriendlyName = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey(Guid fmtid, int pid)
    {
        public Guid Fmtid = fmtid;
        public int Pid = pid;
    }

    /// <summary>PROPVARIANT reducido: solo se lee VT_LPWSTR (todo se libera con PropVariantClear).</summary>
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public IntPtr Pointer;
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);

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
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object endpoint);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
    }

    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    private class PolicyConfigClientCom;

    /// <summary>IPolicyConfig (no documentada; la usa el panel de Sonido de Windows). Solo se llama SetDefaultEndpoint; el resto ocupa su sitio.</summary>
    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat();
        [PreserveSig] int GetDeviceFormat();
        [PreserveSig] int ResetDeviceFormat();
        [PreserveSig] int SetDeviceFormat();
        [PreserveSig] int GetProcessingPeriod();
        [PreserveSig] int SetProcessingPeriod();
        [PreserveSig] int GetShareMode();
        [PreserveSig] int SetShareMode();
        [PreserveSig] int GetPropertyValue();
        [PreserveSig] int SetPropertyValue();
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int role);
        [PreserveSig] int SetEndpointVisibility();
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
