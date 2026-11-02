namespace EaseGit.Models;

/// <summary>Релиз на GitHub, созданный из тега <paramref name="TagName"/>.</summary>
public sealed record GitHubRelease(string TagName, string Name, bool IsDraft, bool IsPrerelease, bool IsLatest);
