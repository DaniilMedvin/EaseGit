using System.Drawing;
using EaseGit.Models;
using EaseGit.UI.Panels;
using Terminal.Gui.Input;

namespace EaseGit.Tests.UI.Panels;

public class MouseTests
{
    private static Mouse Click(int x, int y) => new() { Flags = MouseFlags.LeftButtonClicked, Position = new Point(x, y) };

    private static Commit Commit(string hash) => new(hash.PadRight(40, '0'), "anna", "a@x", DateTimeOffset.Now, "subject " + hash);

    [Fact]
    public void ListPanel_Click_SelectsRowUnderCursorAndIgnoresEmptySpace()
    {
        var panel = new CommitsPanel { Frame = new Rectangle(0, 0, 40, 10) };
        panel.SetItems([Commit("aaa"), Commit("bbb"), Commit("ccc")]);

        panel.NewMouseEvent(Click(5, 2));
        Assert.Equal(2, panel.SelectedIndex);

        // Под последней строкой — пусто: выбор не меняется.
        panel.NewMouseEvent(Click(5, 10));
        Assert.Equal(2, panel.SelectedIndex);
    }

    [Fact]
    public void FilesPanel_ClickOnFolderArrow_TogglesAndHeaderIsNotSelected()
    {
        // Размер как на экране: без него высота 0 и прокрутка уезжает к выбранной строке.
        var panel = new FilesPanel { Frame = new Rectangle(0, 0, 40, 10) };
        panel.SetStatus(new RepoStatus("main", null, null, 0, 0,
        [
            new FileChange("src/a.cs", ChangeKind.Unmodified, ChangeKind.Modified),
            new FileChange("src/b.cs", ChangeKind.Unmodified, ChangeKind.Modified),
        ]));

        // Строка 0 — заголовок «Изменения», 1 — папка src/, стрелка в колонке 3 (отступ, маркер «❯ »).
        panel.NewMouseEvent(Click(10, 0));
        Assert.Null(panel.SelectedRow as SectionHeaderRow);

        panel.NewMouseEvent(Click(3, 1));
        Assert.True(panel.SelectedDirectory?.Collapsed);

        panel.NewMouseEvent(Click(3, 1));
        Assert.False(panel.SelectedDirectory?.Collapsed);

        panel.NewMouseEvent(Click(12, 3));
        Assert.Equal("src/b.cs", panel.SelectedFile?.File.Path);
    }
}
