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
│   ├── CommandLog.cs          журнал изменяющих команд (GitService пишет, CommandLogPanel показывает)
│   ├── UndoPlanner.cs         отмена последнего действия по записи reflog → UndoPlan
│   ├── PatchBuilder.cs        патч из выбранных строк diff для построчного stage (git apply --cached)
│   ├── RepoWatcher.cs         FileSystemWatcher: изменения файлов и .git → одно событие Changed
│   └── Parsers/
│       ├── StatusParser.cs
│       ├── BranchParser.cs
│       ├── ReflogParser.cs
│       ├── StashParser.cs
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
│   ├── AppCommand.cs          описание действия: клавиша, панель, подсказка, превью
│   ├── CommandKey.cs          клавиша команды: подпись и сравнение (в т.ч. символы ? , ~ :)
│   ├── CommandRegistry.cs     все команды; отсюда строятся клавиши, подсказки, ? и палитра
│   └── CommandSearch.cs       нечёткий поиск для палитры
└── UI/
    ├── Theme.cs               цвета и символы из DESIGN.md
    ├── GitErrors.cs           понятные тексты для известных ошибок git
    ├── MainWindow.cs          раскладка
    ├── Panels/                ListPanel<T> (база списков), FilesPanel, BranchesPanel, CommitsPanel, StashPanel, DiffPanel, CommandLogPanel
    ├── Dialogs/               CommitDialog, InputDialog (ветка, stash), ConfirmDialog, SettingsDialog, CommandPalette
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
- В `CommandLog` пишет не `GitRunner`, а `GitService` — только изменяющие команды (stage, commit, push…), чтобы автообновление не засоряло журнал.

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
Каждое действие пользователя описано один раз — `AppCommand`:

```csharp
public sealed class AppCommand
{
    public required string Id { get; init; }          // "files.stage"
    public required string Title { get; init; }       // "Stage / unstage" — справка и палитра
    public required CommandScope Scope { get; init; } // Global | Files | Branches | Commits
    public required CommandKey Key { get; init; }     // подпись "space" + как узнать клавишу
    public required Action Execute { get; init; }
    public string? Hint { get; init; }                // подпись в строке подсказок или null
    public bool HintOptional { get; init; }           // убирается первой, если не помещается
    public Func<bool> IsAvailable { get; init; }      // недоступная не ловит клавишу и скрыта
    public string? UnavailableNote { get; init; }     // "уже есть" — недоступная всё равно видна в справке серым
    public Func<string?>? Preview { get; init; }      // "git add -- Program.cs" — в палитре
    public bool IsDangerous { get; init; }            // ⚠ в палитре
    public string Keywords { get; init; }             // "мерж слить" — поиск в палитре
}
```

`MainWindow` регистрирует команды в `CommandRegistry` (порядок = порядок в подсказках и справке).
Из реестра строятся: обработка клавиш (`Find` — сначала панель в фокусе, потом глобальные; так space
в файлах — stage, а в ветках — checkout), строка подсказок, раздел клавиш в справке и палитра (`ctrl+p` / `:`).
Новая функция = метод в `MainWindow` + одна строка регистрации.

`Execute` вызывает метод `MainWindow`, а не получает контекст: диалоги, статус и выбор в панелях живут в окне,
и переносить их в отдельный `AppContext` пока незачем. Подтверждение опасных действий делает сам метод
(`ConfirmDialog` с `CommandText.Format` его аргументов).

## Поток данных

```
клавиша → панель в фокусе (навигация: ↑↓, enter…) → MainWindow.OnKeyDown
        → CommandRegistry.Find(scope, key) → command.Execute()
        → метод MainWindow: (опасное? ConfirmDialog) → RunAsync(...)  ── в фоне, спиннер для долгих
              → GitService → GitRunner → git        (изменяющие команды → CommandLog)
        → LoadStatusAsync() → App.Invoke(() => панели перерисовываются)
```

## Потоки
- Всё, что вызывает git, работает асинхронно и не блокирует главный цикл Terminal.Gui.
- Изменения UI только через `Application.Invoke(...)`.
- Одновременно выполняется одна изменяющая команда; чтение (diff) можно отменять и перезапускать при смене выбора.

## Ошибки
- Ошибка git → `ErrorDialog` или строка `✗ ...` в статусе с текстом stderr и подсказкой, что делать.
- Известные ситуации распознаются и получают понятный текст: нет upstream, конфликт, грязное дерево, отказ push.
- Необработанное исключение → лог в `~/.local/state/lazygit-cs/error.log`, приложение не падает молча.
