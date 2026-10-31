using LazyGit.Git;

namespace LazyGit.UI;

/// <summary>Понятные тексты для известных ошибок git: что случилось и что делать.</summary>
public static class GitErrors
{
    /// <summary>Текст ошибки. Для неизвестной — первая строка stderr (или stdout, если stderr пуст).</summary>
    public static string Explain(GitResult result)
    {
        var stderr = result.StdErr;
        // Конфликты merge git пишет в stdout.
        if (result.StdOut.Contains("CONFLICT"))
            return "Конфликт при merge: реши файлы из раздела «Конфликты», добавь их (space) и сделай коммит (c)";
        if (stderr.Contains("[remote rejected]"))
            return "Сервер отклонил push (защищённая ветка или хук на сервере). Подробности: git push в терминале";
        if (stderr.Contains("[rejected]"))
            return "Push отклонён: на сервере есть новые коммиты. Нажми p, чтобы сделать pull";
        if (stderr.Contains("Need to specify how to reconcile divergent branches") || stderr.Contains("Not possible to fast-forward"))
            return "Ветка и upstream разошлись. Выбери способ pull: git config pull.rebase false (merge) или true (rebase)";
        if (stderr.Contains("There is no tracking information for the current branch"))
            return "У ветки нет upstream: сначала сделай push (P), он предложит настроить upstream";
        if (stderr.Contains("Authentication failed") || stderr.Contains("could not read Username")
            || stderr.Contains("terminal prompts disabled") || stderr.Contains("Permission denied (publickey)"))
            return "Нет доступа к remote: войди в GitHub (ctrl+g) или настрой SSH-ключ / credential helper";
        if (stderr.Contains("Could not resolve host") || stderr.Contains("Could not read from remote repository")
            || stderr.Contains("unable to access"))
            return "Не удалось подключиться к remote: проверь сеть и адрес репозитория (git remote -v)";
        if (stderr.Contains("No configured push destination") || stderr.Contains("does not appear to be a git repository"))
            return "Remote не настроен. Добавь его: git remote add origin <адрес>";
        if (stderr.Contains("Author identity unknown") || stderr.Contains("Please tell me who you are"))
            return "Git не знает имя автора. Выполни: git config --global user.name \"Имя\" и user.email \"почта\"";
        if (stderr.Contains("would be overwritten by checkout"))
            return "Нельзя переключиться: незакоммиченные изменения конфликтуют с веткой. Сделай коммит или stash";
        if (stderr.Contains("would be overwritten by merge"))
            return "Нельзя выполнить merge: он затронет незакоммиченные изменения. Сделай коммит или stash";
        if (stderr.Contains("You have not concluded your merge") || stderr.Contains("MERGE_HEAD exists"))
            return "Предыдущий merge не завершён: разреши конфликты и сделай коммит (c)";
        if (stderr.Contains("you need to resolve your current index first") || stderr.Contains("unmerged files"))
            return "Есть неразрешённые конфликты: реши их, добавь файлы в индекс (space) и сделай коммит (c)";
        if (stderr.Contains("refusing to merge unrelated histories"))
            return "У веток нет общей истории, git не будет их сливать";
        if (stderr.Contains("is not fully merged"))
            return "В ветке есть коммиты, которых нет в текущей";
        if (stderr.Contains("cannot delete branch", StringComparison.OrdinalIgnoreCase))
            return "Нельзя удалить ветку: она открыта в другом worktree";
        if (stderr.Contains("is already used by worktree") || stderr.Contains("is already checked out at"))
            return "Эта ветка уже открыта в другом worktree";
        if (stderr.Contains("a branch named") && stderr.Contains("already exists"))
            return "Ветка с таким именем уже существует";
        return FirstLine(stderr.Trim().Length > 0 ? stderr : result.StdOut);
    }

    public static string FirstLine(string text)
    {
        var trimmed = text.Trim();
        var newline = trimmed.IndexOf('\n');
        return newline < 0 ? trimmed : trimmed[..newline];
    }
}
