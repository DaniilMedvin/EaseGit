using EaseGit.Highlighting;
using EaseGit.Models;

namespace EaseGit.Tests.Highlighting;

public sealed class CodeChangesTests
{
    private const string Before = """
        namespace Weather;

        public class Client
        {
            private int _count;

            public Client() { }

            public string Load(string city) => city;

            public string Load(int id) => id.ToString();

            public int Count => _count;

            private void Old() { }
        }
        """;

    private const string After = """
        namespace Weather;

        public class Client
        {
            private int _count;

            public Client() { }

            // Комментарий и отступы не считаются изменением.
            public string Load(string city)   =>   city;

            public string Load(int id) => (id + 1).ToString();

            public int Count => _count;

            public Task SaveAsync() => Task.CompletedTask;
        }

        public sealed record Cache(int Ttl)
        {
            public void Clear() { }
        }
        """;

    private static List<string> Describe(IReadOnlyList<CodeChange> changes) =>
        changes.Select(c => $"{c.Kind} {c.What} {c.Name}").ToList();

    [Fact]
    public void Compare_DistinguishesOverloadsIgnoresFormattingAndCollapsesNewTypes()
    {
        var changes = Describe(CodeChanges.Compare(Before, After));

        Assert.Equal(
        [
            "Modified метод Client.Load(int)",
            "Added метод Client.SaveAsync()",
            "Added record Cache",
            "Removed метод Client.Old()",
        ], changes);
    }

    [Fact]
    public void Compare_NewAndDeletedFile()
    {
        Assert.Equal(["Added класс Client"], Describe(CodeChanges.Compare(null, Before)));
        Assert.Equal(["Removed класс Client"], Describe(CodeChanges.Compare(Before, "")));
        Assert.Empty(CodeChanges.Compare(Before, Before));
    }

    [Fact]
    public void Compare_NestedTypesGenericsAndFields()
    {
        const string old = "class Outer<T> { class Inner { int a, b; } }";
        const string @new = "class Outer<T> { class Inner { int a, b, c; void M<U>(List<U> x) { } } }";

        Assert.Equal(["Added поле Outer<T>.Inner.c", "Added метод Outer<T>.Inner.M<U>(List<U>)"],
            Describe(CodeChanges.Compare(old, @new)));
    }
}
