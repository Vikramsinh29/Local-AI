using System.Diagnostics;
using System.Text;
using LocalAI.Core.Interfaces;
using LocalAI.Core.Models;

namespace LocalAI.Infrastructure.Verification;

public sealed class VerificationToolRunner : IVerificationToolRunner
{
    private const int MaximumOutputCharacters = 200_000;

    public async Task<VerificationRunResult> RunAsync(
        VerificationToolKind tool,
        string repositoryRoot,
        string? solutionRelativePath,
        IProgress<VerificationOutputLine>? progress = null,
        CancellationToken cancellationToken = default)
    {
        VerificationCommand command =
            VerificationCommandFactory.Create(
                tool,
                repositoryRoot,
                solutionRelativePath);

        ProcessStartInfo startInfo = CreateStartInfo(command);

        using Process process = new()
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        StringBuilder output = new();
        object outputLock = new();
        bool outputTruncated = false;

        void AppendOutput(string? text, bool isError)
        {
            if (text is null)
            {
                return;
            }

            VerificationOutputLine line = new(text, isError);
            progress?.Report(line);

            lock (outputLock)
            {
                if (outputTruncated)
                {
                    return;
                }

                string formatted = isError
                    ? $"[stderr] {text}"
                    : text;

                int remaining =
                    MaximumOutputCharacters - output.Length;

                if (formatted.Length + Environment.NewLine.Length <=
                    remaining)
                {
                    output.AppendLine(formatted);
                    return;
                }

                if (remaining > 0)
                {
                    output.Append(
                        formatted.AsSpan(
                            0,
                            Math.Min(formatted.Length, remaining)));
                }

                output.AppendLine();
                output.AppendLine(
                    "[Output truncated by Local-AI safety limit]");
                outputTruncated = true;
            }
        }

        process.OutputDataReceived += (_, eventArgs) =>
            AppendOutput(eventArgs.Data, isError: false);

        process.ErrorDataReceived += (_, eventArgs) =>
            AppendOutput(eventArgs.Data, isError: true);

        DateTimeOffset startedAt = DateTimeOffset.Now;

        if (!process.Start())
        {
            throw new InvalidOperationException(
                $"Could not start verification tool: {command.FileName}");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        bool wasCancelled = false;

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            wasCancelled = true;
            TryTerminateProcessTree(process);
            await process.WaitForExitAsync(CancellationToken.None);
        }

        process.WaitForExit();

        DateTimeOffset completedAt = DateTimeOffset.Now;

        return new VerificationRunResult(
            tool,
            command.DisplayText,
            startedAt,
            completedAt,
            process.ExitCode,
            wasCancelled,
            output.ToString().TrimEnd());
    }

    private static ProcessStartInfo CreateStartInfo(
        VerificationCommand command)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = command.FileName,
            WorkingDirectory = command.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (string argument in command.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        startInfo.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        startInfo.Environment["GIT_PAGER"] = "cat";
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GIT_OPTIONAL_LOCKS"] = "0";

        return startInfo;
    }

    private static void TryTerminateProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process already exited while cancellation was handled.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Preserve cancellation even when Windows already removed it.
        }
    }
}
