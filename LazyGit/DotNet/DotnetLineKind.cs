namespace LazyGit.DotNet;

/// <summary>Вид строки вывода dotnet: для цвета и сводки.</summary>
public enum DotnetLineKind
{
    Normal,
    Error,
    Warning,

    /// <summary>«Failed ИмяТеста [12 ms]».</summary>
    FailedTest,

    /// <summary>Итог сборки или тестов: «Build succeeded.», «Passed! - Failed: 0, Passed: 526…».</summary>
    Summary,
}
