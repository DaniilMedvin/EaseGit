#!/usr/bin/env python3
"""Снимок терминала с цветами (tmux capture-pane -e -p) → SVG для README.

    tmux capture-pane -t easegit -e -p | scripts/ansi2svg.py "easegit" > shot.svg

Сетка символов сохраняется точно: каждый отрезок текста растягивается до своей ширины (textLength),
поэтому рамки не «плывут», даже если в шрифте зрителя другие ширины у ⎇ или ╭.
"""
import html
import re
import sys

FONT_SIZE = 14
CELL_W = 8.4
CELL_H = 18
PAD = 16
BAR = 30
BG = "#14161B"
FG = "#D4D4D4"

# 16 базовых цветов (палитра близкая к теме EaseGit).
BASIC = ["#14161B", "#FFC0B9", "#B3F6C0", "#FCE094", "#A6DBFF", "#FFCAFF", "#8CF8F7", "#DCDCDC",
         "#4F5258", "#FFC0B9", "#B3F6C0", "#FCE094", "#A6DBFF", "#FFCAFF", "#8CF8F7", "#FFFFFF"]

SGR = re.compile(r"\x1b\[([0-9;:]*)m")
OTHER_ESC = re.compile(r"\x1b(\[[0-9;?]*[A-Za-z]|\][^\x07]*\x07|[()][A-Z0-9])")


def color256(n):
    if n < 16:
        return BASIC[n]
    if n < 232:
        n -= 16
        steps = [0, 95, 135, 175, 215, 255]
        return "#%02X%02X%02X" % (steps[n // 36], steps[n // 6 % 6], steps[n % 6])
    v = 8 + (n - 232) * 10
    return "#%02X%02X%02X" % (v, v, v)


def apply_sgr(params, st):
    codes = [int(p) if p else 0 for p in re.split("[;:]", params)] if params else [0]
    i = 0
    while i < len(codes):
        c = codes[i]
        if c == 0:
            st.update(fg=None, bg=None, bold=False, dim=False, italic=False, under=False, strike=False, rev=False)
        elif c == 1: st["bold"] = True
        elif c == 2: st["dim"] = True
        elif c == 3: st["italic"] = True
        elif c == 4: st["under"] = True
        elif c == 7: st["rev"] = True
        elif c == 9: st["strike"] = True
        elif c == 22: st["bold"] = st["dim"] = False
        elif c == 23: st["italic"] = False
        elif c == 24: st["under"] = False
        elif c == 27: st["rev"] = False
        elif c == 29: st["strike"] = False
        elif 30 <= c <= 37: st["fg"] = BASIC[c - 30]
        elif 90 <= c <= 97: st["fg"] = BASIC[c - 90 + 8]
        elif 40 <= c <= 47: st["bg"] = BASIC[c - 40]
        elif 100 <= c <= 107: st["bg"] = BASIC[c - 100 + 8]
        elif c == 39: st["fg"] = None
        elif c == 49: st["bg"] = None
        elif c in (38, 48) and i + 1 < len(codes):
            key = "fg" if c == 38 else "bg"
            if codes[i + 1] == 5 and i + 2 < len(codes):
                st[key] = color256(codes[i + 2]); i += 2
            elif codes[i + 1] == 2 and i + 4 < len(codes):
                st[key] = "#%02X%02X%02X" % tuple(codes[i + 2:i + 5]); i += 4
        i += 1


def parse(text):
    """Строки экрана: список ячеек (символ, стиль)."""
    st = dict(fg=None, bg=None, bold=False, dim=False, italic=False, under=False, strike=False, rev=False)
    rows = []
    for line in text.rstrip("\n").split("\n"):
        cells = []
        pos = 0
        for m in SGR.finditer(line):
            for ch in OTHER_ESC.sub("", line[pos:m.start()]):
                cells.append((ch, dict(st)))
            apply_sgr(m.group(1), st)
            pos = m.end()
        for ch in OTHER_ESC.sub("", line[pos:]):
            cells.append((ch, dict(st)))
        rows.append(cells)
    return rows


def colors(s):
    fg, bg = s["fg"] or FG, s["bg"]
    if s["rev"]:
        fg, bg = (bg or BG), fg
    return fg, bg


def render(rows, title):
    cols = max((len(r) for r in rows), default=0)
    width = PAD * 2 + cols * CELL_W
    height = BAR + PAD * 2 + len(rows) * CELL_H
    out = [
        f'<svg xmlns="http://www.w3.org/2000/svg" width="{width:.0f}" height="{height:.0f}" viewBox="0 0 {width:.0f} {height:.0f}">',
        '<style>text{font-family:"JetBrains Mono","Cascadia Mono","SF Mono",Menlo,Consolas,"DejaVu Sans Mono",monospace;'
        f'font-size:{FONT_SIZE}px;white-space:pre}}</style>',
        f'<rect width="100%" height="100%" rx="10" fill="{BG}"/>',
        f'<rect width="100%" height="{BAR}" rx="10" fill="#202227"/><rect y="{BAR - 10}" width="100%" height="10" fill="#202227"/>',
        '<circle cx="20" cy="15" r="6" fill="#FF5F57"/><circle cx="40" cy="15" r="6" fill="#FEBC2E"/><circle cx="60" cy="15" r="6" fill="#28C840"/>',
        f'<text x="{width / 2:.0f}" y="20" fill="#9B9EA4" text-anchor="middle">{html.escape(title)}</text>',
    ]
    for y, cells in enumerate(rows):
        top = BAR + PAD + y * CELL_H
        # Фон: подряд идущие ячейки одного цвета — одним прямоугольником.
        x = 0
        while x < len(cells):
            bg = colors(cells[x][1])[1]
            end = x
            while end < len(cells) and colors(cells[end][1])[1] == bg:
                end += 1
            if bg and bg != BG:
                out.append(f'<rect x="{PAD + x * CELL_W:.1f}" y="{top}" width="{(end - x) * CELL_W + 0.5:.1f}" height="{CELL_H}" fill="{bg}"/>')
            x = end
        # Текст: отрезки одного стиля; пробелы по краям не рисуем.
        x = 0
        while x < len(cells):
            style = cells[x][1]
            end = x
            while end < len(cells) and cells[end][1] == style:
                end += 1
            run = "".join(c for c, _ in cells[x:end])
            stripped = run.strip(" ")
            if stripped:
                lead = len(run) - len(run.lstrip(" "))
                fg = colors(style)[0]
                attrs = [f'x="{PAD + (x + lead) * CELL_W:.1f}"', f'y="{top + CELL_H * 0.74:.1f}"', f'fill="{fg}"',
                         f'textLength="{len(stripped) * CELL_W:.1f}"', 'lengthAdjust="spacingAndGlyphs"']
                if style["bold"]: attrs.append('font-weight="bold"')
                if style["italic"]: attrs.append('font-style="italic"')
                if style["dim"]: attrs.append('opacity="0.6"')
                deco = [d for d, on in (("underline", style["under"]), ("line-through", style["strike"])) if on]
                if deco: attrs.append(f'text-decoration="{" ".join(deco)}"')
                out.append(f'<text {" ".join(attrs)}>{html.escape(stripped)}</text>')
            x = end
    out.append("</svg>")
    return "\n".join(out)


if __name__ == "__main__":
    print(render(parse(sys.stdin.read()), sys.argv[1] if len(sys.argv) > 1 else "easegit"))
