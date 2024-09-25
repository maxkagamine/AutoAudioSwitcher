// Copyright (c) Max Kagamine
// Licensed under the Apache License, Version 2.0

using AutoAudioSwitcher.Properties;
using Serilog;
using System.Reactive.Linq;
using System.Reflection;

namespace AutoAudioSwitcher;

internal class TrayIcon : IDisposable
{
    private const string EmptyStringMenuItem = "(Don't switch)";

    private readonly ILogger logger;
    private readonly NotifyIcon notifyIcon;
    private readonly IObservable<ContextMenuStrip> menu;
    private readonly IBehaviorObservable<Settings> settings;
    private IDisposable? menuSubscription;

    public TrayIcon(
        ConnectedMonitorsMonitor connectedMonitorsMonitor,
        AudioDeviceManager audioDeviceManager,
        IBehaviorObservable<Settings> settings,
        ILogger logger)
    {
        this.logger = logger = logger.ForContext<TrayIcon>();
        this.settings = settings;

        notifyIcon = new()
        {
            Text = Application.ProductName,
            Icon = Resources.TrayIconLight // TODO: Detect light/dark mode
        };

        notifyIcon.Click += (object? sender, EventArgs e) =>
        {
            if (e is MouseEventArgs { Button: MouseButtons.Left })
            {
                // There's no better way to do this
                MethodInfo showContextMenu = typeof(NotifyIcon).GetMethod("ShowContextMenu",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                showContextMenu.Invoke(notifyIcon, null);
            }
        };

        menu = Observable.CombineLatest(
            connectedMonitorsMonitor.ConnectedMonitors,
            audioDeviceManager.PlaybackDevices,
            settings,
            (connectedMonitors, playbackDevices, settings) =>
            {
                logger.Debug("Rebuilding tray menu");

                ContextMenuStrip menu = new();

                foreach (var monitorName in connectedMonitors.Select(m => m.FriendlyName).Order())
                {
                    string monitorPlaybackDevice = settings.Monitors.GetValueOrDefault(monitorName) ?? "";

                    ToolStripMenuItem monitorItem = new(
                        text: monitorName,
                        image: null,
                        dropDownItems: [
                            .. playbackDevices.Select(deviceName =>
                                new PlaybackDeviceMenuItem(this, monitorName, deviceName, monitorPlaybackDevice)),
                            new ToolStripSeparator(),
                            new PlaybackDeviceMenuItem(this, monitorName, "", monitorPlaybackDevice)
                        ]);

                    menu.Items.Add(monitorItem);
                }

                menu.Items.Add(new ToolStripSeparator());

                ToolStripMenuItem enabledItem = new("Enabled") { Checked = settings.Enabled };
                enabledItem.Click += OnEnabledClicked;
                menu.Items.Add(enabledItem);

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

    private void OnPlaybackDeviceMenuItemClicked(object? sender, EventArgs e)
    {
        var item = (PlaybackDeviceMenuItem)sender!;

        logger.Debug("Setting playback device for {MonitorName} to {DeviceName}",
            item.MonitorName, item.DeviceName);

        var newSettings = settings.Value with
        {
            Monitors = new Dictionary<string, string>(settings.Value.Monitors)
            {
                [item.MonitorName] = item.DeviceName
            }
        };

        newSettings.Save();
    }

    private void OnEnabledClicked(object? sender, EventArgs e)
    {
        var newSettings = settings.Value with { Enabled = !settings.Value.Enabled };

        logger.Debug("Changing Enabled to {Enabled}", newSettings.Enabled);

        newSettings.Save();
    }

    public void Dispose()
    {
        menuSubscription?.Dispose();

        notifyIcon.Visible = false;
        notifyIcon.Dispose();
    }

    private class PlaybackDeviceMenuItem : ToolStripMenuItem
    {
        public PlaybackDeviceMenuItem(
            TrayIcon trayIcon,
            string monitorName,
            string deviceName,
            string? deviceNameSetForMonitor)
            : base(deviceName == "" ? EmptyStringMenuItem : deviceName)
        {
            MonitorName = monitorName;
            DeviceName = deviceName;
            Checked = deviceName == (deviceNameSetForMonitor ?? "");
            Click += trayIcon.OnPlaybackDeviceMenuItemClicked;
        }

        public string MonitorName { get; }

        public string DeviceName { get; }
    }
}
