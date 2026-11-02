namespace EaseGit.Models;

/// <summary>Сторона diff, к которой привязан комментарий ревью: LEFT — старая версия (удалённые строки), RIGHT — новая.</summary>
public enum ReviewSide
{
    Right,
    Left,
}
