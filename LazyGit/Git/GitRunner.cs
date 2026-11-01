using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace LazyGit.Git;

public sealed class GitRunner
{
    /// <summary>
    /// Запускает git с аргументами. <paramref name="input"/> передаётся в stdin.
    /// При отмене <paramref name="ct"/> процесс git убивается вместе с дочерними (ssh, credential helper)
    /// и бросается <see cref="OperationCanceledException"/>.
    /// </summary>
    public async Task<GitResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> args,
        CancellationToken ct = default,
        string? input = null)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = input is not null,
            // Без BOM: иначе он попадёт в начало сообщения коммита или патча.
            StandardInputEncoding = input is not null ? new UTF8Encoding(false) : null,
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
