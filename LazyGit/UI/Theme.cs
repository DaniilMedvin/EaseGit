using Terminal.Gui.Drawing;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace LazyGit.UI;

/// <summary>
/// Цвета и символы интерфейса (docs/DESIGN.md). Палитра основана на стандартной теме Neovim.
/// Фон — Color.None, то есть фон терминала.
/// </summary>
public static class Theme
{
    public static readonly Color Fg = Color.None;
    public static readonly Color Muted = new("#9B9EA4");
    public static readonly Color Dim = new("#4F5258");
    public static readonly Color Accent = new("#A6DBFF");
    public static readonly Color Green = new("#B3F6C0");
    public static readonly Color Cyan = new("#8CF8F7");
    public static readonly Color SelectionBg = new("#2C2E33");
    public static readonly Color Success = Green;
    public static readonly Color Error = new("#FFC0B9");
    public static readonly Color Warning = new("#FCE094");
    public static readonly Color Info = Cyan;

    /// <summary>Фон добавленных и удалённых строк в diff.</summary>
    public static readonly Color AddedBg = new("#1D2F25");
    public static readonly Color RemovedBg = new("#3A2427");

    /// <summary>Предполагаемый фон терминала. Используется для приглушения цветов декораций.</summary>
    public static readonly Color Backdrop = new("#14161B");

    public const string SymbolCurrent = "●";
    public const string SymbolEmpty = "○";
    public const string SymbolSuccess = "✓";
    public const string SymbolError = "✗";
    public const string SymbolWarning = "⚠";
    public const string SymbolSeparator = "·";
    public const string SymbolSelection = "❯";
    public const string SymbolBranch = "⎇";
    public const string SymbolAhead = "↑";
    public const string SymbolBehind = "↓";
    public const string SymbolEllipsis = "…";
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

    public static readonly Color SyntaxPlain = new("#DCDCDC");
    public static readonly Color SyntaxKeyword = new("#569CD6");
    public static readonly Color SyntaxControlKeyword = new("#D8A0DF");
    public static readonly Color SyntaxClass = new("#4EC9B0");
    public static readonly Color SyntaxStruct = new("#86C691");
    public static readonly Color SyntaxInterface = new("#B8D7A3");
    public static readonly Color SyntaxMethod = new("#DCDCAA");
    public static readonly Color SyntaxVariable = new("#9CDCFE");
    public static readonly Color SyntaxString = new("#D69D85");
    public static readonly Color SyntaxNumber = new("#B5CEA8");
    public static readonly Color SyntaxComment = new("#57A64A");
    public static readonly Color SyntaxPreprocessor = new("#9B9B9B");
    public static readonly Color SyntaxDisabled = new("#767676");
    public static readonly Color SyntaxPunctuation = new("#B4B4B4");

    /// <summary>Значок и цвет вида изменения файла.</summary>
    public static (string Symbol, Color Color) ForChange(Models.ChangeKind kind) => kind switch
    {
        Models.ChangeKind.Added => ("✚", Success),
        Models.ChangeKind.Modified => ("●", Warning),
        Models.ChangeKind.Deleted => ("✖", Error),
        Models.ChangeKind.Renamed => ("➜", Info),
        Models.ChangeKind.Copied => ("➜", Info),
        Models.ChangeKind.TypeChanged => ("●", Warning),
        Models.ChangeKind.Unmerged => ("!", Error),
        Models.ChangeKind.Untracked => ("?", Muted),
        _ => (" ", Muted),
    };

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
