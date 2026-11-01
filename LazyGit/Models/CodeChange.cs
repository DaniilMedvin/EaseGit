namespace LazyGit.Models;

/// <summary>Изменённый тип или член C#: «метод Cache.LoadAsync(string)».</summary>
/// <param name="What">Вид: класс, метод, свойство… (на текущем языке).</param>
/// <param name="Name">Полное имя внутри файла: «Тип.Член(типы параметров)».</param>
public sealed record CodeChange(CodeChangeKind Kind, string What, string Name);
