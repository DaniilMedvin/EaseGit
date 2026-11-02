using System.ComponentModel;
using System.Diagnostics;

namespace EaseGit.DotNet;

/// <summary>
/// Запуск dotnet с выводом по строкам по мере появления (сборка и тесты идут долго — результат виден сразу).
/// Сообщения dotnet — на английском при любой локали: так итог разбирается надёжно. Отмена убивает процесс со всеми дочерними.
/// </summary>
/// <param name="executable">Команда dotnet; в тестах — несуществующая, чтобы проверить «dotnet не найден».</param>
public sealed class DotnetRunner(string executable = "dotnet")
{
    /// <summary>Код выхода; null — dotnet не найден.</summary>
    public Task<int?> RunAsync(string workingDirectory, IReadOnlyList<string> args, Action<string> onLine, CancellationToken ct = default) =>
        RunAsync(workingDirectory, args, onLine, null, ct);

    /// <summary>То же со своими переменными окружения поверх стандартных.</summary>
    public async Task<int?> RunAsync(string workingDirectory, IReadOnlyList<string> args, Action<string> onLine,
        IReadOnlyDictionary<string, string>? environment, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        psi.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        psi.Environment["DOTNET_NOLOGO"] = "1";
        psi.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        foreach (var (name, value) in environment ?? new Dictionary<string, string>())
            psi.Environment[name] = value;

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
