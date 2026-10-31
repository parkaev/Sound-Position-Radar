using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SoundRadar.Core.Audio;

/// <summary>Процесс-кандидат для захвата.</summary>
public sealed record AudioProcessInfo(int Pid, string Name, string? WindowTitle, bool SessionActive)
{
    public override string ToString() =>
        WindowTitle is { Length: > 0 } t ? $"{Name} (PID {Pid}) — {t}" : $"{Name} (PID {Pid})";
}

/// <summary>
/// Список процессов-кандидатов на захват: сначала процессы с активной аудио-сессией
/// (IAudioSessionManager2 — публичный API), затем все процессы с окнами.
/// </summary>
public static class AudioProcessList
{
    public static IReadOnlyList<AudioProcessInfo> GetCandidates()
    {
        var byPid = new Dictionary<int, AudioProcessInfo>();

        try
        {
            foreach (var (pid, active) in GetAudioSessions())
            {
                AudioProcessInfo? info = null;
                try { info = Describe(pid, active); } catch { }
                byPid[pid] = info ?? new AudioProcessInfo(pid, $"pid{pid}", null, active);
            }
        }
        catch
        {
            // Перечисление сессий может не сработать (экзотические устройства) — остаётся список окон.
        }

        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                if (proc.Id <= 4 || byPid.ContainsKey(proc.Id))
                    continue;
                if (proc.MainWindowHandle == IntPtr.Zero)
                    continue;
                byPid[proc.Id] = new AudioProcessInfo(proc.Id, proc.ProcessName, proc.MainWindowTitle, false);
            }
            catch
            {
                // Процесс мог завершиться между GetProcesses и чтением свойств.
            }
            finally
            {
                proc.Dispose();
            }
        }

        return byPid.Values
            .OrderByDescending(p => p.SessionActive)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Найти PID по имени exe (без расширения) — для автопереподключения после рестарта игры.</summary>
    public static int? ResolvePidByName(string processName)
    {
        var name = processName.Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];
        if (name.Length == 0)
            return null;

        var matches = Process.GetProcessesByName(name);
        if (matches.Length == 0)
            return null;

        // Приоритет процессу с активной аудио-сессией.
        List<(int pid, bool active)> sessions;
        try { sessions = GetAudioSessions(); }
        catch { sessions = new List<(int, bool)>(); }
        Process? withSession = null;
        try
        {
            withSession = matches.FirstOrDefault(p => sessions.Any(s => s.pid == p.Id));
            return (withSession ?? matches[0]).Id;
        }
        finally
        {
            foreach (var p in matches)
                if (!ReferenceEquals(p, withSession))
                    p.Dispose();
        }
    }

    private static AudioProcessInfo? Describe(int pid, bool active)
    {
        using var proc = Process.GetProcessById(pid);
        return new AudioProcessInfo(
            pid,
            proc.ProcessName,
            proc.MainWindowHandle != IntPtr.Zero ? proc.MainWindowTitle : null,
            active);
    }

    // ---- COM: перечисление аудио-сессий устройства рендера по умолчанию -------------------------

    /// <summary>(pid, isActive) всех живых аудио-сессий.</summary>
    private static List<(int pid, bool active)> GetAudioSessions()
    {
        var result = new List<(int, bool)>();
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        try
        {
            enumerator.GetDefaultAudioEndpoint(DataFlowRender, RoleMultimedia, out var device);
            try
            {
                var iid = new Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"); // IID_IAudioSessionManager2
                device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out var obj);
                var manager = (IAudioSessionManager2)obj;
                manager.GetSessionEnumerator(out var sessions);
                try
                {
                    sessions.GetCount(out var count);
                    for (var i = 0; i < count; i++)
                    {
                        sessions.GetSession(i, out var sessionObj);
                        try
                        {
                            if (sessionObj is not IAudioSessionControl2 session)
                                continue;
                            session.GetState(out var state);
                            if (state == SessionStateExpired)
                                continue;
                            session.GetProcessIdentifier(out var pid);
                            if (pid <= 4)
                                continue;
                            result.Add((pid, state == SessionStateActive));
                        }
                        finally
                        {
                            _ = Marshal.ReleaseComObject(sessionObj);
                        }
                    }
                }
                finally
                {
                    _ = Marshal.ReleaseComObject(sessions);
                }
            }
            finally
            {
                _ = Marshal.ReleaseComObject(device);
            }
        }
        finally
        {
            _ = Marshal.ReleaseComObject(enumerator);
        }
        return result;
    }

    private const int DataFlowRender = 0;    // eRender
    private const int RoleMultimedia = 1;    // eMultimedia
    private const int ClsCtxAll = 23;        // CLSCTX_ALL
    private const int SessionStateInactive = 0;
    private const int SessionStateActive = 1;
    private const int SessionStateExpired = 2;

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject
    {
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices); // не используем, нужен для vtable

        [PreserveSig]
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        [PreserveSig]
        int GetAudioSessionControl(IntPtr guid, int flags, out IntPtr control); // vtable: методы IAudioSessionManager

        [PreserveSig]
        int GetSimpleAudioVolume(IntPtr guid, int flags, out IntPtr volume);

        [PreserveSig]
        int GetSessionEnumerator(out IAudioSessionEnumerator enumerator);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        [PreserveSig]
        int GetCount(out int count);

        [PreserveSig]
        int GetSession(int index, [MarshalAs(UnmanagedType.IUnknown)] out object session);
    }

    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2
    {
        // vtable IAudioSessionControl (объявлены полностью, вызываем только нижние)
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr ctx);
        [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, IntPtr ctx);
        [PreserveSig] int GetGroupingParam(out Guid groupingId);
        [PreserveSig] int SetGroupingParam(ref Guid groupingId, IntPtr ctx);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr notifications);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr notifications);

        // IAudioSessionControl2
        [PreserveSig] int GetProcessIdentifier(out int pid);
        [PreserveSig] int IsSystemSoundsSession();
        [PreserveSig] int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
    }
}
