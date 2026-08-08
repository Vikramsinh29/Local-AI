using System.Text;
using LocalAI.Core.Interfaces;
using LocalAI.Core.Models;
using LocalAI.Core.Repositories;

namespace LocalAI.Infrastructure.Repositories;

public sealed class ProjectInstructionService :
    IProjectInstructionService
{
    private const string RootAgentPath = "AGENTS.md";
    private const string SkillFileName = "SKILL.md";

    public async Task<ProjectInstructionManifest> DiscoverAsync(
        string repositoryRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        string root = Path.GetFullPath(repositoryRoot)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);

        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException(
                $"Repository folder does not exist: {root}");
        }

        List<ProjectInstructionFile> files = [];
        List<string> issues = [];

        if (RepositorySourcePathValidator.IsReparsePoint(root))
        {
            files.Add(
                Excluded(
                    ProjectInstructionKind.AgentRules,
                    RootAgentPath,
                    0,
                    "The selected repository root is linked and instruction " +
                    "files were not followed."));
            issues.Add(
                "Instruction discovery stopped because the repository root " +
                "is linked.");

            return new ProjectInstructionManifest(files, issues);
        }

        files.Add(
            await ReadCandidateAsync(
                root,
                RootAgentPath,
                ProjectInstructionKind.AgentRules,
                includeMissingEntry: true,
                cancellationToken));

        string skillsDirectory = Path.Combine(root, "skills");

        if (Directory.Exists(skillsDirectory))
        {
            if (RepositorySourcePathValidator.IsReparsePoint(
                    skillsDirectory))
            {
                issues.Add(
                    "The repository skills directory is linked and was not " +
                    "followed.");
            }
            else
            {
                foreach (string skillDirectory in
                         EnumerateSkillDirectories(
                             skillsDirectory,
                             issues))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string skillName = Path.GetFileName(skillDirectory);
                    string relativePath = Path.Combine(
                        "skills",
                        skillName,
                        SkillFileName);

                    if (RepositorySourcePathValidator.IsReparsePoint(
                            skillDirectory))
                    {
                        files.Add(
                            Excluded(
                                ProjectInstructionKind.Skill,
                                relativePath,
                                0,
                                "Linked skill folders are not followed."));
                        continue;
                    }

                    string skillPath = Path.Combine(
                        skillDirectory,
                        SkillFileName);

                    if (!File.Exists(skillPath))
                    {
                        continue;
                    }

                    files.Add(
                        await ReadCandidateAsync(
                            root,
                            relativePath,
                            ProjectInstructionKind.Skill,
                            includeMissingEntry: false,
                            cancellationToken));
                }
            }
        }

        MarkDuplicatePaths(files);

        ProjectInstructionFile[] orderedFiles = files
            .OrderBy(file => file.Kind)
            .ThenBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToArray();

        return new ProjectInstructionManifest(orderedFiles, issues);
    }

    private static IEnumerable<string> EnumerateSkillDirectories(
        string skillsDirectory,
        List<string> issues)
    {
        try
        {
            return Directory.EnumerateDirectories(skillsDirectory)
                .OrderBy(
                    Path.GetFileName,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(
                    Path.GetFileName,
                    StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            issues.Add(
                $"The repository skills directory could not be inspected: " +
                $"{exception.Message}");
            return [];
        }
    }

    private static async Task<ProjectInstructionFile> ReadCandidateAsync(
        string root,
        string relativePath,
        ProjectInstructionKind kind,
        bool includeMissingEntry,
        CancellationToken cancellationToken)
    {
        string? validationError = RepositorySourcePathValidator.Validate(
            root,
            relativePath,
            out string normalizedPath,
            out string fullPath);

        if (validationError is not null)
        {
            return Excluded(
                kind,
                relativePath,
                0,
                validationError);
        }

        string displayPath = normalizedPath.Replace(
            Path.DirectorySeparatorChar,
            '/');

        if (!File.Exists(fullPath))
        {
            return includeMissingEntry
                ? Excluded(
                    kind,
                    displayPath,
                    0,
                    "Not found at the repository root.")
                : Excluded(
                    kind,
                    displayPath,
                    0,
                    "The instruction file no longer exists.");
        }

        long initialLength;

        try
        {
            initialLength = new FileInfo(fullPath).Length;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return Excluded(
                kind,
                displayPath,
                0,
                $"The instruction file could not be inspected: " +
                $"{exception.Message}");
        }

        if (initialLength >
            ProjectInstructionSelectionBuilder.MaximumInstructionBytes)
        {
            return Excluded(
                kind,
                displayPath,
                initialLength,
                "Excluded because the complete file is larger than the " +
                "8 KB instruction budget.");
        }

        byte[] bytes;

        try
        {
            bytes = await File.ReadAllBytesAsync(
                fullPath,
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return Excluded(
                kind,
                displayPath,
                initialLength,
                $"The instruction file could not be read: " +
                $"{exception.Message}");
        }

        if (bytes.LongLength >
            ProjectInstructionSelectionBuilder.MaximumInstructionBytes)
        {
            return Excluded(
                kind,
                displayPath,
                bytes.LongLength,
                "Excluded because the file changed while being read and " +
                "now exceeds the 8 KB instruction budget.");
        }

        if (bytes.Contains((byte)0))
        {
            return Excluded(
                kind,
                displayPath,
                bytes.LongLength,
                "Binary instruction files are not supported.");
        }

        string content;

        try
        {
            content = new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Excluded(
                kind,
                displayPath,
                bytes.LongLength,
                "The instruction file is not valid UTF-8 text.");
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return Excluded(
                kind,
                displayPath,
                bytes.LongLength,
                "Empty instruction files are not included.");
        }

        int estimatedTokens = Math.Max(1, (content.Length + 3) / 4);

        return new ProjectInstructionFile(
            kind,
            displayPath,
            bytes.LongLength,
            estimatedTokens,
            content,
            ExclusionReason: null);
    }

    private static ProjectInstructionFile Excluded(
        ProjectInstructionKind kind,
        string relativePath,
        long sizeBytes,
        string reason)
    {
        int estimatedTokens = sizeBytes <= 0
            ? 0
            : (int)Math.Min(
                int.MaxValue,
                Math.Max(1L, (sizeBytes + 3) / 4));

        return new ProjectInstructionFile(
            kind,
            relativePath.Replace(Path.DirectorySeparatorChar, '/'),
            sizeBytes,
            estimatedTokens,
            Content: null,
            ExclusionReason: reason);
    }

    private static void MarkDuplicatePaths(
        List<ProjectInstructionFile> files)
    {
        HashSet<ProjectInstructionFile> duplicates = files
            .GroupBy(
                file => file.RelativePath,
                StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .SelectMany(group => group)
            .ToHashSet();

        for (int index = 0; index < files.Count; index++)
        {
            if (!duplicates.Contains(files[index]))
            {
                continue;
            }

            files[index] = files[index] with
            {
                Content = null,
                ExclusionReason =
                    "Duplicate instruction paths are not included."
            };
        }
    }
}
