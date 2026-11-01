namespace LazyGit.Models;

/// <summary>Строка лога шага: job, шаг, текст без отметки времени.</summary>
/// <param name="IsError">Строка «##[error]…» — ошибка, которую подсветил GitHub.</param>
public sealed record ActionsLogLine(string Job, string Step, string Text, bool IsError);
