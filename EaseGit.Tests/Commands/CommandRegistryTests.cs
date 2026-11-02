using EaseGit.Commands;
using Terminal.Gui.Input;

namespace EaseGit.Tests.Commands;

public class CommandRegistryTests
{
    private static AppCommand Command(string id, CommandScope scope, CommandKey key, string? hint = null, Func<bool>? available = null) => new()
    {
        Id = id,
        Title = id,
        Scope = scope,
        Key = key,
        Execute = () => { },
        Hint = hint,
        IsAvailable = available ?? (() => true),
    };

    [Fact]
    public void Find_SameKeyInPanelAndGlobal_PanelWins()
    {
        var registry = new CommandRegistry();
        registry.Add(Command("global.space", CommandScope.Global, CommandKey.Of("space", Key.Space)));
        registry.Add(Command("files.stage", CommandScope.Files, CommandKey.Of("space", Key.Space)));
        registry.Add(Command("branches.checkout", CommandScope.Branches, CommandKey.Of("space", Key.Space)));

        Assert.Equal("files.stage", registry.Find(CommandScope.Files, Key.Space)?.Id);
        Assert.Equal("branches.checkout", registry.Find(CommandScope.Branches, Key.Space)?.Id);
        Assert.Equal("global.space", registry.Find(CommandScope.Commits, Key.Space)?.Id);
    }

    [Fact]
    public void Find_UnavailableCommand_IsSkipped()
    {
        var registry = new CommandRegistry();
        registry.Add(Command("app.login", CommandScope.Global, CommandKey.Of("ctrl+g", Key.G.WithCtrl), available: () => false));

        Assert.Null(registry.Find(CommandScope.Files, Key.G.WithCtrl));
    }

    [Fact]
    public void Find_CharKey_MatchesTypedSymbol()
    {
        var registry = new CommandRegistry();
        registry.Add(Command("app.help", CommandScope.Global, CommandKey.Any("?", CommandKey.Char('?'), CommandKey.Of("F1", Key.F1))));

        Assert.NotNull(registry.Find(CommandScope.Files, new Key('?')));
        Assert.NotNull(registry.Find(CommandScope.Files, Key.F1));
        Assert.Null(registry.Find(CommandScope.Files, Key.Q));
    }

    [Fact]
    public void Hints_PanelFirstThenGlobal_OnlyWithHint()
    {
        var registry = new CommandRegistry();
        registry.Add(Command("quit", CommandScope.Global, CommandKey.Of("q", Key.Q), "выход"));
        registry.Add(Command("refresh", CommandScope.Global, CommandKey.Of("r", Key.R)));
        registry.Add(Command("checkout", CommandScope.Branches, CommandKey.Of("space", Key.Space), "checkout"));
        registry.Add(Command("stage", CommandScope.Files, CommandKey.Of("space", Key.Space), "stage"));

        Assert.Equal(["stage", "quit"], registry.Hints(CommandScope.Files).Select(c => c.Id));
    }

    [Fact]
    public void Add_DuplicateId_Throws()
    {
        var registry = new CommandRegistry();
        registry.Add(Command("a", CommandScope.Global, CommandKey.Of("a", Key.A)));

        Assert.Throws<ArgumentException>(() => registry.Add(Command("a", CommandScope.Files, CommandKey.Of("b", Key.B))));
    }
}
