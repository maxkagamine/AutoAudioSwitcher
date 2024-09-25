// Copyright (c) Max Kagamine
// Licensed under the Apache License, Version 2.0

using CoreAudio;
using Serilog;
using System.Reactive;
using System.Reactive.Linq;

namespace AutoAudioSwitcher;

internal class AudioDeviceManager : IDisposable
{
    private readonly MMDeviceEnumerator deviceEnumerator;
    private readonly MMNotificationClient notificationClient;
    private readonly ILogger logger;
    private readonly IDisposable playbackDevicesSubscription;

    public AudioDeviceManager(ILogger logger)
    {
        this.logger = logger = logger.ForContext<AudioDeviceManager>();

        deviceEnumerator = new();
        notificationClient = new(deviceEnumerator);

        var deviceAdded = Observable.FromEventPattern<DeviceNotificationEventArgs>(
            handler => notificationClient.DeviceAdded += handler,
            handler => notificationClient.DeviceAdded -= handler)
            .Do(e => logger.Debug("DeviceAdded: {DeviceId}", e.EventArgs.DeviceId))
            .Select(_ => Unit.Default);

        var deviceRemoved = Observable.FromEventPattern<DeviceNotificationEventArgs>(
            handler => notificationClient.DeviceRemoved += handler,
            handler => notificationClient.DeviceRemoved -= handler)
            .Do(e => logger.Debug("DeviceRemoved: {DeviceId}", e.EventArgs.DeviceId))
            .Select(_ => Unit.Default);

        var deviceStateChanged = Observable.FromEventPattern<DeviceStateChangedEventArgs>( // Active, disabled, unplugged
            handler => notificationClient.DeviceStateChanged += handler,
            handler => notificationClient.DeviceStateChanged -= handler)
            .Do(e => logger.Debug("DeviceStateChanged ({State}): {DeviceId}", e.EventArgs.DeviceState, e.EventArgs.DeviceId))
            .Select(_ => Unit.Default);

        var deviceDescriptionChanged = Observable.FromEventPattern<DevicePropertyChangedEventArgs>( // Device name, etc.
            handler => notificationClient.DevicePropertyChanged += handler,
            handler => notificationClient.DevicePropertyChanged -= handler)
            .Where(e => e.EventArgs.PropertyKey == PKey.DeviceDescription)
            .Do(e => logger.Debug("DevicePropertyChanged (DeviceDescription): {DeviceId}", e.EventArgs.DeviceId))
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

        playbackDevicesSubscription = playbackDevices.Connect();
        PlaybackDevices = playbackDevices;
    }

    /// <summary>
    /// The names of the active playback devices, sorted. Observers will receive the latest value immediately.
    /// </summary>
    public IObservable<IEnumerable<string>> PlaybackDevices { get; }

    public void SetDefaultPlaybackDevice(string name)
    {
        try
        {
            MMDevice? device = EnumeratePlaybackDevices().FirstOrDefault(d => GetDeviceName(d) == name);

            if (device is null)
            {
                logger.Error("No device with name {Name}.", name);
                return;
            }

            logger.Information("Switching to {Name}", name);
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

    public void Dispose()
    {
        playbackDevicesSubscription.Dispose();
    }
}
