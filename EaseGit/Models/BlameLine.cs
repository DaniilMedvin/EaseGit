namespace EaseGit.Models;

/// <summary>Строка файла в blame: номер, текст и коммит, где она появилась в таком виде.</summary>
public sealed record BlameLine(int Number, string Text, BlameCommit Commit);
