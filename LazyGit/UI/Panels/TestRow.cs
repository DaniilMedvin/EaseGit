namespace LazyGit.UI.Panels;

/// <summary>Строка списка тестов: класс (свёрнутый или нет) или тест внутри развёрнутого класса.</summary>
/// <param name="Key">Полное имя класса или теста.</param>
/// <param name="Selected">Сколько тестов выбрано (у теста — 0 или 1).</param>
/// <param name="Failed">Упал при последнем прогоне (у класса — хоть один его тест).</param>
/// <param name="Passed">Прошёл при последнем прогоне (у класса — все прогнанные).</param>
public sealed record TestRow(bool IsClass, string Key, string Label, int Selected, int Total, bool Expanded, bool Failed, bool Passed);
