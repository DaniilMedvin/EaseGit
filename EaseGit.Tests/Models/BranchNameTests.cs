using EaseGit.Models;

namespace EaseGit.Tests.Models;

public class BranchNameTests
{
    [Theory]
    [InlineData("feature/branches")]
    [InlineData("fix/empty-repo")]
    [InlineData("ветка")]
    [InlineData("v1.2")]
    public void Check_ValidName_ReturnsNull(string name)
    {
        Assert.Null(BranchName.Check(name, ["main"]));
    }

    [Theory]
    [InlineData("", "Введи имя")]
    [InlineData("my branch", "Пробелы")]
    [InlineData("a:b", "Нельзя символы")]
    [InlineData("a..b", "«..»")]
    [InlineData("a@{1}", "«@{»")]
    [InlineData("-x", "начинаться с -")]
    [InlineData("feature/", "Лишний /")]
    [InlineData("a//b", "Лишний /")]
    [InlineData(".hidden", "с точки")]
    [InlineData("x.lock", ".lock")]
    [InlineData("main", "уже существует")]
    public void Check_InvalidName_ExplainsWhy(string name, string expected)
    {
        Assert.Contains(expected, BranchName.Check(name, ["main"]));
    }
}
