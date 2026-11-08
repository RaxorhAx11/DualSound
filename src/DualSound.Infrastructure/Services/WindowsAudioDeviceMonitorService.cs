using System;
using DualSound.Contracts.Enums;
using DualSound.Contracts.Events;
using DualSound.Core.Services;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;

namespace DualSound.Infrastructure.Services;

/// <summary>
/// Monitors Windows CoreAudio endpoint lifecycle events in real time using NAudio 3.x MMDeviceNotificationClient.
/// Manages notification subscriptions and ensures proper unhooking and disposal of Windows audio resources.
/// </summary>
public sealed class WindowsAudioDeviceMonitorService : IAudioDeviceMonitorService
{
    private readonly ILogger<WindowsAudioDeviceMonitorService> _logger;
    private readonly object _syncLock = new();

    private MMDeviceEnumerator? _enumerator;
    private MMDeviceNotificationClient? _notificationClient;
    private bool _isMonitoring;
    private bool _isDisposed;

    public event EventHandler<DeviceChangeEventArgs>? DeviceChanged;

    public bool IsMonitoring
    {
        get
        {
            lock (_syncLock)
            {
                return _isMonitoring;
            }
        }
    }

    public WindowsAudioDeviceMonitorService(ILogger<WindowsAudioDeviceMonitorService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public void StartMonitoring()
    {
        lock (_syncLock)
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(WindowsAudioDeviceMonitorService));
            }

            if (_isMonitoring)
            {
                return;
            }

            try
            {
                _enumerator = new MMDeviceEnumerator();
                _notificationClient = _enumerator.CreateNotificationClient(useSynchronizationContext: false);

                _notificationClient.DeviceAdded += OnDeviceAdded;
                _notificationClient.DeviceRemoved += OnDeviceRemoved;
                _notificationClient.DeviceStateChanged += OnDeviceStateChanged;
                _notificationClient.DefaultDeviceChanged += OnDefaultDeviceChanged;
                _notificationClient.PropertyValueChanged += OnPropertyValueChanged;

                _isMonitoring = true;
                _logger.LogInformation("Windows audio device monitor started successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start Windows audio device monitoring");
                CleanupResources();
                throw;
            }
        }
    }

    /// <inheritdoc />
    public void StopMonitoring()
    {
        lock (_syncLock)
        {
            if (!_isMonitoring)
            {
                return;
            }

            CleanupResources();
            _isMonitoring = false;
            _logger.LogInformation("Windows audio device monitor stopped");
        }
    }

    private void OnDeviceAdded(object? sender, DeviceNotificationEventArgs e)
    {
        _logger.LogInformation("Windows audio device connected/added: {DeviceId}", e.DeviceId);
        RaiseDeviceChanged(new DeviceChangeEventArgs(
            DeviceChangeType.DeviceAdded,
            e.DeviceId,
            AudioDeviceState.Active,
            "Audio endpoint connected / registered"));
    }

    private void OnDeviceRemoved(object? sender, DeviceNotificationEventArgs e)
    {
        _logger.LogInformation("Windows audio device disconnected/removed: {DeviceId}", e.DeviceId);
        RaiseDeviceChanged(new DeviceChangeEventArgs(
            DeviceChangeType.DeviceRemoved,
            e.DeviceId,
            AudioDeviceState.NotPresent,
            "Audio endpoint removed / unregistered"));
    }

    private void OnDeviceStateChanged(object? sender, DeviceStateChangedEventArgs e)
    {
        var mappedState = e.NewState switch
        {
            DeviceState.Active => AudioDeviceState.Active,
            DeviceState.Disabled => AudioDeviceState.Disabled,
            DeviceState.NotPresent => AudioDeviceState.NotPresent,
            DeviceState.Unplugged => AudioDeviceState.Unplugged,
            _ => AudioDeviceState.NotPresent
        };

        _logger.LogInformation("Windows audio device state changed: {DeviceId} -> {State}", e.DeviceId, mappedState);
        RaiseDeviceChanged(new DeviceChangeEventArgs(
            DeviceChangeType.DeviceStateChanged,
            e.DeviceId,
            mappedState,
            $"Device state changed to {mappedState}"));
    }

    private void OnDefaultDeviceChanged(object? sender, DefaultDeviceChangedEventArgs e)
    {
        if (e.Flow == DataFlow.Render)
        {
            _logger.LogInformation("Windows default playback device changed: {DeviceId} for role {Role}", e.DeviceId, e.Role);
            RaiseDeviceChanged(new DeviceChangeEventArgs(
                DeviceChangeType.DefaultDeviceChanged,
                e.DeviceId ?? string.Empty,
                null,
                $"Default playback device changed ({e.Role})"));
        }
    }

    private void OnPropertyValueChanged(object? sender, DevicePropertyChangedEventArgs e)
    {
        RaiseDeviceChanged(new DeviceChangeEventArgs(
            DeviceChangeType.PropertyValueChanged,
            e.DeviceId,
            null,
            $"Property changed: {e.PropertyKey.formatId}:{e.PropertyKey.propertyId}"));
    }

    private void RaiseDeviceChanged(DeviceChangeEventArgs args)
    {
        try
        {
            DeviceChanged?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred in DeviceChanged event subscriber");
        }
    }

    private void CleanupResources()
    {
        if (_notificationClient != null)
        {
            try
            {
                _notificationClient.DeviceAdded -= OnDeviceAdded;
                _notificationClient.DeviceRemoved -= OnDeviceRemoved;
                _notificationClient.DeviceStateChanged -= OnDeviceStateChanged;
                _notificationClient.DefaultDeviceChanged -= OnDefaultDeviceChanged;
                _notificationClient.PropertyValueChanged -= OnPropertyValueChanged;
                _notificationClient.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error disposing MMDeviceNotificationClient");
            }
            _notificationClient = null;
        }

        if (_enumerator != null)
        {
            try
            {
                _enumerator.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error disposing MMDeviceEnumerator");
            }
            _enumerator = null;
        }
    }

    public void Dispose()
    {
        lock (_syncLock)
        {
            if (_isDisposed)
            {
                return;
            }

            StopMonitoring();
            _isDisposed = true;
        }

        GC.SuppressFinalize(this);
    }
}
