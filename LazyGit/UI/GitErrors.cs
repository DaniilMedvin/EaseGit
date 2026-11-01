using LazyGit.Git;

namespace LazyGit.UI;

/// <summary>Понятные тексты для известных ошибок git: что случилось и что делать.</summary>
public static class GitErrors
{
    /// <summary>Текст ошибки. Для неизвестной — первая строка stderr (или stdout, если stderr пуст).</summary>
    public static string Explain(GitResult result)
    {
        var stderr = result.StdErr;
        if (result.Command.StartsWith("git apply --reverse"))
            return T($"Не удалось сбросить выбранные строки ({FirstLine(stderr)}). Обнови экран (r) и попробуй ещё раз", $"Couldn't discard the selected lines ({FirstLine(stderr)}). Refresh (r) and try again");
        if (result.Command.StartsWith("git apply"))
            return T($"Не удалось применить выбранные строки ({FirstLine(stderr)}). Обнови экран (r) и попробуй ещё раз или добавь файл целиком", $"Couldn't apply the selected lines ({FirstLine(stderr)}). Refresh (r) and try again, or stage the whole file");
        var isStash = result.Command.StartsWith("git stash");
        if (isStash && result.StdOut.Contains("CONFLICT"))
            return T("Конфликт в stash: реши файлы в «Конфликтах» и добавь (space), stash сохранён", "Conflict in stash: resolve files under Conflicts and stage them (space), the stash is kept");
        if (isStash && stderr.Contains("would be overwritten"))
            return T("Нельзя применить stash: он затронет незакоммиченные изменения. Сделай коммит или stash их", "Can't apply stash: it would touch uncommitted changes. Commit or stash them");
        if (stderr.Contains("You do not have the initial commit yet"))
            return T("Stash работает только после первого коммита", "Stash works only after the first commit");
        if (stderr.Contains("No local changes to save") || result.StdOut.Contains("No local changes to save"))
            return T("Нечего сохранять в stash: изменений нет", "Nothing to stash: no changes");

        if (result.Command.StartsWith("gh "))
        {
            if (stderr.Contains("Can not approve your own pull request") || stderr.Contains("can not approve your own"))
                return T("Свой PR одобрить нельзя: попроси ревью у другого человека", "You can't approve your own PR: ask someone else to review it");
            if (stderr.Contains("already exists"))
                return T("PR из этой ветки уже открыт: он во вкладке «Этот репозиторий» (g)", "A PR from this branch is already open: see the This repository tab (g)");
            if (stderr.Contains("must first push") || stderr.Contains("No commits between") || stderr.Contains("Head sha can't be blank"))
                return T("Ветки нет на GitHub или в ней нет новых коммитов: сначала сделай push (P)", "The branch is not on GitHub or has no new commits: push first (P)");
            if (stderr.Contains("not mergeable") || stderr.Contains("merge conflict"))
                return T("PR нельзя влить: конфликт с базовой веткой. Разреши его в ветке PR и сделай push", "The PR can't be merged: it conflicts with the base branch. Resolve it in the PR branch and push");
            if (stderr.Contains("Required status check") || stderr.Contains("required status") || stderr.Contains("protected branch"))
                return T("PR нельзя влить: ветка защищена (нужны зелёный CI или одобрение)", "The PR can't be merged: the branch is protected (green CI or an approval is required)");
            if (stderr.Contains("not allowed") && stderr.Contains("merge"))
                return T("Этот способ merge выключен в настройках репозитория: выбери другой", "This merge method is disabled in the repository settings: choose another one");
            if (stderr.Contains("gh auth login") || stderr.Contains("authentication") || stderr.Contains("HTTP 401"))
                return T("gh не авторизован: войди в GitHub (ctrl+g)", "gh is not signed in: sign in to GitHub (ctrl+g)");
            if (stderr.Contains("Could not resolve to a Repository") || stderr.Contains("HTTP 404") || stderr.Contains("HTTP 403"))
                return T("Нет доступа к репозиторию на GitHub", "No access to the repository on GitHub");
            if (stderr.Contains("error connecting") || stderr.Contains("dial tcp") || stderr.Contains("no such host"))
                return T("Нет связи с GitHub: проверь сеть", "Can't reach GitHub: check the network");
        }

        if (result.Command.StartsWith("git rebase"))
        {
            if (stderr.Contains("You have unstaged changes") || stderr.Contains("cannot rebase: You have unstaged changes"))
                return T("Есть изменения вне индекса: добавь их (space) или спрячь (s), затем снова C", "There are unstaged changes: stage them (space) or stash them (s), then C again");
            if (stderr.Contains("is now empty") || result.StdOut.Contains("is now empty"))
                return T("Коммит стал пустым после решения конфликта. Пропусти его: K, или прерви rebase: A", "The commit became empty after resolving the conflict. Skip it: K, or abort the rebase: A");
            if (stderr.Contains("execution failed") || result.StdOut.Contains("execution failed"))
                return T("Не удалось сменить сообщение коммита (хук commit-msg?). Поправь и нажми C или прерви rebase (A)", "Couldn't change the commit message (commit-msg hook?). Fix it and press C, or abort the rebase (A)");
            if (stderr.Contains("could not apply") || stderr.Contains("CONFLICT") || result.StdOut.Contains("CONFLICT")
                || stderr.Contains("unmerged files") || stderr.Contains("needs merge"))
                return T("Конфликт при rebase: реши файлы из «Конфликтов», добавь их (space) и нажми C. Вернуть всё как было: A", "Conflict during rebase: resolve files under Conflicts, stage them (space) and press C. Restore everything: A");
        }

        var isRevert = result.Command.StartsWith("git revert");
        var isPick = result.Command.StartsWith("git cherry-pick");
        if ((isRevert || isPick) && result.StdOut.Contains("CONFLICT"))
            return T($"Конфликт при {(isRevert ? "revert" : "cherry-pick")}: реши файлы из «Конфликтов», добавь их (space) и нажми c. Отменить всё: A", $"Conflict during {(isRevert ? "revert" : "cherry-pick")}: resolve files under Conflicts, stage them (space) and press c. Undo everything: A");
        if ((isRevert || isPick) && stderr.Contains("is a merge but no -m option"))
            return T("Это merge-коммит: его нельзя откатить или скопировать целиком. Выбери обычный коммит", "This is a merge commit: it can't be reverted or copied as a whole. Pick a regular commit");
        if (stderr.Contains("is now empty") || stderr.Contains("nothing to commit"))
            return T("Эти изменения уже есть в ветке, коммит получился бы пустым. Прервать: A", "These changes are already in the branch, the commit would be empty. Abort: A");
        if (stderr.Contains("cherry-pick is already in progress") || stderr.Contains("revert is already in progress"))
            return T("Предыдущая операция не завершена: реши конфликты и нажми c либо прерви её (A)", "The previous operation is not finished: resolve the conflicts and press c, or abort it (A)");

        // Конфликты merge git пишет в stdout.
        if (result.StdOut.Contains("CONFLICT"))
            return T("Конфликт при merge: реши файлы из раздела «Конфликты», добавь их (space) и сделай коммит (c)", "Merge conflict: resolve files under Conflicts, stage them (space) and commit (c)");
        if (stderr.Contains("[remote rejected]"))
            return T("Сервер отклонил push (защищённая ветка или хук на сервере). Подробности: git push в терминале", "The server rejected the push (protected branch or server hook). Details: git push in a terminal");
        if (stderr.Contains("[rejected]"))
            return T("Push отклонён: на сервере есть новые коммиты. Нажми p, чтобы сделать pull", "Push rejected: the server has new commits. Press p to pull");
        if (stderr.Contains("Need to specify how to reconcile divergent branches") || stderr.Contains("Not possible to fast-forward"))
            return T("Ветка и upstream разошлись. Выбери способ pull: git config pull.rebase false (merge) или true (rebase)", "The branch and its upstream have diverged. Choose how to pull: git config pull.rebase false (merge) or true (rebase)");
        if (stderr.Contains("There is no tracking information for the current branch"))
            return T("У ветки нет upstream: сначала сделай push (P), он предложит настроить upstream", "The branch has no upstream: push first (P), it will offer to set the upstream");
        if (stderr.Contains("Authentication failed") || stderr.Contains("could not read Username")
            || stderr.Contains("terminal prompts disabled") || stderr.Contains("Permission denied (publickey)"))
            return T("Нет доступа к remote: войди в GitHub (ctrl+g) или настрой SSH-ключ / credential helper", "No access to the remote: sign in to GitHub (ctrl+g) or set up an SSH key / credential helper");
        if (stderr.Contains("Could not resolve host") || stderr.Contains("Could not read from remote repository")
            || stderr.Contains("unable to access"))
            return T("Не удалось подключиться к remote: проверь сеть и адрес репозитория (git remote -v)", "Couldn't connect to the remote: check the network and the repository address (git remote -v)");
        if (stderr.Contains("No configured push destination") || stderr.Contains("does not appear to be a git repository"))
            return T("Remote не настроен. Добавь его: git remote add origin <адрес>", "No remote configured. Add one: git remote add origin <url>");
        if (stderr.Contains("Author identity unknown") || stderr.Contains("Please tell me who you are"))
            return T("Git не знает имя автора. Выполни: git config --global user.name \"Имя\" и user.email \"почта\"", "Git doesn't know the author name. Run: git config --global user.name \"Name\" and user.email \"email\"");
        if (stderr.Contains("would be overwritten by checkout"))
            return T("Нельзя переключиться: незакоммиченные изменения конфликтуют с веткой. Сделай коммит или stash", "Can't switch: uncommitted changes conflict with the branch. Commit or stash them");
        if (stderr.Contains("would be overwritten by merge"))
            return T("Нельзя выполнить merge: он затронет незакоммиченные изменения. Сделай коммит или stash", "Can't merge: it would touch uncommitted changes. Commit or stash them");
        if (stderr.Contains("You have not concluded your merge") || stderr.Contains("MERGE_HEAD exists"))
            return T("Предыдущий merge не завершён: разреши конфликты и сделай коммит (c)", "The previous merge is not finished: resolve the conflicts and commit (c)");
        if (stderr.Contains("you need to resolve your current index first") || stderr.Contains("unmerged files"))
            return T("Есть неразрешённые конфликты: реши их, добавь файлы в индекс (space) и сделай коммит (c)", "There are unresolved conflicts: resolve them, stage the files (space) and commit (c)");
        if (stderr.Contains("refusing to merge unrelated histories"))
            return T("У веток нет общей истории, git не будет их сливать", "The branches share no history, git won't merge them");
        if (stderr.Contains("is not fully merged"))
            return T("В ветке есть коммиты, которых нет в текущей", "The branch has commits that are not in the current one");
        if (stderr.Contains("cannot delete branch", StringComparison.OrdinalIgnoreCase))
            return T("Нельзя удалить ветку: она открыта в другом worktree", "Can't delete the branch: it is checked out in another worktree");
        if (stderr.Contains("is already used by worktree") || stderr.Contains("is already checked out at"))
            return T("Эта ветка уже открыта в другом worktree", "This branch is already checked out in another worktree");
        if (stderr.Contains("a branch named") && stderr.Contains("already exists"))
            return T("Ветка с таким именем уже существует", "A branch with this name already exists");
        return FirstLine(stderr.Trim().Length > 0 ? stderr : result.StdOut);
    }

    public static string FirstLine(string text)
    {
        var trimmed = text.Trim();
        var newline = trimmed.IndexOf('\n');
        return newline < 0 ? trimmed : trimmed[..newline];
    }
}
