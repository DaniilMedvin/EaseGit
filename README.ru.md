# LGit

[![CI](https://github.com/DaniilMedvin/LazyGit/actions/workflows/ci.yml/badge.svg)](https://github.com/DaniilMedvin/LazyGit/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/DaniilMedvin/LazyGit)](https://github.com/DaniilMedvin/LazyGit/releases/latest)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](LICENSE)

Спокойный git-клиент для терминала, который управляется с клавиатуры. C#, .NET 10, Terminal.Gui v2.

[English](README.md) · **Русский**

![Главный экран: файлы, ветки, коммиты и diff C# с подсветкой](docs/screenshots/main-ru.svg)

## Сделан для C# / .NET

LGit работает с любым git-репозиторием, но заточен под C#-разработчиков:

- **Подсветка C# в diff** работает на Roslyn — настоящем компиляторе C# (`Microsoft.CodeAnalysis`) — в цветах
  тёмной темы Visual Studio. Остальные языки показываются обычным цветным diff.
- **`.gitignore` для .NET одной клавишей** (`I`): актуальный официальный
  [VisualStudio.gitignore](https://github.com/github/gitignore/blob/main/VisualStudio.gitignore) с GitHub,
  без сети — встроенная копия.
- С таким `.gitignore` мусор из `bin/`, `obj/` и `.vs/` не попадает в список файлов.

Подсветку других языков можно добавить — см. [CONTRIBUTING.md](CONTRIBUTING.md).

## Возможности

- **Файлы**: staged / изменения / новые / конфликты, дерево папок, stage файла, папки или всего сразу, сброс с подтверждением.
- **Построчный stage**: `enter` на файле, затем stage, unstage или сброс отдельных строк, выделения (`v`) или фрагмента.
- **Diff** с номерами строк и подсветкой в стиле тёмной темы Visual Studio: C#, файлы проектов и XAML, Razor, JSON;
  смена версий NuGet в файле проекта — заметкой над diff (мажорные обновления выделены);
  `ctrl+e` — какие классы и методы C# добавлены, изменены или удалены (Roslyn, правка только форматирования не считается).
- **Коммиты** по Conventional Commits: выбор типа, предпросмотр, счётчик длины, шаблоны (`ctrl+t`),
  а по запросу — заготовка сообщения по изменённым классам и методам C# (`ctrl+e`).
- **Ветки**: checkout, создание, merge, удаление (для невлитой — второе подтверждение), cherry-pick;
  справа цветной `git log --graph` выбранной ветки (HEAD, ветки, теги) — без checkout.
- **Просмотр коммита**: полный хеш, сообщение и сводка как `git show --stat` (`путь | 12 ++++----`) над diff.
- **Колесо мыши** листает diff; в заголовках панелей счётчики и позиция (`[3] Коммиты · 4/120`).
- **История**: amend, исправить старый коммит staged-изменениями (`F`: fixup + autosquash), reset soft / mixed / hard, revert, cherry-pick, **interactive rebase** (pick, reword, edit, squash, fixup, drop, перестановка).
- **Граф всех веток** (`G`): цвет за каждой веткой, теги и действия над любым коммитом.
- **Синхронизация**: fetch, pull, push с `↑ahead ↓behind`, спиннером и отменой по `esc`, подсказка `push -u` для новой ветки.
- **Конфликты**: по блокам (`enter` на конфликтном файле: наша / их / обе для каждого блока `<<<<<<<`)
  или целиком — наша / их / обе версии файла, продолжить (`C`) или прервать (`A`) merge, cherry-pick, revert и rebase.
- **Pull request на GitHub** (`g`): PR этого репозитория, мои PR и ждущие моего ревью по всем репозиториям —
  со статусом CI и ревью, описанием, проверками и diff; checkout, merge (squash / merge / rebase), approve,
  открыть в браузере; создать PR из текущей ветки (`N`) — заголовок и описание берутся из коммитов;
  `enter` на PR другого проекта открывает его локальную копию или клонирует его; `/` — выбрать любой
  репозиторий (недавние локальные, свои на GitHub или любой `владелец/репо`), чтобы посмотреть и влить его PR. Статус CI для HEAD в шапке (`✓ CI`).
- **История файла** (`h` на файле, `F` — любой файл): все коммиты, менявшие его, через переименования, с diff этого файла.
- **Поиск по истории** (`ctrl+f`): по сообщению, автору или где строка кода появилась или исчезла (`git log -S`),
  в текущей ветке или во всех, результаты по мере ввода.
- **Сравнение** веток или коммитов (`=`): файлы с +/− и diff, коммиты только в одной стороне; от общего предка
  (что войдёт в PR) или между вершинами.
- **Blame** (`B` на файле, `b` в истории файла): кто и в каком коммите написал каждую строку; `p` — версия до этого
  коммита, чтобы увидеть, кто написал строку раньше.
- **Сборка и тесты** по клавише (`F5`): `dotnet build` и `dotnet test` с выводом по мере выполнения, итогом и упавшими тестами;
  какие тесты гонять — выбор по классам или по одному (`space`, `a` — все).
- **GitHub Actions** (`ctrl+o`): запуски ветки, jobs и шаги, лог упавших шагов прямо в LGit, перезапуск упавших
  или всего запуска, отмена; пока что-то идёт, обновляется само.
- **Отмена** (`z`) последнего коммита, amend, merge, pull или checkout через reflog.
- **Stash**: сохранить с сообщением (всё или только выбранный файл или папку, `S`), pop, apply, удалить, посмотреть diff.
- **Теги** (`#`): список, создать на HEAD (с сообщением — аннотированный), push, удалить локально или на сервере.
- **Ещё про ветки**: переименовать (`R`), пропустить текущий коммит rebase / cherry-pick / revert (`K`), лог коммитов подгружается при прокрутке.
- **Прозрачность**: перед каждым действием видна точная git-команда, а Command Log (`~`) показывает, что выполнено.
- **Палитра команд** (`ctrl+p` или `:`) с нечётким поиском, справка (`?`) строится из реальных клавиш.
- **Темы** (тёмная, светлая, свои) и **свои клавиши** в `config.json`; интерфейс **на русском и английском** (`L` переключает).

| Interactive rebase | Граф всех веток |
|---|---|
| ![Interactive rebase](docs/screenshots/rebase-ru.svg) | ![Граф](docs/screenshots/graph-ru.svg) |

## Установка

Один исполняемый файл со всем нужным: скачай его из [Releases](https://github.com/DaniilMedvin/LazyGit/releases/latest), .NET не нужен.

```sh
# Linux x64
curl -L https://github.com/DaniilMedvin/LazyGit/releases/latest/download/lgit-linux-x64.tar.gz | tar xz
sudo mv lgit /usr/local/bin/

# macOS (Apple Silicon)
curl -L https://github.com/DaniilMedvin/LazyGit/releases/latest/download/lgit-osx-arm64.tar.gz | tar xz
sudo mkdir -p /usr/local/bin && sudo mv lgit /usr/local/bin/
```

### macOS

Сборка для Apple Silicon (M1 и новее); на Mac с Intel собери из исходников (см. ниже).
На новом Mac папки `/usr/local/bin` может не быть — команда выше её создаёт.

Если ставить через `curl`, как выше, `lgit` просто запускается. Если архив скачан **в браузере**, macOS помечает
его как скачанный из интернета и не даёт открыть («не удалось проверить разработчика»): файл не нотаризован Apple.
Сними пометку один раз:

```sh
xattr -d com.apple.quarantine lgit
```

или в Finder: правый клик по `lgit` → **Открыть** → **Открыть**.

### Windows

В PowerShell: скачать, распаковать в `%LOCALAPPDATA%\Programs\lgit` и добавить эту папку в пользовательский `PATH`.

```powershell
$dir = "$env:LOCALAPPDATA\Programs\lgit"
New-Item -ItemType Directory -Force $dir | Out-Null
Invoke-WebRequest https://github.com/DaniilMedvin/LazyGit/releases/latest/download/lgit-win-x64.zip -OutFile "$env:TEMP\lgit.zip"
Expand-Archive "$env:TEMP\lgit.zip" -DestinationPath $dir -Force
$path = [Environment]::GetEnvironmentVariable("Path", "User")
if ($path -notlike "*$dir*") { [Environment]::SetEnvironmentVariable("Path", "$path;$dir", "User") }
```

Открой новый терминал и набери `lgit`. Лучше в Windows Terminal: старая консоль плохо рисует рамки.
Файл не подписан, поэтому при первом запуске SmartScreen может предупредить: *Подробнее → Выполнить в любом случае*.
Чтобы входить в GitHub из приложения, поставь `gh`: `winget install GitHub.cli`.
Обновить — выполнить те же команды ещё раз; удалить — стереть папку и убрать её из `PATH`.

Через GitHub CLI можно скачать любой файл релиза: `gh release download --repo DaniilMedvin/LazyGit --pattern 'lgit-linux-x64.tar.gz'`.

Нужен `git`. По желанию — [GitHub CLI](https://cli.github.com/) `gh`, чтобы входить в GitHub из приложения.

### Из исходников

```sh
git clone https://github.com/DaniilMedvin/LazyGit.git && cd LazyGit
dotnet run --project LazyGit -- /путь/к/репозиторию
# или один файл в LazyGit/bin/Release/net10.0/linux-x64/publish/lgit
dotnet publish LazyGit -c Release -r linux-x64
```

## Запуск

```sh
lgit              # репозиторий в текущей папке или стартовый экран
lgit ~/projects/x # открыть конкретный репозиторий
```

| Клавиша | Действие | Клавиша | Действие |
|---|---|---|---|
| `1` `2` `3` `4` / `tab` | переход между панелями | `?` / `F1` | справка со всеми клавишами |
| `space` | stage / checkout / pop | `ctrl+p` / `:` | палитра команд |
| `c` | коммит | `p` / `P` / `f` | pull / push / fetch |
| `enter` на файле | построчный stage | `i` в коммитах | interactive rebase |
| `z` | отменить последнее действие | `G` | граф всех веток |
| `C` / `A` | продолжить / прервать операцию | `T` / `L` | тема / язык |
| `g` | pull request на GitHub | `N` | создать PR |
| `~` | Command Log | `q` | выход |

Клавиши текущей панели всегда видны в нижней строке.

![Окно коммита](docs/screenshots/commit-ru.svg)

## Настройки

`~/.config/lazygit-cs/config.json`: язык, тема, свои темы и клавиши. Подробнее — [docs/CONFIG.md](docs/CONFIG.md).

```jsonc
{
  "language": "ru",
  "theme": "light",
  "keys": { "files.stage": "s", "sync.fetch": "F5" }
}
```

## Разработка

```sh
dotnet build
dotnet test                 # парсеры на реальном выводе git + интеграционные тесты на временных репозиториях
scripts/demo-repos.sh       # репозитории в сложных состояниях (конфликты, разошедшиеся ветки…) для ручной проверки
scripts/screenshots.sh      # переснять скриншоты в docs/screenshots (нужны tmux и python3)
```

Документация проекта: [план](docs/PLAN.md), [архитектура](docs/ARCHITECTURE.md), [соглашения](docs/CONVENTIONS.md), [дизайн](docs/DESIGN.md).

Релизы собирает GitHub Actions: отправь тег `v*`, и к релизу приложатся бинарники для Linux, Windows и macOS.

## Участие

Сообщения об ошибках, идеи и pull request приветствуются: см. [CONTRIBUTING.md](CONTRIBUTING.md).
Вливать в `main` может только автор, каждое изменение проходит через проверенный pull request.

## Лицензия

[MIT](LICENSE)
