namespace LazyGit.Models;

/// <summary>Шаг job: номер, имя, состояние.</summary>
public sealed record ActionsStep(int Number, string Name, CheckState State, string Conclusion);
