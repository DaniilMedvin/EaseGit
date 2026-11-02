using EaseGit.Models;
using EaseGit.UI.Panels;
using Terminal.Gui.Input;

namespace EaseGit.Tests.UI.Panels;

public class PanelFilterTests
{
    private static void Type(PanelFilter filter, string text)
    {
        foreach (var c in text)
            Assert.True(filter.HandleKey(new Key(c)));
    }

    [Fact]
    public void HandleKey_Typing_LettersGoToTextAndArrowsPassThrough()
    {
        var filter = new PanelFilter();
        filter.Start();

        Type(filter, "fix j");
        Assert.True(filter.HandleKey(Key.Backspace));

        Assert.Equal("fix ", filter.Text);
        Assert.False(filter.HandleKey(Key.CursorDown));
        Assert.False(filter.HandleKey(Key.Tab));
        Assert.False(filter.HandleKey(Key.D.WithCtrl));
    }

    [Fact]
    public void HandleKey_EnterKeepsFilterAndEscClearsIt()
    {
        var filter = new PanelFilter();
        filter.Start();
        Type(filter, "ui");

        Assert.True(filter.HandleKey(Key.Enter));
        Assert.False(filter.Typing);
        Assert.True(filter.Active);
        // Вне набора буквы — снова команды панели.
        Assert.False(filter.HandleKey(Key.S));

        Assert.True(filter.HandleKey(Key.Esc));
        Assert.False(filter.Active);
        // Без фильтра esc — не его: уходит дальше (назад к выбору репо).
        Assert.False(filter.HandleKey(Key.Esc));
    }

    [Fact]
    public void CommitsPanel_Filter_ShowsMatchesKeepsItemsAndSelection()
    {
        Commit C(string hash, string subject, string author = "anna") => new(hash.PadRight(40, '0'), author, "a@x", DateTimeOffset.Now, subject);
        var panel = new CommitsPanel();
        panel.SetItems([C("aaa", "feat: filter"), C("bbb", "fix: crash", "boris"), C("ccc", "docs: filter in README")]);
        panel.Filter.Start();
        Type(panel.Filter, "FILTER");

        Assert.Equal(["feat: filter", "docs: filter in README"], panel.Shown.Select(c => c.Subject));
        Assert.Equal(3, panel.Items.Count);

        panel.SelectIndex(1);
        panel.Filter.Clear();

        // После сброса выбран тот же коммит — его видно в контексте.
        Assert.Equal("docs: filter in README", panel.Selected?.Subject);
        Assert.Equal(3, panel.Shown.Count);

        panel.Filter.Start();
        Type(panel.Filter, "bori");
        Assert.Equal("fix: crash", Assert.Single(panel.Shown).Subject);
        panel.Filter.Clear();
        panel.Filter.Start();
        Type(panel.Filter, "ccc");
        Assert.Equal("docs: filter in README", Assert.Single(panel.Shown).Subject);
    }
}
