namespace FModel.Services;

/// <summary>
/// Product-level update policy for the customized FModel distribution.
/// The official release view remains available from the Help menu; this policy
/// only controls the automatic network check performed during startup.
/// </summary>
internal static class FModelUpdatePolicy
{
    /// <summary>
    /// The customized executable cannot consume the official FModel updater.
    /// Keep this as an explicit product policy instead of mutating the user's
    /// NextUpdateCheck setting, so manual release inspection remains truthful.
    /// </summary>
    internal const bool AutomaticStartupUpdateChecksEnabled = false;
}
