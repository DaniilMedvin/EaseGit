namespace EaseGit.Models;

/// <summary>
/// Обсуждение строки PR на GitHub: первый комментарий и ответы.
/// </summary>
/// <param name="Id">Node id обсуждения: по нему ответ и «решено».</param>
/// <param name="Line">Строка в текущем diff (последняя, если выделено несколько); null — строки больше нет (устарело).</param>
/// <param name="OriginalLine">Строка в версии, к которой оставлен комментарий.</param>
public sealed record ReviewThread(string Id, string Path, int? Line, int? OriginalLine, ReviewSide Side, bool IsResolved, bool IsOutdated,
    IReadOnlyList<ReviewComment> Comments);
