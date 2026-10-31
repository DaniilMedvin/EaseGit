namespace LazyGit.UI.Widgets;

public static class RelativeTime
{
    /// <summary>
    /// Короткое «сколько прошло» для узкой колонки: «сейчас», «5 мин», «3 ч», «2 дн», «4 нед», «3 мес», «2 г».
    /// Дата в будущем (сбитые часы) считается как «сейчас».
    /// </summary>
    public static string Format(DateTimeOffset date, DateTimeOffset now)
    {
        var elapsed = now - date;
        if (elapsed.TotalMinutes < 1)
            return "сейчас";
        if (elapsed.TotalHours < 1)
            return $"{(int)elapsed.TotalMinutes} мин";
        if (elapsed.TotalDays < 1)
            return $"{(int)elapsed.TotalHours} ч";
        if (elapsed.TotalDays < 7)
            return $"{(int)elapsed.TotalDays} дн";
        if (elapsed.TotalDays < 30)
            return $"{(int)(elapsed.TotalDays / 7)} нед";
        if (elapsed.TotalDays < 365)
            return $"{(int)(elapsed.TotalDays / 30)} мес";
        return $"{(int)(elapsed.TotalDays / 365)} г";
    }
}
