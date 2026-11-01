# Конфиг

Файл `~/.config/lazygit-cs/config.json` (или `$XDG_CONFIG_HOME/lazygit-cs/config.json`). Создаётся приложением,
можно править руками: имена ключей без учёта регистра, допускаются комментарии `//` и запятая после последнего элемента.
Если файл не разобрать, приложение запускается с настройками по умолчанию, а ваш файл копируется в `config.json.bad`.

```jsonc
{
  "language": "en",                       // ru или en; не задан — по языку системы (LANG)
  "theme": "ocean",                       // dark (по умолчанию), light или имя своей темы
  "customThemes": {
    "ocean": {
      "base": "dark",                     // основа: dark или light
      "accent": "#00AAFF",                // любые цвета из списка ниже, только вида #RRGGBB
      "selectionBg": "#102030"
    }
  },
  "keys": {                               // id команды -> клавиша
    "files.stage": "s",
    "commit": "ctrl+k",
    "sync.fetch": "F5"
  }
}
```

## Темы
Переключение на лету: `T` — следующая тема по кругу (dark → light → свои по алфавиту), выбор запоминается.
Светлая тема рассчитана на светлый терминал: основной цвет текста берётся из самого терминала.

Цвета для `customThemes`: `muted`, `dim`, `accent`, `green`, `cyan`, `selectionBg`, `error`, `warning`, `addedBg`,
`removedBg`, `backdrop` (фон терминала: от него считаются плашки), а также подсветка кода `syntaxPlain`, `syntaxKeyword`,
`syntaxControlKeyword`, `syntaxClass`, `syntaxStruct`, `syntaxInterface`, `syntaxMethod`, `syntaxVariable`, `syntaxString`,
`syntaxNumber`, `syntaxComment`, `syntaxPreprocessor`, `syntaxDisabled`, `syntaxPunctuation`.
Опечатка в имени или цвете не ломает запуск: цвет пропускается, внизу показывается предупреждение.

## Клавиши
Переназначаются команды из справки (`?`), id видны в палитре команд и в [CommandRegistry](../LazyGit/UI/MainWindow.cs).
Запись клавиши: `s`, `S` (= shift+s), `ctrl+x`, `alt+f`, `F5`, `space`, `enter`, `esc`, `tab`, `up`, `down`, `left`,
`right`, `home`, `end`, `pageup`, `pagedown`, `delete`, `backspace`, цифры и символы (`?`, `~`, `,`).
Подсказки внизу и справка показывают новые клавиши. Если клавиша уже занята другой командой этого же экрана,
внизу появится предупреждение; навигация (стрелки, `j`/`k`) и клавиши внутри диалогов не переназначаются.

## Остальное
| Ключ | Значение |
|---|---|
| `language` | `ru` или `en`; без ключа — по языку системы. `L` переключает и запоминает |
| `mascot` | `None`, `Claude`, `ChatGpt`, `Gemini` — маскот в углу diff (также `,`) |
| `showCommandLog` | панель Command Log открыта (`~`) |
| `authPromptSkipped` | окно входа в GitHub пропущено |
| `lastRepoPath` | последний открытый репозиторий |
| `recentRepos` | недавние репозитории (до 20): в них окно GitHub ищет локальную копию проекта PR |
