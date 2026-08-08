using LocalAI.Core.Models;
using LocalAI.Core.Repositories;
using LocalAI.Infrastructure.Repositories;

namespace LocalAI.Tests;

public sealed class ProjectInstructionServiceTests : IDisposable
{
    private readonly string _repositoryRoot;
    private readonly ProjectInstructionService _service = new();

    public ProjectInstructionServiceTests()
    {
        _repositoryRoot = Path.Combine(
            Path.GetTempPath(),
            $"LocalAI-Instructions-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_repositoryRoot);
    }

    [Fact]
    public async Task DiscoverAsync_ReturnsOnlySupportedPathsInStableOrder()
    {
        await WriteAsync("AGENTS.md", "root rules");
        await WriteAsync("skills/zeta/SKILL.md", "zeta rules");
        await WriteAsync("skills/alpha/SKILL.md", "alpha rules");
        await WriteAsync("skills/alpha/notes.md", "not an instruction");
        await WriteAsync("nested/AGENTS.md", "nested rules");
        await WriteAsync(
            "skills/alpha/nested/SKILL.md",
            "nested skill rules");

        ProjectInstructionManifest manifest =
            await _service.DiscoverAsync(_repositoryRoot);

        Assert.Equal(
            [
                "AGENTS.md",
                "skills/alpha/SKILL.md",
                "skills/zeta/SKILL.md"
            ],
            manifest.Files
                .Select(file => file.RelativePath)
                .ToArray());
        Assert.All(manifest.Files, file => Assert.True(file.IsEligible));
        Assert.Empty(manifest.DiscoveryIssues);
    }

    [Fact]
    public async Task DiscoverAsync_ShowsMissingMalformedBinaryAndOversizedReasons()
    {
        await WriteBytesAsync(
            "skills/binary/SKILL.md",
            [65, 0, 66]);
        await WriteBytesAsync(
            "skills/malformed/SKILL.md",
            [0xC3, 0x28]);
        await WriteBytesAsync(
            "skills/large/SKILL.md",
            new byte[
                (int)ProjectInstructionSelectionBuilder
                    .MaximumInstructionBytes + 1]);

        ProjectInstructionManifest manifest =
            await _service.DiscoverAsync(_repositoryRoot);

        ProjectInstructionFile agents = manifest.Files[0];
        Assert.Equal("AGENTS.md", agents.RelativePath);
        Assert.False(agents.IsEligible);
        Assert.Contains(
            "Not found",
            agents.ExclusionReason,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            manifest.Files,
            file => file.RelativePath == "skills/binary/SKILL.md" &&
                file.ExclusionReason!.Contains(
                    "Binary",
                    StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            manifest.Files,
            file => file.RelativePath == "skills/malformed/SKILL.md" &&
                file.ExclusionReason!.Contains(
                    "UTF-8",
                    StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            manifest.Files,
            file => file.RelativePath == "skills/large/SKILL.md" &&
                file.ExclusionReason!.Contains(
                    "8 KB",
                    StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DiscoverAsync_DoesNotFollowLinkedSkillFolder()
    {
        string outside = Path.Combine(
            Path.GetTempPath(),
            $"LocalAI-Instructions-Outside-{Guid.NewGuid():N}");
        string linkedSkill = Path.Combine(
            _repositoryRoot,
            "skills",
            "linked");
        Directory.CreateDirectory(outside);
        await File.WriteAllTextAsync(
            Path.Combine(outside, "SKILL.md"),
            "outside secret material");

        try
        {
            string skills = Path.Combine(_repositoryRoot, "skills");
            Directory.CreateDirectory(skills);
            CreateDirectoryJunction(linkedSkill, outside);

            ProjectInstructionManifest manifest =
                await _service.DiscoverAsync(_repositoryRoot);

            ProjectInstructionFile linked = Assert.Single(
                manifest.Files,
                file => file.RelativePath ==
                    "skills/linked/SKILL.md");
            Assert.False(linked.IsEligible);
            Assert.Null(linked.Content);
            Assert.Contains(
                "Linked",
                linked.ExclusionReason,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try
            {
                if (Directory.Exists(linkedSkill))
                {
                    Directory.Delete(linkedSkill);
                }

                Directory.Delete(outside, recursive: true);
            }
            catch
            {
                // Cleanup must not hide test results.
            }
        }
    }

    private async Task WriteAsync(
        string relativePath,
        string content)
    {
        string fullPath = Path.Combine(
            _repositoryRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, content);
    }

    private async Task WriteBytesAsync(
        string relativePath,
        byte[] bytes)
    {
        string fullPath = Path.Combine(
            _repositoryRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllBytesAsync(fullPath, bytes);
    }

    private static void CreateDirectoryJunction(
        string junctionPath,
        string targetPath)
    {
        System.Diagnostics.ProcessStartInfo startInfo = new()
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(
            $"New-Item -ItemType Junction -Path " +
            $"'{junctionPath.Replace("'", "''")}' -Target " +
            $"'{targetPath.Replace("'", "''")}' | Out-Null");

        using System.Diagnostics.Process process =
            System.Diagnostics.Process.Start(startInfo)!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_repositoryRoot, recursive: true);
        }
        catch
        {
            // Cleanup must not hide test results.
        }
    }
}
