// Copyright (c) Max Kagamine
// Licensed under the Apache License, Version 2.0

using System.Diagnostics;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.InteropServices;
using Windows.Win32.Devices.Display;
using Windows.Win32.Foundation;
using static Windows.Win32.Devices.Display.DISPLAYCONFIG_DEVICE_INFO_TYPE;
using static Windows.Win32.Devices.Display.QUERY_DISPLAY_CONFIG_FLAGS;
using static Windows.Win32.Foundation.WIN32_ERROR;
using static Windows.Win32.PInvoke;

namespace AutoAudioSwitcher;

/// <summary>
/// Enumerates and observes connected monitors. One might say it monitors them. The monitors, that is. This creates a
/// hidden window to listen for window messages to detect monitors being connected and disconnected.
/// </summary>
internal class ConnectedMonitorsMonitor : NativeWindow, IDisposable
{
    private const string UnknownMonitorName = "Unknown";

    private readonly BehaviorSubject<IEnumerable<Monitor>> monitors = new(GetMonitors());

    public ConnectedMonitorsMonitor()
    {
        monitors.Subscribe(currentMonitors =>
        {
            Debug.WriteLine($"Connected monitors:\n  {string.Join("\n  ",
                currentMonitors.Select(m => $"{m.GdiDeviceName}: {m.FriendlyName}"))}", nameof(ConnectedMonitorsMonitor));
        });

        CreateHandle(new CreateParams());
    }

    public IObservable<IEnumerable<Monitor>> ConnectedMonitors => monitors;

    public IEnumerable<Monitor> CurrentConnectedMonitors => monitors.Value;

    private static unsafe Monitor[] GetMonitors()
    {
        // EnumDisplayDevices doesn't return the same monitor names as shown in the control panel (in my case, both
        // displays returned simply "Generic PnP Monitor"), so we have to use a newer, more complicated API instead:
        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-querydisplayconfig#examples

        Span<DISPLAYCONFIG_PATH_INFO> paths;
        Span<DISPLAYCONFIG_MODE_INFO> modes;
        QUERY_DISPLAY_CONFIG_FLAGS flags = QDC_ONLY_ACTIVE_PATHS | QDC_VIRTUAL_MODE_AWARE;
        WIN32_ERROR result;

        List<Monitor> monitors = [];

        do
        {
            result = GetDisplayConfigBufferSizes(flags, out uint pathCount, out uint modeCount);
            if (result != ERROR_SUCCESS)
            {
                Debug.WriteLine($"GetDisplayConfigBufferSizes failed: {Marshal.GetLastPInvokeErrorMessage()}", nameof(ConnectedMonitorsMonitor));
                return [];
            }

            paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            modes = new DISPLAYCONFIG_MODE_INFO[modeCount];

            fixed (DISPLAYCONFIG_PATH_INFO* pathsPtr = &paths[0])
            fixed (DISPLAYCONFIG_MODE_INFO* modesPtr = &modes[0])
            {
                result = QueryDisplayConfig(flags, ref pathCount, pathsPtr, ref modeCount, modesPtr, (DISPLAYCONFIG_TOPOLOGY_ID*)0);
            }

            paths = paths[..(int)pathCount];
            modes = modes[..(int)modeCount];
        }
        while (result == ERROR_INSUFFICIENT_BUFFER);

        if (result != ERROR_SUCCESS)
        {
            Debug.WriteLine($"QueryDisplayConfig failed: {Marshal.GetLastPInvokeErrorMessage()}", nameof(ConnectedMonitorsMonitor));
            return [];
        }

        foreach (DISPLAYCONFIG_PATH_INFO path in paths)
        {
            DISPLAYCONFIG_TARGET_DEVICE_NAME targetName = new()
            {
                header = new()
                {
                    adapterId = path.targetInfo.adapterId,
                    id = path.targetInfo.id,
                    type = DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME,
                    size = (uint)sizeof(DISPLAYCONFIG_TARGET_DEVICE_NAME)
                }
            };

            result = (WIN32_ERROR)DisplayConfigGetDeviceInfo((DISPLAYCONFIG_DEVICE_INFO_HEADER*)&targetName);
            if (result != ERROR_SUCCESS)
            {
                Debug.WriteLine($"DisplayConfigGetDeviceInfo(targetName) failed: {Marshal.GetLastPInvokeErrorMessage()}", nameof(ConnectedMonitorsMonitor));
                return [];
            }

            // The "adapter name", as shown in the docs' example, is a long path that looks like this:
            //   \\?\PCI#VEN_10DE&DEV_2204&SUBSYS_39873842&REV_A1#4&1d81e16&0&0019#{5b45201d-f2f2-4f3b-85bb-30ff1f953599}
            // To match with MONITORINFOEX, we need the GDI device name, which is the "source name" and looks like:
            //   \\.\DISPLAY1
            DISPLAYCONFIG_SOURCE_DEVICE_NAME sourceName = new()
            {
                header = new()
                {
                    adapterId = path.targetInfo.adapterId,
                    id = path.sourceInfo.id,
                    type = DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME,
                    size = (uint)sizeof(DISPLAYCONFIG_SOURCE_DEVICE_NAME)
                }
            };

            result = (WIN32_ERROR)DisplayConfigGetDeviceInfo((DISPLAYCONFIG_DEVICE_INFO_HEADER*)&sourceName);
            if (result != ERROR_SUCCESS)
            {
                Debug.WriteLine($"DisplayConfigGetDeviceInfo(sourceName) failed: {Marshal.GetLastPInvokeErrorMessage()}", nameof(ConnectedMonitorsMonitor));
                return [];
            }

            monitors.Add(new Monitor(
                sourceName.viewGdiDeviceName.ToString(),
                targetName.flags.Anonymous.Anonymous.friendlyNameFromEdid ? targetName.monitorFriendlyDeviceName.ToString() : UnknownMonitorName));
        }

        return [.. monitors];
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_DISPLAYCHANGE)
        {
            Debug.WriteLine("WM_DISPLAYCHANGE", nameof(ConnectedMonitorsMonitor));

            Monitor[] newMonitors = GetMonitors();
            if (!newMonitors.SequenceEqual(CurrentConnectedMonitors))
            {
                monitors.OnNext(newMonitors);
            }
        }

        base.WndProc(ref m);
    }

    public void Dispose()
    {
        monitors.Dispose();
    }
}
