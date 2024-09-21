// Copyright (c) Max Kagamine
// Licensed under the Apache License, Version 2.0

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Accessibility;
using static Windows.Win32.PInvoke;

namespace AutoAudioSwitcher;

/// <summary>
/// Monitors the current monitor.
/// </summary>
internal class CurrentMonitorMonitor
{
    private bool lastIsPrimary = true;

    public CurrentMonitorMonitor()
    {
        SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, null, WinEventProc, 0, 0, WINEVENT_OUTOFCONTEXT);
        SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE, null, WinEventProc, 0, 0, WINEVENT_OUTOFCONTEXT);
    }

    public event EventHandler<bool>? IsPrimaryChanged;

    private void WinEventProc(HWINEVENTHOOK hWinEventHook, uint @event, HWND hwnd, int idObject, int idChild, uint idEventThread, uint dwmsEventTime)
    {
        HWND foregroundWindow = GetForegroundWindow();
        if (foregroundWindow.IsNull)
        {
            Debug.WriteLine("Foreground window is null", nameof(WinEventProc));
            return;
        }

        HMONITOR monitor = MonitorFromWindow(foregroundWindow, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONULL);
        if (monitor == 0)
        {
            Debug.WriteLine("Monitor handle is null", nameof(WinEventProc));
            return;
        }

        MONITORINFO monitorInfo = new()
        {
            cbSize = (uint)Unsafe.SizeOf<MONITORINFO>()
        };

        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            Debug.WriteLine($"GetMonitorInfo failed: {Marshal.GetLastPInvokeErrorMessage()}", nameof(WinEventProc));
            return;
        }

        bool isPrimary = (monitorInfo.dwFlags & MONITORINFOF_PRIMARY) != 0;

        if (isPrimary != lastIsPrimary)
        {
            Debug.WriteLine($"isPrimary = {isPrimary}", nameof(WinEventProc));
            IsPrimaryChanged?.Invoke(this, isPrimary);
        }

        lastIsPrimary = isPrimary;
    }
}
