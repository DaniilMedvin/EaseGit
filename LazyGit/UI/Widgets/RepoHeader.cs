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

    public void SetStatus(RepoStatus status)
    {
        _status = status;
        SetNeedsDraw();
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var user = _auth.IsAuthorized
            ? new Segment($"{Theme.SymbolCurrent} {_auth.Login}", Theme.Success)
            : new Segment($"{Theme.SymbolEmpty} Не авторизован", Theme.Warning);
        var userX = Math.Max(0, Viewport.Width - user.Text.Length - 1);

        var segments = new List<Segment> { new(" " + _repoName, Theme.Fg, TextStyle.Bold) };
        if (_status is not null)
            segments.AddRange(StatusSegments(_status));
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
            yield return new Segment($"{Theme.SymbolBranch} {head} (detached)", Theme.Warning, TextStyle.Bold);
        }
        else
        {
            yield return new Segment($"{Theme.SymbolBranch} {status.Branch}", Theme.Info, TextStyle.Bold);
        }

        if (status.Upstream is not null)
        {
            yield return new Segment($" {Theme.SymbolAhead}{status.Ahead}", status.Ahead > 0 ? Theme.Success : Theme.Dim);
            yield return new Segment($" {Theme.SymbolBehind}{status.Behind}", status.Behind > 0 ? Theme.Warning : Theme.Dim);
        }
        else
        {
            yield return new Segment("  нет upstream", Theme.Dim);
        }

        yield return new Segment("   ", Theme.Fg);
        yield return status.IsClean
            ? new Segment($"{Theme.SymbolSuccess} чисто", Theme.Success)
            : new Segment($"{Theme.SymbolCurrent} {Plural(status.Files.Count)}", Theme.Warning);
    }

    private static string Plural(int count)
    {
        var mod10 = count % 10;
        var mod100 = count % 100;
        var word = mod10 == 1 && mod100 != 11 ? "изменение"
            : mod10 is >= 2 and <= 4 && mod100 is < 12 or > 14 ? "изменения"
            : "изменений";
        return $"{count} {word}";
    }
}
