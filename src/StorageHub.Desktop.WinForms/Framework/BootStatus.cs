namespace StorageHub.Desktop.Framework;

/// <summary>Which startup step the shell is on.</summary>
/// <remarks>
/// CodeLogic publishes no progress of its own — nothing is raised for validation, scaffolding,
/// config generation or the application phases — so the desktop reports these itself around the
/// framework calls. The enum, not a string, is what crosses the boundary: the wording is UI text
/// and becomes localizable, while the step identity stays stable.
/// </remarks>
internal enum BootStage
{
    PreparingData,
    StartingFramework,
    CheckingEnvironment,
    LoadingSettings,
    LoadingLanguage,
    StartingAgent,
    Opening
}

/// <summary>One reportable startup step, optionally carrying a non-fatal warning to show beneath it.</summary>
internal sealed record BootStatus(BootStage Stage, string? Warning = null)
{
    internal static BootStatus PreparingData { get; } = new(BootStage.PreparingData);
    internal static BootStatus StartingFramework { get; } = new(BootStage.StartingFramework);
    internal static BootStatus CheckingEnvironment { get; } = new(BootStage.CheckingEnvironment);
    internal static BootStatus LoadingSettings { get; } = new(BootStage.LoadingSettings);
    internal static BootStatus LoadingLanguage { get; } = new(BootStage.LoadingLanguage);
    internal static BootStatus StartingAgent { get; } = new(BootStage.StartingAgent);
    internal static BootStatus Opening { get; } = new(BootStage.Opening);
}

/// <summary>
/// A startup step that failed in a way the user needs to see. Carries the stage so the splash can
/// choose its wording and whether a retry is worth offering.
/// </summary>
internal sealed class DesktopBootException : Exception
{
    internal DesktopBootException(BootStage stage, string? message)
        : base(string.IsNullOrWhiteSpace(message) ? $"StorageHub startup failed during {stage}." : message) =>
        Stage = stage;

    internal DesktopBootException(BootStage stage, string? message, Exception innerException)
        : base(string.IsNullOrWhiteSpace(message) ? $"StorageHub startup failed during {stage}." : message, innerException) =>
        Stage = stage;

    internal BootStage Stage { get; }
}
