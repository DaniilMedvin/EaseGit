using System.Diagnostics;
using LazyGit.Auth;
using LazyGit.Git;
using LazyGit.UI.Widgets;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace LazyGit.UI;

public enum StartAction
{
    Login,
    OpenRepo,
}

/// <summary>Стартовый экран: логотип, путь к репозиторию и текущий пользователь. Result: Login, OpenRepo или null (выход по Esc).</summary>
public sealed class StartWindow : Runnable<StartAction?>
{
    private readonly GitRunner _git;
    private readonly AuthStatus _auth;
    private readonly ClipboardTextField _path;
    private readonly Label _status;
    private readonly Starfield _stars = new() { Height = Dim.Fill(6) };
    private readonly Waves _waves = new() { X = 0, Y = Pos.AnchorEnd(6) };
    private bool _busy;

    // Путь, для которого ждём повторного ctrl+n, чтобы выполнить git init.
    private string? _pendingInitPath;

    public StartWindow(GitRunner git, AuthStatus auth, string initialPath)
    {
        _git = git;
        _auth = auth;
        Width = Dim.Fill();
        Height = Dim.Fill();
        SetScheme(Theme.Base);

        var logo = new Logo { X = Pos.Center(), Y = Pos.Center() - 7 };

        var tagline = new Label
        {
            Text = "─── git без боли, прямо в терминале ───",
            X = Pos.Center(),
            Y = Pos.Bottom(logo) + 1,
        };
        tagline.SetScheme(Theme.Text(Theme.Muted, TextStyle.Italic));

        var box = new View
        {
            BorderStyle = LineStyle.Rounded,
            X = Pos.Center(),
            Y = Pos.Bottom(tagline) + 2,
            Width = Dim.Percent(60),
            Height = 3,
            CanFocus = true,
        };
        box.SetScheme(Theme.Text(Theme.Accent));

        var prompt = new Label { Text = " > ", X = 0, Y = 0 };
        prompt.SetScheme(Theme.Text(Theme.Accent, TextStyle.Bold));

        _path = new ClipboardTextField
        {
            Text = initialPath,
            X = Pos.Right(prompt),
            Y = 0,
            Width = Dim.Fill(1),
        };
        _path.SetScheme(Theme.Input);
        _path.TextChanged += (_, _) => _pendingInitPath = null;
        _path.Accepting += (_, e) =>
        {
            e.Handled = true;
            _ = OpenRepoAsync();
        };
        box.Add(prompt, _path);

        _status = new Label
        {
            X = Pos.Left(box) + 1,
            Y = Pos.Bottom(box),
            Width = Dim.Width(box) - 2,
            Height = 1,
        };

        var hints = new HintBar { X = 1, Y = Pos.AnchorEnd() };
        var hintList = new List<Hint> { new("enter", "открыть"), new("ctrl+o", "выбрать папку"), new("ctrl+n", "git init") };
        if (!auth.IsAuthorized)
            hintList.Add(new Hint("ctrl+g", "войти в GitHub"));
        hintList.Add(new Hint("esc", "выход"));
        hints.SetHints(hintList);

        Add(_stars, _waves, new UserBadge(auth), logo, tagline, box, BoxTitle.For(box, "Путь к репозиторию", Theme.Accent), _status, hints, new VersionLabel());
        _path.SetFocus();
    }

    public string? RepoPath { get; private set; }

    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.O.WithCtrl)
        {
            _ = PickFolderAsync();
            return true;
        }

        if (key == Key.N.WithCtrl)
        {
            _ = InitRepoAsync();
            return true;
        }

        if (key == Key.G.WithCtrl && !_auth.IsAuthorized)
        {
            if (!_auth.GhInstalled)
            {
                ShowStatus($"{Theme.SymbolError} gh не найден. Установи: sudo dnf install gh", Theme.Error);
                return true;
            }

            Result = StartAction.Login;
            RequestStop();
            return true;
        }

        return base.OnKeyDown(key);
    }

    protected override void OnIsRunningChanged(bool newIsRunning)
    {
        base.OnIsRunningChanged(newIsRunning);
        foreach (var animation in new AnimatedView[] { _stars, _waves })
        {
            if (newIsRunning)
                animation.Start();
            else
                animation.Stop();
        }
    }

    private async Task OpenRepoAsync()
    {
        if (_busy)
            return;

        var path = ExpandHome(_path.Text.Trim());
        if (path.Length == 0)
        {
            ShowStatus($"{Theme.SymbolError} Введи путь к папке с репозиторием", Theme.Error);
            return;
        }

        _busy = true;
        ShowStatus($"{Theme.SpinnerFrames[0]} Проверяю репозиторий…", Theme.Muted);
        try
        {
            var root = await _git.FindRepoRootAsync(path);
            App?.Invoke(() =>
            {
                if (root is null)
                {
                    ShowStatus(Directory.Exists(path)
                        ? $"{Theme.SymbolError} Не git-репозиторий. ctrl+n — создать его здесь"
                        : $"{Theme.SymbolError} Папка не найдена: {path}", Theme.Error);
                    return;
                }

                RepoPath = root;
                Result = StartAction.OpenRepo;
                RequestStop();
            });
        }
        catch (InvalidOperationException ex)
        {
            App?.Invoke(() => ShowStatus($"{Theme.SymbolError} {ex.Message}", Theme.Error));
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>
    /// git init в папке из поля ввода. Первое нажатие ctrl+n просит подтверждения, второе создаёт репозиторий
    /// и открывает его.
    /// </summary>
    private async Task InitRepoAsync()
    {
        if (_busy)
            return;

        var path = ExpandHome(_path.Text.Trim());
        if (path.Length == 0)
        {
            ShowStatus($"{Theme.SymbolError} Введи путь к папке для нового репозитория", Theme.Error);
            return;
        }

        path = Path.GetFullPath(path);
        _busy = true;
        try
        {
            var existing = await _git.FindRepoRootAsync(path);
            if (existing is not null)
            {
                App?.Invoke(() => ShowStatus($"{Theme.SymbolWarning} Уже репозиторий: {existing}. enter — открыть", Theme.Warning));
                return;
            }

            if (_pendingInitPath != path)
            {
                _pendingInitPath = path;
                var note = Directory.Exists(path) ? "" : " (папка будет создана)";
                App?.Invoke(() => ShowStatus($"{Theme.SymbolWarning} ctrl+n ещё раз — создать репозиторий в {path}{note}", Theme.Warning));
                return;
            }

            _pendingInitPath = null;
            var result = await GitService.InitAsync(_git, path);
            App?.Invoke(() =>
            {
                if (!result.Success)
                {
                    ShowStatus($"{Theme.SymbolError} {result.StdErr.Trim()}", Theme.Error);
                    return;
                }

                RepoPath = path;
                Result = StartAction.OpenRepo;
                RequestStop();
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            App?.Invoke(() => ShowStatus($"{Theme.SymbolError} {ex.Message}", Theme.Error));
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task PickFolderAsync()
    {
        var start = ExpandHome(_path.Text.Trim());
        var picked = await PickWithZenityAsync(start);
        if (picked is ZenityResult.Unavailable)
        {
            App?.Invoke(PickWithBuiltInDialog);
            return;
        }

        if (picked is ZenityResult.Picked { Path: var path })
        {
            App?.Invoke(() =>
            {
                _path.Text = path;
                _ = OpenRepoAsync();
            });
        }
    }

    private void PickWithBuiltInDialog()
    {
        var dialog = new OpenDialog
        {
            Title = "Выбери папку репозитория",
            OpenMode = OpenMode.Directory,
        };
        App!.Run(dialog);
        if (!dialog.Canceled && !string.IsNullOrEmpty(dialog.Path))
        {
            _path.Text = dialog.Path;
            _ = OpenRepoAsync();
        }

        dialog.Dispose();
    }

    private abstract record ZenityResult
    {
        public sealed record Picked(string Path) : ZenityResult;
        public sealed record Cancelled : ZenityResult;
        public sealed record Unavailable : ZenityResult;
    }

    // Системный диалог выбора папки (zenity). Без графической сессии возвращает Unavailable.
    private static async Task<ZenityResult> PickWithZenityAsync(string startPath)
    {
        var hasDisplay = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))
                         || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"));
        if (!hasDisplay)
            return new ZenityResult.Unavailable();

        var psi = new ProcessStartInfo("zenity")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("--file-selection");
        psi.ArgumentList.Add("--directory");
        psi.ArgumentList.Add("--title=Выбери папку репозитория");
        if (Directory.Exists(startPath))
            psi.ArgumentList.Add($"--filename={startPath.TrimEnd('/')}/");

        try
        {
            using var process = Process.Start(psi)!;
            var stdout = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            return process.ExitCode == 0
                ? new ZenityResult.Picked(stdout.Trim())
                : new ZenityResult.Cancelled();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return new ZenityResult.Unavailable();
        }
    }

    private void ShowStatus(string text, Color color)
    {
        _status.Text = text;
        _status.SetScheme(Theme.Text(color));
    }

    private static string ExpandHome(string path)
    {
        if (path == "~" || path.StartsWith("~/"))
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + path[1..];
        return path;
    }
}
