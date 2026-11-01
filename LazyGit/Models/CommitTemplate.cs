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

    /// <summary>Встроенные шаблоны: самые частые поводы для коммита. Свойство, а не поле: названия на текущем языке.</summary>
    public static IReadOnlyList<CommitTemplate> BuiltIn =>
    [
        new(T("Новая функция", "New feature"), "feat", "add "),
        new(T("Исправление бага", "Bug fix"), "fix", "", T("Причина:\n- \n\nИсправление:\n- ", "Cause:\n- \n\nFix:\n- ")),
        new(T("Опечатка", "Typo"), "fix", "correct typo in "),
        new(T("Рефакторинг", "Refactoring"), "refactor", "simplify "),
        new(T("Тесты", "Tests"), "test", "add tests for "),
        new(T("Документация", "Documentation"), "docs", "update "),
        new(T("Обновить зависимости", "Update dependencies"), "chore", "update dependencies"),
        new(T("Ломающее изменение", "Breaking change"), null, "feat!: ", "BREAKING CHANGE: "),
        new(T("Первый коммит", "Initial commit"), "chore", "initial commit"),
        new(T("Добавить README", "Add README"), "docs", "add README"),
        new(T("Добавить .gitignore", "Add .gitignore"), "chore", "add .gitignore"),
        new(T("Работа в процессе", "Work in progress"), "chore", "wip"),
    ];
}
