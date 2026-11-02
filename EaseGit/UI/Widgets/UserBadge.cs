using EaseGit.Auth;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace EaseGit.UI.Widgets;

/// <summary>Пользователь в правом верхнем углу мягкой плашкой: «● login» или «○ Не авторизован».</summary>
public sealed class UserBadge : Label
{
    public UserBadge(AuthStatus status)
    {
        X = Pos.AnchorEnd() - 2;
        Y = 1;
        if (status.IsAuthorized)
        {
            Text = $" {Theme.SymbolCurrent} {status.Login} ";
            SetScheme(Pill(Theme.Success));
        }
        else
        {
            Text = T($" {Theme.SymbolEmpty} Не авторизован ", $" {Theme.SymbolEmpty} Not signed in ");
            SetScheme(Pill(Theme.Warning));
        }
    }

    private static Terminal.Gui.Drawing.Scheme Pill(Terminal.Gui.Drawing.Color color) =>
        new(new Terminal.Gui.Drawing.Attribute(color, Theme.Tint(color)));
}
