using System.Diagnostics;
using LazyGit.Auth;
using LazyGit.Git;
using LazyGit.GitHub;
using LazyGit.UI.Panels;
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
    private readonly IReadOnlyList<string> _recentRepos;
    private readonly RecentReposPanel _recent;

    /// <summary>Сколько недавних проектов видно под полем пути.</summary>
    private const int RecentRows = 5;
    private readonly ClipboardTextField _path;
    private readonly Label _status;
    private readonly Starfield _stars = new() { Height = Dim.Fill(6) };
    private readonly Waves _waves = new() { X = 0, Y = Pos.AnchorEnd(6) };
    private bool _busy;

    // Путь, для которого ждём повторного ctrl+n, чтобы выполнить git init.
    private string? _pendingInitPath;

    /// <param name="recentRepos">Недавние проекты: среди них дашборд PR ищет локальные копии.</param>
    public StartWindow(GitRunner git, AuthStatus auth, string initialPath, IReadOnlyList<string> recentRepos)
    {
        _recentRepos = recentRepos;
        _git = git;
        _auth = auth;
        Width = Dim.Fill();
        Height = Dim.Fill();
        SetScheme(Theme.Base);

        // Недавние — только существующие папки, кроме той, что уже в поле.
        var recent = recentRepos.Where(Directory.Exists).ToList();
        var recentRows = Math.Min(recent.Count, RecentRows);
        // Со списком недавних всё чуть выше, но логотип не уходит за верх экрана 80×24.
        var logo = new Logo { X = Pos.Center(), Y = Pos.Center() - 7 - (recentRows > 2 ? 1 : 0) };

        var tagline = new Label
        {
            Text = T("─── git без боли, прямо в терминале ───", "─── painless git, right in the terminal ───"),
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

        _recent = new RecentReposPanel
        {
            X = Pos.Left(box),
            Y = Pos.Bottom(_status) + 1,
            Width = Dim.Width(box),
            Height = recentRows + 2,
            Visible = recentRows > 0,
        };
        _recent.SetItems(recent);
        var recentTitle = BoxTitle.ForFocusable(_recent, T("Недавние", "Recent"));
        recentTitle.Visible = recentRows > 0;
        // Список без фокуса — без выбранной строки, с фокусом — ❯ на ней; маркер рисуется заново.
        _recent.HasFocusChanged += (_, _) => _recent.SetNeedsDraw();
        _recent.LeftTop += (_, _) => _path.SetFocus();

        // Справа внизу версия: подсказки не должны на неё налезать.
        var hints = new HintBar { X = 1, Y = Pos.AnchorEnd(), Width = Dim.Fill(14) };
        var hintList = new List<Hint> { new("enter", T("открыть", "open")) };
        if (recentRows > 0)
            hintList.Add(new Hint("↓", T("недавние", "recent")));
        hintList.AddRange([new Hint("ctrl+o", T("выбрать папку", "choose folder"), Optional: true), new Hint("ctrl+n", "git init", Optional: true)]);
        if (!auth.IsAuthorized)
            hintList.Add(new Hint("ctrl+g", T("войти в GitHub", "sign in to GitHub")));
        else
            hintList.Add(new Hint("ctrl+g", T("мои PR", "my PRs")));
        hintList.Add(new Hint("esc", T("выход", "quit")));
        hints.SetHints(hintList);

        Add(_stars, _waves, new UserBadge(auth), logo, tagline, box, BoxTitle.For(box, T("Путь к репозиторию", "Repository path"), Theme.Accent), _status, _recent, recentTitle, hints, new VersionLabel());
        _path.SetFocus();
    }

    public string? RepoPath { get; private set; }

    protected override bool OnKeyDown(Key key)
    {
        // ↓ из поля пути — в список недавних, ↑ с первой строки списка — обратно, enter в списке — открыть.
        if (_recent.Visible && _path.HasFocus && key == Key.CursorDown)
        {
            _recent.SetFocus();
            return true;
        }

        if (_recent.HasFocus && key == Key.Enter && _recent.Selected is { } recentPath)
        {
            _path.Text = recentPath;
            _ = OpenRepoAsync();
            return true;
        }

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
                ShowStatus(T($"{Theme.SymbolError} gh не найден. Установи: {GhInstallHint.Command()}", $"{Theme.SymbolError} gh not found. Install it: {GhInstallHint.Command()}"), Theme.Error);
                return true;
            }

            Result = StartAction.Login;
            RequestStop();
            return true;
        }

        // После входа ctrl+g — дашборд PR по всем репозиториям.
        if (key == Key.G.WithCtrl)
        {
            using var window = new GitHubWindow(new GitHubService(new GhRunner(), Environment.CurrentDirectory), repo: null, GitHubTab.Mine,
                new LocalProjects(_git, _recentRepos), _recentRepos.FirstOrDefault());
            App!.Run(window);
            if (window.Result == GitHubAction.OpenRepository && window.OpenPath is { } path)
            {
                RepoPath = path;
                Result = StartAction.OpenRepo;
                RequestStop();
            }

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
            ShowStatus(T($"{Theme.SymbolError} Введи путь к папке с репозиторием", $"{Theme.SymbolError} Enter the path to a repository folder"), Theme.Error);
            return;
        }

        _busy = true;
        ShowStatus(T($"{Theme.SpinnerFrames[0]} Проверяю репозиторий…", $"{Theme.SpinnerFrames[0]} Checking the repository…"), Theme.Muted);
        try
        {
            var root = await _git.FindRepoRootAsync(path);
            App?.Invoke(() =>
            {
                if (root is null)
                {
                    ShowStatus(Directory.Exists(path)
                        ? T($"{Theme.SymbolError} Не git-репозиторий. ctrl+n — создать его здесь", $"{Theme.SymbolError} Not a git repository. ctrl+n creates one here")
                        : T($"{Theme.SymbolError} Папка не найдена: {path}", $"{Theme.SymbolError} Folder not found: {path}"), Theme.Error);
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
            ShowStatus(T($"{Theme.SymbolError} Введи путь к папке для нового репозитория", $"{Theme.SymbolError} Enter the path to a folder for the new repository"), Theme.Error);
            return;
        }

        path = Path.GetFullPath(path);
        _busy = true;
        try
        {
            var existing = await _git.FindRepoRootAsync(path);
            if (existing is not null)
            {
                App?.Invoke(() => ShowStatus(T($"{Theme.SymbolWarning} Уже репозиторий: {existing}. enter — открыть", $"{Theme.SymbolWarning} Already a repository: {existing}. enter opens it"), Theme.Warning));
                return;
            }

            if (_pendingInitPath != path)
            {
                _pendingInitPath = path;
                var note = Directory.Exists(path) ? "" : T(" (папка будет создана)", " (the folder will be created)");
                App?.Invoke(() => ShowStatus(T($"{Theme.SymbolWarning} ctrl+n ещё раз — создать репозиторий в {path}{note}", $"{Theme.SymbolWarning} ctrl+n again creates a repository in {path}{note}"), Theme.Warning));
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
            Title = T("Выбери папку репозитория", "Choose the repository folder"),
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
        psi.ArgumentList.Add(T("--title=Выбери папку репозитория", "--title=Choose the repository folder"));
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
