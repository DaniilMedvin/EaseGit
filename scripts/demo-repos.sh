#!/usr/bin/env bash
# Создаёт демо-репозитории для ручной проверки EaseGit.
#
#   scripts/demo-repos.sh [папка]        по умолчанию /tmp/easegit-demo (пересоздаётся)
#   dotnet run --project EaseGit -- /tmp/easegit-demo/conflict
#
# Репозитории:
#   files     — все разделы панели файлов: staged, изменения, новые, rename, вложенные папки,
#               пробелы и кириллица в именах, файл со staged и unstaged частями
#   conflict  — merge остановился на конфликтах: обычный, «добавлен в обеих ветках», C#-файл
#   branches  — ветки: влитая, невлитая, впереди, позади, с удалённым upstream
#   stash     — два stash (со своим сообщением и WIP) и незакоммиченные изменения
#   sync      — ветка разошлась с сервером: push отклонят, pull потребует выбрать способ
#   empty     — репозиторий без коммитов
set -euo pipefail

root="${1:-/tmp/easegit-demo}"
rm -rf "$root"
mkdir -p "$root"

# Своё имя и почта в каждом репозитории, чтобы не зависеть от глобального конфига.
init() {
    git init -q -b main "$1"
    git -C "$1" config user.name "EaseGit Demo"
    git -C "$1" config user.email "demo@example.com"
    git -C "$1" config commit.gpgsign false
}

commit() {
    git -C "$1" add -A
    git -C "$1" commit -q -m "$2"
}

# --- files ------------------------------------------------------------------
r="$root/files"
init "$r"
mkdir -p "$r/src/UI/Panels" "$r/docs" "$r/старое"
printf 'namespace Demo;\n\npublic static class Program\n{\n    public static void Main() => System.Console.WriteLine("Hello");\n}\n' > "$r/src/Program.cs"
printf 'public sealed class FilesPanel { }\n' > "$r/src/UI/Panels/FilesPanel.cs"
printf 'public sealed class DiffPanel { }\n' > "$r/src/UI/Panels/DiffPanel.cs"
printf '# Demo\n' > "$r/README.md"
printf 'old name\n' > "$r/docs/old-name.md"
printf 'будет удалён\n' > "$r/старое/файл.txt"
printf 'line 1\nline 2\n' > "$r/both.txt"
commit "$r" "feat: initial project"

# staged: изменение, rename, новый файл
sed -i 's/Hello/Hello, EaseGit/' "$r/src/Program.cs"
git -C "$r" mv docs/old-name.md docs/new-name.md
printf 'public sealed class CommitsPanel { }\n' > "$r/src/UI/Panels/CommitsPanel.cs"
git -C "$r" add src/Program.cs src/UI/Panels/CommitsPanel.cs
# файл со staged и unstaged частями
printf 'line 1\nline 2 staged\n' > "$r/both.txt"
git -C "$r" add both.txt
printf 'line 1\nline 2 staged\nline 3 unstaged\n' > "$r/both.txt"
# изменения вне индекса: правка во вложенной папке и удаление
printf 'public sealed class FilesPanel\n{\n    // TODO: tree view\n}\n' > "$r/src/UI/Panels/FilesPanel.cs"
rm "$r/старое/файл.txt"
# новые файлы: пробел в имени, кириллица, вложенная папка
printf 'заметки\n' > "$r/docs/my notes.md"
mkdir -p "$r/src/Новое"
printf 'class Черновик { }\n' > "$r/src/Новое/Черновик.cs"

# --- conflict ---------------------------------------------------------------
r="$root/conflict"
init "$r"
printf 'one\ntwo\nthree\n' > "$r/a.txt"
printf 'public static class Greeter\n{\n    public static string Hello() => "Hello";\n}\n' > "$r/Greeter.cs"
printf 'unchanged\n' > "$r/other.txt"
commit "$r" "feat: initial"
git -C "$r" switch -q -c feature
printf 'one\nTHEIRS\nthree\n' > "$r/a.txt"
printf 'public static class Greeter\n{\n    public static string Hello() => "Hi from feature";\n}\n' > "$r/Greeter.cs"
printf 'added in feature\n' > "$r/both-added.txt"
commit "$r" "feat: feature changes"
git -C "$r" switch -q main
printf 'one\nOURS\nthree\n' > "$r/a.txt"
printf 'public static class Greeter\n{\n    public static string Hello() => "Hello from main";\n}\n' > "$r/Greeter.cs"
printf 'added in main\n' > "$r/both-added.txt"
commit "$r" "feat: main changes"
git -C "$r" merge -q feature >/dev/null 2>&1 || true

# --- branches ---------------------------------------------------------------
r="$root/branches"
remote="$root/branches-remote.git"
git init -q --bare -b main "$remote"
init "$r"
git -C "$r" remote add origin "$remote"
printf 'base\n' > "$r/a.txt"
commit "$r" "feat: initial"
git -C "$r" push -q -u origin main 2>/dev/null
git -C "$r" branch merged                                   # уже влита: удалится через D сразу
git -C "$r" switch -q -c unmerged
printf 'work\n' > "$r/unmerged.txt"
commit "$r" "feat: unmerged work"                           # D попросит второе подтверждение
git -C "$r" switch -q -c ahead main
git -C "$r" push -q -u origin ahead 2>/dev/null
printf 'x\n' > "$r/ahead.txt"
commit "$r" "feat: local only"                              # ↑1
git -C "$r" switch -q -c behind main
printf 'y\n' > "$r/behind.txt"
commit "$r" "feat: will be pushed"
git -C "$r" push -q -u origin behind 2>/dev/null
git -C "$r" reset -q --hard HEAD~1                          # ↓1
git -C "$r" switch -q -c gone main
git -C "$r" push -q -u origin gone 2>/dev/null
git -C "$r" push -q origin --delete gone 2>/dev/null
git -C "$r" fetch -q --prune                                # «upstream удалён»
git -C "$r" switch -q main

# --- stash ------------------------------------------------------------------
r="$root/stash"
init "$r"
printf 'v1\n' > "$r/a.txt"
commit "$r" "feat: initial"
printf 'v2\n' > "$r/a.txt"
git -C "$r" stash push -q                                   # WIP on main: …
printf 'v3\n' > "$r/a.txt"
printf 'new\n' > "$r/new.txt"
git -C "$r" stash push -q --include-untracked -m "эксперимент с новым файлом"
printf 'v4 — текущая работа\n' > "$r/a.txt"                 # s спрячет и это

# --- sync -------------------------------------------------------------------
r="$root/sync"
remote="$root/sync-remote.git"
git init -q --bare -b main "$remote"
init "$r"
git -C "$r" remote add origin "$remote"
printf 'base\n' > "$r/a.txt"
commit "$r" "feat: initial"
git -C "$r" push -q -u origin main 2>/dev/null
other="$root/.sync-other"
git clone -q "$remote" "$other" 2>/dev/null
git -C "$other" config user.name "Коллега"
git -C "$other" config user.email "colleague@example.com"
printf 'from colleague\n' > "$other/colleague.txt"
commit "$other" "feat: colleague change"
git -C "$other" push -q 2>/dev/null
rm -rf "$other"
printf 'mine\n' > "$r/mine.txt"
commit "$r" "feat: my change"
git -C "$r" fetch -q                                        # ↑1 ↓1: P отклонят, p попросит выбрать merge/rebase

# --- empty ------------------------------------------------------------------
r="$root/empty"
init "$r"
printf 'first\n' > "$r/first.txt"

echo "Готово: $root"
for name in files conflict branches stash sync empty; do
    echo "  dotnet run --project EaseGit -- $root/$name"
done
