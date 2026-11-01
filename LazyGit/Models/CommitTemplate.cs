namespace LazyGit.Models;

/// <summary>
/// Заготовка сообщения коммита: тип, начало заголовка и необязательное тело.
/// Заголовок остаётся открытым для редактирования: пользователь дописывает суть.
/// </summary>
/// <param name="Name">Название в списке шаблонов.</param>
/// <param name="Type">Тип Conventional Commits из <see cref="CommitMessage.Types"/> или null.</param>
/// <param name="Subject">Заголовок или его начало; пробел в конце означает «допиши дальше».</param>
/// <param name="Body">Заготовка тела коммита.</param>
public sealed record CommitTemplate(string Name, string? Type, string Subject, string Body = "")
{
    /// <summary>Как будет выглядеть заголовок: «feat: add ».</summary>
    public string Title => Type is null || CommitMessage.HasTypePrefix(Subject) ? Subject : $"{Type}: {Subject}";

    /// <summary>Встроенные шаблоны: самые частые поводы для коммита.</summary>
    public static IReadOnlyList<CommitTemplate> BuiltIn { get; } =
    [
        new("Новая функция", "feat", "add "),
        new("Исправление бага", "fix", "", "Причина:\n- \n\nИсправление:\n- "),
        new("Опечатка", "fix", "correct typo in "),
        new("Рефакторинг", "refactor", "simplify "),
        new("Тесты", "test", "add tests for "),
        new("Документация", "docs", "update "),
        new("Обновить зависимости", "chore", "update dependencies"),
        new("Ломающее изменение", null, "feat!: ", "BREAKING CHANGE: "),
        new("Первый коммит", "chore", "initial commit"),
        new("Добавить README", "docs", "add README"),
        new("Добавить .gitignore", "chore", "add .gitignore"),
        new("Работа в процессе", "chore", "wip"),
    ];
}
