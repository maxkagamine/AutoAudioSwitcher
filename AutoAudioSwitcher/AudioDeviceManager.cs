// Copyright (c) Max Kagamine
// Licensed under the Apache License, Version 2.0

using CoreAudio;
using Serilog;
using Serilog.Events;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace AutoAudioSwitcher;

internal sealed class AudioDeviceManager : IDisposable
{
    private readonly MMDeviceEnumerator deviceEnumerator;
    private readonly MMNotificationClient notificationClient;
    private readonly BehaviorSubject<DefaultAudioDevice> defaultPlaybackDevice;
    private readonly CompositeDisposable subscriptions = [];
    private readonly ILogger logger;

    public AudioDeviceManager(ILogger logger)
    {
        this.logger = logger = logger.ForContext<AudioDeviceManager>();

        deviceEnumerator = new();
        notificationClient = new(deviceEnumerator);

        var deviceAdded = Observable.FromEventPattern<DeviceNotificationEventArgs>(
            handler => notificationClient.DeviceAdded += handler,
            handler => notificationClient.DeviceAdded -= handler)
            .Do(e => LogDeviceEvent("DeviceAdded", e.EventArgs))
            .Select(_ => Unit.Default);

        var deviceRemoved = Observable.FromEventPattern<DeviceNotificationEventArgs>(
            handler => notificationClient.DeviceRemoved += handler,
            handler => notificationClient.DeviceRemoved -= handler)
            .Do(e => LogDeviceEvent("DeviceRemoved", e.EventArgs))
            .Select(_ => Unit.Default);

        var deviceStateChanged = Observable.FromEventPattern<DeviceStateChangedEventArgs>( // Active, disabled, unplugged
            handler => notificationClient.DeviceStateChanged += handler,
            handler => notificationClient.DeviceStateChanged -= handler)
            .Do(e => LogDeviceEvent($"DeviceStateChanged ({e.EventArgs.DeviceState})", e.EventArgs))
            .Select(_ => Unit.Default);

        var deviceDescriptionChanged = Observable.FromEventPattern<DevicePropertyChangedEventArgs>( // Device name, etc.
            handler => notificationClient.DevicePropertyChanged += handler,
            handler => notificationClient.DevicePropertyChanged -= handler)
            .Where(e => e.EventArgs.PropertyKey == PKey.DeviceDescription)
            .Do(e => LogDeviceEvent("DevicePropertyChanged (DeviceDescription)", e.EventArgs))
            .Select(_ => Unit.Default);

        var playbackDevices = Observable.Merge(deviceAdded, deviceRemoved, deviceStateChanged, deviceDescriptionChanged)
            .StartWith(Unit.Default)
            .Select(_ => EnumeratePlaybackDevices()
                .Select(d => GetDeviceName(d))
                .Distinct()
                .Order()
                .ToArray())
            .DistinctUntilChanged(EqualityComparer<IEnumerable<string>>.Create(
                (a, b) => a is null ? b is null : b is not null && a.SequenceEqual(b)))
            .Do(devices => logger.Information("Playback devices: {Devices}", devices))
            .Replay(1);

        playbackDevices.Connect().DisposeWith(subscriptions);
        PlaybackDevices = playbackDevices;

        // There is also a "Console" role for system sounds and (oddly) games that's distinct from Multimedia, but it's
        // not exposed in the Windows UI, and in fact when you change one the OS automatically sets the other.
        var initialDefaultMultimediaDevice = GetDeviceName(deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia));
        var initialDefaultCommunicationsDevice = GetDeviceName(deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Communications));

        defaultPlaybackDevice = new(new(initialDefaultMultimediaDevice, initialDefaultCommunicationsDevice));

        Observable.FromEventPattern<DefaultDeviceChangedEventArgs>(
            handler => notificationClient.DefaultDeviceChanged += handler,
            handler => notificationClient.DefaultDeviceChanged -= handler)
            .Where(e => e.EventArgs.DataFlow is DataFlow.Render &&
                        e.EventArgs.Role is Role.Multimedia or Role.Communications)
            .Scan(defaultPlaybackDevice.Value, (DefaultAudioDevice d, EventPattern<DefaultDeviceChangedEventArgs> e) =>
                e.EventArgs.Role is Role.Multimedia ?
                    d with { Multimedia = GetDeviceName(e.EventArgs) } :
                    d with { Communications = GetDeviceName(e.EventArgs) })
            .Throttle(TimeSpan.FromMilliseconds(20))
            .DistinctUntilChanged()
            .Do(d => logger.Information("Default playback device changed to {DefaultDevice}", d))
            .Subscribe(defaultPlaybackDevice)
            .DisposeWith(subscriptions);
    }

    /// <summary>
    /// The names of the active playback devices, sorted. Observers will receive the latest value immediately.
    /// </summary>
    public IObservable<IEnumerable<string>> PlaybackDevices { get; }

    /// <summary>
    /// The names of the default multimedia and communications playback devices. Observers will receive the latest value
    /// immediately.
    /// </summary>
    public IObservable<DefaultAudioDevice> DefaultPlaybackDevice => defaultPlaybackDevice;

    /// <summary>
    /// The names of the default multimedia and communications playback devices.
    /// </summary>
    public DefaultAudioDevice CurrentDefaultPlaybackDevice => defaultPlaybackDevice.Value;

    public void SetDefaultPlaybackDevice(string name)
    {
        try
        {
            if (CurrentDefaultPlaybackDevice == name)
            {
                logger.Information("Default playback device is already \"{Name}\"", name);
                return;
            }

            MMDevice? device = EnumeratePlaybackDevices().FirstOrDefault(d => GetDeviceName(d) == name);

            if (device is null)
            {
                logger.Error("No device with name \"{Name}\"", name);
                return;
            }

            logger.Information("Switching to \"{Name}\"", name);
            device.Selected = true;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to set default playback device");
        }
    }

    private MMDeviceCollection EnumeratePlaybackDevices() =>
        deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

    private static string GetDeviceName(MMDevice device) =>
        device.Properties?[PKey.DeviceDescription]?.Value.ToString() ?? "<Unknown>";

    private static string GetDeviceName(DeviceNotificationEventArgs e) =>
        e.TryGetDevice(out MMDevice? device) ? GetDeviceName(device!) : "<Unknown>";

    private void LogDeviceEvent(string eventName, DeviceNotificationEventArgs e)
    {
        if (logger.IsEnabled(LogEventLevel.Debug))
        {
            logger.Debug("{Event}: \"{Device}\"", eventName, GetDeviceName(e));
        }
    }

    public void Dispose()
    {
        subscriptions.Dispose();
        defaultPlaybackDevice.Dispose();
    }
}
