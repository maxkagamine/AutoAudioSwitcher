// Copyright (c) Max Kagamine
// Licensed under the Apache License, Version 2.0

namespace AutoAudioSwitcher;

internal static class Program
{
    // TODO: Move to configuration (https://github.com/maxkagamine/AutoAudioSwitcher/issues/1)
    private const string PrimaryMonitorAudioDevice = "Speakers";
    private const string NonPrimaryMonitorAudioDevice = "TV";

    [STAThread]
    static void Main()
    {
        CurrentMonitorMonitor monitorMonitor = new();
        AudioDeviceSwitcher audioDeviceSwitcher = new();

        monitorMonitor.IsPrimaryChanged += (object? sender, bool isPrimary) =>
        {
            audioDeviceSwitcher.SetDefaultPlaybackDevice(isPrimary ?
                PrimaryMonitorAudioDevice : NonPrimaryMonitorAudioDevice);
        };

        Application.Run();
    }
}
