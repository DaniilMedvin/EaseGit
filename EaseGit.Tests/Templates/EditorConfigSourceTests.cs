using EaseGit.DotNet;
using EaseGit.Templates;

namespace EaseGit.Tests.Templates;

public sealed class EditorConfigSourceTests
{
    [Fact]
    public async Task Load_DotnetAvailable_ReturnsSdkTemplate()
    {
        var result = await EditorConfigSource.LoadAsync(new DotnetRunner());

        Assert.Equal(EditorConfigOrigin.Sdk, result.Origin);
        Assert.StartsWith("root = true", result.Content);
        Assert.Contains("[*.cs]", result.Content);
        Assert.EndsWith(Environment.NewLine, result.Content);
    }

    [Fact]
    public async Task Load_DotnetMissing_ReturnsEmbeddedCopy()
    {
        var result = await EditorConfigSource.LoadAsync(new DotnetRunner("easegit-no-such-dotnet"));

        Assert.Equal(EditorConfigOrigin.Embedded, result.Origin);
        Assert.Equal(EditorConfigTemplate.Default, result.Content);
    }

    [Fact]
    public void EmbeddedCopy_LooksLikeEditorConfig()
    {
        Assert.True(EditorConfigSource.LooksLikeEditorConfig(EditorConfigTemplate.Default));
        Assert.False(EditorConfigSource.LooksLikeEditorConfig("<html>error</html>"));
    }
}
