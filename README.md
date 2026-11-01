# LGit

[![CI](https://github.com/DaniilMedvin/LazyGit/actions/workflows/ci.yml/badge.svg)](https://github.com/DaniilMedvin/LazyGit/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/DaniilMedvin/LazyGit)](https://github.com/DaniilMedvin/LazyGit/releases/latest)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](LICENSE)

A calm, keyboard-driven git client for the terminal, written in C# on .NET 10 and Terminal.Gui v2.

**English** · [Русский](README.ru.md)

![Main screen: files, branches, commits and a highlighted C# diff](docs/screenshots/main-en.svg)

## Made for C# / .NET

LGit works with any git repository, but it is tuned for C# developers:

- **C# syntax highlighting in diffs** is powered by Roslyn, the real C# compiler (`Microsoft.CodeAnalysis`), with the
  Visual Studio dark theme colors. Other languages are shown as a plain colored diff.
- **`.gitignore` for .NET in one key** (`I`): the latest official
  [VisualStudio.gitignore](https://github.com/github/gitignore/blob/main/VisualStudio.gitignore) from GitHub,
  with a built-in copy when offline.
- Ignores `bin/`, `obj/` and `.vs/` noise out of the box once that `.gitignore` is in place.

Highlighting for more languages is a welcome contribution, see [CONTRIBUTING.md](CONTRIBUTING.md).

## Features

- **Files**: staged / changes / new / conflicts, folder tree, stage a file, a folder or everything, discard with confirmation.
- **Line-by-line staging**: `enter` on a file, then stage, unstage or discard single lines, selections (`v`) or hunks.
- **Diff** with line numbers and C# syntax highlighting in the Visual Studio dark style.
- **Commits** in the style of Conventional Commits: type picker, live preview, length counter, templates (`ctrl+t`).
- **Branches**: checkout, create, merge, delete (with a second confirmation for unmerged ones), cherry-pick;
  the selected branch's `git log --graph` is shown on the right in color (HEAD, branches, tags) without checkout.
- **Commit view**: full hash, message and a `git show --stat` style summary (`path | 12 ++++----`) above the diff.
- **Mouse wheel** scrolls the diff; panel titles show counts and the position (`[3] Commits · 4/120`).
- **History**: amend, reset soft / mixed / hard, revert, cherry-pick, **interactive rebase** (pick, reword, edit, squash, fixup, drop, reorder).
- **Graph of all branches** (`G`) with per-branch colors, tags and actions on any commit.
- **Sync**: fetch, pull, push with `↑ahead ↓behind`, a spinner, `esc` to cancel, and a guided `push -u` for new branches.
- **Conflicts**: resolve block by block (`enter` on a conflicted file: ours / theirs / both for each `<<<<<<<` block)
  or keep ours / theirs / both for the whole file, continue (`C`) or abort (`A`) merge, cherry-pick, revert and rebase.
- **GitHub pull requests** (`g`): PRs of this repository, your PRs and review requests across all repositories,
  with CI and review status, description, checks and diff; checkout, merge (squash / merge / rebase), approve,
  open in browser; create a PR from the current branch (`N`) with the title and description taken from commits;
  `enter` on a PR of another project opens its local copy or clones it; `/` picks any repository
  (recent local ones, yours on GitHub or any `owner/repo`) to review and merge its PRs. CI status of HEAD in the header (`✓ CI`).
- **Undo** (`z`) of the last commit, amend, merge, pull or checkout through reflog.
- **Stash**: save with a message, pop, apply, drop, see its diff.
- **Transparency**: every action shows the exact git command before it runs; the Command Log (`~`) shows what ran.
- **Command palette** (`ctrl+p` or `:`) with fuzzy search, help (`?`) generated from the actual key bindings.
- **Themes** (dark, light, your own) and **remappable keys** in `config.json`; **English and Russian** UI (`L` switches).

| Interactive rebase | Graph of all branches |
|---|---|
| ![Interactive rebase](docs/screenshots/rebase-en.svg) | ![Graph](docs/screenshots/graph-en.svg) |

## Install

Download a single self-contained binary from [Releases](https://github.com/DaniilMedvin/LazyGit/releases/latest), no .NET needed.

```sh
# Linux x64
curl -L https://github.com/DaniilMedvin/LazyGit/releases/latest/download/lgit-linux-x64.tar.gz | tar xz
sudo mv lgit /usr/local/bin/

# macOS (Apple Silicon)
curl -L https://github.com/DaniilMedvin/LazyGit/releases/latest/download/lgit-osx-arm64.tar.gz | tar xz
sudo mkdir -p /usr/local/bin && sudo mv lgit /usr/local/bin/
```

### macOS

The build is for Apple Silicon (M1 and newer); on Intel Macs build from source (see below).
`/usr/local/bin` may be missing on a fresh Mac, so the command above creates it.

Installed with `curl` as above, `lgit` just runs. If you downloaded the archive **in a browser**, macOS marks it
as quarantined and refuses to open it ("cannot verify the developer"), because the binary is not notarized by Apple.
Remove the mark once:

```sh
xattr -d com.apple.quarantine lgit
```

or in Finder: right-click `lgit` → **Open** → **Open**.

### Windows

In PowerShell: download, unpack to `%LOCALAPPDATA%\Programs\lgit` and add that folder to your user `PATH`.

```powershell
$dir = "$env:LOCALAPPDATA\Programs\lgit"
New-Item -ItemType Directory -Force $dir | Out-Null
Invoke-WebRequest https://github.com/DaniilMedvin/LazyGit/releases/latest/download/lgit-win-x64.zip -OutFile "$env:TEMP\lgit.zip"
Expand-Archive "$env:TEMP\lgit.zip" -DestinationPath $dir -Force
$path = [Environment]::GetEnvironmentVariable("Path", "User")
if ($path -notlike "*$dir*") { [Environment]::SetEnvironmentVariable("Path", "$path;$dir", "User") }
```

Open a new terminal and run `lgit`. Windows Terminal is recommended: the classic console draws box characters poorly.
The binary is not signed, so SmartScreen may warn on the first run: *More info → Run anyway*.
Install `gh` with `winget install GitHub.cli` if you want to sign in to GitHub from the app.
To update, run the same commands again; to remove, delete the folder and remove it from `PATH`.

With the GitHub CLI you can also download any release asset: `gh release download --repo DaniilMedvin/LazyGit --pattern 'lgit-linux-x64.tar.gz'`.

Requirements: `git`. Optional: [GitHub CLI](https://cli.github.com/) `gh` to sign in to GitHub from the app.

### From source

```sh
git clone https://github.com/DaniilMedvin/LazyGit.git && cd LazyGit
dotnet run --project LazyGit -- /path/to/repo
# or a single file in LazyGit/bin/Release/net10.0/linux-x64/publish/lgit
dotnet publish LazyGit -c Release -r linux-x64
```

## Usage

```sh
lgit              # repository in the current folder, or the start screen
lgit ~/projects/x # open a specific repository
```

| Key | Action | Key | Action |
|---|---|---|---|
| `1` `2` `3` `4` / `tab` | switch panel | `?` / `F1` | help with all keys |
| `space` | stage / checkout / pop | `ctrl+p` / `:` | command palette |
| `c` | commit | `p` / `P` / `f` | pull / push / fetch |
| `enter` on a file | line-by-line staging | `i` in commits | interactive rebase |
| `z` | undo last action | `G` | graph of all branches |
| `C` / `A` | continue / abort an operation | `T` / `L` | theme / language |
| `g` | GitHub pull requests | `N` | create a PR |
| `~` | Command Log | `q` | quit |

Panel-specific keys are always shown in the bottom line.

![Commit window](docs/screenshots/commit-en.svg)

## Configuration

`~/.config/lazygit-cs/config.json`: language, theme, custom themes and key bindings. See [docs/CONFIG.md](docs/CONFIG.md).

```jsonc
{
  "language": "en",
  "theme": "light",
  "keys": { "files.stage": "s", "sync.fetch": "F5" }
}
```

## Development

```sh
dotnet build
dotnet test                 # parsers on real git output + integration tests on temporary repositories
scripts/demo-repos.sh       # repositories in tricky states (conflicts, diverged branches, …) for manual checks
scripts/screenshots.sh      # re-shoot the screenshots in docs/screenshots (needs tmux and python3)
```

Project docs (in Russian): [plan](docs/PLAN.md), [architecture](docs/ARCHITECTURE.md), [conventions](docs/CONVENTIONS.md), [design](docs/DESIGN.md).

Releases are built by GitHub Actions: push a tag `v*` and binaries for Linux, Windows and macOS are attached to the release.

## Contributing

Bug reports, ideas and pull requests are welcome: see [CONTRIBUTING.md](CONTRIBUTING.md).
Only the maintainer can merge into `main`, every change goes through a reviewed pull request.

## License

[MIT](LICENSE)
