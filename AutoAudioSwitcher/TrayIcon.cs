// Copyright (c) Max Kagamine
// Licensed under the Apache License, Version 2.0

using AutoAudioSwitcher.Properties;
using System.Reactive.Linq;

namespace AutoAudioSwitcher;

internal class TrayIcon : IDisposable
{
    private readonly NotifyIcon notifyIcon;
    private readonly IObservable<ContextMenuStrip> menu;
    private IDisposable? menuSubscription;

    public TrayIcon(
        ConnectedMonitorsMonitor connectedMonitorsMonitor,
        AudioDeviceManager audioDeviceManager,
        IBehaviorObservable<Settings> settings)
    {
        notifyIcon = new()
        {
            Text = Application.ProductName,
            Icon = Resources.TrayIconLight // TODO: Detect light/dark mode
        };

        menu = Observable.CombineLatest(
            connectedMonitorsMonitor.ConnectedMonitors,
            audioDeviceManager.PlaybackDevices,
            settings,
            (connectedMonitors, playbackDevices, settings) =>
            {
                ContextMenuStrip menu = new();

                foreach (var monitorName in connectedMonitors.Select(m => m.FriendlyName).Order())
                {
                    string monitorPlaybackDevice = settings.Monitors.GetValueOrDefault(monitorName) ?? "";

                    ToolStripMenuItem monitorItem = new(
                        text: monitorName,
                        image: null,
                        dropDownItems: playbackDevices
                            .Select(deviceName =>
                            {
                                ToolStripMenuItem deviceItem = new(deviceName)
                                {
                                    Checked = deviceName == monitorPlaybackDevice
                                };

                                // TODO: Update settings when clicked; add "(Don't switch)" option equal to empty string

                                return deviceItem;
                            })
                            .ToArray());

                    menu.Items.Add(monitorItem);
                }

                menu.Items.Add(new ToolStripSeparator());

                // TODO: Handle enabled/disabled
                menu.Items.Add(new ToolStripMenuItem("Enabled") { Checked = true, CheckOnClick = true });

                ToolStripMenuItem exitItem = new("Exit");
                exitItem.Click += (_, _) => Application.Exit();
                menu.Items.Add(exitItem);

                return menu;
            });
    }

    public void Show()
    {
        menuSubscription = menu.Subscribe(menu =>
        {
            notifyIcon.ContextMenuStrip = menu;
            notifyIcon.Visible = true;
        });
    }

    public void Dispose()
    {
        menuSubscription?.Dispose();

        notifyIcon.Visible = false;
        notifyIcon.Dispose();
    }
}
