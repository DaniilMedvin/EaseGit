using System.ComponentModel;
using System.Diagnostics;

namespace EaseGit.Commands;

/// <summary>
/// Запуск своей команды через sh -c (на Windows — cmd /c) с выводом по строкам. stdin пустой: команда не перехватит
/// клавиатуру интерфейса и не зависнет в ожидании ввода. Отмена убивает процесс со всеми дочерними.
/// </summary>
public sealed class ShellRunner
{
    /// <summary>Код выхода; null — shell не найден.</summary>
    public async Task<int?> RunAsync(string workingDirectory, string command, Action<string> onLine, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        // cmd разбирает строку сам: ArgumentList экранировал бы кавычки подстановок по правилам C, а не cmd.
        if (OperatingSystem.IsWindows())
            psi.Arguments = "/d /s /c \"" + command + "\"";
        else
        {
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(command);
        }

        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";

        Process process;
        try
        {
            process = Process.Start(psi)!;
        }
        catch (Win32Exception)
        {
            return null;
        }

        using (process)
        {
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                    onLine(e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                    onLine(e.Data);
            };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.StandardInput.Close();
            using var kill = ct.Register(() =>
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // Уже завершился.
                }
            });
            await process.WaitForExitAsync(ct);
            // Дождаться последних строк вывода после выхода.
            process.WaitForExit();
            return process.ExitCode;
        }
    }
}
