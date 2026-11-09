namespace DualSound.Contracts.Enums;

/// <summary>
/// Operational state of the central DualSound audio engine.
/// </summary>
public enum AudioEngineState
{
    Stopped = 0,
    Initializing = 1,
    Running = 2,
    Paused = 3,
    Recovering = 4,
    Error = 5
}

