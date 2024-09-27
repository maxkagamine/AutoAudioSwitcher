// Copyright (c) Max Kagamine
// Licensed under the Apache License, Version 2.0

using Serilog;
using System.Reactive;
using System.Reactive.Subjects;
using static Windows.Win32.PInvoke;

namespace AutoAudioSwitcher;

internal class WindowMessageListener : NativeWindow
{
    private readonly Subject<Unit> displayChange = new();
    private readonly ILogger logger;

    public WindowMessageListener(ILogger logger)
    {
        this.logger = logger.ForContext<WindowMessageListener>();

        CreateHandle(new CreateParams());
    }

    /// <inheritdoc cref="WM_DISPLAYCHANGE"/>
    public IObservable<Unit> DisplayChange => displayChange;

    protected override void WndProc(ref Message m)
    {
        switch ((uint)m.Msg)
        {
            case WM_DISPLAYCHANGE:
                logger.Debug("WM_DISPLAYCHANGE");
                displayChange.OnNext(Unit.Default);
                break;

            // https://learn.microsoft.com/en-us/windows/win32/rstmgr/guidelines-for-applications
            case WM_QUERYENDSESSION:
                logger.Debug("WM_QUERYENDSESSION");
                m.Result = 1;
                return;

            // Detects when the tray icon is closed externally (e.g. by the installer when updating) and exits the
            // application to prevent it from remaining running in the background.
            case WM_ENDSESSION:
                logger.Debug("WM_ENDSESSION");
                Application.Exit();
                break;
            case WM_CLOSE:
                logger.Debug("WM_CLOSE");
                Application.Exit();
                break;
        }

        base.WndProc(ref m);
    }
}
