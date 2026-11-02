# Contributing to EaseGit

Thanks for wanting to make EaseGit better! [Русская версия ниже](#участие-в-разработке-easegit).

## How it works

1. **Fork** the repository and create a branch: `feature/<short-name>` or `fix/<short-name>`.
2. Make the change, add tests, run `dotnet build` and `dotnet test`.
3. Open a **pull request** to `main` and describe what and why.
4. CI builds and tests the PR. The maintainer reviews it and merges it, or asks for changes.

Nobody except the maintainer can push to `main`, delete branches or rewrite history.
CI for pull requests from new contributors starts only after the maintainer approves it.

## Before you start

- For anything bigger than a small fix, open an issue first so we can agree on the approach.
- Read [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) and [docs/CONVENTIONS.md](docs/CONVENTIONS.md) (in Russian;
  a translator does the job). The short version:
  - git is called only through `GitRunner`, arguments via `ArgumentList`, machine-readable output only;
  - every UI string is written in both languages: `T("Коммит", "Commit")`;
  - colors and symbols come from `Theme`, no magic strings;
  - parsers are covered by tests on real git output.
- Commit messages follow [Conventional Commits](https://www.conventionalcommits.org/): `feat: …`, `fix: …`, `docs: …`.

## Good first contributions

- Syntax highlighting for more languages in the diff panel (see `Highlighting/`).
- `.gitignore` templates for other stacks.
- Testing and fixes on Windows and macOS.

## Security

Please don't report vulnerabilities in public issues, see [SECURITY.md](SECURITY.md).

---

# Участие в разработке EaseGit

1. Сделай **fork** и ветку `feature/<кратко>` или `fix/<кратко>`.
2. Внеси изменения, добавь тесты, проверь `dotnet build` и `dotnet test`.
3. Открой **pull request** в `main`: что сделано и зачем.
4. CI соберёт и проверит PR, автор посмотрит и вольёт или попросит поправить.

Пушить в `main`, удалять ветки и переписывать историю может только автор. CI для PR от новых участников
запускается после одобрения автора. Перед большой правкой открой issue, чтобы договориться о подходе.
Правила кода — в [docs/CONVENTIONS.md](docs/CONVENTIONS.md), архитектура — в [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).
