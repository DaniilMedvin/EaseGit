using LazyGit.Auth;
using LazyGit.Config;
using System.Globalization;
using LazyGit.Git;
using LazyGit.Localization;
using LazyGit.UI;
using Terminal.Gui.App;

namespace LazyGit;

class Program
{
    static int Main(string[] args)
    {
        var configStore = new ConfigStore();
        // Окно входа и сообщения до главного цикла — уже на выбранном языке.
        Loc.Apply(Loc.Resolve(configStore.Load().Language, CultureInfo.CurrentUICulture));
        var auth = new GhAuthService();
        var git = new GitRunner();

        // Запуск внутри репозитория (или с путём к нему) сразу открывает главный экран.
        var launchPath = args.FirstOrDefault() ?? Environment.CurrentDirectory;
        var repoRoot = git.FindRepoRootAsync(launchPath).GetAwaiter().GetResult();

        // На время интерактивного входа через gh TUI останавливается и затем запускается заново.
        while (true)
        {
            var config = configStore.Load();
            var status = auth.GetStatusAsync().GetAwaiter().GetResult();
            var startPath = args.FirstOrDefault() ?? config.LastRepoPath ?? Environment.CurrentDirectory;

            var outcome = RunUi(status, config, configStore, git, repoRoot, startPath);
            if (outcome is not UiOutcome.Login login)
                return 0;

            repoRoot = login.ReturnToRepo;
            RunLogin(auth);
        }
    }

    private abstract record UiOutcome
    {
        public sealed record Quit : UiOutcome;

        /// <param name="ReturnToRepo">Репозиторий, главный экран которого открыть после входа.</param>
        public sealed record Login(string? ReturnToRepo) : UiOutcome;
    }

    /// <param name="repoRoot">Репозиторий для главного экрана; null — начать со стартового экрана.</param>
    /// <param name="startPath">Путь, подставляемый в поле стартового экрана.</param>
    private static UiOutcome RunUi(
        AuthStatus status,
        AppConfig config,
        ConfigStore configStore,
        GitRunner git,
        string? repoRoot,
        string startPath)
    {
        using var app = Application.Create().Init();

        // При открытии репозитория сразу окно входа не показывается: войти можно с главного экрана.
        if (repoRoot is null && !status.IsAuthorized && !config.AuthPromptSkipped)
        {
            using var authWindow = new AuthWindow(status);
            app.Run(authWindow);
            switch (authWindow.Result)
            {
                case null:
                    return new UiOutcome.Quit();
                case AuthChoice.Login:
                    return new UiOutcome.Login(null);
                case AuthChoice.Skip:
                    configStore.Save(config with { AuthPromptSkipped = true });
                    break;
            }
        }

        string? notice = null;
        while (true)
        {
            // Тема и язык задаются до создания окон: виджеты берут цвета и тексты при создании.
            var current = configStore.Load();
            Theme.Apply(ThemeCatalog.Resolve(current).Palette);
            Loc.Apply(Loc.Resolve(current.Language, CultureInfo.CurrentUICulture));

            if (repoRoot is null)
            {
                using var startWindow = new StartWindow(git, status, startPath);
                app.Run(startWindow);
                switch (startWindow.Result)
                {
                    case StartAction.Login:
                        return new UiOutcome.Login(null);
                    case StartAction.OpenRepo:
                        repoRoot = startWindow.RepoPath!;
                        break;
                    default:
                        return new UiOutcome.Quit();
                }
            }

            configStore.Save(configStore.Load() with { LastRepoPath = repoRoot });
            var log = new CommandLog();
            using var mainWindow = new MainWindow(new GitService(git, repoRoot, log), status, configStore, log, notice);
            notice = null;
            app.Run(mainWindow);
            switch (mainWindow.Result)
            {
                case MainResult.Reload:
                    notice = mainWindow.ReloadNotice;
                    continue;
                case MainResult.Quit:
                    return new UiOutcome.Quit();
                case MainResult.Login:
                    return new UiOutcome.Login(repoRoot);
            }

            // Esc на главном экране возвращает к стартовому.
            startPath = repoRoot;
            repoRoot = null;
        }
    }

    private static void RunLogin(GhAuthService auth)
    {
        Console.Clear();
        Console.WriteLine(T("Вход в GitHub через gh. Следуй инструкциям ниже.\n", "Signing in to GitHub with gh. Follow the instructions below.\n"));
        if (auth.LoginInteractive() != 0)
        {
            Console.WriteLine(T("\n✗ Вход не выполнен. Нажми Enter, чтобы вернуться в LGit.", "\n✗ Sign-in failed. Press Enter to return to LGit."));
            Console.ReadLine();
        }
    }
}
