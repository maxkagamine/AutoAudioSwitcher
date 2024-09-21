// Copyright (c) Max Kagamine
// Licensed under the Apache License, Version 2.0

using CoreAudio;
using System.Diagnostics;

namespace AutoAudioSwitcher;

internal class AudioDeviceSwitcher
{
    private readonly MMDeviceEnumerator deviceEnumerator = new();

    public AudioDeviceSwitcher()
    {
#if DEBUG
        MMDeviceCollection devices = EnumeratePlaybackDevices();
        Debug.WriteLine($"Playback devices:\n  {string.Join("\n  ", devices.Select(GetDeviceName))}", nameof(AudioDeviceSwitcher));
#endif
    }

    public void SetDefaultPlaybackDevice(string name)
    {
        MMDevice? device = EnumeratePlaybackDevices().FirstOrDefault(d => GetDeviceName(d) == name);

        if (device is null)
        {
            Debug.WriteLine($"No device with name \"{name}\".", nameof(SetDefaultPlaybackDevice));
            return;
        }

        Debug.WriteLine($"Switching to {name}", nameof(SetDefaultPlaybackDevice));
        device.Selected = true;
    }

    private MMDeviceCollection EnumeratePlaybackDevices() =>
        deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

    private static string GetDeviceName(MMDevice device) =>
        device.Properties?[PKey.DeviceDescription]?.Value.ToString() ?? "<Unknown>";
}
