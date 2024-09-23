// Copyright (c) Max Kagamine
// Licensed under the Apache License, Version 2.0

using Serilog;
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
    private readonly ILogger logger;
    private bool lastIsPrimary = true;

    public CurrentMonitorMonitor(ILogger logger)
    {
        this.logger = logger.ForContext<CurrentMonitorMonitor>();

        SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, null, WinEventProc, 0, 0, WINEVENT_OUTOFCONTEXT);
        SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE, null, WinEventProc, 0, 0, WINEVENT_OUTOFCONTEXT);
    }

    public event EventHandler<bool>? IsPrimaryChanged;

    private void WinEventProc(HWINEVENTHOOK hWinEventHook, uint @event, HWND hwnd, int idObject, int idChild, uint idEventThread, uint dwmsEventTime)
    {
        HWND foregroundWindow = GetForegroundWindow();
        if (foregroundWindow.IsNull)
        {
            // According to the docs, "the foreground window can be null in certain circumstances, such as when a window
            // is losing activation."
            return;
        }

        HMONITOR monitor = MonitorFromWindow(foregroundWindow, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONULL);
        if (monitor == 0)
        {
            // This can happen if "the window does not intersect a display monitor" for whatever reason.
            return;
        }

        MONITORINFO monitorInfo = new()
        {
            cbSize = (uint)Unsafe.SizeOf<MONITORINFO>()
        };

        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            logger.Error("GetMonitorInfo failed: {Message}", Marshal.GetLastPInvokeErrorMessage());
            return;
        }

        bool isPrimary = (monitorInfo.dwFlags & MONITORINFOF_PRIMARY) != 0;

        if (isPrimary != lastIsPrimary)
        {
            logger.Debug("isPrimary = {IsPrimary}", isPrimary);
            IsPrimaryChanged?.Invoke(this, isPrimary);
        }

        lastIsPrimary = isPrimary;
    }
}
