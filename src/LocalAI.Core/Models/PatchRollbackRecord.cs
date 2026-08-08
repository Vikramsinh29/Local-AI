using System.Security.Cryptography;

namespace LocalAI.Core.Models;

public sealed class PatchRollbackRecord
{
    private readonly byte[] _originalBytes;
    private readonly byte[] _appliedBytes;

    public PatchRollbackRecord(
        string repositoryRoot,
        string relativePath,
        byte[] originalBytes,
        byte[] appliedBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentNullException.ThrowIfNull(originalBytes);
        ArgumentNullException.ThrowIfNull(appliedBytes);

        RepositoryRoot = repositoryRoot;
        RelativePath = relativePath;
        _originalBytes = [.. originalBytes];
        _appliedBytes = [.. appliedBytes];
        OriginalSha256 = Convert.ToHexString(
            SHA256.HashData(_originalBytes));
        AppliedSha256 = Convert.ToHexString(
            SHA256.HashData(_appliedBytes));
    }

    public string RepositoryRoot { get; }

    public string RelativePath { get; }

    public string OriginalSha256 { get; }

    public string AppliedSha256 { get; }

    public ReadOnlyMemory<byte> OriginalBytes => _originalBytes;

    public ReadOnlyMemory<byte> AppliedBytes => _appliedBytes;
}
