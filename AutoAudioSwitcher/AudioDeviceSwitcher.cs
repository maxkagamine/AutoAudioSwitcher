// Copyright (c) Max Kagamine
// Licensed under the Apache License, Version 2.0

using CoreAudio;
using Serilog;

namespace AutoAudioSwitcher;

internal class AudioDeviceSwitcher
{
    private readonly MMDeviceEnumerator deviceEnumerator = new();
    private readonly ILogger logger;

    public AudioDeviceSwitcher(ILogger logger)
    {
        this.logger = logger = logger.ForContext<AudioDeviceSwitcher>();

        try
        {
            MMDeviceCollection devices = EnumeratePlaybackDevices();
            logger.Information("Playback devices: {Devices}", devices.Select(GetDeviceName));
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to enumerate playback devices");
        }
    }

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
}
