// Copyright (c) Max Kagamine
// Licensed under the Apache License, Version 2.0

using AutoAudioSwitcher.Properties;
using Microsoft.Win32;
using Serilog;
using System.Reactive.Disposables;
using System.Reactive.Linq;

namespace AutoAudioSwitcher;

internal class TrayIcon : IDisposable
{
    private const string EmptyStringMenuItem = "(Don't switch)";

    private readonly ILogger logger;
    private readonly NotifyIcon notifyIcon;
    private readonly IBehaviorObservable<Settings> settings;
    private readonly IObservable<ContextMenuStrip> menu;
    private readonly CompositeDisposable subscriptions = [];

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
            Text = Application.ProductName
        };

        UpdateIcon();
        subscriptions.Add(settings.Subscribe(_ => UpdateIcon()));
        subscriptions.Add(Observable.FromEventPattern<UserPreferenceChangedEventHandler, UserPreferenceChangedEventArgs>(
            handler => SystemEvents.UserPreferenceChanged += handler,
            handler => SystemEvents.UserPreferenceChanged -= handler)
            .Subscribe(_ => UpdateIcon()));

        notifyIcon.Click += (object? sender, EventArgs e) =>
        {
            if (e is MouseEventArgs { Button: MouseButtons.Left })
            {
                OnEnabledClicked(null, EventArgs.Empty);
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

    private void UpdateIcon(Settings? settings = null)
    {
        settings ??= this.settings.Value;

        notifyIcon.Icon = (Application.IsDarkModeEnabled, settings.Enabled) switch
        {
            (true, true) => Resources.TrayIconLight,
            (true, false) => Resources.TrayIconLightDisabled,
            (false, true) => Resources.TrayIconDark,
            (false, false) => Resources.TrayIconDarkDisabled
        };
    }

    public void Show()
    {
        subscriptions.Add(menu.Subscribe(menu =>
        {
            notifyIcon.ContextMenuStrip = menu;
            notifyIcon.Visible = true;
        }));
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

        UpdateIcon(newSettings); // Update icon with no delay
        newSettings.Save();
    }

    public void Dispose()
    {
        subscriptions.Dispose();

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
