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
            switch (outcome)
            {
                case UiOutcome.Quit:
                    return 0;

                case UiOutcome.Login:
                    RunLogin(auth);
                    continue;

                case UiOutcome.OpenRepo(var repoPath):
                    configStore.Save(configStore.Load() with { LastRepoPath = repoPath });
                    // TODO(этап 2): главный экран репозитория.
                    Console.WriteLine($"✓ Репозиторий: {repoPath}");
                    Console.WriteLine("  Главный экран появится на этапе 2.");
                    return 0;
            }
        }
    }

    private abstract record UiOutcome
    {
        public sealed record Quit : UiOutcome;
        public sealed record Login : UiOutcome;
        public sealed record OpenRepo(string Path) : UiOutcome;
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

        using var startWindow = new StartWindow(git, status, initialPath);
        app.Run(startWindow);
        return startWindow.Result switch
        {
            StartAction.Login => new UiOutcome.Login(),
            StartAction.OpenRepo => new UiOutcome.OpenRepo(startWindow.RepoPath!),
            _ => new UiOutcome.Quit(),
        };
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
