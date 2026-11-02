using Terminal.Gui.Input;

namespace EaseGit.UI.Panels;

/// <summary>
/// Фильтр панели по «/»: текст набирается прямо в панели (поэтому j, k, s и другие буквы не уходят в команды),
/// стрелки по-прежнему двигают выбор. enter — оставить фильтр и выйти из набора, esc — сбросить; esc при
/// оставленном фильтре тоже сбрасывает его. Сравнение — подстрока без учёта регистра.
/// </summary>
public sealed class PanelFilter
{
    public string Text { get; private set; } = "";

    /// <summary>Идёт набор: клавиши-символы попадают в текст.</summary>
    public bool Typing { get; private set; }

    public bool Active => Text.Length > 0;

    /// <summary>Изменился текст или режим набора.</summary>
    public event EventHandler? Changed;

    public void Start()
    {
        Typing = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Выход из набора с сохранением текста (enter или уход фокуса с панели).</summary>
    public void Stop()
    {
        if (!Typing)
            return;

        Typing = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        if (!Typing && !Active)
            return;

        Text = "";
        Typing = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Обрабатывает клавишу фильтра. false — клавиша не его (стрелки, tab, команды вне набора).</summary>
    public bool HandleKey(Key key)
    {
        if (!Typing)
        {
            if (!Active || key != Key.Esc)
                return false;

            Clear();
            return true;
        }

        if (key == Key.Enter)
        {
            Stop();
            return true;
        }

        if (key == Key.Esc)
        {
            Clear();
            return true;
        }

        if (key == Key.Backspace)
        {
            if (Text.Length > 0)
            {
                Text = Text[..^1];
                Changed?.Invoke(this, EventArgs.Empty);
            }

            return true;
        }

        var rune = key.AsRune;
        if (key.IsCtrl || key.IsAlt || rune.Value < ' ' || char.IsControl((char)Math.Min(rune.Value, char.MaxValue)))
            return false;

        Text += rune.ToString();
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public static bool Matches(string text, string filter) => text.Contains(filter, StringComparison.OrdinalIgnoreCase);
}
