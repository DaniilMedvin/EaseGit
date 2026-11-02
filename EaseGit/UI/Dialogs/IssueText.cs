namespace EaseGit.UI.Dialogs;

/// <summary>Что ввели в окне issue: заголовок (у комментария — null) и текст.</summary>
public sealed record IssueText(string? Title, string Body);
