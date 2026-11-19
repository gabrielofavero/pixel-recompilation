param([ValidateRange(1, 300)][int]$Seconds = 10)

# Read Windows per-process output peaks. No microphone access or audio recording.
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public static class PixelAudioMeter
{
    public static string[] Read()
    {
        var enumerator = (IDeviceEnumerator)new DeviceEnumerator();
        Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(0, 0, out var device));
        var id = typeof(ISessionManager).GUID;
        Marshal.ThrowExceptionForHR(device.Activate(ref id, 23, IntPtr.Zero, out var activated));
        var manager = (ISessionManager)activated;
        Marshal.ThrowExceptionForHR(manager.GetSessionEnumerator(out var sessions));
        Marshal.ThrowExceptionForHR(sessions.GetCount(out var count));
        var output = new List<string>();
        for (int i = 0; i < count; i++)
        {
            Marshal.ThrowExceptionForHR(sessions.GetSession(i, out var session));
            var control = (ISessionControl2)session;
            Marshal.ThrowExceptionForHR(control.GetProcessId(out var pid));
            var meter = (IAudioMeter)session;
            Marshal.ThrowExceptionForHR(meter.GetPeakValue(out var peak));
            var volume = (ISimpleVolume)session;
            Marshal.ThrowExceptionForHR(volume.GetMasterVolume(out var level));
            Marshal.ThrowExceptionForHR(volume.GetMute(out var mute));
            output.Add(FormattableString.Invariant($"pid={pid} peak={peak:F6} volume={level:F3} muted={mute}"));
            Marshal.ReleaseComObject(session);
        }
        Marshal.ReleaseComObject(sessions);
        Marshal.ReleaseComObject(manager);
        Marshal.ReleaseComObject(device);
        Marshal.ReleaseComObject(enumerator);
        return output.ToArray();
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] private class DeviceEnumerator { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDevice
    {
        [PreserveSig] int Activate(ref Guid id, uint context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    }
    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISessionManager
    {
        [PreserveSig] int GetAudioSessionControl(IntPtr id, uint flags, out IntPtr session);
        [PreserveSig] int GetSimpleAudioVolume(IntPtr id, uint flags, out IntPtr volume);
        [PreserveSig] int GetSessionEnumerator(out ISessionEnumerator sessions);
    }
    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, [MarshalAs(UnmanagedType.IUnknown)] out object session);
    }
    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISessionControl2
    {
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName(out IntPtr name);
        [PreserveSig] int SetDisplayName(IntPtr name, IntPtr context);
        [PreserveSig] int GetIconPath(out IntPtr path);
        [PreserveSig] int SetIconPath(IntPtr path, IntPtr context);
        [PreserveSig] int GetGroupingParam(out Guid group);
        [PreserveSig] int SetGroupingParam(ref Guid group, IntPtr context);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr client);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr client);
        [PreserveSig] int GetSessionIdentifier(out IntPtr id);
        [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr id);
        [PreserveSig] int GetProcessId(out uint pid);
    }
    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioMeter
    {
        [PreserveSig] int GetPeakValue(out float peak);
    }
    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISimpleVolume
    {
        [PreserveSig] int SetMasterVolume(float volume, IntPtr context);
        [PreserveSig] int GetMasterVolume(out float volume);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, IntPtr context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
'@
$timer = [Diagnostics.Stopwatch]::StartNew()
while ($timer.Elapsed.TotalSeconds -lt $Seconds) {
    $elapsed = $timer.Elapsed.TotalSeconds.ToString('F2', [Globalization.CultureInfo]::InvariantCulture)
    [PixelAudioMeter]::Read() | ForEach-Object { "t=$elapsed $_" }
    Start-Sleep -Milliseconds 200
}
