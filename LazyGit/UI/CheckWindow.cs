using System.Collections.Concurrent;
using System.Diagnostics;
using LazyGit.DotNet;
using LazyGit.UI.Panels;
using LazyGit.UI.Widgets;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace LazyGit.UI;

/// <summary>
/// Проверка перед push по клавише: dotnet build, затем (если собралось) dotnet test --no-build. Сверху итог по шагам
/// и упавшие тесты, ниже весь вывод по мере выполнения. esc — остановить, после — закрыть; r — заново; e — только проблемы.
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

    private readonly DotnetRunner _dotnet = new();
    private readonly string _root;
    private readonly string _target;
    private readonly View _summary;
    private readonly OutputPanel _output;
    private readonly Label _outputTitle;
    private readonly ConcurrentQueue<string> _pending = new();
    private readonly List<string> _buildLines = [];
    private readonly List<string> _testLines = [];
    private readonly List<string> _failedTests = [];
    private CancellationTokenSource? _running;
    private object? _flushTimer;
    private (StepState State, string Note) _build = (StepState.Waiting, "");
    private (StepState State, string Note) _tests = (StepState.Waiting, "");
    private int _frame;

    /// <param name="target">Решение или проект относительно <paramref name="root"/>.</param>
    public CheckWindow(string root, string target)
    {
        _root = root;
        _target = target;
        Width = Dim.Fill();
        Height = Dim.Fill();
        SetScheme(Theme.Base);

        _summary = new View { X = 0, Y = 0, Width = Dim.Fill(), Height = 4, BorderStyle = LineStyle.Rounded };
        _summary.SetScheme(Theme.Text(Theme.Dim));
        _summary.DrawingContent += (_, _) => DrawSummary();

        _output = new OutputPanel { X = 0, Y = Pos.Bottom(_summary), Width = Dim.Fill(), Height = Dim.Fill(1) };
        _outputTitle = BoxTitle.ForFocusable(_output, T("Вывод", "Output"));

        var hints = new HintBar { X = 1, Y = Pos.AnchorEnd() };
        hints.SetHints(
        [
            new Hint("↑↓ PgUp/PgDn", T("листать", "scroll")),
            new Hint("End", T("к концу", "to the end"), Optional: true),
            new Hint("e", T("только проблемы", "problems only")),
            new Hint("r", T("заново", "again")),
            new Hint("esc", T("остановить / закрыть", "stop / close")),
        ]);

        Add(_summary, BoxTitle.For(_summary, T($"Проверка {Theme.SymbolSeparator} {target}", $"Check {Theme.SymbolSeparator} {target}"), Theme.Accent),
            _output, _outputTitle, hints);
        _output.SetFocus();
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
            _ = RunAsync();
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

        if (key == Key.R && _running is null)
        {
            _ = RunAsync();
            return true;
        }

        if (key == Key.E)
        {
            _output.OnlyProblems = !_output.OnlyProblems;
            _outputTitle.Text = _output.OnlyProblems ? T(" Вывод · только проблемы ", " Output · problems only ") : T(" Вывод ", " Output ");
            return true;
        }

        return base.OnKeyDown(key);
    }

    private async Task RunAsync()
    {
        var running = new CancellationTokenSource();
        _running = running;
        _buildLines.Clear();
        _testLines.Clear();
        _failedTests.Clear();
        _output.Clear();
        _build = (StepState.Running, "");
        _tests = (StepState.Waiting, "");
        try
        {
            var watch = Stopwatch.StartNew();
            var build = await _dotnet.RunAsync(_root, ["build", _target, "-tl:off", "-nologo"], line => Receive(line, _buildLines), running.Token);
            Flush();
            if (build is null)
            {
                _build = (StepState.Failed, T("dotnet не найден: установи .NET SDK", "dotnet not found: install the .NET SDK"));
                _tests = (StepState.Skipped, "");
                return;
            }

            var errors = DotnetOutput.CountErrors(_buildLines);
            var warnings = DotnetOutput.CountWarnings(_buildLines);
            var time = $"{watch.Elapsed.TotalSeconds:0.0} {T("с", "s")}";
            var warningsNote = warnings > 0 ? $" {Theme.SymbolSeparator} {Plural(warnings, "предупреждение", "предупреждения", "предупреждений", "warning", "warnings")}" : "";
            if (build != 0)
            {
                _build = (StepState.Failed, $"{Plural(Math.Max(1, errors), "ошибка", "ошибки", "ошибок", "error", "errors")}{warningsNote} {Theme.SymbolSeparator} {time}");
                _tests = (StepState.Skipped, T("сборка не прошла", "the build failed"));
                return;
            }

            _build = (StepState.Passed, $"{time}{warningsNote}");
            _tests = (StepState.Running, "");
            Receive("", _buildLines);
            watch.Restart();
            var test = await _dotnet.RunAsync(_root, ["test", _target, "--no-build", "-tl:off", "-nologo"], line => Receive(line, _testLines), running.Token);
            Flush();
            var totals = DotnetOutput.TestTotals(_testLines);
            time = $"{watch.Elapsed.TotalSeconds:0.0} {T("с", "s")}";
            _tests = totals switch
            {
                null when test == 0 => (StepState.Passed, T($"тестов не найдено {Theme.SymbolSeparator} {time}", $"no tests found {Theme.SymbolSeparator} {time}")),
                null => (StepState.Failed, T($"dotnet test завершился с ошибкой {Theme.SymbolSeparator} {time}", $"dotnet test failed {Theme.SymbolSeparator} {time}")),
                var (failed, passed, skipped, total) when failed > 0 =>
                    (StepState.Failed, T($"упало {failed} из {total}", $"{failed} of {total} failed") + Skips(skipped) + $" {Theme.SymbolSeparator} {time}"),
                var (_, passed, skipped, total) =>
                    (StepState.Passed, T($"{passed} из {total}", $"{passed} of {total}") + Skips(skipped) + $" {Theme.SymbolSeparator} {time}"),
            };
        }
        catch (OperationCanceledException)
        {
            Flush();
            if (_build.State == StepState.Running)
                _build = (StepState.Failed, T("остановлено", "stopped"));
            if (_tests.State is StepState.Running or StepState.Waiting)
                _tests = (StepState.Skipped, T("остановлено", "stopped"));
        }
        finally
        {
            _running = null;
            App?.Invoke(() => _summary.SetNeedsDraw());
        }
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
        _summary.Height = 4 + (failed > 0 ? Math.Min(failed, 5) + 1 : 0);
    }

    /// <summary>«✓ Сборка  3,2 с · 2 предупреждения», «⠹ Тесты  идут…», затем упавшие тесты (до 5).</summary>
    private void DrawSummary()
    {
        DrawStep(0, T("Сборка", "Build"), _build);
        DrawStep(1, T("Тесты", "Tests"), _tests);
        List<string> failed;
        lock (_failedTests)
            failed = _failedTests.Distinct().ToList();
        if (failed.Count == 0)
            return;

        _summary.DrawSegments(1, 2, [new Segment(T("Упавшие тесты:", "Failed tests:"), Theme.Error, TextStyle.Bold)], _summary.Viewport.Width - 1);
        for (var i = 0; i < Math.Min(failed.Count, 5); i++)
        {
            var more = i == 4 && failed.Count > 5 ? T($"  … и ещё {failed.Count - 5}", $"  … and {failed.Count - 5} more") : "";
            _summary.DrawSegments(3, 3 + i, [new Segment($"{Theme.SymbolError} {failed[i]}{more}", Theme.Error)], _summary.Viewport.Width - 3);
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
