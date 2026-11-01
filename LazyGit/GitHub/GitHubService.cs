using LazyGit.Git;
using LazyGit.Git.Parsers;
using LazyGit.Models;

namespace LazyGit.GitHub;

/// <summary>
/// Pull request через gh. Чтение при ошибке бросает <see cref="GitCommandException"/>, изменения возвращают
/// <see cref="GitResult"/> и пишутся в журнал команд — как в <see cref="GitService"/>.
/// </summary>
/// <param name="workingDirectory">Папка для gh pr checkout (локальный репозиторий) или любая.</param>
public sealed class GitHubService(GhRunner gh, string workingDirectory, CommandLog? log = null)
{
    public const int ListLimit = 100;

    /// <summary>Открытые PR репозитория, свежие сверху.</summary>
    public async Task<IReadOnlyList<PullRequest>> ListAsync(GitHubRepo repo, CancellationToken ct = default) =>
        PullRequestParser.ParseList(await RunCheckedAsync(
            ["pr", "list", "--repo", repo.NameWithOwner, "--state", "open", "--limit", ListLimit.ToString(), "--json", PullRequestParser.ListFields], ct));

    /// <summary>
    /// Открытые PR по всем репозиториям: мои (<paramref name="reviewRequested"/> = false) или те, где ждут моего ревью.
    /// Поиск не отдаёт ветки, проверки и ревью — они приходят при выборе PR (<see cref="GetAsync"/>).
    /// </summary>
    public async Task<IReadOnlyList<PullRequest>> SearchAsync(bool reviewRequested, CancellationToken ct = default) =>
        PullRequestParser.ParseList(await RunCheckedAsync(
            ["search", "prs", reviewRequested ? "--review-requested=@me" : "--author=@me", "--state=open", "--sort=updated",
             "--limit", ListLimit.ToString(), "--json", PullRequestParser.SearchFields], ct));

    /// <summary>Открытые issues репозитория, свежие сверху.</summary>
    public async Task<IReadOnlyList<Issue>> ListIssuesAsync(GitHubRepo repo, CancellationToken ct = default) =>
        IssueParser.ParseList(await RunCheckedAsync(
            ["issue", "list", "--repo", repo.NameWithOwner, "--state", "open", "--limit", ListLimit.ToString(), "--json", IssueParser.ListFields], ct));

    /// <summary>Issue целиком: описание и комментарии.</summary>
    public async Task<Issue?> GetIssueAsync(Issue issue, CancellationToken ct = default) =>
        IssueParser.ParseOne(await RunCheckedAsync(
            ["issue", "view", issue.Number.ToString(), "--repo", issue.Repo.NameWithOwner, "--json", IssueParser.ViewFields], ct));

    /// <summary>Аргументы нового issue; описание — через stdin (--body-file -).</summary>
    public static string[] CreateIssueArguments(GitHubRepo repo, string title) =>
        ["issue", "create", "--repo", repo.NameWithOwner, "--title", title, "--body-file", "-"];

    /// <summary>Создаёт issue. При успехе в stdout — его адрес.</summary>
    public Task<GitResult> CreateIssueAsync(GitHubRepo repo, string title, string body, CancellationToken ct = default) =>
        RunActionAsync(CreateIssueArguments(repo, title), ct, body);

    public static string[] CommentIssueArguments(Issue issue) =>
        ["issue", "comment", issue.Number.ToString(), "--repo", issue.Repo.NameWithOwner, "--body-file", "-"];

    public Task<GitResult> CommentIssueAsync(Issue issue, string body, CancellationToken ct = default) =>
        RunActionAsync(CommentIssueArguments(issue), ct, body);

    public static string[] CloseIssueArguments(Issue issue) => ["issue", "close", issue.Number.ToString(), "--repo", issue.Repo.NameWithOwner];

    public Task<GitResult> CloseIssueAsync(Issue issue, CancellationToken ct = default) => RunActionAsync(CloseIssueArguments(issue), ct);

    public Task<GitResult> OpenIssueInBrowserAsync(Issue issue, CancellationToken ct = default) =>
        gh.RunAsync(workingDirectory, ["issue", "view", issue.Number.ToString(), "--repo", issue.Repo.NameWithOwner, "--web"], ct);

    /// <summary>Свои репозитории на GitHub, недавно обновлённые первыми.</summary>
    public async Task<IReadOnlyList<RepoChoice>> ListMyReposAsync(CancellationToken ct = default) =>
        RepoChoices.ParseList(await RunCheckedAsync(["repo", "list", "--limit", ListLimit.ToString(), "--json", RepoChoices.ListFields], ct));

    /// <summary>PR целиком: с описанием, проверками и возможностью merge.</summary>
    public async Task<PullRequest?> GetAsync(PullRequest pr, CancellationToken ct = default) =>
        PullRequestParser.ParseOne(await RunCheckedAsync(
            ["pr", "view", pr.Number.ToString(), "--repo", pr.Repo.NameWithOwner, "--json", PullRequestParser.ViewFields], ct));

    /// <summary>Изменения PR как обычный diff нескольких файлов.</summary>
    public async Task<IReadOnlyList<DiffLine>> GetDiffAsync(PullRequest pr, CancellationToken ct = default) =>
        DiffParser.Parse(await RunCheckedAsync(["pr", "diff", pr.Number.ToString(), "--repo", pr.Repo.NameWithOwner, "--color", "never"], ct));

    /// <summary>Основная ветка репозитория (обычно main): в неё по умолчанию открывается PR.</summary>
    public async Task<string> GetDefaultBranchAsync(GitHubRepo repo, CancellationToken ct = default) =>
        (await RunCheckedAsync(["repo", "view", repo.NameWithOwner, "--json", "defaultBranchRef", "--jq", ".defaultBranchRef.name"], ct)).Trim();

    /// <summary>Открытый PR из ветки <paramref name="branch"/> или null.</summary>
    public async Task<PullRequest?> FindForBranchAsync(GitHubRepo repo, string branch, CancellationToken ct = default) =>
        PullRequestParser.ParseList(await RunCheckedAsync(
            ["pr", "list", "--repo", repo.NameWithOwner, "--head", branch, "--state", "open", "--json", PullRequestParser.ListFields], ct))
            .FirstOrDefault();

    /// <summary>Переключает локальный репозиторий на ветку PR (создаёт её, если нужно).</summary>
    public static string[] CheckoutArguments(PullRequest pr) => ["pr", "checkout", pr.Number.ToString(), "--repo", pr.Repo.NameWithOwner];

    public Task<GitResult> CheckoutAsync(PullRequest pr, CancellationToken ct = default) => RunActionAsync(CheckoutArguments(pr), ct);

    public static string[] MergeArguments(PullRequest pr, MergeMethod method) =>
        ["pr", "merge", pr.Number.ToString(), "--repo", pr.Repo.NameWithOwner, $"--{method.ToString().ToLowerInvariant()}"];

    /// <summary>Вливает PR на GitHub. Локальные ветки не трогает.</summary>
    public Task<GitResult> MergeAsync(PullRequest pr, MergeMethod method, CancellationToken ct = default) =>
        RunActionAsync(MergeArguments(pr, method), ct);

    public static string[] ApproveArguments(PullRequest pr) => ["pr", "review", pr.Number.ToString(), "--repo", pr.Repo.NameWithOwner, "--approve"];

    public Task<GitResult> ApproveAsync(PullRequest pr, CancellationToken ct = default) => RunActionAsync(ApproveArguments(pr), ct);

    /// <summary>Аргументы создания PR. Описание передаётся через stdin (--body-file -).</summary>
    public static string[] CreateArguments(GitHubRepo repo, string head, string @base, string title, bool draft) =>
    [
        "pr", "create", "--repo", repo.NameWithOwner, "--head", head, "--base", @base, "--title", title, "--body-file", "-",
        .. draft ? (string[])["--draft"] : [],
    ];

    /// <summary>Создаёт PR. При успехе в stdout — адрес нового PR.</summary>
    public Task<GitResult> CreateAsync(GitHubRepo repo, string head, string @base, string title, string body, bool draft,
        CancellationToken ct = default) =>
        RunActionAsync(CreateArguments(repo, head, @base, title, draft), ct, body);

    /// <summary>Запуски Actions для коммита <paramref name="sha"/>: по ним статус CI в шапке.</summary>
    public async Task<IReadOnlyList<WorkflowRun>> GetRunsAsync(GitHubRepo repo, string sha, CancellationToken ct = default) =>
        WorkflowRunParser.Parse(await RunCheckedAsync(
            ["run", "list", "--repo", repo.NameWithOwner, "--commit", sha, "--json", WorkflowRunParser.Fields], ct));

    /// <summary>Последние запуски Actions репозитория (только ветки <paramref name="branch"/>, если задана).</summary>
    public async Task<IReadOnlyList<ActionsRun>> ListRunsAsync(GitHubRepo repo, string? branch, CancellationToken ct = default) =>
        ActionsParser.ParseRuns(await RunCheckedAsync(
            ["run", "list", "--repo", repo.NameWithOwner, "--limit", "50", "--json", ActionsParser.RunFields,
             .. branch is null ? (string[])[] : ["--branch", branch]], ct));

    public async Task<IReadOnlyList<ActionsJob>> GetJobsAsync(GitHubRepo repo, long run, CancellationToken ct = default) =>
        ActionsParser.ParseJobs(await RunCheckedAsync(["run", "view", run.ToString(), "--repo", repo.NameWithOwner, "--json", "jobs"], ct));

    /// <summary>Лог только упавших шагов. Пока запуск идёт или лог стёрт по сроку — пусто.</summary>
    public async Task<IReadOnlyList<ActionsLogLine>> GetFailedLogAsync(GitHubRepo repo, long run, CancellationToken ct = default)
    {
        var result = await gh.RunAsync(workingDirectory, ["run", "view", run.ToString(), "--repo", repo.NameWithOwner, "--log-failed"], ct);
        return result.Success ? ActionsParser.ParseFailedLog(result.StdOut) : [];
    }

    public static string[] RerunArguments(GitHubRepo repo, long run, bool failedOnly) =>
        ["run", "rerun", run.ToString(), "--repo", repo.NameWithOwner, .. failedOnly ? (string[])["--failed"] : []];

    /// <summary>Перезапускает запуск целиком или только упавшие jobs.</summary>
    public Task<GitResult> RerunAsync(GitHubRepo repo, long run, bool failedOnly, CancellationToken ct = default) =>
        RunActionAsync(RerunArguments(repo, run, failedOnly), ct);

    public static string[] CancelRunArguments(GitHubRepo repo, long run) => ["run", "cancel", run.ToString(), "--repo", repo.NameWithOwner];

    public Task<GitResult> CancelRunAsync(GitHubRepo repo, long run, CancellationToken ct = default) =>
        RunActionAsync(CancelRunArguments(repo, run), ct);

    public Task<GitResult> OpenRunInBrowserAsync(GitHubRepo repo, long run, CancellationToken ct = default) =>
        gh.RunAsync(workingDirectory, ["run", "view", run.ToString(), "--repo", repo.NameWithOwner, "--web"], ct);

    /// <summary>Открывает запуск Actions в браузере.</summary>
    public Task<GitResult> OpenRunInBrowserAsync(GitHubRepo repo, WorkflowRun run, CancellationToken ct = default) =>
        gh.RunAsync(workingDirectory, ["run", "view", run.Id.ToString(), "--repo", repo.NameWithOwner, "--web"], ct);

    public static string[] CloneArguments(GitHubRepo repo, string path) => ["repo", "clone", repo.NameWithOwner, path];

    /// <summary>Скачивает репозиторий в папку <paramref name="path"/> (gh сам выбирает https или ssh по настройкам).</summary>
    public Task<GitResult> CloneAsync(GitHubRepo repo, string path, CancellationToken ct = default) =>
        RunActionAsync(CloneArguments(repo, path), ct);

    /// <summary>Открывает PR в браузере.</summary>
    public Task<GitResult> OpenInBrowserAsync(PullRequest pr, CancellationToken ct = default) =>
        gh.RunAsync(workingDirectory, ["pr", "view", pr.Number.ToString(), "--repo", pr.Repo.NameWithOwner, "--web"], ct);

    private async Task<GitResult> RunActionAsync(IReadOnlyList<string> args, CancellationToken ct, string? input = null)
    {
        var started = DateTimeOffset.Now;
        try
        {
            var result = await gh.RunAsync(workingDirectory, args, ct, input);
            var output = (result.StdErr.Trim() + "\n" + result.StdOut.Trim()).Trim();
            log?.Add(new CommandLogEntry(started, args, result.ExitCode, DateTimeOffset.Now - started, output, "gh"));
            return result;
        }
        catch (OperationCanceledException)
        {
            log?.Add(new CommandLogEntry(started, args, null, DateTimeOffset.Now - started, "", "gh"));
            throw;
        }
    }

    private async Task<string> RunCheckedAsync(IReadOnlyList<string> args, CancellationToken ct)
    {
        var result = await gh.RunAsync(workingDirectory, args, ct);
        return result.Success ? result.StdOut : throw new GitCommandException(result);
    }
}
