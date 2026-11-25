using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace EaseGit.UI;

/// <summary>
/// Шрифт классической консоли Windows (conhost). Она не подставляет символы из других шрифтов:
/// чего нет в выбранном шрифте (Consolas, Lucida Console), то рисуется пустым прямоугольником или «?».
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ConsoleFont
{
    private const int StdOutputHandle = -11;
    private const int TrueTypeFont = 0x04;
    private const uint MarkNonexistingGlyphs = 0x01;
    private const ushort MissingGlyph = 0xFFFF;

    /// <summary>
    /// Проверка «есть ли все символы строки в шрифте консоли».
    /// null — шрифт узнать не удалось (вывод перенаправлен, псевдоконсоль), тогда считаем, что есть всё.
    /// </summary>
    public static Func<string, bool>? TryCreateGlyphCheck()
    {
        var info = new ConsoleFontInfoEx { Size = (uint)Marshal.SizeOf<ConsoleFontInfoEx>() };
        var output = GetStdHandle(StdOutputHandle);
        if (output == IntPtr.Zero || output == new IntPtr(-1) || !GetCurrentConsoleFontEx(output, false, ref info))
            return null;

        // Растровые шрифты знают только символы OEM-кодировки, GDI для них ответит неточно.
        if ((info.FontFamily & TrueTypeFont) == 0)
            return text => text.All(c => c < 0x80);

        var face = info.FaceName;
        if (string.IsNullOrEmpty(face))
            return null;

        var cache = new Dictionary<string, bool>();
        return text =>
        {
            lock (cache)
            {
                if (!cache.TryGetValue(text, out var supported))
                    cache[text] = supported = HasGlyphs(face, text) ?? true;
                return supported;
            }
        };
    }

    /// <summary>null — GDI не ответил.</summary>
    private static bool? HasGlyphs(string face, string text)
    {
        var dc = CreateCompatibleDC(IntPtr.Zero);
        if (dc == IntPtr.Zero)
            return null;

        var font = CreateFontW(-16, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 0, 0, face);
        try
        {
            if (font == IntPtr.Zero)
                return null;

            var previous = SelectObject(dc, font);
            var indices = new ushort[text.Length];
            var converted = GetGlyphIndicesW(dc, text, text.Length, indices, MarkNonexistingGlyphs);
            SelectObject(dc, previous);
            if (converted == uint.MaxValue)
                return null;

            // Суррогатные пары GDI не понимает: такие символы (эмодзи) считаем отсутствующими.
            return !text.Any(char.IsSurrogate) && indices.All(i => i != MissingGlyph);
        }
        finally
        {
            if (font != IntPtr.Zero)
                DeleteObject(font);
            DeleteDC(dc);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ConsoleFontInfoEx
    {
        public uint Size;
        public uint Font;
        public short FontWidth;
        public short FontHeight;
        public int FontFamily;
        public int FontWeight;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string FaceName;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int handle);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GetCurrentConsoleFontEx(IntPtr output, bool maximumWindow, ref ConsoleFontInfoEx info);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr dc);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFontW(int height, int width, int escapement, int orientation, int weight,
        uint italic, uint underline, uint strikeOut, uint charSet, uint outPrecision, uint clipPrecision,
        uint quality, uint pitchAndFamily, string face);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetGlyphIndicesW(IntPtr dc, string text, int count, [Out] ushort[] indices, uint flags);
}
