using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace EaseGit.UI;

/// <summary>
/// Буфер обмена Windows через user32. Не через powershell / clip: дочерний консольный процесс делит консоль
/// с EaseGit, и powershell при запуске меняет её кодовую страницу — conhost в ответ переключается
/// на растровый шрифт, окно сжимается, а символы превращаются в «?».
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsClipboard
{
    private const uint UnicodeText = 13;
    private const uint MoveableMemory = 0x0002;

    /// <summary>null — буфер занят другим приложением или в нём нет текста.</summary>
    public static string? GetText()
    {
        if (!Open())
            return null;

        try
        {
            var handle = GetClipboardData(UnicodeText);
            if (handle == IntPtr.Zero)
                return null;

            var data = GlobalLock(handle);
            if (data == IntPtr.Zero)
                return null;

            try
            {
                return Marshal.PtrToStringUni(data);
            }
            finally
            {
                GlobalUnlock(handle);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    public static bool SetText(string text)
    {
        if (!Open())
            return false;

        try
        {
            if (!EmptyClipboard())
                return false;

            var bytes = (text.Length + 1) * sizeof(char);
            var handle = GlobalAlloc(MoveableMemory, (UIntPtr)bytes);
            if (handle == IntPtr.Zero)
                return false;

            var data = GlobalLock(handle);
            if (data == IntPtr.Zero)
            {
                GlobalFree(handle);
                return false;
            }

            Marshal.Copy(text.ToCharArray(), 0, data, text.Length);
            Marshal.WriteInt16(data, text.Length * sizeof(char), 0);
            GlobalUnlock(handle);

            // После успешного SetClipboardData память принадлежит системе.
            if (SetClipboardData(UnicodeText, handle) != IntPtr.Zero)
                return true;

            GlobalFree(handle);
            return false;
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>Буфер может быть ненадолго занят другим приложением — пробуем несколько раз.</summary>
    private static bool Open()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (OpenClipboard(IntPtr.Zero))
                return true;
            Thread.Sleep(20);
        }

        return false;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr owner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint format);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint format, IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr memory);
}
