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

    /// <summary>Заголовок файла в diff из нескольких файлов: Text — путь. Добавляется при подготовке к показу.</summary>
    File,

    /// <summary>Строка сообщения коммита. Добавляется при подготовке к показу.</summary>
    Message,

    /// <summary>
    /// Строка сводки коммита «путь | 12 +++---» (как git show --stat): Text — путь, счётчики в <see cref="DiffLine.Counts"/>.
    /// Добавляется при подготовке к показу.
    /// </summary>
    Stat,

    /// <summary>Заметка над diff файла (например, «NuGet: Terminal.Gui 2.4.0 → 2.5.0»). Добавляется при подготовке к показу.</summary>
    Note,

    /// <summary>Строка сводки «что изменилось в коде»: Text — имя члена, вид и тип изменения в <see cref="DiffLine.Change"/>.</summary>
    CodeChange,
}

/// <summary>Строка diff.</summary>
/// <param name="Kind">Вид строки.</param>
/// <param name="Text">Для Added, Removed и Context — содержимое без префикса «+», «-», « »; для остальных — строка целиком.</param>
/// <param name="OldNumber">Номер строки в старой версии файла (Removed, Context).</param>
/// <param name="NewNumber">Номер строки в новой версии файла (Added, Context).</param>
public sealed record DiffLine(DiffLineKind Kind, string Text, int? OldNumber = null, int? NewNumber = null)
{
    /// <summary>Строка в файле кончается на \r (CRLF). Не показывается, но нужна, чтобы патч из строк совпал с файлом.</summary>
    public bool EndsWithCr { get; init; }

    /// <summary>Для <see cref="DiffLineKind.Stat"/>: сколько строк добавлено и удалено; null — бинарный файл.</summary>
    public (int Added, int Removed)? Counts { get; init; }

    /// <summary>Для <see cref="DiffLineKind.Note"/>: заметка-предупреждение (мажорное обновление пакета).</summary>
    public bool IsWarning { get; init; }

    /// <summary>Для <see cref="DiffLineKind.CodeChange"/>: что случилось с членом кода.</summary>
    public CodeChange? Change { get; init; }
}
