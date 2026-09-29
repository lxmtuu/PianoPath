#!/usr/bin/env python3
"""Static sanity checker for the C#/XAML sources.

There is no .NET SDK in this sandbox (WPF also only builds on Windows), so this script does the
checks that do not need a compiler: token-level bracket/quote balance for every C# file, and XML
well-formedness plus resource-reference resolution for the XAML.
"""
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent


def scan_csharp(path: Path):
    text = path.read_text(encoding="utf-8")
    stack = []
    pairs = {")": "(", "]": "[", "}": "{"}
    i, n = 0, len(text)
    line = 1
    errors = []
    while i < n:
        c = text[i]
        if c == "\n":
            line += 1
            i += 1
            continue
        # line comment
        if c == "/" and i + 1 < n and text[i + 1] == "/":
            while i < n and text[i] != "\n":
                i += 1
            continue
        # block comment
        if c == "/" and i + 1 < n and text[i + 1] == "*":
            i += 2
            while i + 1 < n and not (text[i] == "*" and text[i + 1] == "/"):
                if text[i] == "\n":
                    line += 1
                i += 1
            i += 2
            continue
        # verbatim string
        if c == "@" and i + 1 < n and text[i + 1] == '"':
            i += 2
            while i < n:
                if text[i] == '"':
                    if i + 1 < n and text[i + 1] == '"':
                        i += 2
                        continue
                    break
                if text[i] == "\n":
                    line += 1
                i += 1
            i += 1
            continue
        # interpolated string (no nested brace tracking, but quotes must close)
        if c == "$" and i + 1 < n and text[i + 1] == '"':
            i += 2
            while i < n:
                if text[i] == "\\":
                    i += 2
                    continue
                if text[i] == '"':
                    break
                if text[i] == "\n":
                    line += 1
                i += 1
            i += 1
            continue
        # regular string
        if c == '"':
            i += 1
            while i < n:
                if text[i] == "\\":
                    i += 2
                    continue
                if text[i] == '"':
                    break
                if text[i] == "\n":
                    errors.append(f"{path}:{line}: unterminated string literal")
                    break
                i += 1
            i += 1
            continue
        # char literal
        if c == "'":
            i += 1
            while i < n:
                if text[i] == "\\":
                    i += 2
                    continue
                if text[i] == "'":
                    break
                i += 1
            i += 1
            continue
        if c in "([{":
            stack.append((c, line))
        elif c in ")]}":
            if not stack:
                errors.append(f"{path}:{line}: unmatched '{c}'")
            elif stack[-1][0] != pairs[c]:
                errors.append(f"{path}:{line}: '{c}' closes '{stack[-1][0]}' opened on line {stack[-1][1]}")
                stack.pop()
            else:
                stack.pop()
        i += 1
    for token, opened in stack:
        errors.append(f"{path}:{opened}: unclosed '{token}'")
    return errors


def scan_xaml(paths):
    errors = []
    keys = set()
    for path in paths:
        try:
            tree = ET.parse(path)
        except ET.ParseError as ex:
            errors.append(f"{path}: XML error: {ex}")
            continue
        for element in tree.iter():
            key = element.get("Key") or element.get(
                "{http://schemas.microsoft.com/winfx/2006/xaml}Key")
            if key:
                keys.add(key)
    refs = set()
    for path in paths:
        text = path.read_text(encoding="utf-8")
        refs.update(re.findall(r"\{StaticResource\s+([A-Za-z0-9_.]+)\}", text))
    # Framework brushes are legal StaticResource targets too.
    builtin = {"White", "Black", "Transparent", "Red", "Blue", "Green"}
    missing = sorted(r for r in refs if r not in keys and r not in builtin)
    for name in missing:
        errors.append(f"StaticResource '{name}' is referenced but never defined in {', '.join(p.name for p in paths)}")
    return errors, keys


def main():
    errors = []
    cs_files = sorted(p for p in ROOT.glob("**/*.cs") if "obj" not in p.parts and "bin" not in p.parts)
    for path in cs_files:
        errors.extend(scan_csharp(path))
    xaml_files = sorted(p for p in ROOT.glob("**/*.xaml") if "obj" not in p.parts)
    xaml_errors, keys = scan_xaml(xaml_files)
    errors.extend(xaml_errors)
    print(f"checked {len(cs_files)} C# files and {len(xaml_files)} XAML files, {len(keys)} resource keys")
    if errors:
        print(f"\n{len(errors)} problem(s):")
        for e in errors:
            print("  " + e)
        return 1
    print("no bracket, quote, XML or resource-reference problems found")
    return 0


if __name__ == "__main__":
    sys.exit(main())
