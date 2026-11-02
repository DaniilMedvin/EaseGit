using System.ComponentModel;
using System.Diagnostics;

namespace EaseGit.UI;

/// <summary>
/// Системный буфер обмена через утилиты ОС: wl-copy / wl-paste (Wayland), xsel или xclip (X11),
/// pbcopy / pbpaste (macOS), clip / Get-Clipboard (Windows).
/// Если ни одна утилита недоступна, текст хранится внутри приложения.
/// </summary>
public static class SystemClipboard
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    private static string _fallback = "";

    public static string GetText()
    {
        foreach (var (command, args) in PasteCommands())
        {
            if (TryRun(command, args, input: null, out var output))
                return output;
        }

        return _fallback;
    }

    public static void SetText(string text)
    {
        _fallback = text;
        foreach (var (command, args) in CopyCommands())
        {
            if (TryRun(command, args, text, out _))
                return;
        }
    }

    private static IEnumerable<(string Command, string[] Args)> PasteCommands()
    {
        if (OperatingSystem.IsMacOS())
        {
            yield return ("pbpaste", []);
            yield break;
        }

        if (OperatingSystem.IsWindows())
        {
            yield return ("powershell", ["-NoProfile", "-Command", "Get-Clipboard -Raw"]);
            yield break;
        }

        if (HasEnv("WAYLAND_DISPLAY"))
            yield return ("wl-paste", ["--no-newline"]);
        if (HasEnv("DISPLAY"))
        {
            yield return ("xsel", ["--clipboard", "--output"]);
            yield return ("xclip", ["-selection", "clipboard", "-out"]);
        }
    }

    private static IEnumerable<(string Command, string[] Args)> CopyCommands()
    {
        if (OperatingSystem.IsMacOS())
        {
            yield return ("pbcopy", []);
            yield break;
        }

        if (OperatingSystem.IsWindows())
        {
            yield return ("clip", []);
            yield break;
        }

        if (HasEnv("WAYLAND_DISPLAY"))
            yield return ("wl-copy", []);
        if (HasEnv("DISPLAY"))
        {
            yield return ("xsel", ["--clipboard", "--input"]);
            yield return ("xclip", ["-selection", "clipboard", "-in"]);
        }
    }

    private static bool TryRun(string command, string[] args, string? input, out string output)
    {
        output = "";
        var psi = new ProcessStartInfo(command)
        {
            RedirectStandardInput = input is not null,
            RedirectStandardOutput = input is null,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        try
        {
            using var process = Process.Start(psi)!;
            if (input is not null)
            {
                process.StandardInput.Write(input);
                process.StandardInput.Close();
            }

            var read = input is null ? process.StandardOutput.ReadToEndAsync() : Task.FromResult("");
            if (!process.WaitForExit(Timeout))
            {
                process.Kill();
                return false;
            }

            output = read.Result;
            return process.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            // Утилита не установлена — пробуем следующую.
            return false;
        }
    }

    private static bool HasEnv(string name) => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name));
}
