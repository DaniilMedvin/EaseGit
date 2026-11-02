using System.Collections.Concurrent;
using System.Diagnostics;
using EaseGit.Commands;
using EaseGit.UI.Panels;
using EaseGit.UI.Widgets;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace EaseGit.UI;

/// <summary>
/// Своя команда с выводом («output»: true): сверху состояние и команда после подстановок, ниже вывод по мере выполнения.
/// ↑↓ / PageUp / End — прокрутка, esc — остановить (процесс убивается), затем закрыть.
/// </summary>
public sealed class CommandOutputWindow : Runnable
{
    private readonly ShellRunner _shell = new();
    private readonly string _root;
    private readonly string _command;
    private readonly View _summary;
    private readonly OutputPanel _output;
    private readonly ConcurrentQueue<string> _pending = new();
    private CancellationTokenSource? _running;
    private object? _flushTimer;
    private string _state = "";
    private Color _stateColor = Theme.Info;
    private int _frame;

    public CommandOutputWindow(string root, string title, string command)
    {
        _root = root;
        _command = command;
        Width = Dim.Fill();
        Height = Dim.Fill();
        SetScheme(Theme.Base);

        _summary = new View { X = 0, Y = 0, Width = Dim.Fill(), Height = 4, BorderStyle = LineStyle.Rounded };
        _summary.SetScheme(Theme.Text(Theme.Dim));
        _summary.DrawingContent += (_, _) => DrawSummary();

        _output = new OutputPanel { X = 0, Y = Pos.Bottom(_summary), Width = Dim.Fill(), Height = Dim.Fill(1) };

        var hints = new HintBar { X = 1, Y = Pos.AnchorEnd() };
        hints.SetHints(
        [
            new Hint("↑↓", T("прокрутка", "scroll"), Optional: true),
            new Hint("End", T("к концу", "to the end"), Optional: true),
            new Hint("esc", T("остановить / закрыть", "stop / close")),
        ]);

        Add(_summary, BoxTitle.For(_summary, title, Theme.Accent), _output, BoxTitle.ForFocusable(_output, T("Вывод", "Output")), hints);
        _output.SetFocus();
    }

    /// <summary>Код выхода; null — не завершилась (остановлена или shell не найден).</summary>
    public int? ExitCode { get; private set; }

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
        if (key != Key.Esc)
            return base.OnKeyDown(key);

        if (_running is { IsCancellationRequested: false } running)
            running.Cancel();
        else
            RequestStop();
        return true;
    }

    private async Task RunAsync()
    {
        var running = new CancellationTokenSource();
        _running = running;
        var watch = Stopwatch.StartNew();
        try
        {
            var exit = await _shell.RunAsync(_root, _command, _pending.Enqueue, running.Token);
            var time = $"{watch.Elapsed.TotalSeconds:0.0} {T("с", "s")}";
            ExitCode = exit;
            (_state, _stateColor) = exit switch
            {
                null => (T($"{Theme.SymbolError} shell не найден", $"{Theme.SymbolError} shell not found"), Theme.Error),
                0 => (T($"{Theme.SymbolSuccess} готово {Theme.SymbolSeparator} {time}", $"{Theme.SymbolSuccess} done {Theme.SymbolSeparator} {time}"), Theme.Success),
                _ => (T($"{Theme.SymbolError} код выхода {exit} {Theme.SymbolSeparator} {time}", $"{Theme.SymbolError} exit code {exit} {Theme.SymbolSeparator} {time}"), Theme.Error),
            };
        }
        catch (OperationCanceledException)
        {
            (_state, _stateColor) = (T($"{Theme.SymbolError} остановлено", $"{Theme.SymbolError} stopped"), Theme.Error);
        }
        finally
        {
            _running = null;
            Flush();
        }
    }

    private void Flush()
    {
        _frame++;
        var batch = new List<string>();
        while (_pending.TryDequeue(out var line))
            batch.Add(line.Replace(_root + Path.DirectorySeparatorChar, ""));
        App?.Invoke(() =>
        {
            if (batch.Count > 0)
                _output.Append(batch);
            _summary.SetNeedsDraw();
        });
    }

    /// <summary>«⠹ идёт…» / «✓ готово · 1,2 с» / «✗ код выхода 1», ниже команда после подстановок.</summary>
    private void DrawSummary()
    {
        var (state, color) = _running is not null
            ? ($"{Theme.SpinnerFrames[_frame % Theme.SpinnerFrames.Length]} {T("идёт…", "running…")}", Theme.Info)
            : (_state, _stateColor);
        _summary.DrawSegments(1, 0, [new Segment(state, color, TextStyle.Bold)], _summary.Viewport.Width - 1);
        _summary.DrawSegments(1, 1, [new Segment("$ ", Theme.Dim), new Segment(_command, Theme.Muted)], _summary.Viewport.Width - 1);
    }
}
