using System.Globalization;
using LazyGit.Models;

namespace LazyGit.Git.Parsers;

/// <summary>
/// Разбор <c>git for-each-ref refs/tags</c>: записи через NUL, поля через US — имя, тип объекта, коммит аннотированного
/// тега, объект, дата, подпись. Свежие теги первыми.
/// </summary>
public static class TagParser
{
    public static readonly string[] Arguments =
    [
        "for-each-ref", "refs/tags", "--sort=-creatordate",
        "--format=%(refname:short)%1f%(objecttype)%1f%(*objectname)%1f%(objectname)%1f%(creatordate:iso-strict)%1f%(contents:subject)%00",
    ];

    public static IReadOnlyList<Tag> Parse(string output)
    {
        var tags = new List<Tag>();
        foreach (var record in output.Split('\0'))
        {
            var fields = record.TrimStart('\n').Split('\x1f');
            if (fields.Length < 6 || fields[0].Length == 0)
                continue;

            var annotated = fields[1] == "tag";
            DateTimeOffset.TryParse(fields[4], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date);
            tags.Add(new Tag(fields[0], annotated && fields[2].Length > 0 ? fields[2] : fields[3], annotated, date, fields[5]));
        }

        return tags;
    }
}
