using LocalAI.Core.Models;

namespace LocalAI.Desktop.ViewModels;

public sealed class RepositorySearchResultViewModel
{
    public RepositorySearchResultViewModel(RepositorySearchResult result)
    {
        Result = result ?? throw new ArgumentNullException(nameof(result));
    }

    public RepositorySearchResult Result { get; }

    public string Name => Result.Name;

    public string RelativePath => Result.RelativePath;

    public string SizeText => Result.SizeBytes is null
        ? "Unknown"
        : $"{Result.SizeBytes.Value / 1024d:0.#} KB";
}
