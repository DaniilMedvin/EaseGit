using System.Net;
using EaseGit.Templates;

namespace EaseGit.Tests.Templates;

public class GitIgnoreSourceTests
{
    private sealed class FakeHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        public string? RequestedUrl { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            RequestedUrl = request.RequestUri?.ToString();
            return Task.FromResult(respond());
        }
    }

    private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private static string Upstream(string extra = "") =>
        "# Visual Studio\n.vs/\n[Bb]in/\n" + string.Concat(Enumerable.Repeat("# filler rule line\n", 40)) + extra;

    [Fact]
    public async Task Load_GitHubAvailable_ReturnsDownloadedTemplateWithExtras()
    {
        var handler = new FakeHandler(() => Ok(Upstream("*.newrule\n")));

        var result = await GitIgnoreSource.LoadAsync(new HttpClient(handler));

        Assert.Equal(GitIgnoreOrigin.GitHub, result.Origin);
        Assert.Equal(GitIgnoreSource.Url, handler.RequestedUrl);
        Assert.Contains("*.newrule", result.Content);
        Assert.Contains(".env", result.Content);
        Assert.Contains(".idea/", result.Content);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Load_ServerError_FallsBackToEmbedded(HttpStatusCode status)
    {
        var handler = new FakeHandler(() => new HttpResponseMessage(status));

        var result = await GitIgnoreSource.LoadAsync(new HttpClient(handler));

        Assert.Equal(GitIgnoreOrigin.Embedded, result.Origin);
        Assert.Equal(GitIgnoreTemplate.VisualStudio, result.Content);
    }

    [Fact]
    public async Task Load_NetworkFailure_FallsBackToEmbedded()
    {
        var handler = new FakeHandler(() => throw new HttpRequestException("no network"));

        var result = await GitIgnoreSource.LoadAsync(new HttpClient(handler));

        Assert.Equal(GitIgnoreOrigin.Embedded, result.Origin);
    }

    [Fact]
    public async Task Load_HtmlInsteadOfTemplate_FallsBackToEmbedded()
    {
        var html = "<html>" + new string('x', 1000) + ".vs/</html>";
        var handler = new FakeHandler(() => Ok(html));

        var result = await GitIgnoreSource.LoadAsync(new HttpClient(handler));

        Assert.Equal(GitIgnoreOrigin.Embedded, result.Origin);
    }

    [Fact]
    public async Task Load_CallerCancels_Throws()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var handler = new FakeHandler(() => Ok(Upstream()));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => GitIgnoreSource.LoadAsync(new HttpClient(handler), cts.Token));
    }

    [Fact]
    public void WithExtras_TemplateAlreadyHasThem_DoesNotDuplicate()
    {
        var text = Upstream(".env\n.env.local\n.env.prod\n.idea/\n");

        var result = GitIgnoreSource.WithExtras(text);

        Assert.Equal(1, result.Split('\n').Count(l => l.Trim() == ".idea/"));
        Assert.DoesNotContain("Секреты локальной среды", result);
    }

    [Fact]
    public void WithExtras_Missing_AppendsOnceAndEndsWithNewline()
    {
        var result = GitIgnoreSource.WithExtras(Upstream());

        Assert.Contains("# Секреты локальной среды", result);
        Assert.EndsWith(Environment.NewLine, result);
    }

    [Fact]
    public void EmbeddedTemplate_PassesOwnSanityCheck()
    {
        Assert.True(GitIgnoreSource.LooksLikeGitIgnore(GitIgnoreTemplate.VisualStudio));
    }
}
