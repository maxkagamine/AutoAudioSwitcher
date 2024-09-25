// Copyright (c) Max Kagamine
// Licensed under the Apache License, Version 2.0

using Serilog.Events;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoAudioSwitcher;

internal sealed record Settings : IEquatable<Settings?>
{
    /// <summary>
    /// Map of display names (as shown in the Settings app under "Advanced display settings" or similar) to playback
    /// device names (as set in the Sound control panel).
    /// </summary>
    public IReadOnlyDictionary<string, string> Monitors { get; init; } = ReadOnlyDictionary<string, string>.Empty;

    /// <summary>
    /// If false, the application will remain running in the tray but auto-switching will be temporarily disabled.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Log level for the error log. Can be set to "Debug" to enable debug logging.
    /// </summary>
    public LogEventLevel LogLevel { get; init; } = LogEventLevel.Error;

    /// <summary>
    /// Writes the current <see cref="Settings"/> to appsettings.json.
    /// </summary>
    public void Save()
    {
        using FileStream file = File.Open("appsettings.json", FileMode.Create, FileAccess.Write);
        JsonSerializer.Serialize(file, this, SettingsSerializerContext.Default.Options);
    }

    public bool Equals(Settings? other)
    {
        return other is not null &&
               Monitors.Count == other.Monitors.Count &&
               Monitors.All(x => other.Monitors.TryGetValue(x.Key, out var value) && value.Equals(x.Value)) &&
               Enabled == other.Enabled &&
               LogLevel == other.LogLevel;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(
            Monitors.Count,
            Enabled,
            LogLevel);
    }
}

[JsonSerializable(typeof(Settings))]
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
internal partial class SettingsSerializerContext : JsonSerializerContext;
