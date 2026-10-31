using LazyGit.Auth;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace LazyGit.UI.Widgets;

/// <summary>Пользователь в правом верхнем углу: «● login» или «○ Не авторизован».</summary>
public sealed class UserBadge : Label
{
    public UserBadge(AuthStatus status)
    {
        X = Pos.AnchorEnd() - 2;
        Y = 1;
        if (status.IsAuthorized)
        {
            Text = $"{Theme.SymbolCurrent} {status.Login}";
            SetScheme(Theme.Text(Theme.Success));
        }
        else
        {
            Text = $"{Theme.SymbolEmpty} Не авторизован";
            SetScheme(Theme.Text(Theme.Warning));
        }
    }
}
