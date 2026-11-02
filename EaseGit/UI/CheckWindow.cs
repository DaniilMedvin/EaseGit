using System.Collections.Concurrent;
using System.Diagnostics;
using EaseGit.DotNet;
using EaseGit.Templates;
using EaseGit.UI.Panels;
using EaseGit.UI.Widgets;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace EaseGit.UI;

/// <summary>
/// Проверка по клавише: при открытии только dotnet build и список тестов; тесты и dotnet format — по клавишам.
/// Сверху итог по шагам и упавшие тесты, слева тесты по классам с выбором, справа весь вывод по мере выполнения.
/// В списке тестов: space — включить / исключить тест или класс, a — все, → / ← — развернуть / свернуть класс.
/// enter — прогнать выбранные (dotnet test --no-build --filter), b — собрать, r — пересобрать с нуля, f — dotnet format,
/// tab — тесты / вывод, e — только проблемы, esc — остановить, затем закрыть.
/// </summary>
public sealed class CheckWindow : Runnable
{
    private enum StepState
    {
        Waiting,
        Running,
        Passed,
        Failed,
        Skipped,
    }

    private enum Job
    {
        Build,
        Rebuild,
        Tests,
        Format,
    }

    private readonly DotnetRunner _dotnet = new();
    private readonly string _root;
    private readonly string _target;
    private readonly HashSet<string> _excluded;
    private readonly View _summary;
    private readonly TestsPanel _testsPanel;
    private readonly Label _testsTitle;
    private readonly OutputPanel _output;
    private readonly Label _outputTitle;
    private readonly ConcurrentQueue<string> _pending = new();
    private readonly List<string> _buildLines = [];
    private readonly List<string> _testLines = [];
    private readonly List<string> _formatLines = [];
    private readonly List<string> _failedTests = [];
    private readonly HashSet<string> _expanded = new(StringComparer.Ordinal);
    private IReadOnlyList<string> _tests = [];

    // Тесты последнего прогона: по ним в списке отметки ✓ (прогнан и не упал) и ✗.
    private HashSet<string> _ran = new(StringComparer.Ordinal);
    private HashSet<string> _failed = new(StringComparer.Ordinal);
    private bool _built;
    private CancellationTokenSource? _running;
    private object? _flushTimer;
    private (StepState State, string Note) _build = (StepState.Waiting, "");
    private (StepState State, string Note) _testsStep = (StepState.Waiting, "");
    private (StepState State, string Note) _format;
    private int _frame;

    /// <param name="target">Решение или проект относительно <paramref name="root"/>.</param>
    /// <param name="excluded">Исключённые тесты (полные имена). Принадлежит вызывающему: выбор живёт между открытиями окна.</param>
    public CheckWindow(string root, string target, HashSet<string> excluded)
    {
        _root = root;
        _target = target;
        _excluded = excluded;
        Width = Dim.Fill();
        Height = Dim.Fill();
        SetScheme(Theme.Base);
        _format = (StepState.Waiting, T("f — dotnet format", "f — dotnet format") + EditorConfigNote());

        _summary = new View { X = 0, Y = 0, Width = Dim.Fill(), Height = 5, BorderStyle = LineStyle.Rounded };
        _summary.SetScheme(Theme.Text(Theme.Dim));
        _summary.DrawingContent += (_, _) => DrawSummary();

        _testsPanel = new TestsPanel { X = 0, Y = Pos.Bottom(_summary), Width = Dim.Percent(40), Height = Dim.Fill(1) };
        _testsTitle = BoxTitle.ForFocusable(_testsPanel, T("Тесты", "Tests"));

        _output = new OutputPanel { X = Pos.Right(_testsPanel), Y = Pos.Bottom(_summary), Width = Dim.Fill(), Height = Dim.Fill(1) };
        _outputTitle = BoxTitle.ForFocusable(_output, T("Вывод", "Output"));

        var hints = new HintBar { X = 1, Y = Pos.AnchorEnd() };
        hints.SetHints(
        [
            new Hint("space", T("тест / класс", "test / class")),
            new Hint("a", T("все", "all")),
            new Hint("→ ←", T("развернуть", "expand"), Optional: true),
            new Hint("enter", T("прогнать выбранные", "run selected")),
            new Hint("b", T("собрать", "build"), Optional: true),
            new Hint("r", T("пересобрать", "rebuild")),
            new Hint("f", "format"),
            new Hint("tab", T("вывод", "output"), Optional: true),
            new Hint("e", T("только проблемы", "problems only"), Optional: true),
            new Hint("esc", T("остановить / закрыть", "stop / close")),
        ]);

        Add(_summary, BoxTitle.For(_summary, T($"Проверка {Theme.SymbolSeparator} {target}", $"Check {Theme.SymbolSeparator} {target}"), Theme.Accent),
            _testsPanel, _testsTitle, _output, _outputTitle, hints);
        _testsPanel.SetFocus();
    }

    protected override void OnIsRunningChanged(bool newIsRunning)
    {
        base.OnIsRunningChanged(newIsRunning);
        if (newIsRunning)
        {
            _flushTimer = App?.AddTimeout(TimeSpan.FromMilliseconds(100), () =>
            {
                Flush();
                return true;
            });
            _ = RunAsync(Job.Build);
        }
        else
        {
            _running?.Cancel();
            if (_flushTimer is not null)
                App?.RemoveTimeout(_flushTimer);
        }
    }

    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.Esc)
        {
            if (_running is { IsCancellationRequested: false } running)
                running.Cancel();
            else
                RequestStop();
            return true;
        }

        if (key == Key.Tab || key == Key.Tab.WithShift)
        {
            (_testsPanel.HasFocus ? (View)_output : _testsPanel).SetFocus();
            return true;
        }

        if (_running is null && JobFor(key) is { } job)
        {
            _ = RunAsync(job);
            return true;
        }

        if (key == Key.E)
        {
            _output.OnlyProblems = !_output.OnlyProblems;
            _outputTitle.Text = _output.OnlyProblems ? T(" Вывод · только проблемы ", " Output · problems only ") : T(" Вывод ", " Output ");
            return true;
        }

        if (_testsPanel.HasFocus && _testsPanel.Selected is { } row)
        {
            if (key == Key.Space)
                return Toggle(row);
            if (key == Key.A)
                return ToggleAll();
            if (key == Key.CursorRight || key == Key.L)
                return Expand(row, true);
            if (key == Key.CursorLeft || key == Key.H)
                return Expand(row, false);
        }

        return base.OnKeyDown(key);
    }

    /// <summary>space: тест — включить / исключить; класс — если выбран целиком, исключить весь, иначе выбрать весь.</summary>
    private bool Toggle(TestRow row)
    {
        var tests = row.IsClass ? _tests.Where(t => TestSelection.ClassOf(t) == row.Key).ToList() : [row.Key];
        var include = tests.Any(_excluded.Contains);
        foreach (var test in tests)
        {
            if (include)
                _excluded.Remove(test);
            else
                _excluded.Add(test);
        }

        UpdateRows();
        return true;
    }

    /// <summary>a: все выбраны — исключить все, иначе выбрать все.</summary>
    private bool ToggleAll()
    {
        if (_tests.Any(_excluded.Contains))
            _excluded.ExceptWith(_tests);
        else
            _excluded.UnionWith(_tests);
        UpdateRows();
        return true;
    }

    /// <summary>→ — развернуть класс, ← — свернуть (на тесте — свернуть его класс и встать на него).</summary>
    private bool Expand(TestRow row, bool expand)
    {
        var cls = row.IsClass ? row.Key : TestSelection.ClassOf(row.Key);
        if (expand)
            _expanded.Add(cls);
        else
            _expanded.Remove(cls);
        UpdateRows();
        if (!expand && !row.IsClass)
            _testsPanel.SelectIndex(_testsPanel.Items.ToList().FindIndex(r => r.Key == cls));
        return true;
    }

    private void UpdateRows()
    {
        var rows = new List<TestRow>();
        foreach (var group in _tests.GroupBy(TestSelection.ClassOf))
        {
            var tests = group.ToList();
            var expanded = _expanded.Contains(group.Key);
            var ran = tests.Where(_ran.Contains).ToList();
            rows.Add(new TestRow(true, group.Key, ShortName(group.Key), tests.Count(t => !_excluded.Contains(t)), tests.Count, expanded,
                Failed: tests.Any(_failed.Contains), Passed: ran.Count > 0 && !ran.Any(_failed.Contains)));
            if (expanded)
                rows.AddRange(tests.Select(t => new TestRow(false, t, t[(t.LastIndexOf('.') + 1)..], _excluded.Contains(t) ? 0 : 1, 1, false,
                    Failed: _failed.Contains(t), Passed: _ran.Contains(t) && !_failed.Contains(t))));
        }

        _testsPanel.SetItems(rows);
        _testsPanel.SetNeedsDraw();
        var selected = _tests.Count(t => !_excluded.Contains(t));
        _testsTitle.Text = _tests.Count == 0
            ? T(" Тесты ", " Tests ")
            : T($" Тесты {Theme.SymbolSeparator} выбрано {selected}/{_tests.Count} ", $" Tests {Theme.SymbolSeparator} selected {selected}/{_tests.Count} ");
    }

    /// <summary>«EaseGit.Tests.UI.CommitGraphTests» → «UI.CommitGraphTests»: последняя папка и класс.</summary>
    private static string ShortName(string cls)
    {
        var parts = cls.Split('.');
        return parts.Length >= 2 ? $"{parts[^2]}.{parts[^1]}" : cls;
    }

    private static Job? JobFor(Key key)
    {
        if (key == Key.Enter)
            return Job.Tests;
        if (key == Key.B)
            return Job.Build;
        if (key == Key.R)
            return Job.Rebuild;
        if (key == Key.F)
            return Job.Format;
        return null;
    }

    /// <summary>
    /// Сборка — с обновлением списка тестов; тесты — собирают, если ещё не собрано; format — сам по себе.
    /// Тесты и format сами не запускаются: только по клавише.
    /// </summary>
    private async Task RunAsync(Job job)
    {
        var running = new CancellationTokenSource();
        _running = running;
        _output.Clear();
        if (job != Job.Format)
        {
            _testLines.Clear();
            _failedTests.Clear();
        }

        try
        {
            switch (job)
            {
                case Job.Build or Job.Rebuild:
                    if (await BuildAsync(job == Job.Rebuild, running.Token))
                        await ListTestsAsync(running.Token);
                    break;
                case Job.Tests:
                    if (!_built && !await BuildAsync(false, running.Token))
                        return;
                    if (_tests.Count == 0)
                        await ListTestsAsync(running.Token);
                    await TestAsync(running.Token);
                    break;
                case Job.Format:
                    await FormatAsync(running.Token);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            Flush();
            if (_build.State == StepState.Running)
                _build = (StepState.Failed, T("остановлено", "stopped"));
            if (_testsStep.State == StepState.Running)
                _testsStep = (StepState.Skipped, T("остановлено", "stopped"));
            if (_format.State == StepState.Running)
                _format = (StepState.Failed, T("остановлено", "stopped"));
        }
        finally
        {
            _running = null;
            App?.Invoke(() => _summary.SetNeedsDraw());
        }
    }

    /// <summary>dotnet build; rebuild — с нуля (--no-incremental). Возвращает, собралось ли.</summary>
    private async Task<bool> BuildAsync(bool rebuild, CancellationToken ct)
    {
        _buildLines.Clear();
        _build = (StepState.Running, "");
        _built = false;
        var watch = Stopwatch.StartNew();
        string[] args = rebuild
            ? ["build", _target, "--no-incremental", "-tl:off", "-nologo"]
            : ["build", _target, "-tl:off", "-nologo"];
        var build = await _dotnet.RunAsync(_root, args, line => Receive(line, _buildLines), ct);
        Flush();
        if (build is null)
        {
            _build = (StepState.Failed, T("dotnet не найден: установи .NET SDK", "dotnet not found: install the .NET SDK"));
            _testsStep = (StepState.Skipped, "");
            return false;
        }

        var errors = DotnetOutput.CountErrors(_buildLines);
        var warnings = DotnetOutput.CountWarnings(_buildLines);
        var time = $"{watch.Elapsed.TotalSeconds:0.0} {T("с", "s")}";
        var warningsNote = warnings > 0 ? $" {Theme.SymbolSeparator} {Plural(warnings, "предупреждение", "предупреждения", "предупреждений", "warning", "warnings")}" : "";
        if (build != 0)
        {
            _build = (StepState.Failed, $"{Plural(Math.Max(1, errors), "ошибка", "ошибки", "ошибок", "error", "errors")}{warningsNote} {Theme.SymbolSeparator} {time}");
            _testsStep = (StepState.Skipped, T("сборка не прошла", "the build failed"));
            return false;
        }

        _build = (StepState.Passed, $"{(rebuild ? T("с нуля", "from scratch") + $" {Theme.SymbolSeparator} " : "")}{time}{warningsNote}");
        _built = true;
        _testsStep = (StepState.Waiting, T("enter — прогнать выбранные", "enter runs the selected ones"));
        Receive("", _buildLines);
        return true;
    }

    /// <summary>Список тестов (dotnet test --list-tests) для выбора слева; сами тесты не запускаются.</summary>
    private async Task ListTestsAsync(CancellationToken ct)
    {
        var listing = new List<string>();
        await _dotnet.RunAsync(_root, ["test", _target, "--no-build", "--list-tests", "-tl:off", "-nologo"], listing.Add, ct);
        _tests = TestSelection.ParseList(listing);
        App?.Invoke(UpdateRows);
    }

    /// <summary>
    /// dotnet format: правит файлы по .editorconfig (его нет — по правилам по умолчанию). Сообщения format не слушают
    /// DOTNET_CLI_UI_LANGUAGE, поэтому язык задаётся через LC_ALL — иначе не посчитать поправленные файлы.
    /// </summary>
    private async Task FormatAsync(CancellationToken ct)
    {
        _formatLines.Clear();
        _format = (StepState.Running, "");
        var watch = Stopwatch.StartNew();
        var format = await _dotnet.RunAsync(_root, ["format", _target, "-v", "normal"], line => Receive(line, _formatLines),
            new Dictionary<string, string> { ["LC_ALL"] = "C.UTF-8" }, ct);
        Flush();
        var time = $"{watch.Elapsed.TotalSeconds:0.0} {T("с", "s")}";
        var files = DotnetOutput.CountFormattedFiles(_formatLines);
        _format = format switch
        {
            null => (StepState.Failed, T("dotnet не найден: установи .NET SDK", "dotnet not found: install the .NET SDK")),
            not 0 => (StepState.Failed, T($"dotnet format завершился с ошибкой {Theme.SymbolSeparator} {time}", $"dotnet format failed {Theme.SymbolSeparator} {time}")),
            _ when files > 0 => (StepState.Passed, T($"поправлено: {Plural(files, "файл", "файла", "файлов", "file", "files")}", $"fixed {Plural(files, "файл", "файла", "файлов", "file", "files")}")
                + $" {Theme.SymbolSeparator} {time}{EditorConfigNote()}"),
            _ => (StepState.Passed, T("без изменений", "no changes") + $" {Theme.SymbolSeparator} {time}{EditorConfigNote()}"),
        };
    }

    private string EditorConfigNote() => RepoScaffold.EditorConfigExists(_root)
        ? ""
        : T($" {Theme.SymbolSeparator} нет .editorconfig: правила по умолчанию (E в главном окне — создать)",
            $" {Theme.SymbolSeparator} no .editorconfig: default rules (E in the main window creates one)");

    /// <summary>Выбранные тесты: без фильтра, если выбраны все; ничего не выбрано — шаг пропускается.</summary>
    private async Task TestAsync(CancellationToken ct)
    {
        var filter = TestSelection.Filter(_tests, _excluded);
        var selected = _tests.Where(t => !_excluded.Contains(t)).ToHashSet(StringComparer.Ordinal);
        if (filter == "")
        {
            _testsStep = (StepState.Skipped, T("ничего не выбрано: space — тест, a — все", "nothing selected: space picks a test, a — all"));
            return;
        }

        _testsStep = (StepState.Running, "");
        var watch = Stopwatch.StartNew();
        string[] args = filter is null
            ? ["test", _target, "--no-build", "-tl:off", "-nologo"]
            : ["test", _target, "--no-build", "-tl:off", "-nologo", "--filter", filter];
        var test = await _dotnet.RunAsync(_root, args, line => Receive(line, _testLines), ct);
        Flush();

        List<string> failedNames;
        lock (_failedTests)
            failedNames = _failedTests.ToList();
        _ran = selected;
        _failed = failedNames.Select(n => n.IndexOf('(') is var paren and > 0 ? n[..paren] : n).ToHashSet(StringComparer.Ordinal);
        App?.Invoke(UpdateRows);

        var totals = DotnetOutput.TestTotals(_testLines);
        var time = $"{watch.Elapsed.TotalSeconds:0.0} {T("с", "s")}";
        var scope = selected.Count == _tests.Count || _tests.Count == 0
            ? ""
            : T($" {Theme.SymbolSeparator} выбрано {selected.Count} из {_tests.Count}", $" {Theme.SymbolSeparator} {selected.Count} of {_tests.Count} selected");
        _testsStep = totals switch
        {
            null when test == 0 => (StepState.Passed, T($"тестов не найдено {Theme.SymbolSeparator} {time}", $"no tests found {Theme.SymbolSeparator} {time}")),
            null => (StepState.Failed, T($"dotnet test завершился с ошибкой {Theme.SymbolSeparator} {time}", $"dotnet test failed {Theme.SymbolSeparator} {time}")),
            var (failed, _, skipped, total) when failed > 0 =>
                (StepState.Failed, T($"упало {failed} из {total}", $"{failed} of {total} failed") + Skips(skipped) + scope + $" {Theme.SymbolSeparator} {time}"),
            var (_, passed, skipped, total) =>
                (StepState.Passed, T($"{passed} из {total}", $"{passed} of {total}") + Skips(skipped) + scope + $" {Theme.SymbolSeparator} {time}"),
        };
    }

    private static string Skips(int skipped) => skipped > 0 ? T($", пропущено {skipped}", $", {skipped} skipped") : "";

    /// <summary>Строка из фонового потока: в очередь (на экран — пачкой по таймеру) и в лог своего шага.</summary>
    private void Receive(string line, List<string> step)
    {
        lock (step)
            step.Add(line);
        if (DotnetOutput.FailedTestName(line) is { } name)
        {
            lock (_failedTests)
                _failedTests.Add(name);
        }

        // Пути — от корня репозитория: «Broken/Program.cs(1,19): error …» вместо длинного абсолютного.
        _pending.Enqueue(line.Replace(_root + Path.DirectorySeparatorChar, ""));
    }

    private void Flush()
    {
        _frame++;
        var batch = new List<string>();
        while (_pending.TryDequeue(out var line))
            batch.Add(line);
        App?.Invoke(() =>
        {
            if (batch.Count > 0)
                _output.Append(batch);
            UpdateSummaryHeight();
            _summary.SetNeedsDraw();
        });
    }

    private void UpdateSummaryHeight()
    {
        int failed;
        lock (_failedTests)
            failed = _failedTests.Count;
        _summary.Height = 5 + (failed > 0 ? Math.Min(failed, 5) + 1 : 0);
    }

    /// <summary>«✓ Сборка  3,2 с · 2 предупреждения», «⠹ Тесты  идут…», «· Формат  f — dotnet format», затем упавшие тесты (до 5).</summary>
    private void DrawSummary()
    {
        DrawStep(0, T("Сборка", "Build"), _build);
        DrawStep(1, T("Тесты", "Tests"), _testsStep);
        DrawStep(2, T("Формат", "Format"), _format);
        List<string> failed;
        lock (_failedTests)
            failed = _failedTests.Distinct().ToList();
        if (failed.Count == 0)
            return;

        _summary.DrawSegments(1, 3, [new Segment(T("Упавшие тесты:", "Failed tests:"), Theme.Error, TextStyle.Bold)], _summary.Viewport.Width - 1);
        for (var i = 0; i < Math.Min(failed.Count, 5); i++)
        {
            var more = i == 4 && failed.Count > 5 ? T($"  … и ещё {failed.Count - 5}", $"  … and {failed.Count - 5} more") : "";
            _summary.DrawSegments(3, 4 + i, [new Segment($"{Theme.SymbolError} {failed[i]}{more}", Theme.Error)], _summary.Viewport.Width - 3);
        }
    }

    private void DrawStep(int row, string title, (StepState State, string Note) step)
    {
        var (symbol, color) = step.State switch
        {
            StepState.Running => (Theme.SpinnerFrames[_frame % Theme.SpinnerFrames.Length].ToString(), Theme.Info),
            StepState.Passed => (Theme.SymbolSuccess, Theme.Success),
            StepState.Failed => (Theme.SymbolError, Theme.Error),
            StepState.Skipped => ("–", Theme.Dim),
            _ => ("·", Theme.Dim),
        };
        var note = step.State == StepState.Running ? T("идёт…", "running…") : step.Note;
        _summary.DrawSegments(1, row,
        [
            new Segment(symbol + " ", color, TextStyle.Bold),
            new Segment(title.PadRight(8), step.State == StepState.Waiting ? Theme.Dim : Theme.Fg, TextStyle.Bold),
            new Segment(note, step.State == StepState.Failed ? Theme.Error : Theme.Muted),
        ], _summary.Viewport.Width - 1);
    }
}
