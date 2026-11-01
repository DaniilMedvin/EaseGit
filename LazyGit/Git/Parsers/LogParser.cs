using System.Globalization;
using LazyGit.Models;

namespace LazyGit.Git.Parsers;

/// <summary>
/// Разбор вывода <c>git log -z</c>. Коммиты разделены символом NUL,
/// поля — символом US (0x1F): хеш, имя автора, email, дата ISO 8601, заголовок, родители, ссылки.
/// Порядок --topo-order: родитель всегда ниже всех детей, ветки не перемешиваются — это нужно графу.
/// </summary>
public static class LogParser
{
    private const char FieldSeparator = '\x1f';

    /// <param name="allBranches">История всех локальных и удалённых веток и тегов, а не только HEAD. Stash не входит.</param>
    public static string[] Arguments(int limit, bool allBranches = false) =>
    [
        "log",
        "-z",
        $"-n{limit}",
        "--topo-order",
        // Полные имена ссылок: иначе локальную ветку feature/x не отличить от удалённой origin/x.
        "--decorate=full",
        "--format=%H%x1f%an%x1f%ae%x1f%aI%x1f%s%x1f%P%x1f%D",
        .. allBranches ? (string[])["--branches", "--remotes", "--tags", "HEAD"] : [],
    ];

    public static IReadOnlyList<Commit> Parse(string output)
    {
        var commits = new List<Commit>();
        foreach (var record in output.Split('\0'))
        {
            var fields = record.Split(FieldSeparator);
            if (fields.Length < 5)
                continue;

            commits.Add(new Commit(
                Hash: fields[0].Trim(),
                AuthorName: fields[1],
                AuthorEmail: fields[2],
                Date: DateTimeOffset.Parse(fields[3], CultureInfo.InvariantCulture),
                Subject: fields[4])
            {
                Parents = fields.Length > 5 ? fields[5].Split(' ', StringSplitOptions.RemoveEmptyEntries) : [],
                Refs = fields.Length > 6 ? ParseRefs(fields[6]) : [],
            });
        }

        return commits;
    }

    /// <summary>
    /// Ссылки из %D с --decorate=full: «HEAD -> refs/heads/main, tag: refs/tags/v1, refs/remotes/origin/main».
    /// origin/HEAD пропускается: он лишь повторяет основную ветку сервера.
    /// </summary>
    public static IReadOnlyList<CommitRef> ParseRefs(string decoration)
    {
        var refs = new List<CommitRef>();
        foreach (var part in decoration.Split(", ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var current = part.StartsWith("HEAD -> ");
            var name = current ? part["HEAD -> ".Length..] : part;
            if (name.StartsWith("tag: "))
                name = name["tag: ".Length..];

            CommitRef? parsed = name switch
            {
                "HEAD" => new CommitRef("HEAD", RefKind.Head),
                _ when name.StartsWith("refs/heads/") => new CommitRef(name["refs/heads/".Length..], RefKind.Branch, current),
                _ when name.StartsWith("refs/remotes/") && !name.EndsWith("/HEAD") => new CommitRef(name["refs/remotes/".Length..], RefKind.Remote),
                _ when name.StartsWith("refs/tags/") => new CommitRef(name["refs/tags/".Length..], RefKind.Tag),
                _ => null,
            };
            if (parsed is not null)
                refs.Add(parsed);
        }

        return refs;
    }
}
