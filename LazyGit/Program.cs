using LazyGit.Auth;
using LazyGit.Config;
using LazyGit.Git;
using LazyGit.UI;
using Terminal.Gui.App;

namespace LazyGit;

class Program
{
    static int Main(string[] args)
    {
        var configStore = new ConfigStore();
        var auth = new GhAuthService();
        var git = new GitRunner();

        // На время интерактивного входа через gh TUI останавливается и затем запускается заново.
        while (true)
        {
            var config = configStore.Load();
            var status = auth.GetStatusAsync().GetAwaiter().GetResult();
            var initialPath = args.FirstOrDefault() ?? config.LastRepoPath ?? Environment.CurrentDirectory;

            var outcome = RunUi(status, config, configStore, git, initialPath);
            if (outcome is UiOutcome.Quit)
                return 0;

            RunLogin(auth);
        }
    }

    private abstract record UiOutcome
    {
        public sealed record Quit : UiOutcome;
        public sealed record Login : UiOutcome;
    }

    private static UiOutcome RunUi(AuthStatus status, AppConfig config, ConfigStore configStore, GitRunner git, string initialPath)
    {
        using var app = Application.Create().Init();

        if (!status.IsAuthorized && !config.AuthPromptSkipped)
        {
            using var authWindow = new AuthWindow(status);
            app.Run(authWindow);
            switch (authWindow.Result)
            {
                case null:
                    return new UiOutcome.Quit();
                case AuthChoice.Login:
                    return new UiOutcome.Login();
                case AuthChoice.Skip:
                    configStore.Save(config with { AuthPromptSkipped = true });
                    break;
            }
        }

        // Esc на главном экране возвращает к стартовому.
        var path = initialPath;
        while (true)
        {
            using var startWindow = new StartWindow(git, status, path);
            app.Run(startWindow);
            switch (startWindow.Result)
            {
                case StartAction.Login:
                    return new UiOutcome.Login();
                case StartAction.OpenRepo:
                    path = startWindow.RepoPath!;
                    configStore.Save(configStore.Load() with { LastRepoPath = path });
                    break;
                default:
                    return new UiOutcome.Quit();
            }

            using var mainWindow = new MainWindow(new GitService(git, path), status);
            app.Run(mainWindow);
            if (mainWindow.Result is MainResult.Quit)
                return new UiOutcome.Quit();
        }
    }

    private static void RunLogin(GhAuthService auth)
    {
        Console.Clear();
        Console.WriteLine("Вход в GitHub через gh. Следуй инструкциям ниже.\n");
        if (auth.LoginInteractive() != 0)
        {
            Console.WriteLine("\n✗ Вход не выполнен. Нажми Enter, чтобы вернуться в LGit.");
            Console.ReadLine();
        }
    }
}
