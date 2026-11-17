namespace DualSound.Contracts.Enums;

/// <summary>
/// State of system-wide Windows desktop audio routing and endpoint readiness.
/// </summary>
public enum RoutingValidationState
{
    /// <summary>
    /// Initial unverified state before diagnostic checks have run.
    /// </summary>
    Uninitialized,

    /// <summary>
    /// Full system-wide routing confirmed: Windows Default Playback is routed to the virtual cable,
    /// DualSound is capturing from the virtual recording endpoint, and both physical outputs are active and distinct.
    /// </summary>
    FullyConfiguredAndReady,

    /// <summary>
    /// Virtual audio cable exists, but the Windows default playback endpoint is currently pointing
    /// to physical speakers or another device. Normal desktop audio will bypass DualSound.
    /// </summary>
    DefaultDeviceMismatch,

    /// <summary>
    /// No virtual audio cable driver (e.g. VB-Audio CABLE) was detected in the Windows audio subsystem.
    /// </summary>
    VirtualCableMissing,

    /// <summary>
    /// One or both selected physical outputs (TV HDMI or Home Theatre 3.5mm) are disconnected or invalid.
    /// </summary>
    PhysicalOutputMissing,

    /// <summary>
    /// Both physical outputs are assigned to the exact same Windows endpoint ID.
    /// </summary>
    OutputsConflict,

    /// <summary>
    /// Windows recording endpoint cannot be opened (e.g. privacy restriction or access denied).
    /// </summary>
    CaptureAccessDenied
}

