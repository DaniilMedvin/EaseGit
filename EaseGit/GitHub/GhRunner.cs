using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using EaseGit.Git;

namespace EaseGit.GitHub;

/// <summary>
/// Запуск GitHub CLI. Как <see cref="GitRunner"/>: аргументы через ArgumentList, gh ничего не спрашивает
/// (GH_PROMPT_DISABLED), без цвета и пейджера; отмена убивает процесс.
/// </summary>
public sealed class GhRunner
{
    public async Task<GitResult> RunAsync(string workingDirectory, IReadOnlyList<string> args, CancellationToken ct = default,
        string? input = null)
    {
        var psi = new ProcessStartInfo("gh")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = input is not null,
            StandardInputEncoding = input is not null ? new UTF8Encoding(false) : null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        psi.Environment["GH_PROMPT_DISABLED"] = "1";
        psi.Environment["GH_NO_UPDATE_NOTIFIER"] = "1";
        psi.Environment["NO_COLOR"] = "1";
        psi.Environment["GH_PAGER"] = "";
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";

        var command = "gh " + string.Join(' ', args);
        try
        {
            using var process = Process.Start(psi)!;
            using var kill = ct.Register(() =>
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // Процесс уже завершился.
                }
            });
            if (input is not null)
            {
                await process.StandardInput.WriteAsync(input.AsMemory(), ct);
                process.StandardInput.Close();
            }

            var stdout = process.StandardOutput.ReadToEndAsync(ct);
            var stderr = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            return new GitResult(command, process.ExitCode, await stdout, await stderr);
        }
        catch (Win32Exception)
        {
            return new GitResult(command, 127, "", "gh not found");
        }
    }
}
