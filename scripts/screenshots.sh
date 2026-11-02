#!/usr/bin/env bash
# Скриншоты для README: собирает демо-проект, снимает сцены EaseGit в tmux и сохраняет SVG в docs/screenshots/.
#
#   scripts/screenshots.sh [рабочая папка]     нужны tmux и python3
#
# Приложение запускается с HOME = рабочая папка: свой config.json (язык, без окна входа) и короткий путь
# «~/weather-cli» в шапке. Вход в GitHub берётся из настоящего gh (GH_CONFIG_DIR).
set -euo pipefail

project="$(cd "$(dirname "$0")/.." && pwd)"
work="${1:-$(mktemp -d)}"
out="$project/docs/screenshots"
gh_config="${GH_CONFIG_DIR:-$HOME/.config/gh}"
cols=120
rows=34
mkdir -p "$out"

dotnet build "$project/EaseGit" --configuration Release --verbosity quiet --nologo > /dev/null
dll="$project/EaseGit/bin/Release/net10.0/easegit.dll"

# --- демо-проект ------------------------------------------------------------
repo="$work/weather-cli"
remote="$work/weather-cli.git"
rm -rf "$repo" "$remote"
git init -q -b main "$repo"
git init -q --bare -b main "$remote"
g() { git -C "$repo" "$@"; }
g config user.name "Daniil Medvin"
g config user.email "demo@example.com"
g config commit.gpgsign false
g remote add origin "$remote"

# Коммит с датой «N назад», чтобы в логе была правдоподобная давность.
commit() {
    local ago="$1" message="$2"
    local when
    when="$(date -d "-$ago" --iso-8601=seconds)"
    g add -A
    GIT_AUTHOR_DATE="$when" GIT_COMMITTER_DATE="$when" g commit -q -m "$message"
}
merge() {
    local ago="$1" branch="$2"
    local when
    when="$(date -d "-$ago" --iso-8601=seconds)"
    GIT_AUTHOR_DATE="$when" GIT_COMMITTER_DATE="$when" g merge -q --no-ff --no-edit "$branch"
}
write() { mkdir -p "$(dirname "$repo/$1")"; cat > "$repo/$1"; }

write README.md <<'TXT'
# weather-cli

Forecast in your terminal.
TXT
printf 'bin/\nobj/\n' | write .gitignore
commit "12 days" "chore: initial commit"

write src/Forecast.cs <<'TXT'
namespace Weather;

public sealed record Forecast(DateOnly Day, double Temperature, string Summary);

public static class ForecastClient
{
    private static readonly HttpClient Http = new();

    public static async Task<IReadOnlyList<Forecast>> LoadAsync(double lat, double lon)
    {
        var json = await Http.GetStringAsync($"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}");
        return ForecastParser.Parse(json);
    }
}
TXT
commit "11 days" "feat: fetch forecast from open-meteo"

write src/Cli.cs <<'TXT'
namespace Weather;

public static class Cli
{
    public static async Task<int> RunAsync(string[] args)
    {
        var forecast = await ForecastClient.LoadAsync(55.75, 37.62);
        foreach (var day in forecast)
            Console.WriteLine($"{day.Day:ddd}  {day.Temperature,5:0.0}°  {day.Summary}");
        return 0;
    }
}
TXT
commit "9 days" "feat: render forecast as a table"

g switch -q -c feature/units
write src/Units.cs <<'TXT'
namespace Weather;

public enum Unit { Celsius, Fahrenheit }

public static class Units
{
    public static double Convert(double celsius, Unit unit) =>
        unit == Unit.Fahrenheit ? celsius * 9 / 5 + 32 : celsius;
}
TXT
commit "7 days" "feat: celsius and fahrenheit units"
write tests/UnitsTests.cs <<'TXT'
public class UnitsTests
{
    [Fact]
    public void Convert_Fahrenheit() => Assert.Equal(212, Units.Convert(100, Unit.Fahrenheit));
}
TXT
commit "6 days" "test: cover unit conversion"
g switch -q main
merge "5 days" feature/units

sed -i 's/return ForecastParser.Parse(json);/return json.Length == 0 ? [] : ForecastParser.Parse(json);/' "$repo/src/Forecast.cs"
commit "4 days" "fix: handle empty api response"
g tag v0.1.0

g switch -q -c feature/cache
write src/Cache.cs <<'TXT'
namespace Weather;

public sealed class Cache(TimeSpan ttl)
{
    private readonly Dictionary<string, (DateTime At, string Json)> _items = [];
}
TXT
commit "2 days" "feat: cache responses for 10 minutes"
g push -q -u origin feature/cache
g switch -q main

printf '\n## Usage\n\n    weather --unit fahrenheit\n' >> "$repo/README.md"
commit "1 day" "docs: usage examples in README"
g push -q -u origin main
sed -i 's/Forecast in your terminal./Forecast in your terminal, fast./' "$repo/README.md"
commit "3 hours" "fix: typo in help text"
printf '\n## Install\n' >> "$repo/README.md"
commit "1 hour" "wip"
printf '\n    dotnet tool install weather\n' >> "$repo/README.md"
commit "40 minutes" "fix: review notes"

# Незакоммиченное: staged, изменения и новый файл.
sed -i 's/var forecast = await ForecastClient.LoadAsync(55.75, 37.62);/var unit = args.Contains("--fahrenheit") ? Unit.Fahrenheit : Unit.Celsius;\n        var forecast = await ForecastClient.LoadAsync(55.75, 37.62);/; s/{day.Temperature,5:0.0}°/{Units.Convert(day.Temperature, unit),5:0.0}°/' "$repo/src/Cli.cs"
g add src/Cli.cs
sed -i 's/string Summary);/string Summary, double WindSpeed);/' "$repo/src/Forecast.cs"
printf '# Roadmap\n\n- hourly forecast\n' | write docs/roadmap.md

# --- съёмка -----------------------------------------------------------------
session="easegit-shots"
shot() {
    sleep 1.2
    tmux capture-pane -t "$session" -e -p | python3 "$project/scripts/ansi2svg.py" "easegit — ~/weather-cli" > "$out/$1.svg"
    echo "  $out/$1.svg"
}
keys() {
    for key in "$@"; do
        tmux send-keys -t "$session" "$key"
        sleep 0.25
    done
}

for lang in en ru; do
    mkdir -p "$work/.config/easegit"
    printf '{ "language": "%s", "authPromptSkipped": true, "showCommandLog": false }\n' "$lang" > "$work/.config/easegit/config.json"
    tmux kill-session -t "$session" 2> /dev/null || true
    tmux new-session -d -s "$session" -x "$cols" -y "$rows" \
        "cd '$repo' && HOME='$work' XDG_CONFIG_HOME='$work/.config' GH_CONFIG_DIR='$gh_config' dotnet '$dll' '$repo'"
    sleep 3

    # Staged C#-файл: справа diff с подсветкой.
    keys Down
    shot "main-$lang"

    keys G
    shot "graph-$lang"
    keys Escape

    # Rebase трёх последних коммитов: «review notes» вливается в «typo», «wip» выбрасывается.
    keys 3 Down Down i f Down d Up
    shot "rebase-$lang"
    keys Escape 1 Up

    keys c
    tmux send-keys -t "$session" -l "add fahrenheit flag"
    shot "commit-$lang"
    keys Escape

    tmux kill-session -t "$session"
done
