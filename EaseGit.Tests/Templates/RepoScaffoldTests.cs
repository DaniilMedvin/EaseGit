using EaseGit.Templates;

namespace EaseGit.Tests.Templates;

public sealed class RepoScaffoldTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("easegit-scaffold-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void CreateGitIgnore_NewRepo_WritesVisualStudioRules()
    {
        Assert.True(RepoScaffold.CreateGitIgnore(_root));

        var text = File.ReadAllText(Path.Combine(_root, ".gitignore"));
        Assert.Contains(".vs/", text);
        Assert.Contains("[Bb]in/", text);
        Assert.Contains("*.user", text);
        Assert.True(RepoScaffold.GitIgnoreExists(_root));
    }

    [Fact]
    public void CreateGitIgnore_ExistingFile_IsNotOverwritten()
    {
        File.WriteAllText(Path.Combine(_root, ".gitignore"), "my rules");

        Assert.False(RepoScaffold.CreateGitIgnore(_root));

        Assert.Equal("my rules", File.ReadAllText(Path.Combine(_root, ".gitignore")));
    }

    [Fact]
    public void CreateEditorConfig_NewRepo_WritesSdkDefaultWithoutBom()
    {
        Assert.True(RepoScaffold.CreateEditorConfig(_root));

        var bytes = File.ReadAllBytes(Path.Combine(_root, ".editorconfig"));
        Assert.NotEqual(0xEF, bytes[0]);
        var text = File.ReadAllText(Path.Combine(_root, ".editorconfig"));
        Assert.StartsWith("root = true", text);
        Assert.Contains("[*.cs]", text);
        Assert.Contains("dotnet_sort_system_directives_first = true", text);
        Assert.EndsWith("\n", text);
        Assert.True(RepoScaffold.EditorConfigExists(_root));
    }

    [Fact]
    public void CreateEditorConfig_ExistingFile_IsNotOverwritten()
    {
        File.WriteAllText(Path.Combine(_root, ".editorconfig"), "root = true");

        Assert.False(RepoScaffold.CreateEditorConfig(_root));

        Assert.Equal("root = true", File.ReadAllText(Path.Combine(_root, ".editorconfig")));
    }

    [Fact]
    public void CreateReadme_UsesFolderNameAsTitle()
    {
        var project = Directory.CreateDirectory(Path.Combine(_root, "Мой проект")).FullName;

        Assert.True(RepoScaffold.CreateReadme(project));

        Assert.StartsWith("# Мой проект", File.ReadAllText(Path.Combine(project, "README.md")));
    }

    [Fact]
    public void CreateReadme_ExistingFile_IsNotOverwritten()
    {
        File.WriteAllText(Path.Combine(_root, "README.md"), "mine");

        Assert.False(RepoScaffold.CreateReadme(_root));

        Assert.Equal("mine", File.ReadAllText(Path.Combine(_root, "README.md")));
    }

    [Fact]
    public void GitIgnoreTemplate_HasNoBomAndEndsWithNewline()
    {
        RepoScaffold.CreateGitIgnore(_root);

        var bytes = File.ReadAllBytes(Path.Combine(_root, ".gitignore"));
        Assert.NotEqual(0xEF, bytes[0]);
        Assert.EndsWith("\n", GitIgnoreTemplate.VisualStudio);
    }

    [Fact]
    public async Task GitIgnoreTemplate_IgnoresVisualStudioFolderInRealRepo()
    {
        RepoScaffold.CreateGitIgnore(_root);
        Directory.CreateDirectory(Path.Combine(_root, ".vs", "X"));
        File.WriteAllText(Path.Combine(_root, ".vs", "X", "cache.v2"), "x");
        File.WriteAllText(Path.Combine(_root, "Program.cs"), "class P {}");
        var git = new EaseGit.Git.GitRunner();
        await git.RunAsync(_root, ["init", "-b", "main"]);

        var status = await git.RunAsync(_root, ["status", "--porcelain"]);

        Assert.DoesNotContain(".vs", status.StdOut);
        Assert.Contains("Program.cs", status.StdOut);
    }
}
