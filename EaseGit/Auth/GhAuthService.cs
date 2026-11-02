using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace EaseGit.Auth;

public sealed record AuthStatus(bool GhInstalled, string? Login)
{
    public bool IsAuthorized => Login is not null;
}

/// <summary>Авторизация в GitHub через gh CLI. Токен хранится в gh.</summary>
public sealed partial class GhAuthService
{
    private const string Host = "github.com";

    public async Task<AuthStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo("gh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in new[] { "auth", "status", "--hostname", Host, "--active" })
            psi.ArgumentList.Add(arg);
        psi.Environment["LC_ALL"] = "C";
        psi.Environment["GH_PROMPT_DISABLED"] = "1";

        try
        {
            using var process = Process.Start(psi)!;
            var stdout = process.StandardOutput.ReadToEndAsync(ct);
            var stderr = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            if (process.ExitCode != 0)
                return new AuthStatus(GhInstalled: true, Login: null);

            // В зависимости от версии gh статус выводится в stdout или stderr.
            var match = AccountRegex().Match(await stdout + await stderr);
            return new AuthStatus(GhInstalled: true, Login: match.Success ? match.Groups[1].Value : null);
        }
        catch (Win32Exception)
        {
            return new AuthStatus(GhInstalled: false, Login: null);
        }
    }

    /// <summary>
    /// Запускает интерактивный `gh auth login`.
    /// Вызывается при остановленном TUI: gh задаёт вопросы в терминале и открывает браузер.
    /// </summary>
    public int LoginInteractive()
    {
        var psi = new ProcessStartInfo("gh") { UseShellExecute = false };
        foreach (var arg in new[] { "auth", "login", "--hostname", Host, "--web" })
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)!;
        process.WaitForExit();
        return process.ExitCode;
    }

    [GeneratedRegex(@"account (\S+)")]
    private static partial Regex AccountRegex();
}
