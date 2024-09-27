// Copyright (c) Max Kagamine
// Licensed under the Apache License, Version 2.0

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace AutoAudioSwitcher;

internal class Program
{
    private static readonly LoggingLevelSwitch levelSwitch = new(LogEventLevel.Error);

    static ServiceProvider ConfigureServices()
    {
        ServiceCollection services = new();

        if (!File.Exists("appsettings.json"))
        {
            new Settings().Save();
        }

        IConfiguration config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        services.ConfigureObservable<Settings>(config);

        levelSwitch.MinimumLevel = config.GetValue<LogEventLevel>(nameof(Settings.LogLevel));

        services.AddSingleton<ILogger>(_ => new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Debug()
            .WriteTo.File("error.log",
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
    static void Main()
    {
        using Mutex singleInstance = new(true, "f09f929b-e98f-a1e9-9fb3-e383aae383b3" /* This is my favorite GUID */, out bool createdNew);
        if (!createdNew)
        {
            return;
        }

        //CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new("ja-JP");

        ApplicationConfiguration.Initialize();
        Application.SetColorMode(SystemColorMode.System);
        Environment.CurrentDirectory = AppContext.BaseDirectory;
        ServiceProvider provider = ConfigureServices();

        var logger = provider.GetRequiredService<ILogger>();
        AppDomain.CurrentDomain.UnhandledException += (object sender, UnhandledExceptionEventArgs e) =>
        {
            logger.Fatal((Exception)e.ExceptionObject, "Unhandled exception.");
            provider.Dispose();
        };

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
            logger.Information("Connected monitors: {Monitors}",
                currentMonitors.Select(m => $"{m.GdiDeviceName}: {m.FriendlyName}"));

            AddNewMonitorsToSettings(settings, currentMonitors, logger);
        });

        var currentMonitorMonitor = provider.GetRequiredService<CurrentMonitorMonitor>();
        var audioDeviceManager = provider.GetRequiredService<AudioDeviceManager>();
        currentMonitorMonitor.CurrentMonitorChanged.Subscribe(currentMonitor =>
        {
            if (!settings.Value.Enabled)
            {
                return;
            }

            logger.Information("Current monitor is {CurrentMonitor}", currentMonitor.FriendlyName);

            if (settings.Value.Monitors.TryGetValue(currentMonitor.FriendlyName, out string? playbackDevice) &&
                !string.IsNullOrEmpty(playbackDevice))
            {
                audioDeviceManager.SetDefaultPlaybackDevice(playbackDevice);
            }
            else
            {
                logger.Information("No playback device set for {CurrentMonitor}", currentMonitor.FriendlyName);
            }
        });

        provider.GetRequiredService<WindowMessageListener>();
        provider.GetRequiredService<TrayIcon>().Show();

        Application.Run();
    }

    static void AddNewMonitorsToSettings(
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
}
