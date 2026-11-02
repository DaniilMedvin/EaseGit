using Terminal.Gui.ViewBase;

namespace EaseGit.UI.Widgets;

/// <summary>
/// Базовый класс для анимированных декораций: перерисовка по таймеру (≈12 кадров/с).
/// Запуск — <see cref="Start"/>, остановка — <see cref="Stop"/>.
/// </summary>
public abstract class AnimatedView : View
{
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(80);

    private object? _timer;

    protected AnimatedView()
    {
        CanFocus = false;
    }

    /// <summary>Время анимации в секундах с момента запуска.</summary>
    protected double Seconds { get; private set; }

    public void Start()
    {
        if (_timer is not null || App is null)
            return;

        _timer = App.AddTimeout(FrameInterval, () =>
        {
            Seconds += FrameInterval.TotalSeconds;
            OnTick();
            SetNeedsDraw();
            return true;
        });
    }

    public void Stop()
    {
        if (_timer is null)
            return;

        App?.RemoveTimeout(_timer);
        _timer = null;
    }

    /// <summary>Обновление состояния анимации перед каждым кадром.</summary>
    protected virtual void OnTick()
    {
    }
}
