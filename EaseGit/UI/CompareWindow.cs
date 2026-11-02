using EaseGit.Git;
using EaseGit.Models;
using EaseGit.UI.Panels;
using EaseGit.UI.Widgets;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace EaseGit.UI;

/// <summary>
/// Сравнение двух веток или коммитов: сверху стороны и сколько коммитов где, вкладки «Файлы» (статус, путь, +/−)
/// и «Коммиты» (только во второй стороне), снизу diff выбранного. m — от общего предка (что принесла вторая сторона,
/// как в PR) / между вершинами, s — поменять стороны местами, tab / 1 / 2 — вкладка, ctrl+d/u — листать, esc — закрыть.
/// </summary>
public sealed class CompareWindow : Runnable
{
    private readonly GitService _git;
    private readonly View _header;
    private readonly View _tabBar;
    private readonly ComparedFilesPanel _files;
    private readonly CommitsPanel _commits;
    private readonly Label _listTitle;
    private readonly DiffPanel _details;
    private readonly Label _detailsTitle;
    private readonly Label _status;
    private string _left;
    private string _right;
    private bool _fromMergeBase = true;
    private bool _commitsTab;
    private IReadOnlyList<Segment> _headerSegments = [];
    private IReadOnlyList<Segment> _tabSegments = [];
    private CancellationTokenSource? _loading;
    private CancellationTokenSource? _detailsLoading;

    /// <param name="left">База сравнения: обычно текущая ветка или старый коммит.</param>
    /// <param name="right">Что сравниваем с базой: ветка или HEAD.</param>
    public CompareWindow(GitService git, string left, string right)
    {
        _git = git;
        _left = left;
        _right = right;
        Width = Dim.Fill();
        Height = Dim.Fill();
        SetScheme(Theme.Base);

        _header = new View { X = 1, Y = 0, Width = Dim.Fill(1), Height = 1 };
        _header.DrawingContent += (_, _) => _header.DrawSegments(0, 0, _headerSegments, _header.Viewport.Width);
        _tabBar = new View { X = 1, Y = 1, Width = Dim.Fill(1), Height = 1 };
        _tabBar.DrawingContent += (_, _) => _tabBar.DrawSegments(0, 0, _tabSegments, _tabBar.Viewport.Width);

        _files = new ComparedFilesPanel { X = 0, Y = 2, Width = Dim.Fill(), Height = Dim.Percent(40) };
        _files.SelectionChanged += (_, _) => _ = LoadDetailsAsync();
        _commits = new CommitsPanel { X = 0, Y = 2, Width = Dim.Fill(), Height = Dim.Percent(40), Visible = false, ShowAuthor = true };
        _commits.SelectionChanged += (_, _) => _ = LoadDetailsAsync();
        // Один заголовок на обе вкладки: списки стоят на одном месте, виден только один.
        _listTitle = BoxTitle.ForFocusable(_files, "");

        _details = new DiffPanel { X = 0, Y = Pos.Bottom(_files), Width = Dim.Fill(), Height = Dim.Fill(2) };
        _detailsTitle = BoxTitle.For(_details, "Diff", Theme.Muted);

        _status = new Label { X = 1, Y = Pos.AnchorEnd(2), Width = Dim.Fill(1) };
        var hints = new HintBar { X = 1, Y = Pos.AnchorEnd() };
        hints.SetHints(
        [
            new Hint("tab", T("файлы / коммиты", "files / commits")),
            new Hint("m", T("от предка / вершины", "merge base / tips")),
            new Hint("s", T("поменять стороны", "swap sides")),
            new Hint("ctrl+d/u", "diff", Optional: true),
            new Hint("esc", T("закрыть", "close")),
        ]);

        Add(_header, _tabBar, _files, _commits, _listTitle, _details, _detailsTitle, _status, hints);
        _files.SetFocus();
    }

    protected override void OnIsRunningChanged(bool newIsRunning)
    {
        base.OnIsRunningChanged(newIsRunning);
        if (newIsRunning)
            _ = LoadAsync();
    }

    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.Esc)
        {
            RequestStop();
            return true;
        }

        if (key == Key.Tab || key == Key.Tab.WithShift || key == Key.D1 || key == Key.D2)
        {
            var commits = key == Key.D2 || (key != Key.D1 && !_commitsTab);
            if (commits != _commitsTab)
            {
                _commitsTab = commits;
                ShowTab();
            }

            return true;
        }

        if (key == Key.M)
        {
            _fromMergeBase = !_fromMergeBase;
            _ = LoadAsync();
            return true;
        }

        if (key == Key.S)
        {
            (_left, _right) = (_right, _left);
            _ = LoadAsync();
            return true;
        }

        if (key == Key.D.WithCtrl || key == Key.U.WithCtrl)
        {
            _details.ScrollBy((key == Key.D.WithCtrl ? 1 : -1) * Math.Max(1, _details.Viewport.Height / 2));
            return true;
        }

        return base.OnKeyDown(key);
    }

    private void ShowTab()
    {
        _files.Visible = !_commitsTab;
        _commits.Visible = _commitsTab;
        (_commitsTab ? (View)_commits : _files).SetFocus();
        UpdateTitles();
        _ = LoadDetailsAsync();
    }

    private async Task LoadAsync()
    {
        _loading?.Cancel();
        var loading = new CancellationTokenSource();
        _loading = loading;
        var (left, right, fromMergeBase) = (_left, _right, _fromMergeBase);
        ShowStatus($"{Theme.SpinnerFrames[0]} {T("Сравниваю…", "Comparing…")}", Theme.Muted);
        try
        {
            var filesTask = _git.CompareFilesAsync(left, right, fromMergeBase, loading.Token);
            var commitsTask = _git.CommitsOnlyInAsync(left, right, loading.Token);
            var divergenceTask = _git.CountDivergenceAsync(left, right, loading.Token);
            var baseTask = _git.GetMergeBaseAsync(left, right, loading.Token);
            var files = await filesTask;
            var commits = await commitsTask;
            var (onlyLeft, onlyRight) = await divergenceTask;
            var mergeBase = await baseTask;
            App?.Invoke(() =>
            {
                if (loading.IsCancellationRequested)
                    return;

                _headerSegments = HeaderSegments(onlyLeft, onlyRight, mergeBase);
                _header.SetNeedsDraw();
                _files.SetItems(files);
                _commits.SetItems(commits);
                UpdateTitles();
                var added = files.Sum(f => f.Added ?? 0);
                var deleted = files.Sum(f => f.Deleted ?? 0);
                ShowStatus($"{Plural(files.Count, "файл", "файла", "файлов", "file", "files")} {Theme.SymbolSeparator} +{added} {Theme.SymbolSeparator} -{deleted}", Theme.Muted);
                _ = LoadDetailsAsync();
            });
        }
        catch (OperationCanceledException)
        {
            // Режим или стороны сменились — загрузится заново.
        }
        catch (GitCommandException ex)
        {
            App?.Invoke(() => ShowStatus($"{Theme.SymbolError} {GitErrors.FirstLine(ex.Result.StdErr)}", Theme.Error));
        }
    }

    /// <summary>«⎇ main ↔ ⎇ feature · от общего предка a1b2c3d · только в feature: 5 · только в main: 2».</summary>
    private IReadOnlyList<Segment> HeaderSegments(int onlyLeft, int onlyRight, string? mergeBase)
    {
        var mode = _fromMergeBase
            ? mergeBase is null ? T("общей истории нет", "no common history") : T($"от общего предка {mergeBase[..7]}", $"from merge base {mergeBase[..7]}")
            : T("между вершинами", "between tips");
        return
        [
            Segment.Pill($"{Theme.SymbolBranch} {Short(_left)}", Theme.Info, TextStyle.Bold),
            new Segment(" ↔ ", Theme.Dim),
            Segment.Pill($"{Theme.SymbolBranch} {Short(_right)}", Theme.Accent, TextStyle.Bold),
            new Segment($"   {mode}", Theme.Muted),
            new Segment($"   {T("только в", "only in")} {Short(_right)}: ", Theme.Dim),
            new Segment(onlyRight.ToString(), Theme.Success, TextStyle.Bold),
            new Segment($" {Theme.SymbolSeparator} {T("только в", "only in")} {Short(_left)}: ", Theme.Dim),
            new Segment(onlyLeft.ToString(), onlyLeft > 0 ? Theme.Warning : Theme.Dim, TextStyle.Bold),
        ];
    }

    /// <summary>Хеш коммита — коротко, имя ветки — как есть.</summary>
    private static string Short(string revision) =>
        revision.Length == 40 && revision.All(Uri.IsHexDigit) ? revision[..7] : revision;

    private void UpdateTitles()
    {
        var files = T($"1 Файлы {_files.Items.Count}", $"1 Files {_files.Items.Count}");
        var commits = T($"2 Коммиты {_commits.Items.Count}", $"2 Commits {_commits.Items.Count}");
        _tabSegments =
        [
            _commitsTab ? new Segment($" {files} ", Theme.Muted) : Segment.Pill(files, Theme.Accent, TextStyle.Bold),
            new Segment("  ", Theme.Dim),
            _commitsTab ? Segment.Pill(commits, Theme.Accent, TextStyle.Bold) : new Segment($" {commits} ", Theme.Muted),
        ];
        _tabBar.SetNeedsDraw();
        _listTitle.Text = _commitsTab
            ? T($" Коммиты только в {Short(_right)} ", $" Commits only in {Short(_right)} ")
            : $" {CompareTitle()} ";
    }

    private string CompareTitle() => _fromMergeBase ? $"{Short(_left)}...{Short(_right)}" : $"{Short(_left)}..{Short(_right)}";

    private async Task LoadDetailsAsync()
    {
        _detailsLoading?.Cancel();
        var loading = new CancellationTokenSource();
        _detailsLoading = loading;
        try
        {
            PreparedDiff prepared;
            if (_commitsTab)
            {
                if (_commits.Selected is not { } commit)
                {
                    _details.SetMessage(T("Во второй стороне нет своих коммитов", "The second side has no commits of its own"));
                    return;
                }

                _detailsTitle.Text = $" {commit.ShortHash} ";
                var diff = await _git.GetCommitDiffAsync(commit, loading.Token);
                prepared = await Task.Run(() => DiffPanel.PrepareCommit(commit, diff, DateTimeOffset.Now), loading.Token);
            }
            else
            {
                if (_files.Selected is not { } file)
                {
                    _details.SetMessage("");
                    return;
                }

                _detailsTitle.Text = $" {file.Path} ";
                var lines = await _git.CompareFileDiffAsync(_left, _right, _fromMergeBase, file, loading.Token);
                prepared = await Task.Run(() => DiffPanel.Prepare(lines, file.Path), loading.Token);
            }

            App?.Invoke(() =>
            {
                if (!loading.IsCancellationRequested)
                    _details.SetDiff(prepared);
            });
        }
        catch (OperationCanceledException)
        {
            // Выбрано другое.
        }
        catch (GitCommandException ex)
        {
            App?.Invoke(() => _details.SetMessage($"{Theme.SymbolError} {GitErrors.FirstLine(ex.Result.StdErr)}"));
        }
    }

    private void ShowStatus(string text, Color color)
    {
        _status.Text = text;
        _status.SetScheme(Theme.Text(color));
    }
}
