# Архитектура

## Слои

```
┌──────────────────────────────────────────────┐
│ UI            (Terminal.Gui: окна, панели)   │  показывает AppState, ловит клавиши
├──────────────────────────────────────────────┤
│ Commands      (действия пользователя)        │  клавиша → описание → выполнение
├──────────────────────────────────────────────┤
│ State         (AppState)                     │  текущий статус репозитория, события
├──────────────────────────────────────────────┤
│ Git           (GitService → GitRunner)       │  запуск git и разбор вывода
└──────────────────────────────────────────────┘
```

Правило: **каждый слой знает только о слое ниже.** UI никогда не запускает git напрямую.

## Структура папок

```
LazyGit/
├── Program.cs                 точка входа: проверки, создание сервисов, запуск UI
├── Git/
│   ├── GitRunner.cs           запуск процесса git → GitResult
│   ├── GitResult.cs           record: ExitCode, StdOut, StdErr, Command
│   ├── GitService.cs          GetStatusAsync(), StageAsync(), CommitAsync(), ...
│   ├── RepoWatcher.cs         FileSystemWatcher: изменения файлов и .git → одно событие Changed
│   └── Parsers/
│       ├── StatusParser.cs
│       ├── BranchParser.cs
│       ├── LogParser.cs
│       ├── DiffParser.cs
│       └── CommitDiffParser.cs    git show: сообщение коммита + diff
├── Config/
│   ├── AppConfig.cs           настройки: пропуск входа, последний репозиторий, маскот
│   └── ConfigStore.cs         ~/.config/lazygit-cs/config.json
├── Models/
│   ├── ChangeKind.cs          Modified, Added, Deleted, Renamed, Untracked, ...
│   ├── FileChange.cs          Path, Staged, Unstaged, OriginalPath?
│   ├── Branch.cs              Name, IsCurrent, Upstream?, Ahead, Behind
│   ├── Commit.cs              Hash, ShortHash, AuthorName, AuthorEmail, Date, Subject
│   └── RepoStatus.cs          Branch?, HeadHash?, Upstream?, Ahead, Behind, Files
├── State/
│   └── AppState.cs            хранит RepoStatus, событие Changed, RefreshAsync()
├── Commands/
│   ├── IAppCommand.cs
│   ├── CommandRegistry.cs     все команды; отсюда строятся подсказки, ? и палитра
│   └── Files/StageFileCommand.cs, ...
└── UI/
    ├── Theme.cs               цвета и символы из DESIGN.md
    ├── GitErrors.cs           понятные тексты для известных ошибок git
    ├── MainWindow.cs          раскладка
    ├── Panels/                ListPanel<T> (база списков), FilesPanel, BranchesPanel, CommitsPanel, DiffPanel, CommandLogPanel
    ├── Dialogs/               CommitDialog, NewBranchDialog, ConfirmDialog, SettingsDialog, ErrorDialog, CommandPalette
    └── Widgets/               StatusBar (подсказки), Spinner, Header
LazyGit.Tests/
└── Parsers/                   тесты на реальных образцах вывода git
```

## Ключевые компоненты

### GitRunner
- Единственное место, где создаётся `Process`.
- Аргументы передаются через `ProcessStartInfo.ArgumentList`, **никогда** не склеиваются в строку.
- Всегда добавляет `-c core.quotepath=false -c color.ui=never`.
- Окружение: `LC_ALL=C`, `GIT_TERMINAL_PROMPT=0` (git не должен ждать ввода).
- Принимает `CancellationToken`: по `esc` процесс убивается.
- Каждый вызов пишется в `CommandLog`.

```csharp
public sealed record GitResult(string Command, int ExitCode, string StdOut, string StdErr)
{
    public bool Success => ExitCode == 0;
}
```

### GitService
- Методы предметной области: `StageAsync(FileChange)`, `CommitAsync(string message)`, `CheckoutAsync(Branch)`.
- **Чтение** (статус, ветки, лог) возвращает модели; если git завершился с ошибкой, бросается `GitCommandException` — такое не должно происходить в нормальной работе.
- **Изменения** (stage, commit, push) возвращают `GitResult` и **не бросают исключения** на обычные ошибки git (конфликт, отказ push): интерфейс показывает текст ошибки.
- Особые случаи обрабатываются внутри: репозиторий без коммитов, переименованные файлы (оба пути).

### Parsers
- Чистые статические функции: `string → модель`. Без IO, поэтому легко тестируются.

### AppState
- Хранит последний `RepoStatus`, ветки, коммиты.
- `RefreshAsync()` перечитывает всё из git после любой команды.
- Событие `Changed` → панели перерисовываются.

### Commands
Каждое действие пользователя — объект:

```csharp
public interface IAppCommand
{
    string Id { get; }                 // "files.stage"
    string Title { get; }              // "Stage файла"
    Key Key { get; }                   // Key.Space
    string Scope { get; }              // "files" | "branches" | "global"
    bool IsDangerous { get; }          // true → ConfirmDialog
    bool CanExecute(AppContext ctx);
    string Preview(AppContext ctx);    // "git add -- Program.cs" — показываем пользователю
    Task ExecuteAsync(AppContext ctx, CancellationToken ct);
}
```

Отсюда автоматически получаем: строку подсказок внизу, окно `?`, палитру команд, предпросмотр git-команды и подтверждение опасных действий. Новая функция = новая команда, UI трогать почти не нужно.

## Поток данных

```
клавиша → Panel → CommandRegistry.Find(scope, key)
        → (IsDangerous? ConfirmDialog)
        → command.ExecuteAsync()  ── в фоне, показывается Spinner
              → GitService → GitRunner → git
        → AppState.RefreshAsync()
        → AppState.Changed → Application.Invoke(() => панели перерисовываются)
```

## Потоки
- Всё, что вызывает git, работает асинхронно и не блокирует главный цикл Terminal.Gui.
- Изменения UI только через `Application.Invoke(...)`.
- Одновременно выполняется одна изменяющая команда; чтение (diff) можно отменять и перезапускать при смене выбора.

## Ошибки
- Ошибка git → `ErrorDialog` или строка `✗ ...` в статусе с текстом stderr и подсказкой, что делать.
- Известные ситуации распознаются и получают понятный текст: нет upstream, конфликт, грязное дерево, отказ push.
- Необработанное исключение → лог в `~/.local/state/lazygit-cs/error.log`, приложение не падает молча.
