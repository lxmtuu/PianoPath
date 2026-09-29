#!/usr/bin/env python3
"""Static sanity checker for the C#/XAML sources.

There is no .NET SDK in this sandbox (WPF also only builds on Windows), so this script does the
checks that do not need a compiler:

  * token-level bracket/quote balance for every C# file;
  * XML well-formedness of every XAML file, plus unique resource keys, unique x:Name per file and a
    code-behind file for every x:Class;
  * every ``{StaticResource}``/``{DynamicResource}`` reference and every ``FindName``/``FindResource``
    target resolves to something that actually exists;
  * every event handler named in XAML exists in the C# sources.

Run it from the repository root (``python tools/check_sources.py``); CI runs it before the Windows
build so a typo is caught in seconds instead of in a full Windows job.
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
    """Returns (errors, resource keys, named elements reachable with FindName)."""
    errors = []
    keys = set()
    all_names = set()
    xaml_ns = "{http://schemas.microsoft.com/winfx/2006/xaml}"
    template_tags = {"ControlTemplate", "DataTemplate", "ItemsPanelTemplate"}

    def walk(element, scope, in_template):
        """Collects x:Key / x:Name. Inside a template the names live in their own name scope, so a
        template may reuse 'Chrome' or 'Glyph' as often as it likes — but not twice in one template."""
        for child in element:
            tag = child.tag.split("}")[-1]
            child_in_template = in_template or tag in template_tags
            key = child.get("Key") or child.get(xaml_ns + "Key")
            if key and not in_template:
                if key in keys:
                    errors.append(f"{path}: duplicate x:Key '{key}'")
                keys.add(key)
            name = child.get("Name") or child.get(xaml_ns + "Name")
            if name:
                if name in scope:
                    errors.append(f"{path}: duplicate x:Name '{name}' in the same scope")
                scope.add(name)
                if not child_in_template:
                    all_names.add(name)
            walk(child, set() if child_in_template and not in_template else scope, child_in_template)

    for path in paths:
        try:
            tree = ET.parse(path)
        except ET.ParseError as ex:
            errors.append(f"{path}: XML error: {ex}")
            continue
        walk(tree.getroot(), set(), False)

    references = set()
    for path in paths:
        text = path.read_text(encoding="utf-8")
        references.update(re.findall(r"\{(?:Static|Dynamic)Resource\s+([A-Za-z0-9_.]+)\}", text))
    # Framework brushes are legal StaticResource targets too.
    builtin = {"White", "Black", "Transparent", "Red", "Blue", "Green", "Gray"}
    for name in sorted(r for r in references if r not in keys and r not in builtin):
        errors.append(f"StaticResource/DynamicResource '{name}' is referenced but never defined in {', '.join(p.name for p in paths)}")
    return errors, keys, all_names


def scan_xaml_bindings(cs_files, xaml_files, xaml_names, resource_keys):
    errors = []
    for path in cs_files:
        text = path.read_text(encoding="utf-8")
        for name in re.findall(r"FindName\(\"([^\"]+)\"\)", text):
            if name not in xaml_names:
                errors.append(f"{path}: FindName(\"{name}\") target does not exist in any XAML file")
        for name in re.findall(r"FindResource\(\"([^\"]+)\"\)", text):
            if name not in resource_keys:
                errors.append(f"{path}: FindResource(\"{name}\") target is not defined in any XAML file")
        # "x:Name" style lookups through the generated fields are checked by the compiler; here we only
        # verify the reflective ones, which is where a rename silently breaks the UI.

    cs_all_text = "\n".join(p.read_text(encoding="utf-8") for p in cs_files)
    events = [
        "Click", "Checked", "Unchecked", "SelectionChanged", "ValueChanged",
        "KeyDown", "KeyUp", "MouseMove", "PreviewKeyDown",
        "PreviewMouseLeftButtonDown", "PreviewMouseLeftButtonUp", "MouseDoubleClick",
        "TextChanged", "LostFocus", "Deactivated", "Closed", "PianoKeyChanged",
        "MouseLeftButtonUp", "MouseLeftButtonDown", "MouseWheel", "Loaded", "SizeChanged",
    ]
    for path in xaml_files:
        text = path.read_text(encoding="utf-8")
        for ev in events:
            for m in re.finditer(rf"\b{ev}=\"([^\"]+)\"", text):
                handler = m.group(1)
                if not re.search(rf"\b{handler}\b", cs_all_text):
                    errors.append(f"{path}: Event handler \"{handler}\" not found in C# sources")

    # Every x:Class needs a code-behind partial class next to its markup file.
    for path in xaml_files:
        text = path.read_text(encoding="utf-8")
        match = re.search(r"x:Class=\"([^\"]+)\"", text)
        if not match:
            continue
        class_name = match.group(1).split(".")[-1]
        code_behind = path.with_suffix(path.suffix + ".cs")
        if not code_behind.exists():
            errors.append(f"{path}: x:Class '{match.group(1)}' has no {code_behind.name} code-behind")
            continue
        if not re.search(rf"\bpartial\s+class\s+{class_name}\b", code_behind.read_text(encoding="utf-8")):
            errors.append(f"{code_behind}: missing 'partial class {class_name}' for {path.name}")
    return errors


def main():
    errors = []
    cs_files = sorted(p for p in ROOT.glob("**/*.cs") if "obj" not in p.parts and "bin" not in p.parts)
    for path in cs_files:
        errors.extend(scan_csharp(path))
    xaml_files = sorted(p for p in ROOT.glob("**/*.xaml") if "obj" not in p.parts and "bin" not in p.parts)
    xaml_errors, keys, names = scan_xaml(xaml_files)
    errors.extend(xaml_errors)
    errors.extend(scan_xaml_bindings(cs_files, xaml_files, names, keys))
    print(f"checked {len(cs_files)} C# files and {len(xaml_files)} XAML files, {len(keys)} resource keys, {len(names)} named elements")
    if errors:
        print(f"\n{len(errors)} problem(s):")
        for e in errors:
            print("  " + e)
        return 1
    print("no bracket, quote, XML, resource-reference, naming or binding problems found")
    return 0


if __name__ == "__main__":
    sys.exit(main())
