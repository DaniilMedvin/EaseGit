using LazyGit.Auth;
using LazyGit.UI.Widgets;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace LazyGit.UI;

public enum AuthChoice
{
    Login,
    Skip,
}

/// <summary>Окно входа в GitHub при первом запуске. Result: Login, Skip или null (выход по Esc).</summary>
public sealed class AuthWindow : Runnable<AuthChoice?>
{
    private readonly Waves _waves = new() { X = 0, Y = Pos.AnchorEnd(6) };

    public AuthWindow(AuthStatus status)
    {
        Width = Dim.Fill();
        Height = Dim.Fill();
        SetScheme(Theme.Base);

        var logo = new Logo { X = Pos.Center(), Y = Pos.Center() - 8 };

        var box = new View
        {
            BorderStyle = LineStyle.Rounded,
            X = Pos.Center(),
            Y = Pos.Bottom(logo) + 2,
            Width = 56,
            Height = Dim.Auto(),
            CanFocus = true,
        };
        box.SetScheme(Theme.Text(Theme.Accent));
        box.Padding!.Thickness = new Thickness(1, 0, 1, 0);

        var text = new Label
        {
            Text = status.GhInstalled
                ? "Войди в GitHub, чтобы делать push и pull\nбез лишних вопросов.\n\nВход выполнится через gh в браузере.\nПароли и токены LGit не хранит."
                : "Для входа нужен GitHub CLI (gh), он не найден.\nУстанови его и перезапусти LGit:\n\n  sudo dnf install gh",
            X = 0,
            Y = 0,
        };
        text.SetScheme(Theme.Base);

        var login = new Button
        {
            Text = "  Войти  ",
            IsDefault = true,
            NoDecorations = true,
            ShadowStyle = ShadowStyles.None,
            Enabled = status.GhInstalled,
            X = Pos.Center() - 10,
            Y = Pos.Bottom(text) + 1,
        };
        login.SetScheme(Theme.Button);
        login.Accepting += (_, e) =>
        {
            e.Handled = true;
            Finish(AuthChoice.Login);
        };

        var skip = new Button
        {
            Text = "  Пропустить  ",
            NoDecorations = true,
            ShadowStyle = ShadowStyles.None,
            X = Pos.Right(login) + 4,
            Y = Pos.Top(login),
        };
        skip.SetScheme(Theme.Button);
        skip.Accepting += (_, e) =>
        {
            e.Handled = true;
            Finish(AuthChoice.Skip);
        };

        box.Add(text, login, skip);

        var note = new Label
        {
            Text = "Пропуск запоминается — это окно больше не появится.\nВойти можно позже со стартового экрана.",
            X = Pos.Center(),
            Y = Pos.Bottom(box) + 1,
            TextAlignment = Alignment.Center,
        };
        note.SetScheme(Theme.Text(Theme.Muted));

        var version = new VersionLabel();

        var hints = new HintBar { X = 1, Y = Pos.AnchorEnd() };
        hints.SetHints(status.GhInstalled
            ? [new Hint("enter", "войти"), new Hint("s", "пропустить"), new Hint("esc", "выход")]
            : [new Hint("s", "пропустить"), new Hint("esc", "выход")]);

        Add(_waves, new UserBadge(status), logo, box, BoxTitle.For(box, "Вход в GitHub", Theme.Accent), note, hints, version);
        (status.GhInstalled ? login : skip).SetFocus();
    }

    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.S)
        {
            Finish(AuthChoice.Skip);
            return true;
        }

        return base.OnKeyDown(key);
    }

    protected override void OnIsRunningChanged(bool newIsRunning)
    {
        base.OnIsRunningChanged(newIsRunning);
        if (newIsRunning)
            _waves.Start();
        else
            _waves.Stop();
    }

    private void Finish(AuthChoice choice)
    {
        Result = choice;
        RequestStop();
    }
}
