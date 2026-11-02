using System.Reflection;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace EaseGit.UI.Widgets;

/// <summary>Версия приложения в правом нижнем углу.</summary>
public sealed class VersionLabel : Label
{
    public VersionLabel()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        Text = $"EaseGit v{version?.ToString(3) ?? "0.0.0"}";
        X = Pos.AnchorEnd() - 1;
        Y = Pos.AnchorEnd();
        SetScheme(Theme.Text(Theme.Dim));
    }
}
