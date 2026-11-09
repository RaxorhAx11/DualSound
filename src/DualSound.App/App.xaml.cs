using System;
using System.Windows;
using DualSound.App.Services;
using DualSound.App.ViewModels;
using DualSound.Audio.Capture;
using DualSound.Audio.Engine;
using DualSound.Audio.Output;
using DualSound.Audio.Presets;
using DualSound.Audio.Sync;
using DualSound.Core.Services;
using DualSound.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DualSound.App;

/// <summary>
/// Application startup and Dependency Injection orchestration for DualSound.
/// </summary>
public partial class App : Application
{
    private IHost? _host;

    public App()
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            LogFatalException(e.ExceptionObject as Exception, "AppDomain.CurrentDomain.UnhandledException");
        };

        DispatcherUnhandledException += (s, e) =>
        {
            LogFatalException(e.Exception, "DispatcherUnhandledException");
            e.Handled = true; // Prevent abrupt app crash for non-fatal dispatcher exceptions
        };
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Prevent premature shutdown during async dependency injection initialization
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        try
        {
            _host = Host.CreateDefaultBuilder(e.Args)
                .ConfigureLogging((context, logging) =>
                {
                    logging.ClearProviders();
                    logging.AddConsole();
                    logging.AddDebug();
                    logging.SetMinimumLevel(LogLevel.Information);
                })
                .ConfigureServices((context, services) =>
                {
                    ConfigureServices(services);
                })
                .Build();

            await _host.StartAsync();

            // Pre-load local JSON settings
            var settingsService = _host.Services.GetRequiredService<ISettingsService>();
            await settingsService.LoadSettingsAsync();

            // Display main application window
            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = mainWindow;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            mainWindow.Show();

            // Auto-start engine if preferred in settings
            if (settingsService.CurrentSettings.AutoStartEngine)
            {
                var engine = _host.Services.GetRequiredService<IAudioEngineService>();
                _ = engine.StartAsync();
            }
        }
        catch (Exception ex)
        {
            LogFatalException(ex, "Application OnStartup");
            MessageBox.Show(
                $"Failed to start DualSound:\n\n{ex.Message}",
                "DualSound Fatal Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(-1);
        }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Core & Infrastructure Services
        services.AddSingleton<IDispatcherService, WpfDispatcherService>();
        services.AddSingleton<ISettingsService>(sp => 
            new JsonSettingsService(sp.GetRequiredService<ILogger<JsonSettingsService>>()));
        services.AddSingleton<IAudioDeviceDiscoveryService, WindowsAudioDeviceDiscoveryService>();
        services.AddSingleton<IAudioDeviceMonitorService, WindowsAudioDeviceMonitorService>();
        services.AddSingleton<INavigationService, NavigationService>();

        // Audio Engine, DSP, Sync & Dual Outputs
        services.AddSingleton<IAudioCapture, WasapiCaptureService>();
        services.AddSingleton<IAudioSyncManager, AudioSyncManager>();
        services.AddSingleton<IDualAudioOutputService, DualAudioOutputService>();
        services.AddSingleton<IAudioPresetService, AudioPresetService>();
        services.AddSingleton<IAudioEngineService, AudioEngineService>();

        // ViewModels
        services.AddSingleton<MainViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<AudioDevicesViewModel>();
        services.AddTransient<AudioRoutingViewModel>();
        services.AddTransient<EqualizerViewModel>();
        services.AddTransient<SynchronizationViewModel>();
        services.AddTransient<SettingsViewModel>();

        // Windows
        services.AddSingleton<MainWindow>();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host != null)
        {
            try
            {
                // Ensure master audio engine, capture, and monitor resources are cleanly disposed
                var audioEngine = _host.Services.GetService<IAudioEngineService>();
                audioEngine?.Dispose();

                var captureEngine = _host.Services.GetService<IAudioCapture>();
                captureEngine?.Dispose();

                var monitorService = _host.Services.GetService<IAudioDeviceMonitorService>();
                monitorService?.Dispose();

                await _host.StopAsync(TimeSpan.FromSeconds(3));
                _host.Dispose();
            }
            catch
            {
                // Ignore errors during exit cleanup
            }
        }

        base.OnExit(e);
    }

    private void LogFatalException(Exception? ex, string source)
    {
        try
        {
            var folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DualSound");
            System.IO.Directory.CreateDirectory(folder);
            var logPath = System.IO.Path.Combine(folder, "app_debug.log");
            System.IO.File.AppendAllText(logPath, $"[{DateTime.UtcNow:O}] [{source}] {ex}\n\n");
        }
        catch { }

        var logger = _host?.Services.GetService<ILogger<App>>();
        if (logger != null && ex != null)
        {
            logger.LogCritical(ex, "Unhandled exception in {Source}", source);
        }
        else
        {
            Console.Error.WriteLine($"[CRITICAL] Unhandled exception in {source}: {ex}");
        }
    }
}
