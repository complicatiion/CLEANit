namespace CLEANit.App.ViewModels;

/// <summary>Own startup entry from the "Add entry" dialog.</summary>
public sealed record StartupAddRequest(string Name, string Target, string Arguments, bool AllUsers);
