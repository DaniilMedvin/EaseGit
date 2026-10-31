namespace LazyGit.Models;

public enum DiffLineKind
{
    /// <summary>Служебная строка до первого фрагмента: diff --git, index, new file mode, rename from и т.п.</summary>
    Header,

    /// <summary>Заголовок фрагмента: @@ -1,8 +1,9 @@.</summary>
    Hunk,
    Context,
    Added,
    Removed,

    /// <summary>\ No newline at end of file.</summary>
    NoNewline,

    /// <summary>Binary files ... differ.</summary>
    Binary,
}

/// <summary>Строка diff.</summary>
/// <param name="Kind">Вид строки.</param>
/// <param name="Text">Для Added, Removed и Context — содержимое без префикса «+», «-», « »; для остальных — строка целиком.</param>
/// <param name="OldNumber">Номер строки в старой версии файла (Removed, Context).</param>
/// <param name="NewNumber">Номер строки в новой версии файла (Added, Context).</param>
public sealed record DiffLine(DiffLineKind Kind, string Text, int? OldNumber = null, int? NewNumber = null);
