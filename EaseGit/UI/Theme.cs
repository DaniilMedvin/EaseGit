using Terminal.Gui.Drawing;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace EaseGit.UI;

/// <summary>
/// Цвета и символы интерфейса (docs/DESIGN.md). Цвета берутся из активной <see cref="ThemePalette"/>.
/// Фон — Color.None, то есть фон терминала.
/// </summary>
public static class Theme
{
    // Единственное изменяемое состояние: палитра выбирается один раз при запуске окна (Apply), до создания
    // каких-либо виджетов. Переключение темы перезапускает главное окно, поэтому на лету цвета не меняются.
    private static ThemePalette _palette = ThemePalette.Dark;

    /// <summary>Включает палитру. Вызывать до создания окон: уже нарисованные виджеты цвета не обновят.</summary>
    public static void Apply(ThemePalette palette) => _palette = palette;

    public static readonly Color Fg = Color.None;
    public static Color Muted => _palette.Muted;
    public static Color Dim => _palette.Dim;
    public static Color Accent => _palette.Accent;
    public static Color Green => _palette.Green;
    public static Color Cyan => _palette.Cyan;
    public static Color SelectionBg => _palette.SelectionBg;
    public static Color Success => Green;
    public static Color Error => _palette.Error;
    public static Color Warning => _palette.Warning;
    public static Color Info => Cyan;

    /// <summary>Фон добавленных и удалённых строк в diff.</summary>
    public static Color AddedBg => _palette.AddedBg;
    public static Color RemovedBg => _palette.RemovedBg;

    /// <summary>Цвета маскотов (Config.Mascot): Claude — оранжевый, ChatGPT — зелёный, Gemini — градиент.</summary>
    public static readonly Color MascotClaude = new("#D97757");
    public static readonly Color MascotChatGpt = new("#10A37F");
    public static readonly Color[] MascotGemini = [new("#4796E3"), new("#9177C7"), new("#CA6673")];

    /// <summary>Предполагаемый фон терминала. Используется для приглушения цветов декораций.</summary>
    public static Color Backdrop => _palette.Backdrop;

    public const string SymbolCurrent = "●";
    public const string SymbolEmpty = "○";
    public const string SymbolSuccess = "✓";
    public const string SymbolError = "✗";
    // Не ⚠: многие терминалы рисуют его эмодзи двойной ширины, и он налезает на текст.
    public const string SymbolWarning = "▲";
    public const string SymbolSeparator = "·";
    public const string SymbolSelection = "❯";
    public const string SymbolBranch = "⎇";
    public const string SymbolAhead = "↑";
    public const string SymbolBehind = "↓";
    public const string SymbolEllipsis = "…";
    public const string SymbolExpanded = "▾";
    public const string SymbolCollapsed = "▸";
    public const string SymbolTreeGuide = "│";
    public const string SymbolTag = "#";
    public static readonly string[] SpinnerFrames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];

    public static Scheme Text(Color fg, TextStyle style = TextStyle.None) => new(new Attribute(fg, Color.None, style));

    public static Scheme Base => Text(Fg);

    public static Scheme Input => new(new Attribute(Fg, Color.None))
    {
        Focus = new Attribute(Fg, Color.None),
        Editable = new Attribute(Fg, Color.None),
    };

    public static Scheme Button => new(new Attribute(Muted, Color.None))
    {
        Focus = new Attribute(Backdrop, Accent, TextStyle.Bold),
        HotNormal = new Attribute(Muted, Color.None),
        HotFocus = new Attribute(Backdrop, Accent, TextStyle.Bold),
    };

    /// <summary>Цвет подсветки синтаксиса в стиле тёмной темы Visual Studio.</summary>
    public static Color Syntax(Highlighting.TokenClass cls) => cls switch
    {
        Highlighting.TokenClass.Keyword => SyntaxKeyword,
        Highlighting.TokenClass.ControlKeyword => SyntaxControlKeyword,
        Highlighting.TokenClass.Class => SyntaxClass,
        Highlighting.TokenClass.Struct => SyntaxStruct,
        Highlighting.TokenClass.Interface or Highlighting.TokenClass.Enum or Highlighting.TokenClass.TypeParameter => SyntaxInterface,
        Highlighting.TokenClass.Method => SyntaxMethod,
        Highlighting.TokenClass.Variable => SyntaxVariable,
        Highlighting.TokenClass.String => SyntaxString,
        Highlighting.TokenClass.Number => SyntaxNumber,
        Highlighting.TokenClass.Comment => SyntaxComment,
        Highlighting.TokenClass.Preprocessor => SyntaxPreprocessor,
        Highlighting.TokenClass.DisabledText => SyntaxDisabled,
        Highlighting.TokenClass.Punctuation => SyntaxPunctuation,
        _ => SyntaxPlain,
    };

    public static Color SyntaxPlain => _palette.SyntaxPlain;
    public static Color SyntaxKeyword => _palette.SyntaxKeyword;
    public static Color SyntaxControlKeyword => _palette.SyntaxControlKeyword;
    public static Color SyntaxClass => _palette.SyntaxClass;
    public static Color SyntaxStruct => _palette.SyntaxStruct;
    public static Color SyntaxInterface => _palette.SyntaxInterface;
    public static Color SyntaxMethod => _palette.SyntaxMethod;
    public static Color SyntaxVariable => _palette.SyntaxVariable;
    public static Color SyntaxString => _palette.SyntaxString;
    public static Color SyntaxNumber => _palette.SyntaxNumber;
    public static Color SyntaxComment => _palette.SyntaxComment;
    public static Color SyntaxPreprocessor => _palette.SyntaxPreprocessor;
    public static Color SyntaxDisabled => _palette.SyntaxDisabled;
    public static Color SyntaxPunctuation => _palette.SyntaxPunctuation;

    /// <summary>
    /// Буква и цвет плашки вида изменения, как в VS Code и Rider:
    /// M — изменён, A — добавлен, D — удалён, R — переименован, C — скопирован, T — сменился тип,
    /// U — новый (untracked), ! — конфликт.
    /// </summary>
    public static (string Letter, Color Color) ForChange(Models.ChangeKind kind) => kind switch
    {
        Models.ChangeKind.Added => ("A", Success),
        Models.ChangeKind.Modified => ("M", Warning),
        Models.ChangeKind.Deleted => ("D", Error),
        Models.ChangeKind.Renamed => ("R", Info),
        Models.ChangeKind.Copied => ("C", Info),
        Models.ChangeKind.TypeChanged => ("T", Warning),
        Models.ChangeKind.Unmerged => ("!", Error),
        Models.ChangeKind.Untracked => ("U", Cyan),
        _ => (" ", Muted),
    };

    /// <summary>Приглушённый фон плашки цвета <paramref name="color"/>.</summary>
    public static Color Tint(Color color) => Blend(Backdrop, color, 0.22);

    /// <summary>Линейная интерполяция цвета: t = 0 → a, t = 1 → b.</summary>
    public static Color Blend(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return new Color(
            (int)Math.Round(a.R + (b.R - a.R) * t),
            (int)Math.Round(a.G + (b.G - a.G) * t),
            (int)Math.Round(a.B + (b.B - a.B) * t));
    }
}
