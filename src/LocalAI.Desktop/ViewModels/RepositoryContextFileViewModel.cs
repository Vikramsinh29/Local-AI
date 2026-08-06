using LocalAI.Core.Models;

namespace LocalAI.Desktop.ViewModels;

public sealed class RepositoryContextFileViewModel
{
    public RepositoryContextFileViewModel(RepositoryContextFile file)
    {
        File = file;
    }

    public RepositoryContextFile File { get; }

    public string RelativePath => File.RelativePath;

    public string SizeText =>
        $"{File.SizeBytes / 1024d:0.#} KB • ~{File.EstimatedTokens:N0} tokens";
}
