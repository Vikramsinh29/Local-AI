using System.Text;
using System.Text.RegularExpressions;
using LocalAI.Core.Models;

namespace LocalAI.Core.Repositories;

public static partial class ProposedPatchParser
{
    public const string StartMarker = "<<<LOCAL_AI_PATCH_V1>>>";
    public const string OriginalMarker = "<<<ORIGINAL>>>";
    public const string ReplacementMarker = "<<<REPLACEMENT>>>";
    public const string EndFileMarker = "<<<END_FILE>>>";
    public const string EndMarker = "<<<END_LOCAL_AI_PATCH>>>";

    private const int MaximumResponseCharacters = 120_000;
    private const int MaximumSummaryCharacters = 2_000;
    private const int MaximumFiles = 20;
    private const int MaximumReplacementCharacters = 50_000;
    private const int MaximumRelativePathCharacters = 500;
    private const long MaximumSourceFileBytes = 1_048_576;

    private static readonly HashSet<string> ExcludedDirectories =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".git", ".local-ai", ".vs", "artifacts", "bin", "obj",
            "node_modules"
        };

    private static readonly HashSet<string> SecretFileNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".env", "credentials.json", "secrets.json",
            "service-account.json"
        };

    private static readonly HashSet<string> SecretExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".key", ".p12", ".pem", ".pfx"
        };

    private static readonly HashSet<string> ReservedWindowsNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5",
            "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5",
            "LPT6", "LPT7", "LPT8", "LPT9"
        };

    public static ProposedPatchParseResult Parse(
        string modelResponse,
        string repositoryRoot,
        IEnumerable<string> allowedRelativePaths)
    {
        ArgumentNullException.ThrowIfNull(modelResponse);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(allowedRelativePaths);

        if (string.IsNullOrWhiteSpace(modelResponse))
        {
            return ProposedPatchParseResult.Failure(
                "The model returned an empty patch proposal.");
        }

        if (modelResponse.Length > MaximumResponseCharacters)
        {
            return ProposedPatchParseResult.Failure(
                "The proposed patch exceeds the Local-AI preview limit.");
        }

        string root = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(repositoryRoot));

        if (!Directory.Exists(root))
        {
            return ProposedPatchParseResult.Failure(
                "The selected repository no longer exists.");
        }

        if (IsReparsePoint(root))
        {
            return ProposedPatchParseResult.Failure(
                "Patch previews require a non-linked repository root.");
        }

        ProposedPatchParseResult? allowedPathFailure =
            BuildAllowedPathSet(
                root,
                allowedRelativePaths,
                out HashSet<string> allowedPaths);

        if (allowedPathFailure is not null)
        {
            return allowedPathFailure;
        }

        string response = NormalizeLineEndings(modelResponse).Trim();
        string startLine = StartMarker + "\n";
        string endLine = "\n" + EndMarker;

        if (CountOccurrences(response, StartMarker) != 1 ||
            CountOccurrences(response, EndMarker) != 1)
        {
            return ProposedPatchParseResult.Failure(
                "The model response is not one complete LOCAL_AI_PATCH_V1 " +
                "proposal.");
        }

        int proposalStart = response.IndexOf(
            StartMarker,
            StringComparison.Ordinal);
        int proposalEndMarker = response.IndexOf(
            EndMarker,
            proposalStart + StartMarker.Length,
            StringComparison.Ordinal);

        if (proposalEndMarker < 0)
        {
            return ProposedPatchParseResult.Failure(
                "The model response is not one complete LOCAL_AI_PATCH_V1 " +
                "proposal.");
        }

        int proposalEnd = proposalEndMarker + EndMarker.Length;
        string prefix = response[..proposalStart].Trim();
        string suffix = response[proposalEnd..].Trim();

        if (ContainsMarkdownFence(prefix) || ContainsMarkdownFence(suffix))
        {
            return ProposedPatchParseResult.Failure(
                "The patch proposal must not be wrapped in Markdown fences.");
        }

        if (ContainsPatchMarkerSyntax(prefix) ||
            ContainsPatchMarkerSyntax(suffix))
        {
            return ProposedPatchParseResult.Failure(
                "Text outside the patch proposal contains unexpected " +
                "patch-marker syntax.");
        }

        string proposal = response[proposalStart..proposalEnd];

        if (!proposal.StartsWith(startLine, StringComparison.Ordinal) ||
            !proposal.EndsWith(endLine, StringComparison.Ordinal))
        {
            return ProposedPatchParseResult.Failure(
                "The model response is not one complete LOCAL_AI_PATCH_V1 " +
                "proposal.");
        }

        string body = proposal[startLine.Length..^endLine.Length];
        const string summaryPrefix = "SUMMARY:\n";

        if (!body.StartsWith(summaryPrefix, StringComparison.Ordinal))
        {
            return ProposedPatchParseResult.Failure(
                "The proposed patch is missing its SUMMARY section.");
        }

        int firstFileMarker = body.IndexOf(
            "\n<<<FILE:",
            summaryPrefix.Length,
            StringComparison.Ordinal);

        if (firstFileMarker < 0)
        {
            return ProposedPatchParseResult.Failure(
                "The proposed patch does not contain a FILE block.");
        }

        string summary = body[
            summaryPrefix.Length..
            firstFileMarker].Trim();

        if (string.IsNullOrWhiteSpace(summary) ||
            summary.Length > MaximumSummaryCharacters)
        {
            return ProposedPatchParseResult.Failure(
                "The proposed patch summary is missing or too long.");
        }

        string fileBlocks = body[(firstFileMarker + 1)..];
        List<ProposedPatchFile> files = [];
        List<string> previewDiffs = [];
        HashSet<string> uniquePaths =
            new(StringComparer.OrdinalIgnoreCase);

        while (fileBlocks.Length > 0)
        {
            if (!fileBlocks.StartsWith(
                    "<<<FILE:",
                    StringComparison.Ordinal))
            {
                return ProposedPatchParseResult.Failure(
                    "Unexpected content appears between patch FILE blocks.");
            }

            int headerEnd = fileBlocks.IndexOf(">>>\n", StringComparison.Ordinal);

            if (headerEnd < 0)
            {
                return ProposedPatchParseResult.Failure(
                    "A patch FILE header is malformed.");
            }

            string relativePath = fileBlocks[
                "<<<FILE:".Length..
                headerEnd].Trim();
            int changeStart = headerEnd + ">>>\n".Length;
            string endFileLine = "\n" + EndFileMarker;
            int changeEnd = fileBlocks.IndexOf(
                endFileLine,
                changeStart,
                StringComparison.Ordinal);

            if (changeEnd < 0)
            {
                return ProposedPatchParseResult.Failure(
                    $"The FILE block for '{relativePath}' is incomplete.");
            }

            string changeBlock = fileBlocks[changeStart..changeEnd];
            ProposedPatchParseResult? failure = ValidateFileBlock(
                root,
                relativePath,
                changeBlock,
                allowedPaths,
                uniquePaths,
                out ProposedPatchFile? file,
                out string previewDiff);

            if (failure is not null)
            {
                return failure;
            }

            files.Add(file!);
            previewDiffs.Add(previewDiff);

            if (files.Count > MaximumFiles)
            {
                return ProposedPatchParseResult.Failure(
                    $"A patch preview cannot contain more than " +
                    $"{MaximumFiles} files.");
            }

            int next = changeEnd + endFileLine.Length;
            fileBlocks = fileBlocks[next..].TrimStart('\n');
        }

        return ProposedPatchParseResult.Success(
            new ProposedPatchPreview(
                summary,
                files.AsReadOnly(),
                string.Join("\n\n", previewDiffs)));
    }

    private static ProposedPatchParseResult? BuildAllowedPathSet(
        string repositoryRoot,
        IEnumerable<string> allowedRelativePaths,
        out HashSet<string> allowedPaths)
    {
        allowedPaths = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (string relativePath in allowedRelativePaths)
        {
            string? error = ValidatePath(
                repositoryRoot,
                relativePath,
                out string normalizedPath,
                out string fullPath);

            if (error is not null || !File.Exists(fullPath))
            {
                return ProposedPatchParseResult.Failure(
                    "Selected patch evidence contains an unavailable or " +
                    "unsafe source path.");
            }

            allowedPaths.Add(normalizedPath);
        }

        return allowedPaths.Count == 0
            ? ProposedPatchParseResult.Failure(
                "Select at least one existing source file for a patch " +
                "preview.")
            : null;
    }

    private static bool ContainsMarkdownFence(string value)
    {
        return value.Contains("```", StringComparison.Ordinal) ||
            value.Contains("~~~", StringComparison.Ordinal);
    }

    private static bool ContainsPatchMarkerSyntax(string value)
    {
        return value.Contains("<<<", StringComparison.Ordinal) ||
            value.Contains(">>>", StringComparison.Ordinal);
    }

    private static ProposedPatchParseResult? ValidateFileBlock(
        string repositoryRoot,
        string relativePath,
        string changeBlock,
        HashSet<string> allowedPaths,
        HashSet<string> uniquePaths,
        out ProposedPatchFile? file,
        out string previewDiff)
    {
        file = null;
        previewDiff = string.Empty;

        string? pathError = ValidatePath(
            repositoryRoot,
            relativePath,
            out string normalizedPath,
            out string fullPath);

        if (pathError is not null)
        {
            return ProposedPatchParseResult.Failure(pathError);
        }

        if (!allowedPaths.Contains(normalizedPath))
        {
            return ProposedPatchParseResult.Failure(
                $"The proposed path '{normalizedPath}' was not selected as " +
                "source evidence.");
        }

        if (!uniquePaths.Add(normalizedPath))
        {
            return ProposedPatchParseResult.Failure(
                $"The proposed path '{normalizedPath}' appears more than once.");
        }

        ProposedPatchParseResult? structureFailure = ParseReplacement(
            normalizedPath,
            changeBlock,
            out string original,
            out string replacement);

        if (structureFailure is not null)
        {
            return structureFailure;
        }

        string source;

        try
        {
            if (!File.Exists(fullPath) ||
                new FileInfo(fullPath).Length > MaximumSourceFileBytes)
            {
                return ProposedPatchParseResult.Failure(
                    $"The selected source file '{normalizedPath}' is " +
                    "unavailable or too large.");
            }

            source = NormalizeLineEndings(File.ReadAllText(fullPath));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return ProposedPatchParseResult.Failure(
                $"The selected source file '{normalizedPath}' could not be " +
                "verified.");
        }

        if (source.Contains('\0'))
        {
            return ProposedPatchParseResult.Failure(
                $"The selected source file '{normalizedPath}' is not text.");
        }

        int matchIndex = source.IndexOf(original, StringComparison.Ordinal);

        if (matchIndex < 0)
        {
            int indentationMatchCount =
                FindIndentationInsensitiveMatches(
                    source,
                    original,
                    out matchIndex,
                    out string matchedOriginal);

            if (indentationMatchCount == 0)
            {
                return ProposedPatchParseResult.Failure(
                    $"The ORIGINAL text for '{normalizedPath}' does not " +
                    "exist in the selected source file.");
            }

            if (indentationMatchCount > 1)
            {
                return ProposedPatchParseResult.Failure(
                    $"The ORIGINAL text for '{normalizedPath}' is " +
                    "ambiguous because it appears more than once.");
            }

            if (!TryRestoreSourceIndentation(
                    matchedOriginal,
                    replacement,
                    out string indentedReplacement))
            {
                return ProposedPatchParseResult.Failure(
                    $"The indentation-normalized replacement for " +
                    $"'{normalizedPath}' must preserve its line count.");
            }

            original = matchedOriginal;
            replacement = indentedReplacement;
        }
        else if (source.IndexOf(
                     original,
                     matchIndex + original.Length,
                     StringComparison.Ordinal) >= 0)
        {
            return ProposedPatchParseResult.Failure(
                $"The ORIGINAL text for '{normalizedPath}' is ambiguous " +
                "because it appears more than once.");
        }

        previewDiff = BuildPreviewDiff(
            normalizedPath,
            source,
            matchIndex,
            original,
            replacement,
            out int addedLines,
            out int removedLines);
        file = new ProposedPatchFile(
            normalizedPath,
            addedLines,
            removedLines);
        return null;
    }

    private static int FindIndentationInsensitiveMatches(
        string source,
        string requestedOriginal,
        out int matchIndex,
        out string matchedOriginal)
    {
        matchIndex = -1;
        matchedOriginal = string.Empty;
        string[] requestedLines = requestedOriginal.Split('\n');

        if (requestedLines.Length > 200)
        {
            return 0;
        }

        string[] sourceLines = source.Split('\n');
        string[] normalizedRequestedLines = requestedLines
            .Select(line => line.TrimStart())
            .ToArray();
        string[] normalizedSourceLines = sourceLines
            .Select(line => line.TrimStart())
            .ToArray();
        int[] sourceOffsets = new int[sourceLines.Length];
        int offset = 0;

        for (int index = 0; index < sourceLines.Length; index++)
        {
            sourceOffsets[index] = offset;
            offset += sourceLines[index].Length + 1;
        }

        int matches = 0;

        for (int start = 0;
             start + requestedLines.Length <= sourceLines.Length;
             start++)
        {
            bool isMatch = true;

            for (int line = 0; line < requestedLines.Length; line++)
            {
                if (!normalizedSourceLines[start + line].Equals(
                        normalizedRequestedLines[line],
                        StringComparison.Ordinal))
                {
                    isMatch = false;
                    break;
                }
            }

            if (!isMatch)
            {
                continue;
            }

            matches++;

            if (matches == 1)
            {
                matchIndex = sourceOffsets[start];
                matchedOriginal = string.Join(
                    "\n",
                    sourceLines.Skip(start).Take(requestedLines.Length));
            }

            if (matches > 1)
            {
                return matches;
            }
        }

        return matches;
    }

    private static bool TryRestoreSourceIndentation(
        string matchedOriginal,
        string requestedReplacement,
        out string replacement)
    {
        string[] originalLines = matchedOriginal.Split('\n');
        string[] replacementLines = requestedReplacement.Split('\n');

        if (originalLines.Length != replacementLines.Length)
        {
            replacement = string.Empty;
            return false;
        }

        for (int index = 0; index < originalLines.Length; index++)
        {
            int contentStart = 0;

            while (contentStart < originalLines[index].Length &&
                   char.IsWhiteSpace(originalLines[index][contentStart]))
            {
                contentStart++;
            }

            replacementLines[index] =
                originalLines[index][..contentStart] +
                replacementLines[index].TrimStart();
        }

        replacement = string.Join("\n", replacementLines);
        return true;
    }

    private static ProposedPatchParseResult? ParseReplacement(
        string normalizedPath,
        string changeBlock,
        out string original,
        out string replacement)
    {
        original = string.Empty;
        replacement = string.Empty;
        string originalLine = OriginalMarker + "\n";
        string replacementLine = "\n" + ReplacementMarker + "\n";

        if (!changeBlock.StartsWith(
                originalLine,
                StringComparison.Ordinal) ||
            CountOccurrences(changeBlock, OriginalMarker) != 1 ||
            CountOccurrences(changeBlock, ReplacementMarker) != 1)
        {
            return ProposedPatchParseResult.Failure(
                $"The replacement block for '{normalizedPath}' is " +
                "malformed.");
        }

        int replacementIndex = changeBlock.IndexOf(
            replacementLine,
            originalLine.Length,
            StringComparison.Ordinal);

        if (replacementIndex < 0)
        {
            return ProposedPatchParseResult.Failure(
                $"The replacement block for '{normalizedPath}' is " +
                "incomplete.");
        }

        original = changeBlock[
            originalLine.Length..
            replacementIndex];
        replacement = changeBlock[
            (replacementIndex + replacementLine.Length)..];

        if (string.IsNullOrWhiteSpace(original) ||
            string.IsNullOrWhiteSpace(replacement) ||
            original.Length > MaximumReplacementCharacters ||
            replacement.Length > MaximumReplacementCharacters ||
            original.Equals(replacement, StringComparison.Ordinal))
        {
            return ProposedPatchParseResult.Failure(
                $"The replacement for '{normalizedPath}' must contain " +
                "distinct, non-empty ORIGINAL and REPLACEMENT text.");
        }

        return null;
    }

    private static string BuildPreviewDiff(
        string normalizedPath,
        string source,
        int matchIndex,
        string original,
        string replacement,
        out int addedLines,
        out int removedLines)
    {
        int lineStart = matchIndex == 0
            ? 0
            : source.LastIndexOf('\n', matchIndex - 1) + 1;
        int originalEnd = matchIndex + original.Length;
        int nextLineBreak = source.IndexOf('\n', originalEnd);
        int lineEnd = nextLineBreak >= 0
            ? nextLineBreak
            : source.Length;
        string originalLinesText = source[lineStart..lineEnd];
        int relativeMatchIndex = matchIndex - lineStart;
        string replacementLinesText = originalLinesText
            .Remove(relativeMatchIndex, original.Length)
            .Insert(relativeMatchIndex, replacement);
        string[] originalLines = originalLinesText.Split('\n');
        string[] replacementLines = replacementLinesText.Split('\n');
        int startLine = GetLineNumber(source, lineStart);
        string slashPath = normalizedPath.Replace('\\', '/');

        removedLines = originalLines.Length;
        addedLines = replacementLines.Length;

        StringBuilder builder = new();
        builder.AppendLine($"--- a/{slashPath}");
        builder.AppendLine($"+++ b/{slashPath}");
        builder.AppendLine(
            $"@@ -{startLine},{removedLines} " +
            $"+{startLine},{addedLines} @@");

        foreach (string line in originalLines)
        {
            builder.AppendLine($"-{line}");
        }

        foreach (string line in replacementLines)
        {
            builder.AppendLine($"+{line}");
        }

        return builder.ToString().TrimEnd('\r', '\n');
    }

    private static int GetLineNumber(string source, int index)
    {
        int lineNumber = 1;

        for (int current = 0; current < index; current++)
        {
            if (source[current] == '\n')
            {
                lineNumber++;
            }
        }

        return lineNumber;
    }

    private static string? ValidatePath(
        string repositoryRoot,
        string relativePath,
        out string normalizedPath,
        out string fullPath)
    {
        normalizedPath = relativePath.Replace(
            '/',
            Path.DirectorySeparatorChar);
        fullPath = string.Empty;

        if (string.IsNullOrWhiteSpace(relativePath) ||
            relativePath.Contains("<<<", StringComparison.Ordinal) ||
            Path.IsPathRooted(normalizedPath) ||
            DrivePathRegex().IsMatch(relativePath))
        {
            return "Every proposed file path must be repository-relative.";
        }

        if (relativePath.Length > MaximumRelativePathCharacters)
        {
            return $"The proposed path '{relativePath}' is invalid.";
        }

        string[] segments = normalizedPath.Split(
            Path.DirectorySeparatorChar,
            StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0 ||
            segments.Any(segment =>
                segment is "." or ".." ||
                segment.EndsWith(' ') ||
                segment.EndsWith('.') ||
                IsReservedWindowsName(segment) ||
                segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
        {
            return $"The proposed path '{relativePath}' is invalid.";
        }

        if (segments.Any(ExcludedDirectories.Contains) ||
            segments.Any(IsSecretPath))
        {
            return $"The proposed path '{relativePath}' is protected.";
        }

        try
        {
            fullPath = Path.GetFullPath(
                Path.Combine(repositoryRoot, normalizedPath));
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            NotSupportedException or
            PathTooLongException)
        {
            return $"The proposed path '{relativePath}' is invalid.";
        }

        string rootPrefix = repositoryRoot.EndsWith(
            Path.DirectorySeparatorChar)
                ? repositoryRoot
                : repositoryRoot + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(
                rootPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            return $"The proposed path '{relativePath}' escapes the repository.";
        }

        if (ContainsExistingReparsePoint(repositoryRoot, fullPath))
        {
            return $"The proposed path '{relativePath}' crosses a linked path.";
        }

        normalizedPath = Path.GetRelativePath(repositoryRoot, fullPath);
        return null;
    }

    private static bool IsSecretPath(string fileName)
    {
        return SecretFileNames.Contains(fileName) ||
               fileName.StartsWith(".env.", StringComparison.OrdinalIgnoreCase) ||
               SecretExtensions.Contains(Path.GetExtension(fileName));
    }

    private static bool IsReservedWindowsName(string segment)
    {
        int dotIndex = segment.IndexOf('.');
        string baseName = dotIndex >= 0
            ? segment[..dotIndex]
            : segment;
        return ReservedWindowsNames.Contains(baseName);
    }

    private static bool ContainsExistingReparsePoint(
        string repositoryRoot,
        string fullPath)
    {
        FileSystemInfo? current = new FileInfo(fullPath);

        while (current is not null)
        {
            if ((File.Exists(current.FullName) ||
                 Directory.Exists(current.FullName)) &&
                IsReparsePoint(current.FullName))
            {
                return true;
            }

            if (current.FullName.Equals(
                    repositoryRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            current = current switch
            {
                FileInfo file => file.Directory,
                DirectoryInfo directory => directory.Parent,
                _ => null
            };
        }

        return true;
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return File.GetAttributes(path)
                .HasFlag(FileAttributes.ReparsePoint);
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static string NormalizeLineEndings(string value)
    {
        return value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
    }

    private static int CountOccurrences(string value, string pattern)
    {
        int count = 0;
        int index = 0;

        while ((index = value.IndexOf(
                   pattern,
                   index,
                   StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += pattern.Length;
        }

        return count;
    }

    [GeneratedRegex(@"^[A-Za-z]:[\\/]")]
    private static partial Regex DrivePathRegex();
}
