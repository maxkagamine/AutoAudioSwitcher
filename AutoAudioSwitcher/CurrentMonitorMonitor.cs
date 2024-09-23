// Copyright (c) Max Kagamine
// Licensed under the Apache License, Version 2.0

using Serilog;
using System.Reactive.Linq;
using System.Reactive.Subjects;
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
    private readonly Subject<Monitor> subject = new();
    private readonly ILogger logger;
    private readonly ConnectedMonitorsMonitor connectedMonitorsMonitor;

    public CurrentMonitorMonitor(ConnectedMonitorsMonitor connectedMonitorsMonitor, ILogger logger)
    {
        this.connectedMonitorsMonitor = connectedMonitorsMonitor;
        this.logger = logger.ForContext<CurrentMonitorMonitor>();

        SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, null, WinEventProc, 0, 0, WINEVENT_OUTOFCONTEXT);
        SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE, null, WinEventProc, 0, 0, WINEVENT_OUTOFCONTEXT);
    }

    public IObservable<Monitor> CurrentMonitorChanged => subject.DistinctUntilChanged(x => x.GdiDeviceName);

    private unsafe void WinEventProc(HWINEVENTHOOK hWinEventHook, uint @event, HWND hwnd, int idObject, int idChild, uint idEventThread, uint dwmsEventTime)
    {
        HWND foregroundWindow = GetForegroundWindow();
        if (foregroundWindow.IsNull)
        {
            // According to the docs, "the foreground window can be null in certain circumstances, such as when a window
            // is losing activation."
            return;
        }

        HMONITOR monitorHandle = MonitorFromWindow(foregroundWindow, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONULL);
        if (monitorHandle == 0)
        {
            // This can happen if "the window does not intersect a display monitor" for whatever reason.
            return;
        }

        MONITORINFOEXW monitorInfo = new()
        {
            monitorInfo = new()
            {
                cbSize = (uint)sizeof(MONITORINFOEXW)
            }
        };

        if (!GetMonitorInfo(monitorHandle, (MONITORINFO*)&monitorInfo))
        {
            logger.Error("GetMonitorInfo failed: {Message}", Marshal.GetLastPInvokeErrorMessage());
            return;
        }

        string gdiDisplayName = monitorInfo.szDevice.ToString();
        var currentMonitors = connectedMonitorsMonitor.CurrentConnectedMonitors;
        Monitor? monitor = currentMonitors.FirstOrDefault(m => m.GdiDeviceName == gdiDisplayName);
        if (monitor is null)
        {
            logger.Error("GetMonitorInfo returned {Monitor}, but the current connected monitors are {@CurrentConnectedMonitors}",
                gdiDisplayName, currentMonitors);
            return;
        }

        subject.OnNext(monitor);
    }
}
