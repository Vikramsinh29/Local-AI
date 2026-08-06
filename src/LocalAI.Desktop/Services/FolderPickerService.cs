using System.IO;
using LocalAI.Core.Interfaces;
using Microsoft.Win32;

namespace LocalAI.Desktop.Services;

public sealed class FolderPickerService : IFolderPickerService
{
    public string? PickFolder(string? initialDirectory = null)
    {
        OpenFolderDialog dialog = new()
        {
            Title = "Select a software repository",
            Multiselect = false
        };

        if (!string.IsNullOrWhiteSpace(initialDirectory) &&
            Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        return dialog.ShowDialog() == true
            ? dialog.FolderName
            : null;
    }
}
