using System.ComponentModel;
using System.Diagnostics;

namespace LazyGit.Git;

public sealed class GitRunner
{
    public async Task<GitResult> RunAsync(string workingDirectory, IReadOnlyList<string> args, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("core.quotepath=false");
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("color.ui=never");
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        psi.Environment["LC_ALL"] = "C";
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";

        var command = "git " + string.Join(' ', args);
        try
        {
            using var process = Process.Start(psi)!;
            var stdout = process.StandardOutput.ReadToEndAsync(ct);
            var stderr = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            return new GitResult(command, process.ExitCode, await stdout, await stderr);
        }
        catch (Win32Exception)
        {
            throw new InvalidOperationException("git не найден. Установи git и добавь его в PATH.");
        }
    }

    /// <summary>Возвращает корень репозитория или null, если путь не находится внутри git-репозитория.</summary>
    public async Task<string?> FindRepoRootAsync(string path, CancellationToken ct = default)
    {
        if (!Directory.Exists(path))
            return null;

        var result = await RunAsync(path, ["rev-parse", "--show-toplevel"], ct);
        return result.Success ? result.StdOut.Trim() : null;
    }
}
