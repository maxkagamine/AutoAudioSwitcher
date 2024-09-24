// Copyright (c) Max Kagamine
// Licensed under the Apache License, Version 2.0

using System.Collections.ObjectModel;

namespace AutoAudioSwitcher;

internal class Settings : IEquatable<Settings?>
{
    /// <summary>
    /// Map of display names (as shown in the Settings app under "Advanced display settings" or similar) to playback
    /// device names (as set in the Sound control panel).
    /// </summary>
    public IReadOnlyDictionary<string, string> Monitors { get; init; } = ReadOnlyDictionary<string, string>.Empty;

    /// <summary>
    /// Whether to write debug messages to the error log.
    /// </summary>
    public bool EnableDebugLogging { get; init; }

    public override bool Equals(object? obj) => Equals(obj as Settings);

    public bool Equals(Settings? other)
    {
        return other is not null &&
               Monitors.Count == other.Monitors.Count &&
               Monitors.All(x => other.Monitors.TryGetValue(x.Key, out var value) && value.Equals(x.Value));
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Monitors.Count);
    }
}
