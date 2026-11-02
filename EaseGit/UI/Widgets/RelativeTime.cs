namespace EaseGit.UI.Widgets;

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
            return T("сейчас", "now");
        if (elapsed.TotalHours < 1)
            return T($"{(int)elapsed.TotalMinutes} мин", $"{(int)elapsed.TotalMinutes}m");
        if (elapsed.TotalDays < 1)
            return T($"{(int)elapsed.TotalHours} ч", $"{(int)elapsed.TotalHours}h");
        if (elapsed.TotalDays < 7)
            return T($"{(int)elapsed.TotalDays} дн", $"{(int)elapsed.TotalDays}d");
        if (elapsed.TotalDays < 30)
            return T($"{(int)(elapsed.TotalDays / 7)} нед", $"{(int)(elapsed.TotalDays / 7)}w");
        if (elapsed.TotalDays < 365)
            return T($"{(int)(elapsed.TotalDays / 30)} мес", $"{(int)(elapsed.TotalDays / 30)}mo");
        return T($"{(int)(elapsed.TotalDays / 365)} г", $"{(int)(elapsed.TotalDays / 365)}y");
    }

    /// <summary>Для подписи даты: «только что», «3 ч назад».</summary>
    public static string Ago(DateTimeOffset date, DateTimeOffset now)
    {
        var age = Format(date, now);
        return age == T("сейчас", "now") ? T("только что", "just now") : T($"{age} назад", $"{age} ago");
    }
}
