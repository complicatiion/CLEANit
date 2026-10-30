using CommunityToolkit.Mvvm.ComponentModel;
using CLEANit.Core.Models;

namespace CLEANit.App.ViewModels;

public readonly record struct CleanConfirmResult(bool Confirmed, bool RestorePoint);

public sealed record CleanConfirmRow(string Name, string Category, RiskLevel Risk, string RiskText, string Size, string Files, string? Note)
{
    public bool IsSafe => Risk == RiskLevel.Safe;

    public bool IsModerate => Risk == RiskLevel.Moderate;

    public bool IsDeep => Risk == RiskLevel.Deep;

    public bool HasNote => !string.IsNullOrEmpty(Note);
}

/// <summary>Content of the cleaning confirmation: what will be removed, warnings, Deep acknowledgement.</summary>
public sealed partial class CleanConfirmModel : ObservableObject
{
    public required string Title { get; init; }

    public required string Summary { get; init; }

    public required IReadOnlyList<CleanConfirmRow> Rows { get; init; }

    /// <summary>Warnings of the Deep rules ("You will be signed out …").</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    public bool HasDeep { get; init; }

    public bool HasWarnings => Warnings.Count > 0;

    public bool ShowRestorePoint { get; init; }

    /// <summary>Restore point switch (off unless the setting says otherwise).</summary>
    [ObservableProperty]
    private bool _createRestorePoint;

    /// <summary>Without Deep items the main button is the accent button; with Deep items it turns red and says so.</summary>
    public bool IsNormal => !HasDeep;
}

/// <summary>Row of a check list dialog.</summary>
public sealed partial class ChecklistItem : ObservableObject
{
    public required string Text { get; init; }

    public string? Detail { get; init; }

    public object? Tag { get; init; }

    [ObservableProperty]
    private bool _isChecked = true;
}
