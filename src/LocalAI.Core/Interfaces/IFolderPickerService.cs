namespace LocalAI.Core.Interfaces;

public interface IFolderPickerService
{
    string? PickFolder(string? initialDirectory = null);
}
