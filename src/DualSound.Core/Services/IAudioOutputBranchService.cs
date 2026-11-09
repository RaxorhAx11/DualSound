using System;
using DualSound.Contracts.Enums;
using DualSound.Contracts.Models;

namespace DualSound.Core.Services;

/// <summary>
/// Controls an individual physical audio output branch (TV HDMI or Home Theatre 3.5mm).
/// </summary>
public interface IAudioOutputBranchService
{
    AudioOutputBranch Branch { get; }

    string? DeviceId { get; }

    string DeviceName { get; }

    string FormatDescription { get; }

    float Volume { get; set; }

    bool IsMuted { get; set; }

    bool IsEnabled { get; set; }

    float DelayMilliseconds { get; set; }

    AudioLevels CurrentLevels { get; }

    AudioSyncStatus SyncStatus { get; }

    AudioEngineState State { get; }

    void SetDevice(string? deviceId);

    void ApplySettings(OutputBranchSettings settings);
}

