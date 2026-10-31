namespace LazyGit.Git;

/// <summary>
/// Следит за изменениями в рабочей копии и в состоянии git (индекс, HEAD, ветки).
/// События объединяются: <see cref="Changed"/> вызывается один раз после паузы в <see cref="Debounce"/>.
/// Событие приходит из пула потоков.
/// </summary>
public sealed class RepoWatcher : IDisposable
{
    public static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(300);

    private readonly string _root;
    private readonly FileSystemWatcher _watcher;
    private readonly Timer _timer;

    public RepoWatcher(string repoRoot)
    {
        _root = repoRoot;
        _timer = new Timer(_ => Changed?.Invoke(this, EventArgs.Empty));
        _watcher = new FileSystemWatcher(repoRoot)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        _watcher.Changed += OnFileEvent;
        _watcher.Created += OnFileEvent;
        _watcher.Deleted += OnFileEvent;
        _watcher.Renamed += OnFileEvent;
        // При переполнении буфера событий часть изменений потеряна — обновляемся целиком.
        _watcher.Error += (_, _) => Schedule();
        _watcher.EnableRaisingEvents = true;
    }

    public event EventHandler? Changed;

    /// <summary>
    /// Влияет ли изменение файла на то, что показывает LGit. Внутри .git учитываются только
    /// индекс, HEAD, ссылки на ветки и состояние слияния; объекты, логи и lock-файлы игнорируются.
    /// </summary>
    /// <param name="relativePath">Путь относительно корня репозитория с разделителем «/».</param>
    public static bool IsRelevant(string relativePath)
    {
        if (relativePath != ".git" && !relativePath.StartsWith(".git/"))
            return true;

        var inside = relativePath.Length > 5 ? relativePath[5..] : "";
        if (inside.EndsWith(".lock"))
            return false;

        return inside is "index" or "HEAD" or "packed-refs" or "MERGE_HEAD" or "REBASE_HEAD" or "CHERRY_PICK_HEAD"
               || inside.StartsWith("refs/");
    }

    public void Dispose()
    {
        _watcher.Dispose();
        _timer.Dispose();
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        var relative = Path.GetRelativePath(_root, e.FullPath).Replace('\\', '/');
        if (IsRelevant(relative))
            Schedule();
    }

    private void Schedule() => _timer.Change(Debounce, Timeout.InfiniteTimeSpan);
}
