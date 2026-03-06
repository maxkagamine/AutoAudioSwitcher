// Copyright (c) Max Kagamine
// Licensed under the Apache License, Version 2.0

using AutoAudioSwitcher.Properties;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Templates;
using System.Diagnostics;
using System.Reactive;
using System.Reactive.Linq;
using System.Text.Json;

namespace AutoAudioSwitcher;

internal sealed class Program
{
    private static readonly TimeSpan WindowsAutomaticDefaultDeviceChangeThreshold = TimeSpan.FromSeconds(2);

    private const string LogsDirectory = "logs";
    public const string SettingsFile = "appsettings.json";

    private static readonly LoggingLevelSwitch levelSwitch = new(LogEventLevel.Error);
    private static ServiceProvider? provider;
    private static ILogger? logger;
    private static Mutex? singleInstanceMutex;

    private static ServiceProvider ConfigureServices()
    {
        ServiceCollection services = new();

        if (!File.Exists(SettingsFile))
        {
            new Settings().Save();
        }

        IConfiguration config = new ConfigurationBuilder()
            .AddJsonFile(SettingsFile, optional: false, reloadOnChange: true)
            .SetFileLoadExceptionHandler(e =>
            {
                // Happens if the file is empty or not a JSON object. This will revert the settings to default; no point
                // showing an error here since IConfiguration normally ignores invalid property values anyway.
                e.Ignore = e.Exception.GetBaseException() is JsonException;
            })
            .Build();

        services.ConfigureObservable<Settings>(config);

        levelSwitch.MinimumLevel = config.GetValue<LogEventLevel>(nameof(Settings.LogLevel));

        services.AddSingleton<ILogger>(_ => new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Debug()
            .WriteTo.File(
                path: Path.Combine(LogsDirectory, ".log"),
                formatter: new ExpressionTemplate(
                    "{@t:yyyy-MM-dd HH:mm:ss.fff zzz} [{@l:u3}] {#if SourceContext is not null}[{Substring(SourceContext, LastIndexOf(SourceContext, '.') + 1)}] {#end}{@m}\n{@x}"),
                levelSwitch: levelSwitch,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 5,
                fileSizeLimitBytes: 10485760 /* 10 MiB */)
            .CreateLogger());

        services.AddSingleton<AudioDeviceManager>();
        services.AddSingleton<ConnectedMonitorsMonitor>();
        services.AddSingleton<CurrentMonitorMonitor>();
        services.AddSingleton<TrayIcon>();
        services.AddSingleton<WindowMessageListener>();

        return services.BuildServiceProvider();
    }

    [STAThread]
    public static void Main()
    {
        singleInstanceMutex = new(true, "f09f929b-e98f-a1e9-9fb3-e383aae383b3" /* This is my favorite GUID */, out bool createdNew);
        if (!createdNew)
        {
            return;
        }

        Environment.CurrentDirectory = AppContext.BaseDirectory;

        //CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new("ja-JP");

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (sender, e) => HandleCrash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (sender, e) => HandleCrash((Exception)e.ExceptionObject);

        ApplicationConfiguration.Initialize();

        // Automatic dark mode is restricted to Win11+ for no good reason. It uses the "AppsUseLightTheme" setting
        // rather than "SystemUsesLightTheme" anyway, so to be consistent with the taskbar and system icons' context
        // menus, we'll manage it ourselves instead.
        Application.SetColorMode(IsSystemDarkModeEnabled() ? SystemColorMode.Dark : SystemColorMode.Classic);
        SystemEvents.UserPreferenceChanged += (_, _) =>
        {
            Application.SetColorMode(IsSystemDarkModeEnabled() ? SystemColorMode.Dark : SystemColorMode.Classic);
        };

        provider = ConfigureServices();
        logger = provider.GetRequiredService<ILogger>();
        logger.Information("Application is starting.");

        Application.ApplicationExit += (_, _) =>
        {
            logger.Information("Application is exiting.");
            provider.Dispose();
        };

        var settings = provider.GetRequiredService<IBehaviorObservable<Settings>>();
        settings.Subscribe(currentSettings =>
        {
            levelSwitch.MinimumLevel = currentSettings.LogLevel;

            logger.Information("Loaded settings: {@Settings}", currentSettings);
        });

        var connectedMonitorsMonitor = provider.GetRequiredService<ConnectedMonitorsMonitor>();
        connectedMonitorsMonitor.ConnectedMonitors.Subscribe(currentMonitors =>
        {
            logger.Information("Connected monitors: {Monitors}", currentMonitors.Select(m => m.FriendlyName));

            AddNewMonitorsToSettings(settings, currentMonitors, logger);
        });

        var currentMonitorMonitor = provider.GetRequiredService<CurrentMonitorMonitor>();
        var audioDeviceManager = provider.GetRequiredService<AudioDeviceManager>();

        // See https://github.com/maxkagamine/AutoAudioSwitcher/issues/11
        IObservable<Monitor> currentMonitorWhenWindowsChangesDefaultDeviceAutomatically =
            audioDeviceManager.PlaybackDevices.Skip(1)
                .Join(
                    right: audioDeviceManager.DefaultPlaybackDevice.Skip(1),
                    leftDurationSelector: _ => Observable.Timer(WindowsAutomaticDefaultDeviceChangeThreshold),
                    rightDurationSelector: _ => Observable.Empty<Unit>(),
                    resultSelector: (_, _) => currentMonitorMonitor.CurrentCurrentMonitor /* lol */)
                .Where(x => x is not null && settings.Value.Enabled)
                .Do(_ => logger.Information("Detected Windows automatically changing the default audio device. Rechecking the current monitor..."))!;

        currentMonitorMonitor.CurrentMonitor
            .Merge(currentMonitorWhenWindowsChangesDefaultDeviceAutomatically)
            .Subscribe(currentMonitor =>
            {
                if (!settings.Value.Enabled)
                {
                    return;
                }

                logger.Information("Current monitor is \"{CurrentMonitor}\"", currentMonitor.FriendlyName);

                if (settings.Value.Monitors.TryGetValue(currentMonitor.FriendlyName, out string? playbackDevice) &&
                    !string.IsNullOrEmpty(playbackDevice))
                {
                    audioDeviceManager.SetDefaultPlaybackDevice(playbackDevice);
                }
                else
                {
                    logger.Information("No playback device set for \"{CurrentMonitor}\"", currentMonitor.FriendlyName);
                }
            });

        provider.GetRequiredService<WindowMessageListener>();
        provider.GetRequiredService<TrayIcon>().Show();

        Application.Run();
    }

    private static void AddNewMonitorsToSettings(
        IBehaviorObservable<Settings> settings, IEnumerable<Monitor> currentMonitors, ILogger logger)
    {
        try
        {
            string[] newMonitors = currentMonitors
                .Select(m => m.FriendlyName)
                .Except(settings.Value.Monitors.Keys)
                .Distinct()
                .ToArray();

            if (newMonitors.Length == 0)
            {
                return;
            }

            logger.Information("Adding new monitors to appsettings.json: {Monitors}", newMonitors);

            var newSettings = settings.Value with
            {
                Monitors = new Dictionary<string, string>([
                    .. settings.Value.Monitors,
                    .. newMonitors.Select(m => new KeyValuePair<string, string>(m, ""))])
            };

            newSettings.Save();
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to add new monitors to appsettings.json");
        }
    }

    public static bool IsSystemDarkModeEnabled()
    {
        // https://github.com/maxkagamine/AutoAudioSwitcher/issues/9
        int? systemUsesLightTheme = null;

        try
        {
            systemUsesLightTheme = Registry.GetValue(
                keyName: @"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                valueName: "SystemUsesLightTheme",
                defaultValue: 1) as int?;
        }
        catch { }

        return systemUsesLightTheme == 0;
    }

    private static void HandleCrash(Exception ex)
    {
        try
        {
            logger?.Fatal(ex, "Unhandled exception.");
            provider?.Dispose();
        }
        catch { }

        try
        {
            TaskDialogCommandLinkButton restartButton = new(Resources.Restart);
            TaskDialogCommandLinkButton exitButton = new(Resources.Exit);
            TaskDialogCommandLinkButton logsButton = new(Resources.OpenLogDirectory, allowCloseDialog: false);

            string str = ex.ToString();
            var stackTraceIndex = str.IndexOf("   at ", StringComparison.OrdinalIgnoreCase);
            string text = stackTraceIndex > 0 ? str[..stackTraceIndex].TrimEnd() : str;
            TaskDialogExpander? stackTrace = stackTraceIndex > 0 ? new(str[stackTraceIndex..]) : null;

            text = text.Replace("\\", "\\\u200B"); // Zero width space to allow paths to wrap instead of getting shortened with an ellipsis

            TaskDialogPage taskDialog = new()
            {
                Heading = Resources.UnhandledException,
                Text = text,
                Expander = stackTrace,
                SizeToContent = true,
                Caption = Resources.ProgramName,
                Icon = TaskDialogIcon.Error,
                Buttons = logger is null ? // Don't show logs button if program crashed during init before logger setup
                    [restartButton, exitButton] :
                    [restartButton, exitButton, logsButton],
            };

            logsButton.Click += (_, _) =>
            {
                Process.Start(new ProcessStartInfo(LogsDirectory) { UseShellExecute = true });
            };

            if (TaskDialog.ShowDialog(taskDialog) == restartButton)
            {
                singleInstanceMutex?.Dispose();
                Application.Restart();
                return;
            }
        }
        catch { }

        Application.Exit();
    }
}
