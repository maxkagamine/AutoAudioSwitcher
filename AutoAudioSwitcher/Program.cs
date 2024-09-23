// Copyright (c) Max Kagamine
// Licensed under the Apache License, Version 2.0

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Events;

namespace AutoAudioSwitcher;

internal class Program
{
    // TODO: Move to configuration (https://github.com/maxkagamine/AutoAudioSwitcher/issues/1)
    private const string PrimaryMonitorAudioDevice = "Speakers2";
    private const string NonPrimaryMonitorAudioDevice = "TV";

    static ServiceProvider ConfigureServices()
    {
        ServiceCollection services = new();

        IConfiguration config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        services.ConfigureObservable<Settings>(config);

        services.AddSingleton<ILogger>(_ => new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Debug()
            .WriteTo.File("error.log",
                restrictedToMinimumLevel: LogEventLevel.Error,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 5,
                fileSizeLimitBytes: 10485760 /* 10 MiB */)
            .CreateLogger());

        services.AddSingleton<CurrentMonitorMonitor>();
        services.AddSingleton<AudioDeviceSwitcher>();
        services.AddSingleton<ConnectedMonitorsMonitor>();

        return services.BuildServiceProvider();
    }

    [STAThread]
    static void Main()
    {
        Environment.CurrentDirectory = AppContext.BaseDirectory;

        ServiceProvider provider = ConfigureServices();

        var logger = provider.GetRequiredService<ILogger>();
        AppDomain.CurrentDomain.UnhandledException += (object sender, UnhandledExceptionEventArgs e) =>
        {
            logger.Fatal((Exception)e.ExceptionObject, "Unhandled exception.");
            provider.Dispose();
        };

        var settings = provider.GetRequiredService<IObservable<Settings>>();
        settings.Subscribe(currentSettings =>
        {
            logger.Debug("Loaded settings: {@Settings}", currentSettings);
        });

        var currentMonitorMonitor = provider.GetRequiredService<CurrentMonitorMonitor>();
        var audioDeviceSwitcher = provider.GetRequiredService<AudioDeviceSwitcher>();
        var connectedMonitorsMonitor = provider.GetRequiredService<ConnectedMonitorsMonitor>();

        currentMonitorMonitor.IsPrimaryChanged += (object? sender, bool isPrimary) =>
        {
            audioDeviceSwitcher.SetDefaultPlaybackDevice(isPrimary ?
                PrimaryMonitorAudioDevice : NonPrimaryMonitorAudioDevice);
        };

        Application.Run();
    }
}
