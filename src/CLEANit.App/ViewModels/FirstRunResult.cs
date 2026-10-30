namespace CLEANit.App.ViewModels;

/// <summary>Answer of the first-start window: expert features and start in the system tray at sign-in (both off by default).</summary>
public sealed record FirstRunResult(bool Expert, bool TrayAtSignIn);
