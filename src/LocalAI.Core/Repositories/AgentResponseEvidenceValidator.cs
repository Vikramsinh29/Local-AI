using System.IO;
using System.Text.RegularExpressions;
using LocalAI.Core.Models;

namespace LocalAI.Core.Repositories;

public static partial class AgentResponseEvidenceValidator
{
    public static AgentResponseEvidenceValidationResult Validate(
        string modelResponse,
        IEnumerable<RepositoryContextFile> sourceFiles,
        ProjectInstructionSelection? instructionSelection = null)
    {
        ArgumentNullException.ThrowIfNull(modelResponse);
        ArgumentNullException.ThrowIfNull(sourceFiles);

        string[] expectedPaths =
            (instructionSelection?.IncludedFiles ??
             Array.Empty<ProjectInstructionFile>())
            .Select(file => file.RelativePath)
            .Concat(sourceFiles.Select(file => file.RelativePath))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        string[] missingRequiredPaths = expectedPaths
            .Where(path => !modelResponse.Contains(
                path,
                StringComparison.Ordinal))
            .ToArray();

        HashSet<string> expectedNormalizedPaths = expectedPaths
            .Select(NormalizePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> expectedExtensions = expectedPaths
            .Select(Path.GetExtension)
            .Where(extension => !string.IsNullOrWhiteSpace(extension))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        string[] unexpectedPaths = EvidencePathPattern()
            .Matches(modelResponse)
            .Cast<Match>()
            .Select(match => match.Value)
            .Where(candidate => expectedExtensions.Contains(
                Path.GetExtension(candidate)))
            .Where(candidate => !expectedNormalizedPaths.Contains(
                NormalizePath(candidate)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new AgentResponseEvidenceValidationResult(
            missingRequiredPaths,
            unexpectedPaths);
    }

    private static string NormalizePath(string path) =>
        path.Replace('\\', '/');

    [GeneratedRegex(
        @"(?<![A-Za-z0-9_.-])(?:[A-Za-z0-9_.-]+[\\/])*[A-Za-z0-9_.-]+\.[A-Za-z0-9]+",
        RegexOptions.CultureInvariant)]
    private static partial Regex EvidencePathPattern();
}
