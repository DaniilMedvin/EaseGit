using LazyGit.Auth;
using LazyGit.Models;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;

namespace LazyGit.UI.Widgets;

/// <summary>
/// Шапка главного экрана: имя репозитория, ветка, ↑ahead ↓behind, количество изменений,
/// путь к репозиторию и пользователь справа. При нехватке места обрезается путь.
/// </summary>
public sealed class RepoHeader : View
{
    private readonly string _repoName;
    private readonly string _repoPath;
    private readonly AuthStatus _auth;
    private RepoStatus? _status;
    private RepoOperation _operation;

    public RepoHeader(string repoRoot, AuthStatus auth)
    {
        _repoName = Path.GetFileName(repoRoot.TrimEnd('/'));
        _repoPath = TextDrawing.ShortenHome(repoRoot);
        _auth = auth;
        Width = Dim.Fill();
        Height = 3;
        BorderStyle = LineStyle.Rounded;
        SetScheme(Theme.Text(Theme.Dim));
    }

    public void SetStatus(RepoStatus status, RepoOperation operation = RepoOperation.None)
    {
        _status = status;
        _operation = operation;
        SetNeedsDraw();
    }

    /// <summary>Название операции для плашки: «merge», «cherry-pick».</summary>
    public static string OperationName(RepoOperation operation) => operation switch
    {
        RepoOperation.Merge => "merge",
        RepoOperation.CherryPick => "cherry-pick",
        RepoOperation.Revert => "revert",
        RepoOperation.Rebase => "rebase",
        _ => "",
    };

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var user = _auth.IsAuthorized
            ? Segment.Pill($"{Theme.SymbolCurrent} {_auth.Login}", Theme.Success)
            : Segment.Pill(T($"{Theme.SymbolEmpty} Не авторизован", $"{Theme.SymbolEmpty} Not signed in"), Theme.Warning);
        var userX = Math.Max(0, Viewport.Width - user.Text.Length - 1);

        var segments = new List<Segment> { new(" " + _repoName, Theme.Fg, TextStyle.Bold) };
        if (_status is not null)
        {
            segments.AddRange(StatusSegments(_status));
            if (_operation != RepoOperation.None)
            {
                segments.Add(new Segment("   ", Theme.Fg));
                segments.Add(Segment.Pill(T($"{Theme.SymbolWarning} идёт {OperationName(_operation)}", $"{Theme.SymbolWarning} {OperationName(_operation)} in progress"), Theme.Warning, TextStyle.Bold));
            }
        }

        segments.Add(new Segment("   " + _repoPath, Theme.Dim));

        this.DrawSegments(0, 0, segments, Math.Max(0, userX - 2));
        this.DrawSegments(userX, 0, [user], Viewport.Width - userX);
        return true;
    }

    private static IEnumerable<Segment> StatusSegments(RepoStatus status)
    {
        yield return new Segment("   ", Theme.Fg);
        if (status.IsDetached)
        {
            var head = status.HeadHash is { Length: >= 7 } hash ? hash[..7] : "HEAD";
            yield return Segment.Pill($"{Theme.SymbolBranch} {head} (detached)", Theme.Warning, TextStyle.Bold);
        }
        else
        {
            yield return Segment.Pill($"{Theme.SymbolBranch} {status.Branch}", Theme.Info, TextStyle.Bold);
        }

        if (status.Upstream is not null)
        {
            yield return new Segment($" {Theme.SymbolAhead}{status.Ahead}", status.Ahead > 0 ? Theme.Success : Theme.Dim);
            yield return new Segment($" {Theme.SymbolBehind}{status.Behind}", status.Behind > 0 ? Theme.Warning : Theme.Dim);
        }
        else
        {
            yield return new Segment(T("  нет upstream", "  no upstream"), Theme.Dim);
        }

        yield return new Segment("   ", Theme.Fg);
        yield return status.IsClean
            ? Segment.Pill(T($"{Theme.SymbolSuccess} чисто", $"{Theme.SymbolSuccess} clean"), Theme.Success)
            : Segment.Pill(ChangesWord(status.Files.Count), Theme.Warning);
    }

    private static string ChangesWord(int count) => Plural(count, "изменение", "изменения", "изменений", "change", "changes");
}
