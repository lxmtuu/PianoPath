#!/usr/bin/env python3
"""Adds one key to both string tables, in ordinal order, with its Vietnamese translation.

    python3 tools/add_string.py "The English text" "Câu tiếng Việt"

The tables are kept in ordinal order so a translation diff reads cleanly, and both must carry the same
keys — ``tools/check_sources.py`` fails the build when they do not. This script keeps that bookkeeping
out of the way while editing by hand.
"""

from __future__ import annotations

import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent / "Localization"
LINE = re.compile(r'^\s*\[(.*)\]\s*=\s*(.*?),?\s*$')


def escape(text: str) -> str:
    return text.replace('\\', '\\\\').replace('"', '\\"').replace('\n', '\\n')


def unescape(text: str) -> str:
    return text.replace('\\"', '"').replace('\\n', '\n').replace('\\\\', '\\')


def key_of(line: str) -> str | None:
    """The key a table line declares, unescaped and without the quotes that wrap it."""
    match = LINE.match(line)
    if not match:
        return None
    text = match.group(1)
    if len(text) >= 2 and text.startswith('"') and text.endswith('"'):
        text = text[1:-1]
    return unescape(text)


def add(path: pathlib.Path, key: str, value: str) -> None:
    lines = path.read_text(encoding="utf-8").splitlines(keepends=True)
    position = None
    for index, line in enumerate(lines):
        existing = key_of(line)
        if existing is not None and existing > key:
            position = index
            break
    if position is None:
        raise SystemExit("the table ends before the new key can be placed: " + path.name)
    indent = re.match(r"^(\s*)", lines[position]).group(1)
    lines.insert(position, f'{indent}["{escape(key)}"] = "{escape(value)}",\n')
    path.write_text("".join(lines), encoding="utf-8")


def main() -> int:
    if len(sys.argv) != 3:
        print(__doc__)
        return 2
    add(ROOT / "Strings.English.cs", sys.argv[1], sys.argv[1])
    add(ROOT / "Strings.Vietnamese.cs", sys.argv[1], sys.argv[2])
    print("added:", sys.argv[1])
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
