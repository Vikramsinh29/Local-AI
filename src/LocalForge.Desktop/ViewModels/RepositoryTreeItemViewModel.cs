using System.Collections.ObjectModel;
using System.IO;
using LocalForge.Core.Models;

namespace LocalForge.Desktop.ViewModels;

public sealed class RepositoryTreeItemViewModel
{
    public RepositoryTreeItemViewModel(
        RepositoryTreeNode node)
    {
        Name = node.Name;
        RelativePath = node.RelativePath;
        IsDirectory = node.IsDirectory;
        SizeBytes = node.SizeBytes;
        LastModifiedUtc = node.LastModifiedUtc;

        Children = new ObservableCollection<RepositoryTreeItemViewModel>(
            node.Children.Select(
                child => new RepositoryTreeItemViewModel(child)));
    }

    public string Name { get; }

    public string RelativePath { get; }

    public bool IsDirectory { get; }

    public long? SizeBytes { get; }

    public DateTime? LastModifiedUtc { get; }

    public ObservableCollection<RepositoryTreeItemViewModel> Children
    {
        get;
    }

    public string TypeText =>
        IsDirectory
            ? "Folder"
            : string.IsNullOrWhiteSpace(Path.GetExtension(Name))
                ? "File"
                : $"{Path.GetExtension(Name).TrimStart('.').ToUpperInvariant()} file";

    public string SizeText =>
        IsDirectory
            ? "—"
            : FormatSize(SizeBytes);

    public string ModifiedText =>
        LastModifiedUtc?.ToLocalTime().ToString("g")
        ?? "Unknown";

    private static string FormatSize(long? bytes)
    {
        if (bytes is null)
        {
            return "Unknown";
        }

        string[] units =
        [
            "B",
            "KB",
            "MB",
            "GB",
            "TB"
        ];

        double value = bytes.Value;
        int unitIndex = 0;

        while (value >= 1024 &&
               unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.##} {units[unitIndex]}";
    }
}
