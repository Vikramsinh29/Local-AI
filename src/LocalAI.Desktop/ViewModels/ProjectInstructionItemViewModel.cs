using System.ComponentModel;
using System.Runtime.CompilerServices;
using LocalAI.Core.Models;

namespace LocalAI.Desktop.ViewModels;

public sealed class ProjectInstructionItemViewModel :
    INotifyPropertyChanged
{
    private bool _isIncluded;
    private string _stateReason = string.Empty;

    public ProjectInstructionItemViewModel(ProjectInstructionFile file)
    {
        File = file ?? throw new ArgumentNullException(nameof(file));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ProjectInstructionFile File { get; }

    public string RelativePath => File.RelativePath;

    public string TypeText => File.Kind switch
    {
        ProjectInstructionKind.AgentRules => "AGENTS",
        ProjectInstructionKind.Skill => "Skill",
        _ => "Instruction"
    };

    public string SizeText =>
        $"{File.SizeBytes:N0} B • " +
        $"~{File.EstimatedTokens:N0} tokens";

    public bool IsEligible => File.IsEligible;

    public bool IsIncluded
    {
        get => _isIncluded;
        private set => SetField(ref _isIncluded, value);
    }

    public string StateReason
    {
        get => _stateReason;
        private set => SetField(ref _stateReason, value);
    }

    public string InclusionText =>
        IsIncluded ? "Included" : "Excluded";

    public void ApplySelection(
        ProjectInstructionSelectionItem selectionItem)
    {
        ArgumentNullException.ThrowIfNull(selectionItem);

        IsIncluded = selectionItem.IsIncluded;
        StateReason = selectionItem.StateReason;
        OnPropertyChanged(nameof(InclusionText));
    }

    private bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }
}
