using LazyGit.Models;

namespace LazyGit.Git.Parsers;

/// <summary>
/// Разбор <c>git worktree list --porcelain -z</c>: поля через NUL, записи через двойной NUL. С -z текст причин
/// блокировки идёт как есть (без экранирования кириллицы). Первая запись — основная папка.
/// </summary>
public static class WorktreeParser
{
    public static readonly string[] Arguments = ["worktree", "list", "--porcelain", "-z"];

    public static IReadOnlyList<Worktree> Parse(string output)
    {
        var worktrees = new List<Worktree>();
        foreach (var record in output.Split("\0\0", StringSplitOptions.RemoveEmptyEntries))
        {
            string? path = null, head = null, branch = null, locked = null, prunable = null;
            var bare = false;
            foreach (var field in record.Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                var space = field.IndexOf(' ');
                var (key, value) = space < 0 ? (field, "") : (field[..space], field[(space + 1)..]);
                switch (key)
                {
                    case "worktree": path = value; break;
                    case "HEAD": head = value; break;
                    case "branch": branch = value.StartsWith("refs/heads/", StringComparison.Ordinal) ? value["refs/heads/".Length..] : value; break;
                    case "bare": bare = true; break;
                    case "locked": locked = value; break;
                    case "prunable": prunable = value; break;
                }
            }

            if (path is not null)
                worktrees.Add(new Worktree(path, head ?? "", branch, worktrees.Count == 0, bare, locked, prunable));
        }

        return worktrees;
    }
}
