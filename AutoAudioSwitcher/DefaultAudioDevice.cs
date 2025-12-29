// Copyright (c) Max Kagamine
// Licensed under the Apache License, Version 2.0

namespace AutoAudioSwitcher;

/// <summary>
/// Represents the name of the default audio device (or devices) for multimedia and communications.
/// </summary>
internal sealed record DefaultAudioDevice(string Multimedia, string Communications) : IEquatable<string>
{
    public bool Equals(string? device) => device is not null && Multimedia == device && Communications == device;
    public static bool operator ==(DefaultAudioDevice defaultAudioDevice, string device) => defaultAudioDevice.Equals(device);
    public static bool operator !=(DefaultAudioDevice defaultAudioDevice, string device) => !defaultAudioDevice.Equals(device);

    public override string ToString() => Multimedia == Communications ?
        $"\"{Multimedia}\"" : $"\"{Multimedia}\" (Multimedia), \"{Communications}\" (Communications)";
}
