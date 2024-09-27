// Copyright (c) Max Kagamine
// Licensed under the Apache License, Version 2.0

using Serilog;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.InteropServices;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.WindowsAndMessaging;
using static Windows.Win32.PInvoke;
using static Windows.Win32.UI.WindowsAndMessaging.OBJECT_IDENTIFIER;

namespace AutoAudioSwitcher;

/// <summary>
/// Monitors the current monitor.
/// </summary>
internal class CurrentMonitorMonitor
{
    // Sometimes when plugging in a display, Windows will briefly steal focus to that display before switching back
    private static readonly TimeSpan DebounceTimeout = TimeSpan.FromMilliseconds(50);

    private readonly Subject<Monitor> subject = new();
    private readonly ILogger logger;
    private readonly ConnectedMonitorsMonitor connectedMonitorsMonitor;

    private readonly WINEVENTPROC winEventProc;

    public CurrentMonitorMonitor(ConnectedMonitorsMonitor connectedMonitorsMonitor, ILogger logger)
    {
        this.connectedMonitorsMonitor = connectedMonitorsMonitor;
        this.logger = logger.ForContext<CurrentMonitorMonitor>();

        // It's important to hold a reference to the delegate rather than directly pass the method (which compiles to an
        // inline new(WinEventProc)), as it's being passed to unmanaged code as a function pointer and we don't want the
        // garbage collector cleaning it up.
        //
        // This seems to have been the cause of an ExecutionEngineException (sometimes NullReferenceException) arising
        // from Application.Run() and originating at the PeekMessage() in Application.ComponentManager.FPushMessageLoop.
        winEventProc = new(WinEventProc);

        SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, null, winEventProc, 0, 0, WINEVENT_OUTOFCONTEXT);
        SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE, null, winEventProc, 0, 0, WINEVENT_OUTOFCONTEXT);
    }

    public IObservable<Monitor> CurrentMonitorChanged => subject
        .DistinctUntilChanged(x => x.GdiDeviceName)
        .Throttle(DebounceTimeout)
        .DistinctUntilChanged(x => x.GdiDeviceName);

    private unsafe void WinEventProc(HWINEVENTHOOK hWinEventHook, uint @event, HWND hwnd, int idObject, int idChild, uint idEventThread, uint dwmsEventTime)
    {
        if (idObject != (int)OBJID_WINDOW)
        {
            // EVENT_OBJECT_LOCATIONCHANGE also fires for the cursor and text caret
            return;
        }

        logger.Verbose("Received WinEvent {Event} (hwnd = {Hwnd}, idObject = {ObjectId}, idChild = {ChildId})",
            @event switch
            {
                EVENT_SYSTEM_FOREGROUND => nameof(EVENT_SYSTEM_FOREGROUND),
                EVENT_OBJECT_LOCATIONCHANGE => nameof(EVENT_OBJECT_LOCATIONCHANGE),
                _ => @event.ToString()
            },
            hwnd, (OBJECT_IDENTIFIER)idObject, idChild);

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
        Monitor[] currentMonitors = connectedMonitorsMonitor.CurrentConnectedMonitors;

        for (int i = 0; i < currentMonitors.Length; i++) // Loop instead of FirstOrDefault() to avoid memory allocations for lambda
        {
            if (currentMonitors[i].GdiDeviceName == gdiDisplayName)
            {
                subject.OnNext(currentMonitors[i]);
                return;
            }
        }

        logger.Error("GetMonitorInfo returned {Monitor}, but the current connected monitors are {@CurrentConnectedMonitors}",
            gdiDisplayName, currentMonitors);
    }
}
