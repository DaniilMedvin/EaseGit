using EaseGit.Commands;
using EaseGit.Config;

namespace EaseGit.Tests.Commands;

public sealed class CustomCommandTests
{
    private static readonly PlaceholderValues Values = new("/repo", File: "/repo/Мои файлы/it's.cs", Dir: "/repo/Мои файлы",
        Commit: "abc123", Branch: "feature/x", Current: "main");

    [Fact]
    public void Expand_QuotesValuesForShAndKeepsOtherBraces()
    {
        var (command, missing) = CommandTemplate.Expand("rider {file} && echo ${HOME} {a,b} {repo}", Values, windows: false);

        Assert.Null(missing);
        Assert.Equal("rider '/repo/Мои файлы/it'\\''s.cs' && echo ${HOME} {a,b} '/repo'", command);
    }

    [Fact]
    public void Expand_WindowsUsesDoubleQuotes()
    {
        var (command, _) = CommandTemplate.Expand("code {dir} {commit}", Values with { Dir = "C:\\Мои \"файлы\"" }, windows: true);

        Assert.Equal("code \"C:\\Мои \"\"файлы\"\"\" \"abc123\"", command);
    }

    [Fact]
    public void Expand_MissingValue_ReportsFirstMissingPlaceholder()
    {
        var (command, missing) = CommandTemplate.Expand("git log {branch} -- {file}", new PlaceholderValues("/repo"), windows: false);

        Assert.Null(command);
        Assert.Equal("branch", missing);
    }

    [Fact]
    public async Task ShellRunner_PassesQuotedPathAsOneArgumentAndReturnsExitCode()
    {
        if (OperatingSystem.IsWindows())
            return;

        var lines = new List<string>();
        var (command, _) = CommandTemplate.Expand("printf '%s\\n' {file}; exit 3", Values, windows: false);

        var exit = await new ShellRunner().RunAsync(Path.GetTempPath(), command!, lines.Add);

        Assert.Equal(3, exit);
        Assert.Equal(["/repo/Мои файлы/it's.cs"], lines);
    }

    [Fact]
    public void Config_ReadsCustomCommands()
    {
        var dir = Directory.CreateTempSubdirectory("easegit-config-").FullName;
        try
        {
            var path = Path.Combine(dir, "config.json");
            File.WriteAllText(path, """
                {
                  // своя команда
                  "customCommands": [
                    { "key": "ctrl+r", "title": "Открыть в Rider", "command": "rider {file}", "scope": "files" },
                    { "key": "F6", "command": "dotnet test", "output": true, "confirm": true },
                  ]
                }
                """);

            var commands = new ConfigStore(path).Load().CustomCommands!;

            Assert.Equal("rider {file}", commands[0].Command);
            Assert.Equal("files", commands[0].Scope);
            Assert.True(commands[1].Output);
            Assert.True(commands[1].Confirm);
            Assert.Null(commands[1].Title);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
