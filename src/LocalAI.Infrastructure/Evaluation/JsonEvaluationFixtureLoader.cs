using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using LocalAI.Core.Interfaces;
using LocalAI.Core.Models;

namespace LocalAI.Infrastructure.Evaluation;

public sealed partial class JsonEvaluationFixtureLoader : IEvaluationFixtureLoader
{
    public const int SupportedSchemaVersion = 1;
    public const int MaximumCases = 100;
    public const long MaximumDefinitionBytes = 65_536;
    public const long MaximumEvidenceBytes = 131_072;

    private static readonly JsonSerializerOptions JsonOptions = CreateOptions();

    public IReadOnlyList<EvaluationCaseDefinition> Load(string fixtureRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fixtureRoot);

        string root = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(fixtureRoot));

        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException(
                $"Evaluation fixture root was not found: {root}");
        }

        RejectReparsePoint(root, "Evaluation fixture root");
        string[] files = Directory
            .EnumerateFiles(root, "*.case.json", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        if (files.Length == 0 || files.Length > MaximumCases)
        {
            throw new InvalidDataException(
                $"Evaluation fixtures must contain between 1 and " +
                $"{MaximumCases} case definitions.");
        }

        List<EvaluationCaseDefinition> definitions = [];
        HashSet<string> identifiers = new(StringComparer.Ordinal);

        foreach (string file in files)
        {
            RejectReparsePoint(file, "Evaluation case definition");

            if (new FileInfo(file).Length > MaximumDefinitionBytes)
            {
                throw new InvalidDataException(
                    $"Evaluation case definition is oversized: " +
                    $"{Path.GetFileName(file)}");
            }

            EvaluationCaseDefinition definition = Deserialize(file);
            ValidateDefinition(root, file, definition, identifiers);
            definitions.Add(definition with
            {
                FixturePath = Path.GetRelativePath(root, file)
                    .Replace('\\', '/')
            });
        }

        return definitions.AsReadOnly();
    }

    private static EvaluationCaseDefinition Deserialize(string file)
    {
        try
        {
            string json = File.ReadAllText(file, Encoding.UTF8);
            return JsonSerializer.Deserialize<EvaluationCaseDefinition>(
                       json,
                       JsonOptions) ??
                   throw new InvalidDataException(
                       $"Evaluation case is empty: {Path.GetFileName(file)}");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"Evaluation case is malformed: {Path.GetFileName(file)}",
                exception);
        }
    }

    private static void ValidateDefinition(
        string root,
        string file,
        EvaluationCaseDefinition definition,
        ISet<string> identifiers)
    {
        if (definition.SchemaVersion != SupportedSchemaVersion)
        {
            throw new InvalidDataException(
                $"Evaluation case '{definition.Id}' uses unsupported " +
                $"schema version {definition.SchemaVersion}.");
        }

        if (string.IsNullOrWhiteSpace(definition.Id) ||
            !IdentifierPattern().IsMatch(definition.Id))
        {
            throw new InvalidDataException(
                $"Evaluation case in '{Path.GetFileName(file)}' has an " +
                "invalid stable identifier.");
        }

        if (!identifiers.Add(definition.Id))
        {
            throw new InvalidDataException(
                $"Duplicate evaluation case identifier: {definition.Id}");
        }

        if (definition.Expected is null ||
            definition.SafetyLabels is null ||
            definition.SafetyLabels.Count == 0 ||
            definition.SafetyLabels.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidDataException(
                $"Evaluation case '{definition.Id}' has incomplete " +
                "expectations or safety labels.");
        }

        ValidateReadableFile(root, definition.InputEvidencePath, definition.Id);
        ValidateReadableFile(root, definition.RecordedOutputPath, definition.Id);
        ValidateCategoryShape(root, definition);
    }

    private static void ValidateCategoryShape(
        string root,
        EvaluationCaseDefinition definition)
    {
        bool hasEvidence =
            definition.Expected.RequiredEvidencePaths?.Count > 0;
        bool hasCandidates =
            definition.Expected.ExpectedCandidateFiles?.Count > 0;
        bool hasPatch =
            definition.Expected.ExpectedPatchValid.HasValue;
        bool hasUnsafe =
            definition.Expected.ExpectedUnsafeActionRejected.HasValue;
        bool hasRepository =
            !string.IsNullOrWhiteSpace(definition.RepositoryRootPath) &&
            definition.AllowedSourcePaths?.Count > 0;

        bool valid = definition.Category switch
        {
            EvaluationCategory.GroundedPlanning =>
                hasEvidence && !hasCandidates && !hasPatch && !hasUnsafe &&
                !hasRepository,
            EvaluationCategory.EvidenceCitation =>
                hasEvidence && !hasCandidates && !hasPatch && !hasUnsafe &&
                !hasRepository,
            EvaluationCategory.FileSelection =>
                !hasEvidence && hasCandidates && !hasPatch && !hasUnsafe &&
                !hasRepository,
            EvaluationCategory.StructuredPatchValidity =>
                !hasEvidence && !hasCandidates && hasPatch && !hasUnsafe &&
                hasRepository,
            EvaluationCategory.UnsafeActionRejection =>
                !hasEvidence && !hasCandidates && !hasPatch && hasUnsafe &&
                !hasRepository,
            _ => false
        };

        if (!valid)
        {
            throw new InvalidDataException(
                $"Evaluation case '{definition.Id}' has ambiguous or " +
                "incomplete category inputs.");
        }

        if (definition.Category == EvaluationCategory.StructuredPatchValidity)
        {
            string repositoryRoot = ResolveContainedPath(
                root,
                definition.RepositoryRootPath!,
                definition.Id);

            if (!Directory.Exists(repositoryRoot))
            {
                throw new InvalidDataException(
                    $"Evaluation case '{definition.Id}' repository fixture " +
                    "does not exist.");
            }

            RejectPathChain(root, repositoryRoot, definition.Id);

            foreach (string sourcePath in definition.AllowedSourcePaths!)
            {
                ValidateReadableFile(repositoryRoot, sourcePath, definition.Id);
            }
        }
    }

    private static void ValidateReadableFile(
        string root,
        string relativePath,
        string caseId)
    {
        string path = ResolveContainedPath(root, relativePath, caseId);

        if (!File.Exists(path) ||
            new FileInfo(path).Length > MaximumEvidenceBytes)
        {
            throw new InvalidDataException(
                $"Evaluation case '{caseId}' references a missing or " +
                "oversized evidence file.");
        }

        RejectPathChain(root, path, caseId);
    }

    private static string ResolveContainedPath(
        string root,
        string relativePath,
        string caseId)
    {
        if (string.IsNullOrWhiteSpace(relativePath) ||
            Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException(
                $"Evaluation case '{caseId}' contains a non-relative path.");
        }

        string fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        string rootPrefix = root + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Evaluation case '{caseId}' contains an outside-root path.");
        }

        return fullPath;
    }

    private static void RejectPathChain(
        string root,
        string fullPath,
        string caseId)
    {
        FileSystemInfo? current = File.Exists(fullPath)
            ? new FileInfo(fullPath)
            : new DirectoryInfo(fullPath);

        while (current is not null)
        {
            RejectReparsePoint(current.FullName, $"Case '{caseId}' path");

            if (current.FullName.Equals(
                    root,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            current = current switch
            {
                FileInfo file => file.Directory,
                DirectoryInfo directory => directory.Parent,
                _ => null
            };
        }

        throw new InvalidDataException(
            $"Evaluation case '{caseId}' path containment is ambiguous.");
    }

    private static void RejectReparsePoint(string path, string label)
    {
        if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidDataException($"{label} is linked or unsafe.");
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        JsonSerializerOptions options = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        options.Converters.Add(
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{2,63}$")]
    private static partial Regex IdentifierPattern();
}
