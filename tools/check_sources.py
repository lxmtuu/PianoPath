#!/usr/bin/env python3
"""Static sanity checker for the C#/XAML sources.

There is no .NET SDK in this sandbox (WPF also only builds on Windows), so this script does the
checks that do not need a compiler:

  * token-level bracket/quote balance for every C# file;
  * XML well-formedness of every XAML file, plus unique resource keys, unique x:Name per file and a
    code-behind file for every x:Class;
  * every ``{StaticResource}``/``{DynamicResource}`` reference and every ``FindName``/``FindResource``
    target resolves to something that actually exists;
  * every event handler named in XAML exists in the C# sources;
  * the localization tables: every language translates exactly the keys of the English inventory,
    placeholders and line breaks survive a translation, every literal the sources can print is a key
    of that inventory, and every key of the inventory is still held by a source file — a key that
    nothing prints is a sentence a translator carries for nothing (see ``docs/LOCALIZATION.md``);
  * the command line: every switch the app parses is in the README table and vice versa, and every
    switch and path the preview workflow passes to the executable really exists;
  * every Markdown table in the repository: a run of pipe rows whose second line is not a delimiter row
    (``|---|``) is not a table anywhere but in the head of whoever wrote it, so it is reported before a
    reader meets it as a paragraph of raw pipes;
  * the sentence in both READMEs that says what is still a WPF layer drawn over a GPU frame, checked
    against the engine that draws the watermark (see ``scan_gpu_overlay_evidence``);
  * the installer: every message name the Vietnamese wizard text overrides exists in the Inno Setup
    the script claims to support, keeps the placeholders of the English message, and is reachable
    from the ``[Languages]``/``[LangOptions]`` sections that Inno Setup reads (see
    ``tools/inno_messages.py``);
  * the generated documentation assets: ``docs/samples`` still matches the script that builds it;
  * the Phase 1 visual fixture: timing, tempo-map duration, notes, checkpoints and reference presets agree.

Run it from the repository root (``python tools/check_sources.py``); CI runs it before the Windows
build so a typo is caught in seconds instead of in a full Windows job. A .NET SDK is not needed.
"""
import json
import math
import re
import struct
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent


def _raw_string(text: str, i: int):
    """A C# raw string literal starts at ``i`` (three or more quotes, optionally after ``$`` signs) and runs
    to the next run of that many quotes. Returns ``(end, newlines)`` or ``None`` when this is not one."""
    j = i
    while j < len(text) and text[j] == "$":
        j += 1
    if text[j:j + 1] != '"':
        return None
    quotes = 0
    while text[j + quotes: j + quotes + 1] == '"':
        quotes += 1
    if quotes < 3:
        return None
    k = j + quotes
    while k < len(text):
        if text[k] == '"':
            run = 0
            while text[k + run: k + run + 1] == '"':
                run += 1
            if run >= quotes:
                return k + run, text.count("\n", i, k + run)
            k += run
            continue
        k += 1
    return len(text), text.count("\n", i)


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
        # raw string literal (C# 11): its content is verbatim, so the scanner skips it whole
        raw = _raw_string(text, i)
        if raw is not None:
            i, added = raw
            line += added
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


def scan_settings_navigation():
    """The dock navigation lives in two places on purpose: ``SettingsPages`` owns the catalogue and
    ``MainWindow.xaml`` owns the markup of each page. This check proves the two agree, so inserting a
    page in one of them cannot silently leave the other behind."""
    errors = []
    catalogue = (ROOT / "Ui" / "SettingsPages.cs").read_text(encoding="utf-8")
    xaml = (ROOT / "Ui" / "MainWindow.xaml").read_text(encoding="utf-8")

    names = dict(re.findall(r'internal const string (\w+) = "([^"]+)";', catalogue))
    sections_block = re.search(r"SettingsSection\[\]\s+Sections\s*=\s*\[(.*?)\n    \];", catalogue, re.S)
    if not sections_block:
        return ["Ui/SettingsPages.cs: the Sections catalogue could not be parsed"]
    expected = []
    for label_expr, pages in re.findall(r"new\((\w+),\s*\[([^\]]*)\]\)", sections_block.group(1)):
        label = names.get(label_expr, label_expr)
        pages = [names.get(p.strip(), p.strip()) for p in pages.split(",") if p.strip()]
        if not pages:
            errors.append(f"Ui/SettingsPages.cs: section '{label}' lists no pages")
        expected.append((label, pages))
    if not expected:
        errors.append("Ui/SettingsPages.cs: no navigation sections found")

    declared = []
    for tag in re.findall(r"<TabItem\b[^>]*>", xaml):
        header = re.search(r'Header="([^"]+)"', tag)
        section = re.search(r'local:SettingsPages\.Section="([^"]+)"', tag)
        if header:
            declared.append((header.group(1).replace("&amp;", "&"), section.group(1).replace("&amp;", "&") if section else None))
    if not declared:
        return errors + ["Ui/MainWindow.xaml: the settings TabControl has no TabItem pages"]

    flat = [(label, page) for label, pages in expected for page in pages]
    if len(flat) != len(declared):
        errors.append(f"the dock has {len(declared)} TabItems but the catalogue lists {len(flat)} pages")
    for index, ((label, page), (header, section)) in enumerate(zip(flat, declared)):
        # A header may decorate the page name (Camera → "Camera & FX") but must start with it, which
        # still catches a page that was reordered or renamed on one side only.
        if header != page and not header.startswith(page + " "):
            errors.append(f"Ui/MainWindow.xaml: tab #{index} is '{header}' but the catalogue expects page '{page}'")
        first_of_section = flat[index - 1][0] != label if index else True
        if first_of_section and section != label:
            errors.append(f"Ui/MainWindow.xaml: '{header}' should print the section header '{label}'" + (f" (found '{section}')" if section else " (none set)"))
        if not first_of_section and section:
            errors.append(f"Ui/MainWindow.xaml: '{header}' repeats the section header '{section}'; only the first page of a group carries it")
    labels = [label for label, _ in expected]
    if len(set(labels)) != len(labels):
        errors.append("Ui/SettingsPages.cs: two sections share the same caption")
    return errors


def scan_icon_glyphs():
    """Every control template draws its icon with ``Data="{TemplateBinding Tag}"``. The icon set mixes
    filled shapes (``IconPlay``, ``IconFolder``) with open outlines (``IconClose``, ``IconMinimize``),
    so a template that paints with ``Fill`` only renders the outline icons as nothing at all — the play
    dialog lost its close glyph exactly that way. Both properties must be bound."""
    errors = []
    app_xaml = (ROOT / "App.xaml").read_text(encoding="utf-8")
    for match in re.finditer(r'<Path[^>]*Data="\{TemplateBinding Tag\}"[^>]*/>', app_xaml):
        glyph = match.group(0)
        line = app_xaml[: match.start()].count("\n") + 1
        missing = [name for name in ("Fill", "Stroke") if f"{name}=" not in glyph]
        if missing:
            errors.append(f"App.xaml:{line}: an icon glyph paints with {' and '.join('no ' + name for name in missing)}; "
                          "bind both Fill and Stroke so filled and outline icons both render")
    return errors


def scan_accessible_names():
    """A button whose caption is a glyph or a single letter is anonymous to a screen reader. The WPF
    layer mirrors the tooltip into ``AutomationProperties.Name`` at load time (see ``Loc.Track``), so
    the static rule is: every glyph-only button carries a tooltip or an explicit accessible name. A new
    icon button without either would ship a control that assistive technology cannot announce."""
    errors = []
    xaml = (ROOT / "Ui" / "MainWindow.xaml").read_text(encoding="utf-8")
    for match in re.finditer(r"<Button\b[^>]*>", xaml):
        tag = match.group(0)
        line = xaml[: match.start()].count("\n") + 1
        content = re.search(r'Content="([^"]*)"', tag)
        text = content.group(1).replace("&amp;", "&") if content else ""
        named = re.search(r'(ToolTip|AutomationProperties\.Name)="[^"]+"', tag) is not None
        if content is None or len([char for char in text if char.isalpha()]) < 2:
            if not named:
                errors.append(f"Ui/MainWindow.xaml:{line}: a button shows “{text or 'an icon'}”, which is not a caption; "
                              "add a ToolTip (the screen reader reads it) or AutomationProperties.Name")
    return errors


def scan_theme_tokens():
    """``ShellThemeManager`` writes every theme token into ``Application.Resources`` at runtime. Each
    one needs a matching default in ``App.xaml``, otherwise the very first frame (before the theme is
    published) would render an unresolved resource."""
    errors = []
    theme = (ROOT / "Theme" / "ShellTheme.cs").read_text(encoding="utf-8")
    keys = set(re.findall(r'Set\(resources,\s*"([^"]+)"', theme))
    if not keys:
        return ["Theme/ShellTheme.cs: no published theme tokens found"]
    app_xaml = (ROOT / "App.xaml").read_text(encoding="utf-8")
    defined = set(re.findall(r'x:Key="([^"]+)"', app_xaml))
    for key in sorted(keys - defined):
        errors.append(f"App.xaml: theme token '{key}' is published by ShellThemeManager but has no default resource")
    return errors


# ==================================================================================================
# Localization: the string tables are the product's surface, so they are checked like source.
# ==================================================================================================

def _call_args(text: str, open_index: int):
    """Split the argument list of a call whose ``(`` sits at ``open_index``, honouring nesting."""
    args, buf, depth, i, n = [], [], 1, open_index + 1, len(text)
    while i < n:
        c = text[i]
        if c == '"':
            j = i + 1
            while j < n:
                if text[j] == "\\":
                    j += 2
                    continue
                if text[j] == '"':
                    break
                j += 1
            buf.append(text[i:j + 1])
            i = j + 1
            continue
        if c in "([{":
            depth += 1
        elif c in ")]}":
            depth -= 1
            if depth == 0:
                args.append("".join(buf).strip())
                return args
        elif c == "," and depth == 1:
            args.append("".join(buf).strip())
            buf = []
            i += 1
            continue
        buf.append(c)
        i += 1
    return args


def _code(text: str) -> str:
    """Blank out comments so a string in prose is never mistaken for a key (positions are kept)."""
    out, i, n = [], 0, len(text)
    while i < n:
        c = text[i]
        raw = _raw_string(text, i)
        if raw is not None:
            # Keep the literal in place — it may be a localization key, so positions must not move.
            out.append(text[i:raw[0]])
            i = raw[0]
        elif c == "/" and text[i + 1:i + 2] == "/":
            j = text.find("\n", i)
            j = n if j < 0 else j
            out.append(" " * (j - i))
            i = j
        elif c == "/" and text[i + 1:i + 2] == "*":
            j = text.find("*/", i + 2)
            j = n if j < 0 else j + 2
            out.append("".join(ch if ch == "\n" else " " for ch in text[i:j]))
            i = j
        elif c == '@' and text[i + 1:i + 2] == '"':
            j = i + 2
            while j < n:
                if text[j] == '"':
                    if text[j + 1:j + 2] == '"':
                        j += 2
                        continue
                    break
                j += 1
            out.append(text[i:j + 1])
            i = j + 1
        elif c == '"':
            j = i + 1
            while j < n:
                if text[j] == "\\":
                    j += 2
                    continue
                if text[j] == '"':
                    break
                j += 1
            out.append(text[i:j + 1])
            i = j + 1
        elif c == "'":
            j = i + 1
            while j < n and text[j] != "'":
                j += 2 if text[j] == "\\" else 1
            out.append(text[i:j + 1])
            i = j + 1
        else:
            out.append(c)
            i += 1
    return "".join(out)


LITERAL = re.compile(r'(?<!\$)"((?:[^"\\]|\\.)*)"')


def _literal_only(arg: str):
    """The string of an argument that is nothing but one literal, else ``None``."""
    match = re.fullmatch(r'"(?:[^"\\]|\\.)*"', arg.strip())
    return json.loads(arg.strip()) if match else None


def _literals(arg: str):
    """Every plain (non-interpolated) literal inside an argument, for ``cond ? "a" : "b"``."""
    return [json.loads(f'"{inner}"') for inner in LITERAL.findall(arg)]


def read_table(path: Path):
    """Parse ``["key"] = "value",`` rows in source order."""
    rows, order = {}, []
    if not path.exists():
        return None, []
    text = path.read_text(encoding="utf-8")
    for match in re.finditer(r'^\s{8}\[("(?:[^"\\]|\\.)*")\] = ("(?:[^"\\]|\\.)*"),$', text, re.M):
        key = json.loads(match.group(1))
        rows[key] = json.loads(match.group(2))
        order.append(key)
    return rows, order


def scan_localization(cs_files):
    """Prove the tables are complete, consistent and in step with the sources.

    Three directions, because each one fails differently: the two tables must hold the same keys (a
    missing translation is invisible in the language it does not affect), the English table must map
    every key to itself (it is the inventory, and a reworded key is a lost translation), and every
    literal the sources can print must be a key of that inventory (a typo prints raw template text
    and no language can fix it).
    """
    errors = []
    root = ROOT / "Localization"
    # The registry lives with the string table, not with the WPF label binder: Loc.cs holds Languages
    # and the look-up, Localizer.cs only the half that paints live labels. Reading the folder rather
    # than one hard-coded name keeps this check pointing at whichever file carries the registry.
    registry_files = sorted(p for p in root.glob("*.cs") if not p.name.startswith("Strings."))
    registry = "\n".join(path.read_text(encoding="utf-8") for path in registry_files)
    registered = re.findall(r'new\("([a-z]{2}(?:-[A-Za-z]{2})?)", "([^"]*)", "([^"]*)", Strings(\w+)\.Table\)', registry)
    if not registered:
        names = ", ".join(p.name for p in registry_files) or "no file"
        return [f"Localization/ ({names}): no language is registered in Languages"], 0, 0

    tables, sheet = {}, {}
    for code, _english, _native, table in registered:
        path = root / f"Strings.{table}.cs"
        rows, order = read_table(path)
        if rows is None:
            errors.append(f"Localizer.cs: language '{code}' expects {path.name}, which does not exist")
            continue
        if order != sorted(order, key=lambda key: [ord(c) for c in key]):
            errors.append(f"{path.name}: entries are not in ordinal order; a checklist has to diff cleanly")
        tables[code] = rows
        sheet[code] = path.name

    inventory = tables.get("en")
    if inventory is None:
        return errors + ["Localizer.cs: English must be registered, its table is the inventory"], 0, 0

    for key, value in inventory.items():
        if key != value:
            errors.append(f"Strings.English.cs: '{key}' maps to '{value}'; the inventory maps a key to itself")

    def holes(text):
        return sorted(re.findall(r"\{\d+(?::[^}]*)?\}", text))

    for code, rows in tables.items():
        if code == "en":
            continue
        missing = sorted(set(inventory) - set(rows), key=lambda key: [ord(c) for c in key])
        extra = sorted(set(rows) - set(inventory), key=lambda key: [ord(c) for c in key])
        name = sheet.get(code, f"Strings.{code}.cs")
        for key in missing[:6]:
            errors.append(f"{name} does not translate '{key}'")
        if len(missing) > 6:
            errors.append(f"{name} is missing {len(missing) - 6} further translations")
        for key in extra[:6]:
            errors.append(f"{name} translates '{key}', which is not a key of the inventory")
        if len(extra) > 6:
            errors.append(f"{name} carries {len(extra) - 6} further unknown entries")
        for key, value in rows.items():
            if key not in inventory:
                continue
            if holes(key) != holes(value):
                errors.append(f"{name}: '{value}' changes the placeholders of '{key}'")
            elif value and key.count("\n") != value.count("\n"):
                errors.append(f"{name}: '{value}' drops a line break of '{key}'")
            elif not value.strip():
                errors.append(f"{name}: '{key}' is translated to an empty string")

    # Every route a caption takes from a literal in the sources to the screen. A key that reaches the
    # UI through a variable (the shortcut card, a theme blurb) is followed back to its table instead of
    # being excused, because that is exactly where a reword slips through.
    used = {}

    def note(key, origin):
        if key and key not in used:
            used[key] = origin

    builders = {"Card": (1, 2), "Toggle": (1, 3), "SliderRow": (1, 5), "ColorRow": (1, 3), "Note": (1,), "Choice": (1, 3)}
    for path in cs_files:
        if path.parent.name == "Localization":
            continue
        text = _code(path.read_text(encoding="utf-8"))
        rel = path.relative_to(ROOT)
        for match in re.finditer(r"\bLoc\.(T|F|Page)\(", text):
            args = _call_args(text, match.end() - 1)
            if args:
                # A key may be chosen by a condition — T(a ? "READY" : "NO AUDIO DEVICE") — and both
                # branches are keys, so every literal of the first argument is collected.
                for key in _literals(args[0]):
                    note(key, f"{rel}: Loc.{match.group(1)}")
        for match in re.finditer(r"\bLoc\.(Set|Format)\(", text):
            args = _call_args(text, match.end() - 1)
            if len(args) > 1:
                for key in _literals(args[1]):
                    note(key, f"{rel}: Loc.{match.group(1)}")
        for name, positions in builders.items():
            for match in re.finditer(rf"(?<![\w.]){name}\(", text):
                args = _call_args(text, match.end() - 1)
                for position in positions:
                    if position < len(args):
                        note(_literal_only(args[position]), f"{rel}: {name}")
                if name == "Choice":
                    for arg in args[4:]:
                        for option in re.finditer(r'\(\s*"(?:[^"\\]|\\.)*"\s*,\s*"((?:[^"\\]|\\.)*)"\s*\)', arg):
                            note(json.loads(f'"{option.group(1)}"'), f"{rel}: Choice option")
        for match in re.finditer(r"(?<![\w.])ButtonRow\(", text):
            for arg in _call_args(text, match.end() - 1)[1:]:
                head = re.match(r'\(\s*"((?:[^"\\]|\\.)*)"\s*,', arg.strip())
                if head:
                    note(json.loads(f'"{head.group(1)}"'), f"{rel}: ButtonRow")
        for match in re.finditer(r"(?<![\w.])ApplyVisualSettings\(", text):
            args = _call_args(text, match.end() - 1)
            if args:
                note(_literal_only(args[0]), f"{rel}: status line")
        if rel.name == "MainWindow.Shortcuts.cs" and "ShortcutGroups =" in text:
            block = text[text.index("ShortcutGroups ="):]
            block = block[:block.index("\n    ];")] if "\n    ];" in block else block
            for match in re.finditer(r'\(\s*"((?:[^"\\]|\\.)*)"\s*,\s*\[', block):
                note(json.loads(f'"{match.group(1)}"'), f"{rel}: shortcut column")
            for match in re.finditer(r'\(\s*"(?:[^"\\]|\\.)*"\s*,\s*"((?:[^"\\]|\\.)*)"\s*\)', block):
                note(json.loads(f'"{match.group(1)}"'), f"{rel}: shortcut row")
        if rel.name == "SettingsPages.cs":
            for match in re.finditer(r'internal const string \w+ = "([^"]+)";', text):
                note(match.group(1), f"{rel}: navigation")
        if rel.name == "MainWindow.Menu.cs" and "PlayInlineSections = new()" in text:
            # The Play dialog repeats rows of the dock inside each OPTIONS layer; their labels are
            # records built from literals, so they are collected from the table itself.
            block = text[text.index("PlayInlineSections = new()"):]
            for match in re.finditer(r'new\(\s*"((?:[^"\\]|\\.)*)"\s*,\s*nameof\(', block):
                note(json.loads(f'"{match.group(1)}"'), f"{rel}: Play dialog layer row")
        if rel.name == "ShellTheme.cs":
            for match in re.finditer(r'new ShellTheme\(\s*"[^"]*",\s*"((?:[^"\\]|\\.)*)",\s*"((?:[^"\\]|\\.)*)"', text, re.S):
                note(json.loads(f'"{match.group(1)}"'), f"{rel}: theme name")
                note(json.loads(f'"{match.group(2)}"'), f"{rel}: theme blurb")
        if rel.name == "VisualPresets.cs" and "BuiltIn { get; } =" in text:
            block = text[text.index("BuiltIn { get; } ="):]
            block = block[:block.index("\n    ];")] if "\n    ];" in block else block
            for match in re.finditer(r'new\(("(?:[^"\\]|\\.)*"|\w+),\s*"((?:[^"\\]|\\.)*)"', block):
                if match.group(1).startswith('"'):
                    note(json.loads(match.group(1)), f"{rel}: preset name")
                note(json.loads(f'"{match.group(2)}"'), f"{rel}: preset description")

    markup = ROOT / "Ui" / "MainWindow.xaml"
    if markup.exists():
        xaml = markup.read_text(encoding="utf-8")
        # Attribute names may carry a dot (AutomationProperties.Name, local:Loc.Localize), so the
        # qualified-name pattern has to allow one or the whole tag falls out of the match.
        for match in re.finditer(r"<[A-Za-z][\w.]*((?:\s+[\w:.]+=\"[^\"]*\")+)[^>]*?/?>", xaml):
            attrs = match.group(1)
            if 'local:Loc.Localize="True"' not in attrs:
                continue
            for attr in re.finditer(r'(Text|Content|Header|ToolTip|Title|AutomationProperties\.Name)="([^"]*)"', attrs):
                value = (attr.group(2).replace("&amp;", "&").replace("&lt;", "<")
                         .replace("&gt;", ">").replace("&quot;", '"'))
                # A single glyph (the ↺ of the speed reset) is not a sentence and the runtime skips it too.
                if len(value) > 1 and any(char.isalpha() for char in value):
                    note(value, f"Ui/MainWindow.xaml: {attr.group(1)}")

    for key, origin in sorted(used.items(), key=lambda item: [ord(c) for c in item[0]]):
        if key not in inventory:
            errors.append(f"{origin}: “{key}” is printed by the app but is not a key of Strings.English.cs")
    return errors, len(used), len(inventory)


def scan_dead_keys(cs_files, xaml_files):
    """Every key of the inventory has to be held by a source file.

    A key nothing holds is a sentence no screen can ever show, and the two tables become the only
    place it lives: a translator still carries it, the checklist diff still shows it, and the next
    reword leaves a second copy of the same sentence beside it (that is how the nine keys this rule
    was written for came to be — "STAGE ATMOSPHERE" next to "ATMOSPHERE", three shapes of "Next
    recording…" where one row prints, and so on).

    The lookup text is compared exactly, not as a substring: "Next recording: … · {4}." is a prefix
    of the live "… · {4}. {5}", so a substring test would let a dead key stay for ever. A key can
    also reach a surface through a value rather than a literal (``BackdropStyle.Acoustic`` arrives
    at the picker through ``ToString()``), which is what ``runtime`` names. A key a source assembles
    at run time out of pieces would read as dead here; none does today, and the message says what to
    do if one ever appears.
    """
    rows, _order = read_table(ROOT / "Localization" / "Strings.English.cs")
    if rows is None:
        return []

    # Keys a surface looks up through a value, so no source file spells them out.
    runtime = {
        "Acoustic", "Imperial", "Obsidian",     # BackdropStyle names, looked up as style.ToString()
    }

    def decoded(literal: str):
        """The strings a source literal can mean: as written, and with C#/XML escapes resolved."""
        xml = literal.replace("&amp;", "&").replace("&lt;", "<").replace("&gt;", ">").replace("&quot;", '"')
        try:
            return {xml, json.loads(f'"{literal}"')}
        except ValueError:
            return {xml}

    held = set()
    for path in list(cs_files) + list(xaml_files):
        if path.parent.name == "Localization":
            continue
        for inner in re.findall(r'"((?:[^"\\]|\\.)*)"', path.read_text(encoding="utf-8")):
            held |= decoded(inner)

    errors = []
    for key in sorted(set(rows) - held - runtime, key=lambda key: [ord(c) for c in key]):
        errors.append(f"Strings.English.cs: '{key}' is a key no source file holds, so nothing can print it — "
                      "delete it from both tables, or name it in scan_dead_keys when a surface looks it up "
                      "through a value instead of a literal")
    return errors


def readme_slug(heading):
    """GitHub's heading anchor: lower-case, drop punctuation, spaces to dashes."""
    text = heading.strip().lower()
    text = re.sub(r"[^\w\s\-À-ỹ]", "", text, flags=re.UNICODE)
    return re.sub(r"\s", "-", text)


# The READMEs are one document in two languages: both have to stay complete, and a relative link in
# either of them has to resolve. The Vietnamese file is the original; the English one is what a reader
# who does not speak Vietnamese opens first, so neither may quietly lose a section.
READMES = ["README.md", "README.en.md"]


def scan_readme():
    """Documentation is part of the product: a screenshot that no longer exists or a table-of-contents
    link that points at a renamed heading is a broken README for everyone who reads it first."""
    errors = []
    # ``docs/previews`` is rendered by CI and committed back, so a shot the workflow knows about can be
    # one commit behind the README line that introduces it. Every other image has to exist right now.
    workflow = ROOT / ".github" / "workflows" / "build.yml"
    workflow_text = workflow.read_text(encoding="utf-8") if workflow.exists() else ""
    rendered = set(re.findall(r"Name\s*=\s*'([^']+\.png)'", workflow_text))
    # Shared JPEG studies are assembled from per-engine captures and saved outside the language-specific $shots list.
    rendered.update(re.findall(r"\$sheet\.Save\(\(Join-Path \$PWD 'docs/previews/([^']+\.jpe?g)'", workflow_text))
    pending = []
    # Each edition shows the interface in its own language, so every preview a README points at has to sit
    # in that edition's own set — and the workflow has to render both sets in the first place.
    rendered_languages = set()
    language_loop = re.search(r"foreach \(\$lang in @\((.+?)\)\)", workflow.read_text(encoding="utf-8")) if workflow.exists() else None
    if language_loop:
        rendered_languages = set(re.findall(r"'([a-z]{2})'", language_loop.group(1)))
    if rendered_languages != {"en", "vi"}:
        errors.append("build.yml no longer renders one preview set per language (expected a loop over 'en' and 'vi')")
    for name in READMES:
        own = "vi" if name == "README.md" else "en"
        readme = ROOT / name
        if not readme.exists():
            errors.append(f"{name} is missing")
            continue
        text = readme.read_text(encoding="utf-8")
        for target in re.findall(r"!\[[^\]]*\]\(([^)\s]+)\)", text):
            if re.match(r"^(https?:|data:)", target):
                continue
            if (ROOT / target).exists():
                continue
            relative = target.replace("\\", "/")
            if relative.startswith("docs/previews/"):
                parts = relative.split("/")
                # Shared root-level galleries (preset/material studies) are not language-specific; CI renders them separately.
                if len(parts) == 3 and Path(target).name in rendered:
                    pending.append(f"{name}:{Path(target).name}")
                    continue
                if parts[2] != own:
                    errors.append(f"{name} shows the preview '{target}', which is not the '{own}' set this edition reads")
                    continue
                if Path(target).name in rendered:
                    pending.append(f"{name}:{Path(target).name}")
                    continue
            errors.append(f"{name} references the image '{target}', which does not exist")
        headings = {readme_slug(m.group(2)) for m in re.finditer(r"^(#{1,6})\s+(.*)$", text, re.M)}
        for anchor in re.findall(r"\]\(#([^)\s]+)\)", text):
            if anchor not in headings:
                errors.append(f"{name} links to '#{anchor}', which is not a heading in the file")
        for link in re.findall(r"\]\((?!https?:|#|mailto:)([^)\s]+)\)", text):
            if any(link.startswith(prefix) for prefix in ("!",)):
                continue
            if Path(link).name in {entry.split(":", 1)[1] for entry in pending if entry.startswith(name + ":")}:
                continue
            if not (ROOT / link).exists() and not link.startswith("http"):
                # A repo-relative link to a file that is not in the checkout (a published binary, a
                # user-preset folder…) is tolerated when it carries no path separator.
                if "/" in link or "\\" in link:
                    errors.append(f"{name} links to '{link}', which does not exist")
    if pending:
        print(f"note: {len(pending)} README image reference(s) point at previews CI renders — "
              f"{', '.join(sorted(pending))} — which this commit does not carry yet")
    # Both files document the same product, so they have to link to each other: a reader who lands on
    # one of them must be able to reach the other without editing the URL.
    for name, other in (("README.md", "README.en.md"), ("README.en.md", "README.md")):
        if (ROOT / name).exists() and (ROOT / other).exists() and other not in (ROOT / name).read_text(encoding="utf-8"):
            errors.append(f"{name} never links to {other}; the two language editions have to reference each other")
    return errors


# docs/previews belongs to CI: the build runs ``git add docs/previews`` and commits whatever is there. A
# diagnostic written into it (a capture report with a measured duration, a hash, a log) changes on every run
# and so guarantees a fresh commit forever, which is exactly what happened once with ``*.report.txt``. The
# folder holds pictures and explicitly allowlisted native frame bundles; diagnostics go outside the
# repository (see ``App.ReportPreview``).
PREVIEW_PICTURES = {".png", ".jpg", ".jpeg"}


def scan_markdown_tables():
    """Every Markdown table in the repository has to be a table.

    A row of pipes is a table in GitHub's renderer only when the *second* line is the delimiter row
    (``|---|---|``); without it the whole block is printed as one paragraph of raw pipes. That is not a
    style question — it is the difference between a plan somebody can read and a wall of text — and it
    fails silently, because the source file looks exactly like a table to whoever edits it. Two real
    cases went unnoticed through several review rounds for that reason: the P1 table of
    ``docs/ROADMAP.md`` was cut in two by a pair of blank lines between rows 9 and 10, and a single row
    of ``docs/REPO-AUDIT.md`` §7.2 was left standing alone between two blank lines.

    A block is judged by its own shape, so the rule needs no list of files: a run of pipe rows whose
    second line is not a delimiter row is reported once, with the line the run starts on.

    What is *not* judged is anything Markdown renders as code: a fenced block (```` ``` ````/``~~~``) or a
    line indented four spaces. A snippet that prints a table is text, not a table — this repository
    already carries one such snippet in ``docs/VISUAL-EFFECTS-MASTER-ROADMAP.md`` — and reporting it
    would be a false alarm that turns the job red for a document that is perfectly correct.
    """
    def cells(line: str):
        stripped = line.strip()
        if not (stripped.startswith("|") and stripped.endswith("|")):
            return None
        return stripped[1:-1].split("|")

    def is_row(line: str) -> bool:
        found = cells(line)
        # Two pipes at least — ``| |`` is a valid one-cell row, but a stray ``|`` is not a row.
        return found is not None and len(found) >= 1 and line.count("|") >= 2

    def is_delimiter(line: str) -> bool:
        found = cells(line)
        return bool(found) and all(re.fullmatch(r"\s*:?-{2,}:?\s*", cell) for cell in found)

    def rendered_as_code(lines):
        """One flag per line: inside a fenced block, or indented far enough to be a code block."""
        fence, flags = None, []
        for line in lines:
            stripped = line.strip()
            if fence is not None:
                flags.append(True)
                if stripped.startswith(fence):
                    fence = None
                continue
            if stripped.startswith("```") or stripped.startswith("~~~"):
                fence = stripped[:3]
                flags.append(True)
                continue
            flags.append(line.startswith("    ") or line.startswith("\t"))
        return flags

    errors = []
    for path in sorted(ROOT.glob("**/*.md")):
        if "obj" in path.parts or "bin" in path.parts:
            continue
        lines = path.read_text(encoding="utf-8").split("\n")
        code = rendered_as_code(lines)
        index = 0
        while index < len(lines):
            if code[index] or not is_row(lines[index]):
                index += 1
                continue
            end = index
            while end < len(lines) and not code[end] and is_row(lines[end]):
                end += 1
            rows = end - index
            if rows < 2 or not is_delimiter(lines[index + 1]):
                relative = path.relative_to(ROOT).as_posix()
                errors.append(
                    f"{relative}:{index + 1}: {rows} line(s) of pipe rows without a delimiter row "
                    f"(|---|) beneath the header, so Markdown renders them as text and not as a table")
            index = end
    return errors


# The sentence in each README that claims what is still a WPF layer over a GPU frame, plus the wording that
# says the engine draws the watermark itself. scan_gpu_overlay_evidence reads the list between the marker and
# the end of the clause, so the two halves are one check: the list may not name the watermark, and the
# sentence has to say where it went instead.
GPU_OVERLAY_CLAIMS = {
    "README.md": ("Trên sân khấu GPU,", "vẫn là lớp vector WPF",
                  "riêng **watermark do chính engine in vào khung hình**"),
    "README.en.md": ("On the GPU stage the", "are still WPF vector layers",
                     "the engine prints it inside the frame itself"),
}


def scan_gpu_overlay_evidence():
    """Where the watermark is drawn is a fact about the renderer, and both READMEs said it wrong for a release.

    The limitation bullet in each README listed the watermark among the WPF vector layers still drawn over the
    GPU frame, while the engine had been printing the strip *inside* the frame since the render-to-video work
    (sprite kind 16, ``Gpu/GpuStageRenderer.cs``). Nothing goes red when a sentence like that goes stale — the
    engine, the changelog and the README simply disagree, and the reader is the one who finds out. So the claim
    is derived from the engine here and compared with the prose, in the same spirit as the Media Foundation
    GUIDs above: the sentence is only correct while the code says so.

    Four places have to agree, and each one fails differently:
      * ``GpuStageRenderer`` pushes the strip as a sprite -> the engine draws it;
      * ``PianoStage.HasGpuOverlays`` -> a take with only the watermark on must not blend an empty overlay pass;
      * ``MainWindow.BlendStageOverlays`` -> the summary names the layers it rasterizes;
      * both READMEs' GPU limitation bullet -> what is still a WPF layer, and what is not.
    """
    errors = []
    renderer_path = ROOT / "Gpu" / "GpuStageRenderer.cs"
    stage_path = ROOT / "Stage" / "PianoStage.Gpu.cs"
    blend_path = ROOT / "Ui" / "MainWindow.Gpu.cs"
    for path in (renderer_path, stage_path, blend_path):
        if not path.exists():
            return [f"{path.relative_to(ROOT).as_posix()} is missing; it is one of the files that decide where the watermark is drawn"]

    renderer = renderer_path.read_text(encoding="utf-8")
    # The strip is drawn by the engine when the renderer pushes it as a sprite of its own kind.
    engine_draws = re.search(r"GpuSpriteInstance\s*\{[^}]*KindWatermark", renderer, re.S) is not None

    stage = stage_path.read_text(encoding="utf-8")
    overlay = re.search(r"internal bool HasGpuOverlays\s*=>(.*?);", stage, re.S)
    if not overlay:
        errors.append("Stage/PianoStage.Gpu.cs no longer declares HasGpuOverlays, so nothing decides whether a "
                      "GPU take needs a WPF overlay pass; update scan_gpu_overlay_evidence with the new shape")
    elif engine_draws and "ShowWatermark" in overlay.group(1):
        errors.append("Stage/PianoStage.Gpu.cs: HasGpuOverlays still asks for the watermark, but "
                      "Gpu/GpuStageRenderer.cs draws it inside the frame (sprite kind 16) — a take with only the "
                      "watermark on would blend an overlay pass that draws nothing")
    elif not engine_draws and "ShowWatermark" not in overlay.group(1):
        errors.append("Stage/PianoStage.Gpu.cs: the engine no longer draws the watermark itself, so HasGpuOverlays "
                      "has to include ShowWatermark or the watermark is drawn nowhere")

    blend = blend_path.read_text(encoding="utf-8")
    layers = re.search(r"Rasterizes the stage's WPF overlays \(([^)]*)\)", blend)
    if not layers:
        errors.append("Ui/MainWindow.Gpu.cs: the BlendStageOverlays summary no longer names the layers it "
                      "rasterizes; update scan_gpu_overlay_evidence with the new wording")
    elif engine_draws and "watermark" in layers.group(1).lower():
        errors.append("Ui/MainWindow.Gpu.cs: BlendStageOverlays lists the watermark among the layers it blends, "
                      "while the engine draws it inside the frame")

    for name, (start_marker, list_end_marker, engine_wording) in GPU_OVERLAY_CLAIMS.items():
        text = (ROOT / name).read_text(encoding="utf-8")
        # Both READMEs mention the phrase more than once (the keyboard row says «On the GPU stage the
        # shading sliders…»), so the bullet is the occurrence whose own item carries the clause being read.
        # The item runs from the marker to the next bullet, heading or blank line, and its lines are joined:
        # wrapping a long sentence over two lines is a normal edit that this check must not call a lie.
        def item_from(index: int) -> str:
            body = [text[index:].split("\n", 1)[0]]
            for line in text[index:].split("\n")[1:]:
                if not line.strip() or line.startswith(("-", "#", "*", "+", "|", ">")) or line[0].isspace():
                    break
                body.append(line.strip())
            return " ".join(body)

        sentence = next((item_from(found.start()) for found in re.finditer(re.escape(start_marker), text)
                         if list_end_marker in item_from(found.start())), None)
        if sentence is None:
            errors.append(f"{name}: no item starting with {start_marker!r} also says {list_end_marker!r}, so the "
                          f"list of layers a GPU frame still takes from WPF cannot be read; update "
                          f"scan_gpu_overlay_evidence with the new wording")
            continue
        listed = sentence[:sentence.index(list_end_marker)]
        if engine_draws and "watermark" in listed.lower():
            errors.append(f"{name}: the GPU limitation bullet still lists the watermark among the WPF layers drawn "
                          f"over the frame, while Gpu/GpuStageRenderer.cs draws it inside the frame")
        if not engine_draws and "watermark" not in listed.lower():
            errors.append(f"{name}: the engine no longer draws the watermark inside the frame, so the GPU "
                          f"limitation bullet has to list it as a WPF layer again")
        if engine_draws and engine_wording not in sentence:
            errors.append(f"{name}: the GPU limitation bullet no longer says the engine draws the watermark "
                          f"itself (expected {engine_wording!r})")
    return errors


def scan_wisp_motion_contract():
    """The wisp's sideways wander is one curve, and every renderer has to draw that curve.

    Before W0 the same expression lived in four C# files — twice in ``GpuStageSimulation``, once in
    ``GpuArLayer.Effects`` and once in ``ArParticleAlignmentReview``, the diagnostic that *measures* AR
    alignment against its own arithmetic. The software stage held a fifth, different version that added the
    sine to the particle's velocity, so the same mote was drawn 10.6 px apart at 0.8 s and nothing compared
    them. Unit tests pin the curve's shape; this check pins the wiring, which is the half that drifted.

    Three things are enforced:
      * the constants live only in ``Stage/WispMotion.cs``;
      * each consumer calls ``WispMotion.Offset``, including the software stage's draw — a renderer that
        quietly reintroduces its own sine fails the fast lane instead of drifting in a video;
      * that call is gated on ``ParticleFlow <= 0``, because W1's flow already carries the wander in the
        velocity and drawing the old curve on top would scatter the stream twice — and would put AR, which
        adds the same offset at draw time, up to 3 px away from the sprites it is measuring.
    """
    errors = []
    curve_path = ROOT / "Stage" / "WispMotion.cs"
    if not curve_path.exists():
        return ["Stage/WispMotion.cs is missing; the wisp sway would have no single home again"]
    consumers = {
        "Gpu/GpuStageSimulation.cs": 2,
        "Gpu/GpuArLayer.Effects.cs": 1,
        "Stage/PianoStage.cs": 1,
        "Diagnostics/ArParticleAlignmentReview.cs": 1,
    }
    for name, expected in consumers.items():
        path = ROOT / name
        if not path.exists():
            errors.append(f"{name} is missing, but it is one of the paths that draws a wisp")
            continue
        text = path.read_text(encoding="utf-8")
        found = _code(text).count("WispMotion.Offset")
        if found != expected:
            errors.append(f"{name}: expected {expected} call(s) to WispMotion.Offset, found {found} — every "
                          f"renderer of a wisp must draw the shared curve, not its own copy")
    # Any multiplication of a turbulence value by a literal is somebody's own sway — whatever the variable is
    # called and however the spaces fall. Passing the setting to WispMotion, dividing it by 100 to normalise it
    # or reading it through P() is not, so this keys on the `*` and not on the name alone.
    sway = re.compile(r"(?i)(?:\b\w*turbulence\b)\s*\*")
    for path in sorted(ROOT.rglob("*.cs")):
        if "obj" in path.parts or "bin" in path.parts or path == curve_path:
            continue
        rel = path.relative_to(ROOT).as_posix()
        for number, line in enumerate(_code(path.read_text(encoding="utf-8")).split("\n"), 1):
            if sway.search(line):
                errors.append(f"{rel}:{number}: multiplies a wisp turbulence value by a literal, which is a "
                              f"second copy of the sway; WispMotion owns that curve now")
    # W1 draws a coherent stream by *moving* the motes; the draw-time wander is exactly what made them
    # wave on their own phases, so it has to stand down while the flow runs. Gating it in one renderer and
    # forgetting another would put the scatter back on half the frame, which is worse than either look.
    for name in consumers:
        path = ROOT / name
        if not path.exists():
            continue
        lines = _code(path.read_text(encoding="utf-8")).split("\n")
        for number, line in enumerate(lines):
            if "WispMotion.Offset" not in line:
                continue
            context = ((lines[number - 1] if number else "") + line).replace(" ", "").replace("\t", "")
            if "ParticleFlow<=0" not in context:
                errors.append(f"{name}:{number + 1}: draws the wisp's draw-time sway without checking "
                              f"`ParticleFlow <= 0`; while the flow runs the wander is already in the "
                              f"velocity, so adding it here scatters the stream twice")
    # The software stage must not integrate the sway into velocity again: the same sine inside the wisp
    # update moves the mote faster in slow motion and re-opens the 10.6 px disagreement.
    stage = ROOT / "Stage" / "PianoStage.cs"
    if stage.exists():
        branch = re.search(r"if \(p\.Wisp\)\s*\{(.*?)\}", stage.read_text(encoding="utf-8"), re.S)
        if branch and "Math.Sin(p.Age" in branch.group(1):
            errors.append("Stage/PianoStage.cs: the software stage is integrating the wisp sway into velocity "
                          "again; it must be a draw-time offset, or slow motion moves the mote")
    return errors


def scan_stage_clock_contract():
    """Slow motion must scale the particles and nothing else, on both renderers.

    The GPU simulation has always done this: ``particleDt = frameDt * PhysicsTimeFactor`` feeds the particle
    integrator, while rings, flashes, live bars, key heat and the beat envelope read ``frameDt``. The software
    stage used one scaled clock for everything, so at any factor but 100 its live bars fell at a different
    speed from the engine's — the same song, a different animation. Nothing compared the two, so nothing went
    red; the check below is the cheap half of the replacement.

    Three facts, each one a way the split has already been (or could be) undone:
      * the software stage asks ``StageClock.Particle`` for its particle clock;
      * it does not feed that scaled clock to the things a player reads as real time — the held bar, the
        impact rings and the impact flash ages;
      * the GPU keeps the same division of labour, so the two renderers stay comparable.
    """
    errors = []
    stage_path = ROOT / "Stage" / "PianoStage.cs"
    gpu_path = ROOT / "Gpu" / "GpuStageSimulation.cs"
    for path in (stage_path, gpu_path):
        if not path.exists():
            return [f"{path.relative_to(ROOT).as_posix()} is missing; it decides which clock a frame runs on"]
    stage = _code(stage_path.read_text(encoding="utf-8"))
    if "StageClock.Particle(" not in stage:
        errors.append("Stage/PianoStage.cs: the software stage no longer asks StageClock for its particle clock, "
                      "so the slow-motion slider is scaling the whole stage again")
    for line in ("trail.Age += particleDt", "trail.HeldSeconds += particleDt",
                 "ring.Age += particleDt", "flash.Age += particleDt"):
        if line in stage:
            errors.append(f"Stage/PianoStage.cs: '{line}' puts a real-time effect on the particle clock; only "
                          f"the particles may slow down")
    gpu = _code(gpu_path.read_text(encoding="utf-8"))
    if "particleDt = frameDt * look.PhysicsTimeFactor" not in gpu:
        errors.append("Gpu/GpuStageSimulation.cs: the GPU no longer derives particleDt from the frame clock "
                      "and the look's factor, so StageClock.Particle mirrors a formula that has moved")
    return errors


def scan_flame_motion_contract():
    """The held-key flame must stay one effect, not two.

    The engine draws it as a stream of motes and always has; the software stage drew a plume pinned to the key
    top, so the same preset showed fire on a GPU machine and a stuck decal without one. ``Stage/FlameMotion.cs``
    now holds the engine's law and the fallback calls it.

    This is a drift detector, not a duplicate of the C# tests: if the engine's literals change, the shared
    helper has to change with them, and the failure message says which literal moved. It also refuses the old
    anchored plume's geometry, because that is the exact shape this replaced.
    """
    errors = []
    helper_path = ROOT / "Stage" / "FlameMotion.cs"
    stage_path = ROOT / "Stage" / "PianoStage.cs"
    gpu_path = ROOT / "Gpu" / "GpuStageSimulation.cs"
    for path in (helper_path, stage_path, gpu_path):
        if not path.exists():
            return [f"{path.relative_to(ROOT).as_posix()} is missing; it is part of the flame contract"]
    helper = _code(helper_path.read_text(encoding="utf-8"))
    stage = _code(stage_path.read_text(encoding="utf-8"))
    gpu = _code(gpu_path.read_text(encoding="utf-8"))
    for name in ("EmissionRate", "RiseSpeed", "LifeSeconds", "Size", "Sway", "Ramp"):
        if name not in helper:
            errors.append(f"Stage/FlameMotion.cs: the flame's {name} is gone, so the fallback and the engine no longer share a law")
        elif f"FlameMotion.{name}(" not in stage:
            errors.append(f"Stage/PianoStage.cs: nothing calls FlameMotion.{name}, so the frame being drawn is not the engine's flame")
    for literal, what in (
        ("70 * look.FlameIntensity * _heat[pitch]", "birth rate"),
        ("-(50 + 150 * look.FlameHeight)", "rise speed"),
        (".22f + look.FlameHeight * .45f", "lifetime"),
        ("layout.Lane * (.35f + Rand() * .3f)", "mote size"),
    ):
        if literal not in gpu:
            errors.append(f"Gpu/GpuStageSimulation.cs: the flame's {what} changed away from '{literal}'; update "
                          "Stage/FlameMotion.cs in the same commit or the two renderers drift apart again")
    if "keyTop - flameH * .42" in stage:
        errors.append("Stage/PianoStage.cs: the anchored flame plume is back (keyTop - flameH * .42); it is the shape "
                      "that never moves and it belongs to no renderer any more")
    # The W2 candidate (buoyant rise, drag, cooling swell) is one law with one home. If the engine grows its own
    # copy of the four numbers, the review clips stop measuring the same flame the software stage could adopt.
    for name in ("V3Buoyancy", "V3Drag", "V3Lift", "V3Swell", "StepV3"):
        if name not in helper:
            errors.append(f"Stage/FlameMotion.cs: the flame's W2 {name} is gone, so the motion candidate has no law")
    if "FlameMotion.StepV3(ref p.X" not in gpu:
        errors.append("Gpu/GpuStageSimulation.cs: the flame no longer steps through FlameMotion.StepV3, so the "
                      "review clips and the shared law have diverged")
    if "look.FlameMotionV3ForReview" not in gpu:
        errors.append("Gpu/GpuStageSimulation.cs: the W2 flame step lost its review flag; the accepted v2 captures "
                      "pin the constant-rise plume and must not move")
    if "p.Vx += MathF.Sin(p.Age * 11 + p.Phase) * 40 * particleDt;" not in gpu:
        errors.append("Gpu/GpuStageSimulation.cs: the shipped flame integrator changed; that path is pinned by the "
                      "accepted captures, so the candidate belongs in FlameMotion.StepV3 instead")
    return errors


def scan_particle_flow_contract():
    """W1's flow is one law, two integrators and one switch that defaults to off.

    The shipped wisps and flames are pinned by the accepted ``wisps-v3``/``flames-v3`` captures, so the flow
    may only run when the slider is raised: the engines keep their old arithmetic on the zero path, and both of
    them call ``Stage/ParticleFlow.cs`` on the other one. This check is a drift detector for that shape — a
    second copy of the wind in either renderer, or a setting nobody wired, is what it exists to catch.
    """
    errors = []
    helper_path = ROOT / "Stage" / "ParticleFlow.cs"
    stage_path = ROOT / "Stage" / "PianoStage.cs"
    gpu_path = ROOT / "Gpu" / "GpuStageSimulation.cs"
    settings_path = ROOT / "Stage" / "PianoVisualSettings.cs"
    look_path = ROOT / "Gpu" / "GpuLook.cs"
    dock_path = ROOT / "Ui" / "MainWindow.Settings.cs"
    for path in (helper_path, stage_path, gpu_path, settings_path, look_path, dock_path):
        if not path.exists():
            return [f"{path.relative_to(ROOT).as_posix()} is missing; it is part of the flow contract"]
    helper = _code(helper_path.read_text(encoding="utf-8"))
    stage = _code(stage_path.read_text(encoding="utf-8"))
    gpu = _code(gpu_path.read_text(encoding="utf-8"))
    settings = _code(settings_path.read_text(encoding="utf-8"))
    look = _code(look_path.read_text(encoding="utf-8"))
    dock = _code(dock_path.read_text(encoding="utf-8"))
    for name in ("EntrainSeconds", "VerticalShare", "DiffusionRate", "Wind(", "Curl(", "Diffusion(", "Step("):
        if name not in helper:
            errors.append(f"Stage/ParticleFlow.cs: {name} is gone, so the flow's four parts no longer live in one law")
    # Both renderers call the law; neither carries the numbers.
    # The engine steps a wisp and a flame through one branch; the software stage keeps its two lists apart, so
    # it has one call per list. Both counts are exact: a third call means a third copy of the law.
    for path, text, gate, calls in (("Gpu/GpuStageSimulation.cs", gpu, "look.ParticleFlow > 0", 1),
                                    ("Stage/PianoStage.cs", stage, "_visual.ParticleFlow > 0", 2)):
        if text.count("ParticleFlow.Step(") != calls:
            errors.append(f"{path}: expected exactly {calls} call(s) to ParticleFlow.Step, found {text.count('ParticleFlow.Step(')}")
        if gate not in text:
            errors.append(f"{path}: the flow step lost its '{gate}' gate, so raising the slider is not what turns it on")
        for name in ("EntrainSeconds", "VerticalShare", "DiffusionRate"):
            if name in text:
                errors.append(f"{path}: carries the flow's {name} as well as calling it; the constant belongs in Stage/ParticleFlow.cs")
        if "ParticleFlow.Turn" not in text:
            errors.append(f"{path}: the per-particle phase is no longer folded through ParticleFlow.Turn")
    if "public double ParticleFlow { get; set; }" not in settings:
        errors.append("Stage/PianoVisualSettings.cs: the Flow setting is gone")
    if "ParticleFlow = Math.Clamp(ParticleFlow, 0, 100);" not in settings:
        errors.append("Stage/PianoVisualSettings.cs: ParticleFlow is not clamped with its own range")
    if "ParticleFlow = P(s.ParticleFlow)" not in look:
        errors.append("Gpu/GpuLook.cs: the engine never receives the flow slider")
    if "nameof(PianoVisualSettings.ParticleFlow)" not in dock:
        errors.append("Ui/MainWindow.Settings.cs: no row drives the flow slider")
    return errors


def scan_note_width_parity():
    """One bar, one slider: the held-key bar and the falling note must take ``Note width`` as it is.

    The slider runs 30-100. The software stage's held bar used to floor it at 60%, so a 30% preset drew a bar
    twice as wide as its own falling notes - and twice as wide as the same preset on a GPU, where the engine has
    always read the slider. ``NoteGeometry.BarWidth`` is now the one law for that slider; this scan fails if the
    floor comes back, if either software path stops going through the helper, or if the engine stops reading the
    same slider.
    """
    errors = []
    geometry_path = ROOT / "Stage" / "NoteGeometry.cs"
    stage_path = ROOT / "Stage" / "PianoStage.cs"
    gpu_path = ROOT / "Gpu" / "GpuStageSimulation.cs"
    look_path = ROOT / "Gpu" / "GpuLook.cs"
    for path in (geometry_path, stage_path, gpu_path, look_path):
        if not path.exists():
            return [f"{path.relative_to(ROOT).as_posix()} is missing; it is part of the note-width contract"]
    geometry = _code(geometry_path.read_text(encoding="utf-8"))
    stage = _code(stage_path.read_text(encoding="utf-8"))
    gpu = _code(gpu_path.read_text(encoding="utf-8"))
    look = _code(look_path.read_text(encoding="utf-8"))
    if "internal static double BarWidth(double lane, double noteWidthPercent, double blackKeyWidth)" not in geometry:
        errors.append("Stage/NoteGeometry.cs: BarWidth is gone, so the two software note paths no longer share a law")
    if stage.count("NoteGeometry.BarWidth(lane, _visual.NoteWidth,") != 2:
        errors.append("Stage/PianoStage.cs: the falling note and the held bar must both take NoteWidth through "
                      "NoteGeometry.BarWidth (exactly two call sites)")
    if "Math.Max(_visual.NoteWidth" in stage:
        errors.append("Stage/PianoStage.cs: the held bar's 60% floor is back; it makes a thin preset draw a wide "
                      "bar on the software stage and a thin one on the engine")
    if "NoteGeometry.Width(lane, _visual.NoteWidth" in stage:
        errors.append("Stage/PianoStage.cs: a note path passes the raw slider as a fraction again; use BarWidth")
    if "NoteGeometry.Width(layout.Lane, look.NoteWidth," not in gpu:
        errors.append("Gpu/GpuStageSimulation.cs: the engine stopped sizing its notes from look.NoteWidth")
    if "NoteWidth = P(s.NoteWidth)" not in look:
        errors.append("Gpu/GpuLook.cs: NoteWidth is no longer the slider divided by 100, so the engine and the "
                      "software stage disagree about the unit")
    return errors


def scan_held_emission_parity():
    """A held key must let off the same amount on both renderers.

    The engine births ``ParticleAmount * 1.3`` embers and ``WispAmount * 4`` wisps a second for every sounding
    key. The software stage had no sustained-ember channel and birthed wisps at ``WispAmount`` — a quarter of
    the engine's — so one preset showed two twisting smoke strands and a stream of embers on a GPU machine and a
    thin trickle on a bare key without one. ``Stage/HeldEmission.cs`` now holds both rates.

    The engine keeps its own float expressions so the frames pinned by the particle evidence stay identical;
    this check makes sure nobody changes those numbers without changing the shared helper, and that the fallback
    still asks the helper instead of inventing a rate. ``EffectBudget`` already allows the two stages different
    ceilings — that is a frame-time budget, not a smaller rate.
    """
    errors = []
    helper_path = ROOT / "Stage" / "HeldEmission.cs"
    stage_path = ROOT / "Stage" / "PianoStage.cs"
    gpu_path = ROOT / "Gpu" / "GpuStageSimulation.cs"
    for path in (helper_path, stage_path, gpu_path):
        if not path.exists():
            return [f"{path.relative_to(ROOT).as_posix()} is missing; it is part of the held-emission contract"]
    stage = _code(stage_path.read_text(encoding="utf-8"))
    gpu = _code(gpu_path.read_text(encoding="utf-8"))
    for name in ("Wisps", "Sparks"):
        if f"HeldEmission.{name}(" not in stage:
            errors.append(f"Stage/PianoStage.cs: nothing calls HeldEmission.{name}, so the fallback is not emitting "
                          f"the engine's {name.lower()} rate")
    for literal, what in (
        ("look.WispAmount * 4f", "wisp density"),
        ("look.ParticleAmount * 1.3f", "sustained-ember density"),
    ):
        if literal not in gpu:
            errors.append(f"Gpu/GpuStageSimulation.cs: the held emitter's {what} changed away from '{literal}'; update "
                          "Stage/HeldEmission.cs in the same commit or one renderer will emit less than the other")
    for stale, what in (
        ("_wispBudget[pitch] += _visual.WispAmount", "wisp budget that ignores the x4 rate"),
        ("(int)_wispBudget[pitch]", "hand-rolled wisp credit instead of KeyEmissionBudget"),
    ):
        if stale in stage:
            errors.append(f"Stage/PianoStage.cs: the old {what} is back; sustained emission goes through "
                          "KeyEmissionBudget.Take with a HeldEmission rate")
    if "KeyEmissionBudget.Take(ref _sparkBudget[pitch]" not in stage:
        errors.append("Stage/PianoStage.cs: the sustained-ember channel is gone again; a held key must stream the "
                      "selected burst family on the software stage too")
    return errors


# The eleven settings strings that are a user-visible choice in both renderers. The value lists live in
# PianoVisualSettings; the option matrix (Diagnostics/OptionMatrixPlan.cs) copies them so a Windows capture run
# can walk them without WPF. This table is the only place that knows the two are the same list.
NUMERIC_OPTION_SETTINGS = {
    "NoteWidth": "the width of a note and of a held bar",
    "NoteFallSpeed": "how fast a note falls",
    "FallingTrailIntensity": "how bright a falling trail is",
    "ImpactWaveIntensity": "how bright a wave is",
    "RingSize": "how far a wave grows",
    "ParticleAmount": "how many particles a hit emits",
    "ImpactFlashIntensity": "how bright a hit flash is",
    "ReleaseIntensity": "how strong a release effect is",
}

OPTION_MATRIX_FAMILIES = {
    "NoteStyle": "NoteStyles",
    "FallingTrail": "FallingTrails",
    "ImpactWave": "ImpactWaves",
    "ImpactBurst": "ImpactBursts",
    "ImpactMorph": "ImpactMorphs",
    "ImpactFlashStyle": "ImpactFlashStyles",
    "ReleaseEffect": "ReleaseEffects",
    "AmbientEnergy": "AmbientEnergies",
    "AmbientNature": "AmbientNatures",
    "AmbientLight": "AmbientLights",
    "AmbientCosmic": "AmbientCosmics",
}


def scan_preset_roundtrip():
    """A saved look has to be the look that is loaded again.

    ``PresetRoundTripReview`` applies every shipped preset through the dock's own path, writes it with the
    product's writer, reads it back with the product's reader and asks both renderers for the same frame before
    and after. That is only a claim about the product while it keeps walking the product's list and the
    product's file path: this check fails when the review hardcodes a preset list, stops using
    ``WritePresetFile``/``ReadPresetFile``, drops the property comparison, or when the dock's apply path grows a
    step the review does not copy.
    """
    errors = []
    paths = {
        "review": ROOT / "Diagnostics" / "PresetRoundTripReview.cs",
        "suite": ROOT / "Diagnostics" / "VerificationSuite.cs",
        "ui": ROOT / "Ui" / "MainWindow.Settings.cs",
        "settings": ROOT / "Stage" / "PianoVisualSettings.cs",
        "presets": ROOT / "Stage" / "VisualPresets.cs",
    }
    for name, path in paths.items():
        if not path.exists():
            return [f"{path.relative_to(ROOT).as_posix()} is missing; the preset round trip needs it"]
    review = _code(paths["review"].read_text(encoding="utf-8"))
    suite = _code(paths["suite"].read_text(encoding="utf-8"))
    ui = _code(paths["ui"].read_text(encoding="utf-8"))
    settings = _code(paths["settings"].read_text(encoding="utf-8"))
    presets = _code(paths["presets"].read_text(encoding="utf-8"))

    for required, why in (
        ("VisualPresets.BuiltIn", "the review stopped walking the built-in looks"),
        ("CommunityPresets.All", "the review stopped walking the community shelf"),
        ("VisualPresetStore.WritePresetFile(", "the review no longer saves through the product's preset writer"),
        ("VisualPresetStore.ReadPresetFile(", "the review no longer reads through the product's preset reader"),
        ("CopyFrom(item.Settings, keepBackgroundImage: true)", "the review no longer applies a preset the way the dock does"),
        ("typeof(PianoVisualSettings).GetProperties()", "the review compares a hand-written list instead of every property"),
        ("ExpectedPresets = 20", "the review no longer pins the number of shipped presets"),
    ):
        if required not in review:
            errors.append(f"Diagnostics/PresetRoundTripReview.cs: {why}")
    # Both failure modes have to stay failures: a changed byte and a changed property. The frame half is an
    # exact comparison (tolerance 0 plus a byte compare), so a loosened tolerance would quietly weaken the claim
    # the suite prints while the docs still say "byte-identical".
    if "OptionMatrixPlan.ChangedPixels(a, b, tolerance: 0)" not in review or "SequenceEqual(b)" not in review:
        errors.append("Diagnostics/PresetRoundTripReview.cs no longer compares the two frames byte for byte; "
                      "'the same picture' after a save/load has to mean the same bytes, not the option matrix's "
                      "2/255 tolerance")
    if "if (changed != 0" not in review:
        errors.append("Diagnostics/PresetRoundTripReview.cs no longer fails on a single changed pixel after the round trip")
    # The file half and the dock half are two different claims: what came back from the file, and what the
    # player's settings looked like after the dock applied it.
    for required, why in (
        ('"after the preset file", before, stored.Settings', "the review stopped comparing the settings read back from the file"),
        ("\"through the dock's apply path\", before, after", "the review stopped comparing the dock's applied settings"),
    ):
        if required not in review:
            errors.append(f"Diagnostics/PresetRoundTripReview.cs: {why}")
    if "came back as" not in review:
        errors.append("Diagnostics/PresetRoundTripReview.cs no longer names the property that changed, so a failure "
                      "would only say that something moved")
    if "WritePresetFile(before, null, item.Description)" not in review:
        errors.append("Diagnostics/PresetRoundTripReview.cs no longer writes the preset's own description, so the "
                      "envelope would not be exercised")

    # The dock's apply path: three lines the review copies. If the product grows a fourth, the review has to
    # grow it too, or it would be testing a path the user never takes.
    for line, what in (
        ("_visualSettings.CopyFrom(preset.Settings, keepBackgroundImage: true);", "the preset copy"),
        ("_visualSettings.PresetName = preset.Name; _visualSettings.PresetModified = false;", "the name/modified reset"),
        ('if (_visualSettings.BackgroundMode == "Image" && string.IsNullOrWhiteSpace(_visualSettings.BackgroundImagePath)) _visualSettings.BackgroundMode = "Solid";',
         "the image-background guard"),
    ):
        if line not in ui:
            errors.append(f"Ui/MainWindow.Settings.cs: {what} in ApplyPreset changed; mirror it in "
                          "Diagnostics/PresetRoundTripReview.cs so the review keeps testing the path a click takes")
    if "settings.ApplyMigrations();" not in settings or "settings.Clamp();" not in settings:
        errors.append("Stage/PianoVisualSettings.cs: FromJson no longer migrates and clamps what it reads, so the "
                      "round-trip review would no longer cover the load path")
    if "internal static PresetFileContent ReadPresetFile(string json)" not in presets:
        errors.append("Stage/VisualPresets.cs: ReadPresetFile is gone or renamed; the review reads through it")
    if '"--verify-preset-roundtrip-only"' not in suite:
        errors.append("Diagnostics/VerificationSuite.cs lost the --verify-preset-roundtrip-only route")
    if "PresetRoundTripReview.Render(" not in suite:
        errors.append("Diagnostics/VerificationSuite.cs does not call PresetRoundTripReview.Render, so the preset "
                      "round trip never runs")
    if "result.Presets == 20" not in suite:
        errors.append("Diagnostics/VerificationSuite.cs no longer asserts twenty shipped presets")
    return errors


def scan_option_numeric():
    """A slider has no list, so its plan has to come from the dock and the settings class.

    ``OptionNumericPlan`` names nine numeric axes, three values each, and the review fails when two of those
    values paint the same picture on either renderer. That claim is only worth something while the plan's range
    is the product's range: this check reads every endpoint out of the dock's ``SliderRow`` call and the
    settings class's ``Clamp`` and compares it with the plan. A slider whose range moves, or an axis added to the
    plan without a slider, fails here on Linux before Windows renders a frame.
    """
    errors = []
    paths = {
        "plan": ROOT / "Diagnostics" / "OptionNumericPlan.cs",
        "review": ROOT / "Diagnostics" / "OptionNumericReview.cs",
        "settings": ROOT / "Stage" / "PianoVisualSettings.cs",
        "ui": ROOT / "Ui" / "MainWindow.Settings.cs",
        "suite": ROOT / "Diagnostics" / "VerificationSuite.cs",
    }
    for name, path in paths.items():
        if not path.exists():
            return [f"{path.relative_to(ROOT).as_posix()} is missing; the numeric option review needs it"]
    plan = _code(paths["plan"].read_text(encoding="utf-8"))
    review = _code(paths["review"].read_text(encoding="utf-8"))
    settings = _code(paths["settings"].read_text(encoding="utf-8"))
    ui = _code(paths["ui"].read_text(encoding="utf-8"))
    suite = _code(paths["suite"].read_text(encoding="utf-8"))

    number = r"(-?\d+(?:\.\d+)?)"
    axes = re.findall(
        r'new Axis\(\"([^\"]+)\",\s*\"([^\"]+)\",\s*\"([^\"]+)\",\s*\"([^\"]+)\",\s*OptionMatrixPlan\.Scene\.(\w+),\s*'
        + number + r",\s*" + number + r",\s*\[(.*?)\]\)",
        plan, re.S)
    if len(axes) != 9:
        errors.append(f"Diagnostics/OptionNumericPlan.cs must declare nine numeric axes; it declares {len(axes)}")
    exempt_match = re.search(
        r"EndpointExempt\s*\{\s*get;\s*\}\s*=\s*new HashSet<string>\(StringComparer\.Ordinal\)\s*\{([^}]*)\}", plan)
    exempt = set(re.findall(r'\"([^\"]+)\"', exempt_match.group(1))) if exempt_match else set()
    if not exempt:
        errors.append("Diagnostics/OptionNumericPlan.cs lost its EndpointExempt set; the endpoint rule below has no "
                      "way to state which axis may skip an end of its slider")
    seen = set()
    for axis, setting, family, family_value, scene, low, high, values in axes:
        if axis in seen:
            errors.append(f"Diagnostics/OptionNumericPlan.cs declares the axis {axis!r} twice")
        seen.add(axis)
        if setting not in NUMERIC_OPTION_SETTINGS:
            errors.append(f"Diagnostics/OptionNumericPlan.cs axis {axis!r} names {setting!r}, which is not one of the "
                          "numeric settings the dock exposes")
            continue
        slider = re.search(rf'SliderRow\([^;]*?nameof\(PianoVisualSettings\.{setting}\),\s*{number},\s*{number}', ui, re.S)
        if not slider:
            errors.append(f"Ui/MainWindow.Settings.cs no longer offers a slider for {setting}; the numeric plan would "
                          "claim a range the dock never shows")
        else:
            declared_low, declared_high = slider.group(1), slider.group(2)
            if float(declared_low) != float(low) or float(declared_high) != float(high):
                errors.append(f"Diagnostics/OptionNumericPlan.cs captures {setting} over {low}..{high}, but the dock's "
                              f"slider is {declared_low}..{declared_high}; move the plan with the slider")
        clamp = re.search(rf"{setting}\s*=\s*Math\.Clamp\({setting},\s*{number},\s*{number}\)", settings)
        if not clamp:
            errors.append(f"Stage/PianoVisualSettings.cs no longer clamps {setting}; the plan's range has no product "
                          "law behind it")
        elif float(clamp.group(1)) > float(low) or float(clamp.group(2)) < float(high):
            errors.append(f"Stage/PianoVisualSettings.cs clamps {setting} to {clamp.group(1)}..{clamp.group(2)}, which "
                          f"is narrower than the plan's {low}..{high}; the capture would be filed under a value the "
                          "product moved")
        points = [float(point) for point in re.findall(number, values)]
        if len(points) != 3 or points != sorted(set(points)):
            errors.append(f"Diagnostics/OptionNumericPlan.cs axis {axis!r} needs three ascending distinct values; it has {points}")
        if points and (min(points) < float(low) - 1e-9 or max(points) > float(high) + 1e-9):
            errors.append(f"Diagnostics/OptionNumericPlan.cs axis {axis!r} shoots {points}, outside its own {low}..{high}")
        # An axis may skip an endpoint only if the plan's own EndpointExempt set says so, and the exemption has to
        # be honest in both directions: an exempt axis really skips one, a non-exempt one really takes one.
        at_endpoint = any(abs(point - float(low)) < 1e-9 or abs(point - float(high)) < 1e-9 for point in points)
        if axis in exempt and at_endpoint:
            errors.append(f"Diagnostics/OptionNumericPlan.cs exempts {axis!r} from the endpoint rule, but it samples "
                          "an endpoint anyway; the exemption has gone stale")
        if axis not in exempt and not at_endpoint:
            errors.append(f"Diagnostics/OptionNumericPlan.cs axis {axis!r} never samples an endpoint; a clamp at the "
                          "end of the range would stay invisible")
        # The arrangement family must exist and offer the value, so the numeric scene is a pinned one.
        if f'new Family("{family}",' not in plan and f'"{family}"' not in plan:
            errors.append(f"Diagnostics/OptionNumericPlan.cs axis {axis!r} arranges itself through unknown family {family!r}")
        if f'"{family_value}"' not in plan:
            errors.append(f"Diagnostics/OptionNumericPlan.cs axis {axis!r} arranges through {family_value!r}, which it never names")

    for axis in exempt - {declared[0] for declared in axes}:
        errors.append(f"Diagnostics/OptionNumericPlan.cs exempts unknown axis {axis!r} from the endpoint rule")

    for contract in (
        ("OptionMatrixReview.CaptureWpf(", "the numeric review stopped using the flat matrix's WPF render path"),
        ("OptionMatrixReview.CaptureGpu(", "the numeric review stopped using the flat matrix's engine render path"),
        ("settings.Clamp()", "the numeric review no longer clamps before reading the value back"),
        ("Math.Abs(drawn - value)", "the numeric review no longer proves it drew the value it files the capture under"),
        ("MinimumChangedPixels", "the numeric review lost its pixel floor"),
    ):
        if contract[0] not in review:
            errors.append(f"Diagnostics/OptionNumericReview.cs: {contract[1]}")
    if '"--verify-option-numeric-only"' not in suite:
        errors.append("Diagnostics/VerificationSuite.cs lost the --verify-option-numeric-only route")
    if "OptionNumericReview.Render(" not in suite:
        errors.append("Diagnostics/VerificationSuite.cs does not call OptionNumericReview.Render, so the numeric review never runs")
    if "Captures == 54" not in suite:
        errors.append("Diagnostics/VerificationSuite.cs no longer asserts 54 numeric captures; nine axes at three "
                      "values on two renderers is the whole plan")
    if "OptionMatrixPlan.Scene.Held" not in _code((ROOT / "Diagnostics" / "OptionMatrixReview.cs").read_text(encoding="utf-8")):
        errors.append("Diagnostics/OptionMatrixReview.cs lost the held scene's press, so the bar-width axis would "
                      "capture a stage with nothing held")
    return errors


def scan_review_sheets():
    """The contact sheet is the part of a review that is committed, so it has to be the whole review.

    ``Diagnostics/ReviewSheet.cs`` lays the capture buffers out in one grid and, because it is plain arithmetic
    over a byte array, ``ReviewSheetTests`` pins it on Linux. This check keeps the pieces around it honest: the
    composer stays linked into the test project (or those checks silently stop running), each of the four N12
    reviews composes a canvas, refuses a page that is missing captures, and writes the file the workflow copies,
    and the workflow still copies those files into ``docs/evidence``.
    """
    errors = []
    composer = ROOT / "Diagnostics" / "ReviewSheet.cs"
    project = ROOT / "tests" / "PianoPath.Tests" / "PianoPath.Tests.csproj"
    workflow = ROOT / ".github" / "workflows" / "build.yml"
    for path in (composer, project, workflow):
        if not path.exists():
            return [f"{path.relative_to(ROOT).as_posix()} is missing; the N12 contact sheets need it"]
    if "Diagnostics\\ReviewSheet.cs" not in project.read_text(encoding="utf-8"):
        errors.append("tests/PianoPath.Tests/PianoPath.Tests.csproj no longer links Diagnostics/ReviewSheet.cs; "
                      "the sheet's layout, downscale and alpha handling are only checked while it compiles there")
    reviews = {
        "OptionMatrixReview.cs": "option-matrix/sheet-",
        "OptionNumericReview.cs": "option-numeric/sheet.png",
        "OptionPoseReview.cs": "option-pose/sheet-",
        "PresetRoundTripReview.cs": "preset-roundtrip/sheet.png",
        "RefractionReview.cs": "refraction/sheet-",
        "ArMaterialReview.cs": "ar-material/sheet.png",
    }
    for name, page in reviews.items():
        path = ROOT / "Diagnostics" / name
        if not path.exists():
            errors.append(f"Diagnostics/{name} is missing; the N12 contact sheets need it")
            continue
        code = _code(path.read_text(encoding="utf-8"))
        if "new ReviewSheet.Canvas(" not in code:
            errors.append(f"Diagnostics/{name} no longer composes a contact sheet; its pictures would only exist "
                          "inside the run's artifact again")
        # Either spelling counts: the N12 reviews compare the page's tile count with the captures they wrote,
        # the N8 review compares it through the canvas it filled. Both refuse a page that lost a picture.
        if "sheet.Tiles !=" not in code and "Count != tiles" not in code and "sheet.Finish()" not in code:
            errors.append(f"Diagnostics/{name} no longer refuses a sheet that is missing captures")
        if page not in code:
            errors.append(f"Diagnostics/{name} no longer writes '{page}', which is what gets committed")
    workflow_text = workflow.read_text(encoding="utf-8")
    for needed in ("docs/evidence/n12-sheets", "option-matrix/sheet-wpf.png", "option-matrix/sheet-gpu-warp.png",
                   "option-numeric/sheet.png", "preset-roundtrip/sheet.png", "option-pose/sheet-*.png",
                   "docs/evidence/refraction-sheets", "refraction/sheet-wpf.png", "refraction/sheet-gpu-warp.png",
                   "docs/evidence/ar-material-sheets", "ar-material/sheet.png"):
        if needed not in workflow_text:
            errors.append(f".github/workflows/build.yml no longer copies '{needed}' into docs/evidence, so that "
                          "page would only exist inside the run's artifact")
    if "git add docs/evidence/n12-sheets docs/evidence/refraction-sheets docs/evidence/ar-material-sheets" not in workflow_text:
        errors.append(".github/workflows/build.yml no longer commits both evidence folders; a sheet the stage step "
                      "copies but the save step forgets is deleted with the runner")
    return errors


def scan_option_pose():
    """The AR/pose slice has to keep shooting the option surface the overlay can draw.

    ``OptionPoseReview`` renders each axis twice — off and on — through three calibrated poses and fails when the
    two frames differ by fewer than twelve pixels. That is only a claim about the product while the rows are
    product values in two disjoint parts: this check reads the plan, keeps part 1 at five effect axes and part 2
    at the two song-note axes plus four ambient layers, refuses a family in both parts or twice in one, and
    keeps the capture count equal to three poses times the rows. It also keeps the review on the shared capture
    driver and the suite on the route, so a family deleted from the plan cannot silently shrink the review.
    """
    errors = []
    paths = {
        "plan": ROOT / "Diagnostics" / "OptionMatrixPlan.cs",
        "review": ROOT / "Diagnostics" / "OptionPoseReview.cs",
        "suite": ROOT / "Diagnostics" / "VerificationSuite.cs",
        "held": ROOT / "Diagnostics" / "HeldParticleControlPlan.cs",
    }
    for name, path in paths.items():
        if not path.exists():
            return [f"{path.relative_to(ROOT).as_posix()} is missing; the AR/pose slice needs it"]
    plan = _code(paths["plan"].read_text(encoding="utf-8"))
    review = _code(paths["review"].read_text(encoding="utf-8"))
    suite = _code(paths["suite"].read_text(encoding="utf-8"))
    held = _code(paths["held"].read_text(encoding="utf-8"))

    # The families and their values, exactly as the flat matrix declares them.
    declared = re.findall(
        r'new Family\("([^\"]+)",\s*"([^\"]+)",\s*Scene\.(\w+),\s*\[(.*?)\],\s*"([^\"]+)"(?:,\s*"([^\"]+)")?\)',
        plan, re.S)
    families = {family_id: (setting, re.findall(r'"([^"]*)"', values), neutral)
                for family_id, setting, _, values, neutral, _ in declared}

    def rows(name, expected, what, one_per_family):
        block = re.search(rf"{name}\s*\{{\s*get;\s*\}}\s*=\s*Array\.AsReadOnly\(new\[\]\s*\{{(.*?)\}}\);", plan, re.S)
        if not block:
            return None, [f"Diagnostics/OptionMatrixPlan.cs no longer declares {name}; {what}"]
        found = re.findall(r'\("([^\"]+)",\s*"([^\"]+)"\)', block.group(1))
        problems = []
        if len(found) != expected:
            problems.append(f"Diagnostics/OptionMatrixPlan.cs {name} declares {len(found)} rows; {what} needs {expected}")
        seen, families_seen = set(), set()
        for family_id, value in found:
            if (family_id, value) in seen:
                problems.append(f"Diagnostics/OptionMatrixPlan.cs {name} shoots {family_id}/{value!r} twice")
            seen.add((family_id, value))
            if one_per_family and family_id in families_seen:
                problems.append(f"Diagnostics/OptionMatrixPlan.cs {name} names the family {family_id!r} twice; part 2 "
                                "shoots one value per family, so a second row would be a duplicate")
            families_seen.add(family_id)
            if family_id not in families:
                problems.append(f"Diagnostics/OptionMatrixPlan.cs {name} names unknown family {family_id!r}")
                continue
            setting, values, neutral = families[family_id]
            if value not in values:
                problems.append(f"Diagnostics/OptionMatrixPlan.cs {name} shoots {family_id}/{value!r}, which "
                                f"PianoVisualSettings.{setting} does not offer")
            if value == neutral:
                problems.append(f"Diagnostics/OptionMatrixPlan.cs {name} shoots {family_id} at its neutral value "
                                f"{neutral!r}; the two captures would be the same picture by construction")
        return found, problems

    # Part 1 deliberately shoots two channels of one family (the morph particles and its flash), both against
    # the same off state, so the one-row-per-family rule belongs to part 2 only.
    part_one, problems = rows("PoseSelections", 5, "part 1 (the five effect axes drawn into the camera image)", False)
    errors.extend(problems)
    part_two, problems = rows("PoseSliceSelections", 6, "part 2 (the note material, the trail and the four ambient layers)", True)
    errors.extend(problems)
    if part_one and part_two:
        overlap = {family_id for family_id, _ in part_one} & {family_id for family_id, _ in part_two}
        if overlap:
            errors.append(f"Diagnostics/OptionMatrixPlan.cs shoots {sorted(overlap)} in both AR parts; part 2 is the "
                          "rest of the option surface, not a second look at part 1")
        expected_settings = {"NoteStyle", "FallingTrail", "AmbientEnergy", "AmbientNature", "AmbientLight", "AmbientCosmic"}
        covered = {families[family_id][0] for family_id, _ in part_two if family_id in families}
        if covered != expected_settings:
            errors.append(f"Diagnostics/OptionMatrixPlan.cs part 2 covers {sorted(covered)}; it has to cover exactly "
                          f"{sorted(expected_settings)} — the two song-note axes and the four ambient layers")

    # The review: three calibrated poses, the shared floor and count, and one capture driver.
    if "HeldParticleControlPlan.Views.Where(view => view.OverCamera)" not in review or "poses.Length != 3" not in review:
        errors.append("Diagnostics/OptionPoseReview.cs no longer requires the three calibrated poses from "
                      "HeldParticleControlPlan.Views")
    if "internal static IReadOnlyList<View> Views { get; }" not in held:
        errors.append("Diagnostics/HeldParticleControlPlan.cs no longer publishes the calibrated views the AR/pose "
                      "slice shoots; a new fixture would be an unreviewed pose")
    floor = re.search(r"MinimumChangedPixels = (\d+)", review)
    if not floor or floor.group(1) != "12":
        errors.append("Diagnostics/OptionPoseReview.cs no longer keeps the 12-pixel floor of the flat matrix; a "
                      "stray pixel is not a drawn option")
    counts = {name: re.search(rf"private const int {name} = ([^;]+);", review) for name in ("ExpectedPart1", "ExpectedPart2")}
    if not all(counts.values()):
        errors.append("Diagnostics/OptionPoseReview.cs lost its per-part capture counts; a missing pose or family "
                      "would have to name itself")
    else:
        if counts["ExpectedPart1"].group(1).strip() != "27":
            errors.append("Diagnostics/OptionPoseReview.cs part 1 is no longer the 27 captures of three poses times "
                          "(four off states and five values); update it with the docs and the suite")
        if counts["ExpectedPart2"].group(1).strip() != "36":
            errors.append("Diagnostics/OptionPoseReview.cs part 2 is no longer the 36 captures of three poses times "
                          "six families off and on; update it with the docs and the suite")
    if part_one and part_two:
        rows_per_pose = len({family_id for family_id, _ in part_one}) + len(part_one) + 2 * len(part_two)
        if 3 * rows_per_pose != 63:
            errors.append(f"Diagnostics/OptionPoseReview.cs would write {3 * rows_per_pose} captures for the rows it "
                          "shoots, not the 63 the suite asserts")
    if "OptionMatrixReview.Settings(" not in review or "OptionMatrixPlan.ChangedPixels(" not in review:
        errors.append("Diagnostics/OptionPoseReview.cs no longer shares the flat matrix's settings builder and pixel "
                      "comparison; two copies would describe two different frames")
    if "GpuStageRenderer.Create(forceWarp: true)" not in review or "!renderer.IsWarp" not in review:
        errors.append("Diagnostics/OptionPoseReview.cs no longer pins itself to WARP; the artifact would claim "
                      "hardware it never drew on")
    if '"--verify-option-pose-only"' not in suite or "OptionPoseReview.Render(" not in suite:
        errors.append("Diagnostics/VerificationSuite.cs lost the --verify-option-pose-only route or stopped calling "
                      "OptionPoseReview.Render")
    if "captures == 63" not in suite:
        errors.append("Diagnostics/VerificationSuite.cs no longer asserts the 63 AR/pose captures")
    for name in ("README.md", "README.en.md"):
        readme = ROOT / name
        if readme.exists() and "63 captures" not in readme.read_text(encoding="utf-8"):
            errors.append(f"{name}: the --verify-option-pose-only row no longer states the 63 captures, so the "
                          "documented size of the AR/pose slice has drifted from the review")
    return errors

def scan_option_matrix():
    """Every option the settings offer is a picture, not only a word in a combo box.

    The audit could say "chưa test riêng option X" for thirty-odd catalog rows because the suite exercised one
    chosen style per family. ``--verify`` now walks the whole axis twice (WPF and D3D/WARP) at one fixed scene
    and fails when a value draws nothing or two values paint the same frame. That claim is only worth anything
    while the captured list is the settings list: this check keeps the plan's literal values equal, in order,
    to the arrays the settings class validates against, and keeps every family wired from the plan through the
    capture driver into the suite. Add a style to the product and this check fails until it has a capture.
    """
    errors = []
    paths = {
        "settings": ROOT / "Stage" / "PianoVisualSettings.cs",
        "plan": ROOT / "Diagnostics" / "OptionMatrixPlan.cs",
        "review": ROOT / "Diagnostics" / "OptionMatrixReview.cs",
        "suite": ROOT / "Diagnostics" / "VerificationSuite.cs",
    }
    for name, path in paths.items():
        if not path.exists():
            return [f"{path.relative_to(ROOT).as_posix()} is missing; the option matrix needs it"]
    settings = _code(paths["settings"].read_text(encoding="utf-8"))
    plan = _code(paths["plan"].read_text(encoding="utf-8"))
    review = _code(paths["review"].read_text(encoding="utf-8"))
    suite = _code(paths["suite"].read_text(encoding="utf-8"))

    offered = {}
    for setting, array in OPTION_MATRIX_FAMILIES.items():
        found = re.search(rf"string\[\] {array} = \[(.*?)\];", settings)
        if not found:
            errors.append(f"Stage/PianoVisualSettings.cs no longer declares {array}; the option matrix cannot know "
                          f"which values {setting} accepts")
            continue
        offered[setting] = re.findall(r'\"([^\"]*)\"', found.group(1))

    declared = re.findall(
        r'new Family\("([^\"]+)",\s*"([^\"]+)",\s*Scene\.(\w+),\s*\[(.*?)\],\s*"([^\"]+)"(?:,\s*"([^\"]+)")?\)',
        plan, re.S)
    names = [setting for _, setting, _, _, _, _ in declared]
    if len(declared) != len(OPTION_MATRIX_FAMILIES) or len(set(names)) != len(names):
        errors.append("Diagnostics/OptionMatrixPlan.cs must declare exactly one family per settings axis "
                      f"({', '.join(OPTION_MATRIX_FAMILIES)}); it declares {names or 'none'}")
    for family_id, setting, scene, values, neutral, gate in declared:
        if setting not in OPTION_MATRIX_FAMILIES:
            errors.append(f"Diagnostics/OptionMatrixPlan.cs family {family_id!r} names {setting!r}, which is not one "
                          "of the eleven option axes the matrix verifies")
            continue
        planned = re.findall(r'\"([^\"]*)\"', values)
        if setting in offered and planned != offered[setting]:
            errors.append(f"Diagnostics/OptionMatrixPlan.cs captures {setting} as {planned}, but "
                          f"PianoVisualSettings.{OPTION_MATRIX_FAMILIES[setting]} offers {offered[setting]}; a new or "
                          "renamed option needs a capture in the same commit")
        if neutral not in planned:
            errors.append(f"Diagnostics/OptionMatrixPlan.cs family {family_id!r} keeps the neutral value {neutral!r}, "
                          "which is not one of its own values")
        if gate and setting not in ("ImpactBurst", "ImpactFlashStyle"):
            errors.append(f"Diagnostics/OptionMatrixPlan.cs gives {setting} a visibility gate; only the families with "
                          "no off value (burst, flash) need one")

    for setting in OPTION_MATRIX_FAMILIES:
        if f'case "{setting}":' not in review:
            errors.append(f"Diagnostics/OptionMatrixReview.cs never applies {setting} to the settings it renders; the "
                          "family would be captured as its baseline")
    if "OptionMatrixPlan.Families" not in review or "OptionMatrixPlan.CapturedValues(" not in review:
        errors.append("Diagnostics/OptionMatrixReview.cs no longer walks the plan; it would capture a hardcoded list")
    if "OptionMatrixReview.Render(" not in suite:
        errors.append("Diagnostics/VerificationSuite.cs does not call OptionMatrixReview.Render, so the matrix never runs")
    if '"--verify-option-matrix-only"' not in suite:
        errors.append("Diagnostics/VerificationSuite.cs lost the --verify-option-matrix-only route")
    errors.extend(scan_effect_channel_ownership())
    return errors


def scan_audit_matrix_claims():
    """The audit may not still call an option untested once it has a picture.

    §4.1–§4.8 of ``docs/EFFECT-COVERAGE-AUDIT.md`` was written before N12(b) existed, so fifty-odd rows said
    "chưa test riêng option X" for choices the matrix had since captured. The fix is prose, and prose rots:
    add a style, capture it, and the row can drift back. This check reads the plan the renderer actually walks
    (never a second hand-written list) and holds both directions of the claim:

    * a row naming a ``Setting="Value"`` the matrix captures must say ``N12(b)``;
    * a row saying ``N12(b)`` must name a value on a matrix axis at all, so the claim points at something a
      reader can check — a bare "đã cô lập" with no value in it is exactly the sentence that goes stale.

    The value has to be spelled the way the product spells it, so a rename shows up here as a stale citation; a
    row that explicitly rules a value out (``không có `Setting="Value"` ``) is read as a denial, not a claim.
    """
    audit_path = ROOT / "docs" / "EFFECT-COVERAGE-AUDIT.md"
    plan_path = ROOT / "Diagnostics" / "OptionMatrixPlan.cs"
    for path in (audit_path, plan_path):
        if not path.exists():
            return [f"{path.relative_to(ROOT).as_posix()} is missing; the audit's evidence claims need it"]

    plan = _code(plan_path.read_text(encoding="utf-8"))
    axes = {}
    for family_id, setting, _, values, neutral, gate in re.findall(
            r'new Family\("([^\"]+)",\s*"([^\"]+)",\s*Scene\.(\w+),\s*\[(.*?)\],\s*"([^\"]+)"(?:,\s*"([^\"]+)")?\)',
            plan, re.S):
        declared = re.findall(r'"([^"]*)"', values)
        axes[setting] = (declared, declared if gate else [v for v in declared if v != neutral], neutral)

    audit = audit_path.read_text(encoding="utf-8")
    rows = [line for line in audit.splitlines() if re.match(r"^\| `[a-z0-9.-]+` \|", line)]
    if len(rows) < 80:
        return [f"docs/EFFECT-COVERAGE-AUDIT.md shows {len(rows)} catalog rows; the audit lost its table"]

    errors = []
    isolated = 0
    for row in rows:
        entry = re.match(r"^\| `([^`]+)`", row).group(1)
        cited = []
        for setting, value in re.findall(r'`([A-Za-z]+)="([^"]*)"`', row):
            if setting not in axes or f'không có `{setting}="{value}"`' in row:
                continue
            offered, captured, neutral = axes[setting]
            if value not in offered:
                errors.append(f"docs/EFFECT-COVERAGE-AUDIT.md row {entry!r} cites {setting}=\"{value}\", which "
                              f"Diagnostics/OptionMatrixPlan.cs does not capture; the value was renamed or the row "
                              "is quoting a setting the product does not have")
                continue
            cited.append((setting, value, value in captured))
        if any(is_captured for _, _, is_captured in cited):
            isolated += 1
            if "N12(b)" not in row:
                pairs = ", ".join(f"{setting}=\"{value}\"" for setting, value, ok in cited if ok)
                errors.append(f"docs/EFFECT-COVERAGE-AUDIT.md row {entry!r} names {pairs}, which the N12(b) matrix "
                              "captures, but the row never says so; it still reads like an untested option")
        elif "N12(b)" in row and not cited:
            errors.append(f"docs/EFFECT-COVERAGE-AUDIT.md row {entry!r} claims N12(b) without naming a "
                          "Setting=\"Value\" on any axis the matrix captures; the claim cannot be checked")
    if isolated < 50:
        errors.append(f"only {isolated} audit rows cite a value the N12(b) matrix captures; the audit and the plan "
                      "have drifted apart")
    return errors


def scan_refraction_contract():
    """The N8 review may only promise what the shader and the software stage actually do.

    ``NoteRefraction`` is deliberately two behaviours: the software stage paints a fringe for every material and
    weights it per style, while the Direct3D shader reads ``NoteB.w`` in the satin sheen and in three material
    branches only. ``docs/NOTE-EFFECTS-ACTION-PLAN.md`` §3.6 calls that difference intentional, so the evidence
    has to say *which* materials move on *which* renderer — and that list is a fact about the shader, not a
    choice. This check derives it from ``StageShaders.hlsl`` by reading the material branches the way the shader
    reads them, and compares it with the list ``RefractionReview`` asserts against at runtime. Change a branch
    and the review fails on the first capture; forget the list and this fails without Windows.

    It also keeps the review reading the product's own material list (a copy would drift), keeps the three
    levels and the 36-capture count pinned, and keeps the suite on the route that CI actually runs.
    """
    errors = []
    paths = {
        "review": ROOT / "Diagnostics" / "RefractionReview.cs",
        "settings": ROOT / "Stage" / "PianoVisualSettings.cs",
        "shader": ROOT / "Gpu" / "StageShaders.hlsl",
        "stage": ROOT / "Stage" / "PianoStage.cs",
        "suite": ROOT / "Diagnostics" / "VerificationSuite.cs",
        "matrix": ROOT / "Diagnostics" / "OptionMatrixReview.cs",
    }
    for name, path in paths.items():
        if not path.exists():
            return [f"{path.relative_to(ROOT).as_posix()} is missing; the N8 refraction evidence needs it"]
    review = _code(paths["review"].read_text(encoding="utf-8"))
    settings = _code(paths["settings"].read_text(encoding="utf-8"))
    stage = _code(paths["stage"].read_text(encoding="utf-8"))
    suite = _code(paths["suite"].read_text(encoding="utf-8"))
    matrix = _code(paths["matrix"].read_text(encoding="utf-8"))
    shader = paths["shader"].read_text(encoding="utf-8")

    if "PianoVisualSettings.NoteStyles" not in review:
        errors.append("Diagnostics/RefractionReview.cs no longer reads PianoVisualSettings.NoteStyles; a copied "
                      "list of materials is how the page and the product drift apart")
    if '"CrystalOutline"' in review:
        errors.append("Diagnostics/RefractionReview.cs names a material itself; the six materials come from the "
                      "settings class, so a rename or a new style cannot leave a stale row on the page")
    levels = re.search(r"Levels \{ get; \} = \[([^\]]*)\]", review)
    if not levels or [int(value) for value in re.findall(r"\d+", levels.group(1))] != [0, 50, 100]:
        errors.append("Diagnostics/RefractionReview.cs no longer shoots NoteRefraction at 0/50/100; the page is "
                      "the acceptance evidence for that slider's travel")
    if "ExpectedCaptures = 36" not in review:
        errors.append("Diagnostics/RefractionReview.cs no longer pins 36 captures (six materials × three levels "
                      "× two renderers), so a material could drop off the page unnoticed")

    declared = re.search(r"GpuConsumers \{ get; \} = new HashSet<string>\(StringComparer\.Ordinal\)\s*\{(.*?)\};",
                         review, re.S)
    if not declared:
        errors.append("Diagnostics/RefractionReview.cs no longer declares which materials consume NoteB.w on the "
                      "GPU; that set is the other half of the claim")
        consumers = set()
    else:
        consumers = set(re.findall(r'"([^"]+)"', declared.group(1)))

    styles = re.findall(r'"([^"]+)"', re.search(r"string\[\] NoteStyles = \[(.*?)\];", settings, re.S).group(1)) \
        if re.search(r"string\[\] NoteStyles = \[(.*?)\];", settings, re.S) else []
    if len(styles) != 6:
        errors.append(f"Stage/PianoVisualSettings.cs lists {len(styles)} note materials; the refraction page pins six")

    # Read the material function the shader actually shades with, then ask each branch whether it reads NoteB.w.
    # The file declares the function before it defines it, so take the last match that is followed by a brace
    # before a semicolon — the definition — and read to the first closing brace back at column zero.
    start = None
    for match in re.finditer(r"float4 ShadeNoteMaterial\(NoteMaterialInput v\)", shader):
        brace, semi = shader.find("{", match.end()), shader.find(";", match.end())
        if brace >= 0 and (semi < 0 or brace < semi):
            start = match.start()
    if start is None:
        errors.append("Gpu/StageShaders.hlsl no longer has ShadeNoteMaterial; the refraction contract points at it")
        shader_styles = set()
    else:
        body = shader[start:shader.find("\n}\n", start)]
        thresholds = {"0.5": "Solid", "1.5": "Neon", "2.5": "Glass", "3.5": "Fire", "4.5": "Gem",
                      "5.5": "CrystalOutline"}
        markers = [(match.start(), match.group(1)) for match in re.finditer(r"else if \(style < ([0-9.]+)\)", body)]
        shader_styles = set()
        for index, (position, threshold) in enumerate(markers):
            block = body[position:markers[index + 1][0] if index + 1 < len(markers) else len(body)]
            if "NoteB.w" in block and threshold in thresholds:
                shader_styles.add(thresholds[threshold])
        if "if (satin)" in body and "NoteB.w" in body[:markers[0][0]]:
            # The satin sheen is shared by Solid and Glass, and only by them.
            shader_styles.update({"Solid", "Glass"})
    if consumers != shader_styles:
        errors.append("Diagnostics/RefractionReview.cs expects NoteB.w to be consumed by "
                      f"{sorted(consumers) or 'nothing'}, but Gpu/StageShaders.hlsl consumes it in "
                      f"{sorted(shader_styles) or 'no material branch'}; the N8 page would promise the wrong pixels")

    if "!satin && _visual.NoteRefraction > 0" not in stage:
        errors.append("Stage/PianoStage.cs no longer draws the soft fringe under `!satin && _visual.NoteRefraction > 0`; "
                      "the review asserts that every material moves on WPF because of that line")
    if "BaseSettings" not in review or "internal static PianoVisualSettings BaseSettings" not in matrix:
        errors.append("Diagnostics/RefractionReview.cs no longer starts from OptionMatrixReview.BaseSettings(); a "
                      "second copy of the pinned scene is how two reviews end up describing different frames")
    for route in ("--verify-refraction-only", "VerifyRefraction(reviewOutput!)", "RefractionReview.Render("):
        if route not in suite:
            errors.append(f"Diagnostics/VerificationSuite.cs lost '{route}', so the refraction review never runs")
    return errors


def scan_ar_material_review():
    """The AR material page may only claim what the typed contract says about each material.

    ``EffectContracts`` keeps the AR bindings of the note materials at ``Unverified`` because no review ever
    compared two AR frames of one material. ``ArMaterialReview`` is that review, and its whole value is that it
    walks the contract instead of a copy of it: the materials come from ``NoteMaterialPilot``, the settings from
    the parameters those contracts declare, and what each cell is expected to do from the contract's own
    ``GpuStage`` binding — a material whose shader branch ignores a setting must ignore it in AR too, because the
    quad is shaded by the same function. This check keeps that wiring honest without a Windows machine: the driven
    setting names equal the contract's, every one of them has an ``Apply`` case, the review names no material
    itself, and the suite keeps both the route and the call.
    """
    errors = []
    review_path = ROOT / "Diagnostics" / "ArMaterialReview.cs"
    contract_path = ROOT / "Stage" / "Effects" / "EffectContracts.cs"
    suite_path = ROOT / "Diagnostics" / "VerificationSuite.cs"
    for path in (review_path, contract_path, suite_path):
        if not path.exists():
            return [f"{path.relative_to(ROOT).as_posix()} is missing; the N16 AR material page needs it"]
    review = _code(review_path.read_text(encoding="utf-8"))
    contracts = _code(contract_path.read_text(encoding="utf-8"))
    suite = _code(suite_path.read_text(encoding="utf-8"))

    if "EffectContractCatalog.NoteMaterialPilot" not in review:
        errors.append("Diagnostics/ArMaterialReview.cs no longer reads EffectContractCatalog.NoteMaterialPilot; a "
                      "copied material list is how the page and the contract drift apart")

    declared = []
    for list_name in ("SolidParameters", "GemParameters", "CrystalOutlineParameters"):
        block = re.search(rf"{list_name} = Array\.AsReadOnly\(new\[\]\s*\{{(.*?)\}}\);", contracts, re.S)
        if not block:
            errors.append(f"Stage/Effects/EffectContracts.cs no longer declares {list_name}; the AR material page "
                          "reads its settings from there")
            continue
        declared.extend(re.findall(r'(?:Percentage|Boolean)\("([^"]+)"', block.group(1)))
    contract_settings = set(declared)
    if len(contract_settings) < 6:
        errors.append(f"Stage/Effects/EffectContracts.cs declares only {sorted(contract_settings)} for the note "
                      "materials; the AR page pins the six settings it can drive")

    driven = re.search(r"Known = \[([^\]]*)\]", review)
    driven_names = set(re.findall(r'"([^"]+)"', driven.group(1))) if driven else set()
    if driven_names != contract_settings:
        errors.append("Diagnostics/ArMaterialReview.cs drives "
                      f"{sorted(driven_names)} but the contract declares {sorted(contract_settings)}; a setting "
                      "that is declared and not driven (or driven and not declared) makes the page a partial claim")

    applied = set(re.findall(r'case "([^"]+)":', review))
    missing = contract_settings - applied
    if missing:
        errors.append(f"Diagnostics/ArMaterialReview.cs has no Apply/Read case for {sorted(missing)}; the setting "
                      "would be shot at whatever the settings class keeps")

    for material in ("Solid", "Gem", "CrystalOutline"):
        if f'"{material}"' in review:
            errors.append(f"Diagnostics/ArMaterialReview.cs names the material {material!r} itself; materials, "
                          "their settings and their expected AR binding all have to come from the contract")

    if "Bindings.GpuStage" not in review or "EffectParameterBindingState.Ignored" not in review:
        errors.append("Diagnostics/ArMaterialReview.cs no longer derives the expected binding from the contract's "
                      "GpuStage state; it would be asserting a hope instead of the contract")

    for route in ("--verify-ar-material-only", "VerifyArMaterial(reviewOutput!)", "ArMaterialReview.Render("):
        if route not in suite:
            errors.append(f"Diagnostics/VerificationSuite.cs lost '{route}', so the AR material review never runs")
    return errors


def scan_ar_effect_family_review():
    """The AR effect-family page may only shoot families and settings the typed contract declares.

    N17 measures the effect families the contract keeps at ``Unverified`` on the overlay. Its value is that it
    walks the contract instead of a copy of it: the driven family ids and scenes are a table in the review,
    but every setting it drives is read from that family's own parameters, and the far end of a percentage
    comes from the declared maximum. What the contract cannot say is which of the layer's counters proves a
    family drew something - or which scene a family's lifecycle needs - so those two tables are written by
    hand, and this check keeps them from drifting: the witnesses have to name exactly the driven families,
    every declared setting needs an ``Apply``/``Read`` case, the counters have to exist on the renderer, and
    every driven id has to exist in the contract at all.

    It also keeps the route, the call and the CI step, so a page that stops running fails the fast lane
    instead of quietly leaving those families Unverified forever.
    """
    errors = []
    review_path = ROOT / "Diagnostics" / "ArEffectFamilyReview.cs"
    contract_path = ROOT / "Stage" / "Effects" / "EffectContracts.cs"
    suite_path = ROOT / "Diagnostics" / "VerificationSuite.cs"
    renderer_path = ROOT / "Gpu" / "GpuStageRenderer.cs"
    workflow_path = ROOT / ".github" / "workflows" / "build.yml"
    for path in (review_path, contract_path, suite_path, renderer_path, workflow_path):
        if not path.exists():
            return [f"{path.relative_to(ROOT).as_posix()} is missing; the N17 AR effect-family page needs it"]
    review = _code(review_path.read_text(encoding="utf-8"))
    contracts = _code(contract_path.read_text(encoding="utf-8"))
    suite = _code(suite_path.read_text(encoding="utf-8"))
    renderer = _code(renderer_path.read_text(encoding="utf-8"))
    workflow = workflow_path.read_text(encoding="utf-8")

    if "EffectContractCatalog.AllFamilies" not in review:
        errors.append("Diagnostics/ArEffectFamilyReview.cs no longer resolves its families through "
                      "EffectContractCatalog.AllFamilies; a copied list of parameters is how the page and the "
                      "contract drift apart")

    driven = re.findall(r'\("([a-z][a-z.\-]+)", Stage\.(\w+)\)', review)
    if not driven:
        return errors + ["Diagnostics/ArEffectFamilyReview.cs no longer declares which families it drives; the "
                         "page would shoot nothing"]
    driven_ids = {name for name, _ in driven}
    scenes = {name: scene for name, scene in driven}

    declared_settings = set()
    for family in sorted(driven_ids):
        anchor = contracts.find(f'"{family}"')
        if anchor < 0:
            errors.append(f"the AR effect review drives '{family}', which Stage/Effects/EffectContracts.cs does "
                          "not declare; the page would shoot a family nobody owns")
            continue
        start = contracts.find("Array.AsReadOnly(new[]", anchor)
        stop = contracts.find("}),", start) if start >= 0 else -1
        if start < 0 or stop < 0:
            errors.append(f"Stage/Effects/EffectContracts.cs declares '{family}' without a parameter list; the "
                          "review reads its settings from there")
            continue
        declared_settings.update(re.findall(r'(?:ArProjected)?(?:Boolean|Percentage|Choice)\("([^"]+)"',
                                            contracts[start:stop]))

    witnesses = set(re.findall(r'"([a-z][a-z\-]*\.[a-z\-]+)" => renderer\.', review))
    if witnesses != driven_ids:
        errors.append("Diagnostics/ArEffectFamilyReview.cs witnesses "
                      f"{sorted(witnesses)} but drives {sorted(driven_ids)}; a family without a witness counter is "
                      "a column whose pictures nobody checked were drawn")

    applied = set(re.findall(r'case "([^"]+)":', review))
    missing = declared_settings - applied
    if missing:
        errors.append(f"Diagnostics/ArEffectFamilyReview.cs has no Apply/Read case for {sorted(missing)}; the "
                      "setting would be shot at whatever the settings class keeps")

    for family in sorted(driven_ids):
        anchor = contracts.find(f'"{family}"')
        start = contracts.find("Array.AsReadOnly(new[]", anchor) if anchor >= 0 else -1
        stop = contracts.find("}),", start) if start >= 0 else -1
        if start < 0 or stop < 0:
            continue
        for choice in re.findall(r'Choice\("([^"]+)"', contracts[start:stop]):
            if f'"{choice}" => settings.' not in review:
                errors.append(f"'{family}' declares the choice {choice}, but the review never reads it back; a "
                              "value the settings class silently replaced would be shot under the wrong caption")

    for counter in set(re.findall(r"renderer\.(Ar[A-Za-z]+Count)", review)):
        if f"internal int {counter} =>" not in renderer:
            errors.append(f"Diagnostics/ArEffectFamilyReview.cs reads renderer.{counter}, which GpuStageRenderer "
                          "no longer exposes; the witness would not compile")

    if "Bindings" in review or "EffectBackendState" in review:
        errors.append("Diagnostics/ArEffectFamilyReview.cs reads a binding state; the contract keeps these "
                      "families Unverified, so the page records pixels and claims nothing")

    for route in ("--verify-ar-effect-only", "VerifyArEffect(reviewOutput!);", "ArEffectFamilyReview.Render("):
        if route not in suite:
            errors.append(f"Diagnostics/VerificationSuite.cs lost '{route}', so the AR effect-family review never runs")
    expected_cells = len(driven) * 3
    if f"Assert(cells == {expected_cells}" not in suite:
        errors.append(f"Diagnostics/VerificationSuite.cs no longer pins the {expected_cells} AR effect-family "
                      f"cells ({len(driven)} families x 3 rows); the page would pass with columns silently missing")

    if "AR hold-family review (N17)" not in workflow or "--verify-ar-effect-only" not in workflow:
        errors.append(".github/workflows/build.yml no longer runs --verify-ar-effect-only, so a crash in the "
                      "review takes the whole suite down instead of naming itself")
    if "docs/evidence/ar-effect-sheets" not in workflow:
        errors.append(".github/workflows/build.yml no longer stages docs/evidence/ar-effect-sheets; the page's "
                      "committed pictures would never reach the repository")
    return errors


def scan_effect_channel_ownership():
    """An option must draw on its own, not behind another layer's switch.

    The first N12(b) run failed on the shipping stage: Shatter, Melt and Bounce are particles and both ring
    releases are rings, while the stage drew those lists only when ``ShowEmbers`` / ``ShowImpactRings`` were
    on — so a user who chose a morph or a release effect could get nothing at all even though the GPU stage
    drew it. ``Stage/Effects/EffectChannel.cs`` tags each effect with the channel that spawned it and the
    three draw paths skip only layer-owned effects behind their own switch. That ownership is the whole
    value of the fix, so it is pinned here: an edit that re-gates a list, or spawns a morph/release effect
    without a tag, fails on Linux before Windows ever renders a frame.
    """
    errors = []
    channel_path = ROOT / "Stage" / "Effects" / "EffectChannel.cs"
    stage_path = ROOT / "Stage" / "PianoStage.cs"
    if not channel_path.exists():
        return ["Stage/Effects/EffectChannel.cs is missing; the morph and release channels lost their owner"]
    if not stage_path.exists():
        return ["Stage/PianoStage.cs is missing; the option matrix cannot verify a stage that does not exist"]
    channel = _code(channel_path.read_text(encoding="utf-8"))
    stage = _code(stage_path.read_text(encoding="utf-8"))
    for owner, number in (("Layer", 0), ("Morph", 1), ("Release", 2)):
        if not re.search(rf"const\s+byte\s+{owner}\s*=\s*{number}\s*;", channel):
            errors.append(f"Stage/Effects/EffectChannel.cs must keep {owner} = {number}: the GPU ring channels use "
                          "the same numbering, so the two renderers cannot disagree about who owns an effect")
    if "OwnsItsDraw" not in channel:
        errors.append("Stage/Effects/EffectChannel.cs lost OwnsItsDraw, the one rule the draw paths share")
    # A list that is skipped because of a layer switch must skip only the effects that switch owns.
    for owner, switch, what in (
        ("particle", "ShowEmbers", "particles"),
        ("ring", "ShowImpactRings", "rings"),
        ("flash", "ShowImpactFlash", "flashes"),
    ):
        if f"!EffectChannel.OwnsItsDraw({owner}.Channel) && !_visual.{switch}" not in stage:
            errors.append(f"Stage/PianoStage.cs no longer skips only layer-owned {what} when {switch} is off; a "
                          "chosen morph or release effect would hide behind another layer's switch again")
        if f"EffectChannel.OwnsItsDraw({owner}.Channel)" not in stage:
            errors.append(f"Stage/PianoStage.cs does not ask EffectChannel.OwnsItsDraw({owner}.Channel), so the "
                          f"{what} list is drawn or skipped as a whole and one channel can silence another")
    if "KeyRingChannelGain.Resolve(ring.Channel" not in stage:
        errors.append("Stage/PianoStage.cs no longer resolves a ring's brightness through KeyRingChannelGain; a "
                      "morph ring would follow the wave slider and a release ring would be dimmed twice")
    # Every spawn site of the two self-owning families must carry its tag, or the effect it spawns stays
    # hidden by ShowEmbers / ShowImpactRings / ShowImpactFlash.
    for owner, expected in (("Morph", 6), ("Release", 6)):
        found = stage.count(f"Channel = EffectChannel.{owner}")
        if found != expected:
            errors.append(f"Stage/PianoStage.cs tags {found} {owner.lower()} effect(s) with EffectChannel.{owner}, "
                          f"expected {expected}; a new or removed morph/release branch must be tagged and captured")
    return errors


def scan_previews_folder():
    folder = ROOT / "docs" / "previews"
    if not folder.exists():
        return []
    # Bounded native animation assets: exact BGRA frames, not arbitrary diagnostics/archives.
    # The family validators verify frame count, decoded length, hashes and publication budgets.
    native_bundles = {f"{family}/{view}-{phase}.bgra.gz"
                      for family in ("fireworks-art-v1", "confetti-art-v1", "dust-art-v1", "dust-art-v2", "shatter-art-v1", "melt-art-v1", "bounce-art-v1", "release-float-up-v1", "release-dissolve-v1", "release-smoke-v1")
                      for view in ("live-stage", "ar-reference") for phase in ("burst", "held")}
    native_bundles.update(f"shatter-billboard-v1/{view}-{phase}.bgra.gz"
                          for view in ("ar-reference","ar-near-overhead") for phase in ("burst","held"))
    native_bundles.update(f"ar-particle-budget-v1/{recipe}.bgra.gz"
                          for recipe in ("embers-wisps", "splash-flames"))
    return [f"{path.relative_to(ROOT).as_posix()}: docs/previews may hold pictures or explicitly named native frame bundles only; "
            "write diagnostics outside the repository (see App.ReportPreview)"
            for path in sorted(folder.rglob("*")) if path.is_file()
            and path.suffix.lower() not in PREVIEW_PICTURES
            and path.relative_to(folder).as_posix() not in native_bundles]


def scan_cli_and_samples():
    """Command line, workflow and documentation are one product, so they are checked against each other.

    A switch the app parses but nobody documented is invisible to the people who read the README first,
    and a switch the README promises but the app never parses is a trap. The same goes for the workflow:
    it passes ``--background-image`` by name, and a typo there would silently render a black stage in the
    documentation instead of failing, so every flag it uses has to exist and every path it points at has
    to be in the checkout.
    """
    errors = []
    parsed = set()
    # The files that read the command line. A switch parsed in a file this list does not name is invisible to
    # the check, and a switch documented in a README but parsed nowhere is a trap, so both directions compare.
    for name in ("App.xaml.cs", "Diagnostics/VerificationSuite.cs", "Diagnostics/FrameBenchmark.cs", "Diagnostics/RecordingSoak.cs"):
        parsed |= set(re.findall(r'"(--[a-z][a-z-]*)', (ROOT / name).read_text(encoding="utf-8")))
    # Both language editions carry their own command-line table, and each one has to stand on its own:
    # a reader of ``README.en.md`` must never have to open the Vietnamese file for a switch name.
    for name, heading in (("README.md", "Tham số dòng lệnh"), ("README.en.md", "Command line switches")):
        readme = ROOT / name
        if not readme.exists():
            continue
        table = re.search(rf"^### {re.escape(heading)}$(.*?)^### ", readme.read_text(encoding="utf-8"), re.M | re.S)
        if not table:
            errors.append(f"{name} lost the '### {heading}' section that documents every switch")
            continue
        documented = set(re.findall(r"--[a-z][a-z-]*", table.group(1)))
        for flag in sorted(parsed - documented):
            errors.append(f"the app parses {flag}, but the {name} CLI table does not document it")
        for flag in sorted(documented - parsed):
            errors.append(f"the {name} CLI table documents {flag}, which no source file parses")
    workflow = ROOT / ".github" / "workflows" / "build.yml"
    if not workflow.exists():
        return errors
    workflow_text = workflow.read_text(encoding="utf-8")
    shots = re.search(r"\$shots = @\((.*?)\n\s*\)\n", workflow_text, re.S)
    if not shots:
        return errors + ["build.yml lost the $shots list that renders the README previews"]
    block = shots.group(1)
    # The preset gallery renders every shipped look by name (spaces dropped, as --preset accepts them).
    gallery = re.search(r"\$presets = @\(([^)]*)\)", workflow_text)
    presets_source = ROOT / "Stage" / "VisualPresets.cs"
    if gallery and presets_source.exists():
        text = presets_source.read_text(encoding="utf-8")
        default = re.search(r'DefaultPresetName = "([^"]+)"', text)
        names = re.findall(r'^\s*new\("([^"]+)", "', text, re.M) + ([default.group(1)] if default else [])
        community = {path.stem for path in (ROOT / "presets").glob("*.json")}
        wanted = {name.replace(" ", "") for name in [*names, *community]}
        listed = set(re.findall(r"'([^']+)'", gallery.group(1)))
        for name in sorted(wanted - listed):
            errors.append(f"build.yml's preset gallery leaves out the shipped preset {name}")
        for name in sorted(listed - wanted):
            errors.append(f"build.yml's preset gallery renders {name}, which is not a shipped preset")
    # The note-material study is its own render gate: all legacy styles plus the two new materials,
    # both depth states where supported, and one capture for each stage renderer.
    note_step = re.search(r"^      - name: Render the note-material gallery at 50%\n(.*?)(?=^      - name: |\Z)", workflow_text, re.M | re.S)
    note_gallery = re.search(r"\$styles = @\((.*?)\n\s*\)", note_step.group(1), re.S) if note_step else None
    required_styles = {
        "Solid-2D": ("Solid", "2d"), "Solid-3D": ("Solid", "3d"),
        "Glass-2D": ("Glass", "2d"), "Glass-3D": ("Glass", "3d"),
        "Neon": ("Neon", "2d"), "Fire": ("Fire", "2d"),
        "Gem-2D": ("Gem", "2d"), "Gem-3D": ("Gem", "3d"),
        "Crystal-Outline": ("CrystalOutline", "2d"),
    }
    if not note_gallery:
        errors.append("build.yml lost the $styles list that renders the note-material gallery")
    else:
        entries = {
            match.group(1): (match.group(2), match.group(3))
            for match in re.finditer(r"@\{\s*Id\s*=\s*'([^']+)'\s*;\s*Style\s*=\s*'([^']+)'\s*;\s*Depth\s*=\s*'([^']+)'\s*\}", note_gallery.group(1))
        }
        for name, expected in required_styles.items():
            if entries.get(name) != expected:
                errors.append(f"build.yml's note-material gallery needs {name} = {expected[0]}/{expected[1]}")
        for name in sorted(entries.keys() - required_styles.keys()):
            errors.append(f"build.yml's note-material gallery renders the unplanned case {name}")
        engines = re.search(r"\$engines = @\(([^)]*)\)", note_step.group(1)) if note_step else None
        if not engines or set(re.findall(r"'([^']+)'", engines.group(1))) != {"sw", "gpu"}:
            errors.append("build.yml's note-material gallery must render both WPF/software and GPU/WARP")
        if not note_step or not re.search(r"\$cellW\s*=\s*540\s*;\s*\$cellH\s*=\s*350", note_step.group(1)) or "'--compact'" not in note_step.group(1):
            errors.append("build.yml's note-material gallery must reduce a 1080×700 capture to 540×350 (50%)")
        if not note_step or "'docs/previews/note-styles.jpg'" not in note_step.group(1):
            errors.append("build.yml's note-material gallery must write docs/previews/note-styles.jpg")
        if note_step and any(token not in note_step.group(1) for token in (
            "foreach ($style in $styles)", "foreach ($engine in $engines)",
            '"--note-style=$($style.Style)"', '"--note-depth=$($style.Depth)"',
            "if ($engine -eq 'gpu')", "'--software'",
        )):
            errors.append("build.yml must render every note material/depth through both explicit engine overrides")
    artifact_step = re.search(r"^      - name: Upload README previews\n(.*?)(?=^      - name: |\Z)", workflow_text, re.M | re.S)
    if not artifact_step or "docs/previews/note-styles.jpg" not in artifact_step.group(1):
        errors.append("build.yml's README preview artifact must include docs/previews/note-styles.jpg")
    drift_step = re.search(r"^      - name: Report preview drift\n(.*?)(?=^      - name: |\Z)", workflow_text, re.M | re.S)
    if not drift_step or not re.search(r"steps\.render-note-styles\.outcome\s*==\s*'success'", drift_step.group(1)):
        errors.append("build.yml must report preview drift only after the note-material gallery succeeds")
    for flag in sorted(set(re.findall(r"(--[a-z][a-z-]*)", block)) - parsed):
        errors.append(f"build.yml passes {flag} to PianoPath.exe, which does not parse it")
    for path in sorted(set(re.findall(r"((?:docs|Assets)/[A-Za-z0-9_./-]+)", block))):
        if not (ROOT / path).exists():
            errors.append(f"build.yml renders a preview from '{path}', which is not in the checkout")
    return errors


MESSAGE_SECTIONS = ("Messages", "CustomMessages")


def read_isl(text: str):
    """Parse an Inno Setup language file the way the compiler does: ``[Section]`` headers, ``;``
    comments and ``name=text`` lines. Returns ``{section: {name: text}}``."""
    sections, current = {}, None
    for line in text.splitlines():
        line = line.strip()
        if not line or line.startswith(";"):
            continue
        header = re.match(r"^\[(\w+)\]$", line)
        if header:
            current = header.group(1)
            sections.setdefault(current, {})
        elif "=" in line and current is not None:
            name, value = line.split("=", 1)
            sections[current][name.strip()] = value.strip()
    return sections


def scan_installer():
    """The setup program is part of the product, and Inno Setup is deliberately forgiving about wizard
    text: a message name it does not recognize is only a *warning* — the line is dropped and the
    English wording is shipped — and a name that is right but sits in the wrong section never
    overrides anything. Both would ship half-translated dialogs with a green build, so the name list
    from ``tools/inno_messages.py`` and ``installer/Languages/Vietnamese.isl`` are checked against
    each other here.

    The rest of the rules mirror what the compiler really does with these files:
      * the .isl is UTF-8 with a BOM, which is how the compiler is told to read the diacritics (it
        would otherwise guess the encoding, and fall back to the code page for an ANSI file);
      * the .isl only overrides messages: LanguageName/LanguageID/LanguageCodePage live in the script,
        prefixed with the language name, because a partial file must not count on overriding the
        values Default.isl already set — and because an unprefixed LanguageID/LanguageName/
        LanguageCodePage stops the compile as soon as a second language exists;
      * placeholders (%1, %n, [name], [name/ver], [mb] …) survive the translation, since a dropped
        %1 shows the user a message with no folder name or no version in it.
    """
    errors = []
    script = ROOT / "installer" / "Keyflow.iss"
    reference = ROOT / "installer" / "Languages" / "messages.txt"
    translation = ROOT / "installer" / "Languages" / "Vietnamese.isl"
    for path in (script, reference, translation):
        if not path.exists():
            return [f"{path.relative_to(ROOT)} is missing; the installer would fall back to English"
                    " wizard text, and these checks have nothing to compare"]
    source = script.read_text(encoding="utf-8")

    def section(name):
        found = re.search(rf"^\[{name}\]\s*$(.*?)(?=^\[|\Z)", source, re.M | re.S)
        return found.group(1) if found else ""

    # [Languages]: the Vietnamese entry is Default.isl plus the partial translation, and the relative
    # path is resolved from the folder the script lives in (the compiler's source directory).
    languages = re.findall(r'^Name:\s*"([^"]+)"\s*;\s*MessagesFile:\s*"([^"]+)"', section("Languages"), re.M)
    if len(languages) < 2:
        errors.append("installer/Keyflow.iss declares fewer than two languages; the README promises the"
                      " installer ships English and Vietnamese wizard text")
    declared = {name for name, _ in languages}
    partial = ""
    for name, files in languages:
        entries = [entry.strip() for entry in files.split(",")]
        ours = [entry for entry in entries if not entry.startswith("compiler:")]
        for entry in ours:
            # The script is written for Windows: a relative MessagesFile uses "\" as its separator.
            if not (script.parent / entry.replace("\\", "/")).exists():
                errors.append(f'installer/Keyflow.iss: MessagesFile "{entry}" of the "{name}" language'
                              " is not next to the script, which is where the compiler looks for it")
        if name == "vietnamese":
            if len(ours) != 1 or entries[-1] != ours[0]:
                errors.append('installer/Keyflow.iss: the "vietnamese" language must list exactly'
                              " compiler:Default.isl followed by Languages\\Vietnamese.isl, last, because"
                              " the last file wins per message")
            else:
                partial = ours[0]

    # [LangOptions]: the directives that describe one language need the "<language>." prefix. Without
    # it the compile stops with "can only be specified for a single language" once two languages exist.
    lang_options = section("LangOptions")
    for key in ("LanguageName", "LanguageID", "LanguageCodePage"):
        for match in re.finditer(rf"^(\w+\.)?{key}\s*=\s*(\S+)\s*$", lang_options, re.M):
            prefix = (match.group(1) or "")[:-1]
            if prefix not in declared:
                errors.append(f"installer/Keyflow.iss: [LangOptions] {key} is"
                              f"{' not' if not prefix else ' prefixed with an undeclared language; it is'}"
                              " part of the language description, and the compiler rejects an unprefixed"
                              f" value the moment a second language exists (write `{key}` as"
                              f" `vietnamese.{key}=…`)")
        if partial and not re.search(rf"^vietnamese\.{key}\s*=", lang_options, re.M):
            errors.append(f"installer/Keyflow.iss: [LangOptions] does not set vietnamese.{key}; a partial"
                          " translation inherits the English value from Default.isl unless it overrides it")
    if partial and not re.search(r"^vietnamese\.LanguageID\s*=\s*\$041e\s*$", lang_options, re.M):
        errors.append("installer/Keyflow.iss: vietnamese.LanguageID should be $041e (Vietnamese), which is"
                      " what makes Setup pick this translation on a Vietnamese Windows")

    # The translation: UTF-8 with a BOM, and only message sections (see the docstring).
    raw = translation.read_bytes()
    if not raw.startswith(b"\xef\xbb\xbf"):
        errors.append("installer/Languages/Vietnamese.isl is not UTF-8 with a BOM; Inno Setup would guess"
                      " the encoding of the Vietnamese text instead of being told")
    text = raw.decode("utf-8-sig")
    for number, line in enumerate(text.splitlines(), 1):
        stripped = line.strip()
        if stripped and not stripped.startswith(";") and "=" in stripped and line != line.lstrip():
            errors.append(f"installer/Languages/Vietnamese.isl:{number}: the line starts with a space; the"
                          " compiler trims message names, but the space is a typo")
    translated = read_isl(text)
    for name in sorted(set(translated) - set(MESSAGE_SECTIONS)):
        errors.append(f"installer/Languages/Vietnamese.isl: [{name}] is not a message section; a partial"
                      " translation may only override [Messages] and [CustomMessages] (the language name,"
                      " id and code page belong in installer/Keyflow.iss)")
    for name, messages in translated.items():
        if not messages:
            errors.append(f"installer/Languages/Vietnamese.isl: [{name}] translates nothing")

    # The generated name list: valid names with the placeholders of the English message.
    valid, marked = {}, set()
    for section_name, messages in read_isl(reference.read_text(encoding="utf-8")).items():
        for name, signature in messages.items():
            is_marked = name.startswith("!")
            valid[(section_name, name.lstrip("!"))] = signature
            if is_marked:
                marked.add((section_name, name.lstrip("!")))
    if not valid:
        errors.append("installer/Languages/messages.txt has no message names; run"
                      " `python3 tools/inno_messages.py`")

    placeholder = re.compile(r"%\d+|%n|\[[a-z/]+\]")
    for section_name, messages in translated.items():
        for name, value in messages.items():
            if (section_name, name) not in valid:
                hit = next((other for other in MESSAGE_SECTIONS if (other, name) in valid), None)
                where = f" (it is a [{hit}] message)" if hit else " — Default.isl does not define it"
                errors.append(f"installer/Languages/Vietnamese.isl: [{section_name}] {name}{where};"
                              " Inno Setup would only warn and ship the English text")
                continue
            if (section_name, name) not in marked:
                errors.append(f"installer/Languages/messages.txt does not mark [{section_name}] {name} as"
                              " translated; run `python3 tools/inno_messages.py` so the list matches")
            expected = set(filter(None, valid[(section_name, name)].split(",")))
            found = set(placeholder.findall(value))
            if expected != found:
                errors.append(f"installer/Languages/Vietnamese.isl: {name} uses"
                              f" {sorted(found) or 'no placeholders'} but the English message uses"
                              f" {sorted(expected) or 'none'}; a dropped placeholder never reaches the dialog")
    translated_keys = {(section_name, name) for section_name, messages in translated.items() for name in messages}
    for section_name, name in sorted(marked - translated_keys):
        errors.append(f"installer/Languages/messages.txt marks [{section_name}] {name} as translated, but"
                      " installer/Languages/Vietnamese.isl does not translate it; regenerate the list")

    # Display text in the script comes from a message, and every message it asks for is translated —
    # otherwise the wizard shows a hardcoded English caption next to Vietnamese ones.
    for match in re.finditer(r"(Description|GroupDescription):\s*\"([^\"]*)\"", source):
        if "{cm:" not in match.group(2):
            errors.append(f"installer/Keyflow.iss: {match.group(1)} \u201c{match.group(2)}\u201d is a literal"
                          " caption; use a {cm:MessageName} so both languages follow the translation")
    for name in sorted(set(re.findall(r"\{cm:([A-Za-z0-9_]+)", source))):
        if name not in translated.get("CustomMessages", {}):
            errors.append(f"installer/Keyflow.iss asks for the custom message {name}, which"
                          " installer/Languages/Vietnamese.isl does not translate")

    # Both workflows compile the installer through the same script: the per-push check is what keeps
    # the wizard text honest, and the release job is what ships it.
    for workflow in ("build.yml", "release.yml"):
        path = ROOT / ".github" / "workflows" / workflow
        if path.exists() and "tools/build_installer.ps1" not in path.read_text(encoding="utf-8"):
            errors.append(f".github/workflows/{workflow} no longer compiles the installer with"
                          " tools/build_installer.ps1; the wizard text would only be checked when"
                          " nobody is looking")
    return errors


def scan_generated_assets():
    """``docs/samples`` holds pictures the repository builds for itself (``tools/make_*.py``), because the
    README wants to show the background feature without shipping artwork somebody else owns. A committed
    asset whose generator disagrees about size or bit depth is either hand-edited or stale, so compare the
    PNG header with the constants of the script that writes it."""
    errors = []
    # (script, picture): every picture the repository draws for itself, and the recipe that is its licence.
    assets = [("make_stage_background.py", "stage-backdrop.png"),
              ("make_ar_sample.py", "ar-keyboard-topdown.png")]
    for script_name, picture in assets:
        script = ROOT / "tools" / script_name
        target = ROOT / "docs" / "samples" / picture
        if not script.exists():
            errors.append(str(script.relative_to(ROOT)) + " is missing; it is the licence and the recipe of " + picture)
            continue
        if not target.exists():
            errors.append(str(target.relative_to(ROOT)) + f" is missing; regenerate it with `python3 tools/{script_name}`")
            continue
        width, height = (int(n) for n in re.search(r"^W, H = (\d+), (\d+)", script.read_text(encoding="utf-8"), re.M).groups())
        head = target.read_bytes()
        if not head.startswith(b"\x89PNG\r\n\x1a\n"):
            errors.append(str(target.relative_to(ROOT)) + " is not a PNG")
            continue
        fields = struct.unpack(">IIBBBBB", head[16:16 + 13])
        if (fields[0], fields[1], fields[2], fields[3]) != (width, height, 8, 2):
            errors.append(f"{target.relative_to(ROOT)} is {fields[0]}x{fields[1]} at {fields[2]} bit colour type {fields[3]}, "
                          f"but tools/{script_name} writes {width}x{height} at 8 bit colour type 2 (RGB) — "
                          f"regenerate it instead of editing the picture by hand")
    # The AR sample has a ground-truth file beside it; a checker or a calibration test reads it, so it has to parse
    # and describe the picture next to it.
    truth = ROOT / "docs" / "samples" / "ar-keyboard-topdown.json"
    if not truth.exists():
        errors.append(str(truth.relative_to(ROOT)) + " is missing; tools/make_ar_sample.py writes it beside the picture")
    else:
        try:
            document = json.loads(truth.read_text(encoding="utf-8"))
            if document.get("image") != [1280, 720] or len(document.get("corners", [])) != 4 or len(document.get("homography", [])) != 3:
                errors.append(str(truth.relative_to(ROOT)) + " no longer describes the picture (image size, four corners, a 3x3 homography)")
            elif len(document.get("keyTopCentres", {})) != document.get("keys"):
                errors.append(str(truth.relative_to(ROOT)) + " should name the top centre of every key")
        except (json.JSONDecodeError, AttributeError) as error:
            errors.append(f"{truth.relative_to(ROOT)} is not the JSON tools/make_ar_sample.py writes: {error}")
    return errors


def scan_phase1_scene_fixture():
    """Keep the agreed Phase 1 scene coherent with the Windows verification capture that consumes it."""
    path = ROOT / "docs" / "samples" / "visual-effects-phase1-scene.json"
    label = str(path.relative_to(ROOT))
    if not path.exists():
        return [f"{label} is missing; it is the reference-scene fixture for the visual-effects review"]
    try:
        document = json.loads(path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as error:
        return [f"{label} is not valid JSON: {error}"]
    if not isinstance(document, dict):
        return [f"{label} must be a JSON object"]

    errors = []

    def number(value):
        if not isinstance(value, (int, float)) or isinstance(value, bool):
            return False
        try:
            return math.isfinite(value)
        except OverflowError:
            return False

    if document.get("schemaVersion") != 1:
        errors.append(f"{label} must declare schemaVersion 1")
    if document.get("status") != "review-fixture-consumed-by-verification":
        errors.append(f"{label} must identify the Phase 1 scene as a verification-consumed review fixture")
    consumer = ROOT / "Diagnostics" / "Phase1VisualReview.cs"
    if not consumer.exists() or "docs/samples/visual-effects-phase1-scene.json" not in consumer.read_text(encoding="utf-8"):
        errors.append(f"{label} must be read by Diagnostics/Phase1VisualReview.cs")
    verification = ROOT / "Diagnostics" / "VerificationSuite.cs"
    if not verification.exists() or "--verify-review-out=" not in verification.read_text(encoding="utf-8"):
        errors.append(f"{label} must be reachable from the optional --verify-review-out verification switch")
    workflow = ROOT / ".github" / "workflows" / "build.yml"
    workflow_text = workflow.read_text(encoding="utf-8") if workflow.exists() else ""
    verify_step = re.search(r"^      - name: Verification suite\n(.*?)(?=^      - name: |\Z)", workflow_text, re.M | re.S)
    if not verify_step or "--verify-review-out=$review" not in verify_step.group(1):
        errors.append("build.yml must render the Phase 1 scene during the Windows verification suite")
    artifact_step = re.search(r"^      - name: Publish Phase 1 visual review\n(.*?)(?=^      - name: |\Z)", workflow_text, re.M | re.S)
    if not artifact_step or "phase1-visual-review" not in artifact_step.group(1) or "actions/upload-artifact@v7" not in artifact_step.group(1):
        errors.append("build.yml must upload the Phase 1 review images as the phase1-visual-review artifact")

    render = document.get("render")
    if not isinstance(render, dict):
        return errors + [f"{label}.render must be an object"]
    for key, expected in (("width", 1920), ("height", 1080), ("dpi", 96),
                          ("framesPerSecond", 60), ("durationSeconds", 12), ("frameCount", 720),
                          ("randomSeed", 424242), ("colorSpace", "sRGB")):
        if render.get(key) != expected:
            errors.append(f"{label}.render.{key} must remain {expected!r} for the fixed review capture")
    fps, duration, frame_count = (render.get("framesPerSecond"), render.get("durationSeconds"), render.get("frameCount"))
    if number(fps) and number(duration) and number(frame_count) and duration * fps != frame_count:
        errors.append(f"{label}.render.frameCount must equal durationSeconds × framesPerSecond")
    if render.get("initialTimeSeconds") != 4.1:
        errors.append(f"{label}.render.initialTimeSeconds must open on the held-chord checkpoint at 4.1 seconds")

    keyboard = render.get("keyboard")
    if not isinstance(keyboard, dict):
        errors.append(f"{label}.render.keyboard must describe the 88-key review stage")
        keyboard = {}
    key_values = (keyboard.get("keyCount"), keyboard.get("firstMidiPitch"), keyboard.get("lastMidiPitch"))
    if key_values != (88, 21, 108) or key_values[2] - key_values[1] + 1 != key_values[0]:
        errors.append(f"{label}.render.keyboard must cover MIDI 21–108 exactly (88 keys)")
    hit_line = keyboard.get("hitLineNormalizedY")
    if not number(hit_line) or not 0 < hit_line < 1:
        errors.append(f"{label}.render.keyboard.hitLineNormalizedY must be normalized between 0 and 1")
    if keyboard.get("noteDirection") != "Down":
        errors.append(f"{label}.render.keyboard.noteDirection must be Down")
    lookahead = keyboard.get("lookaheadSeconds")
    if not number(lookahead) or lookahead <= 0:
        errors.append(f"{label}.render.keyboard.lookaheadSeconds must be positive")

    music = document.get("music")
    if not isinstance(music, dict):
        return errors + [f"{label}.music must be an object"]
    loop = music.get("loopBeats")
    if not isinstance(loop, list) or len(loop) != 2 or not all(number(value) for value in loop) or loop[1] <= loop[0]:
        return errors + [f"{label}.music.loopBeats must be an increasing pair of beat positions"]
    loop_start, loop_end = loop
    if loop != [0, 16]:
        errors.append(f"{label}.music.loopBeats must remain the four-bar 0–16 beat fixture")
    if music.get("meter") != [4, 4]:
        errors.append(f"{label}.music.meter must remain 4/4")
    tempo_source = music.get("tempoMap")
    tempo = []
    if isinstance(tempo_source, list):
        for point in tempo_source:
            if (not isinstance(point, dict) or not number(point.get("beat")) or
                    not number(point.get("bpm")) or point["bpm"] <= 0):
                tempo = []
                break
            tempo.append((point["beat"], point["bpm"]))
    if (not tempo or tempo[0][0] != loop_start or
            any(left[0] >= right[0] for left, right in zip(tempo, tempo[1:])) or
            any(beat < loop_start or beat >= loop_end for beat, _ in tempo)):
        errors.append(f"{label}.music.tempoMap must have ordered positive tempos beginning at loopBeats[0]")
        tempo = []
    elif tempo != [(0, 96), (12, 72)]:
        errors.append(f"{label}.music.tempoMap must keep the agreed 96 BPM to 72 BPM change at beat 12")

    notes_source = music.get("notes")
    notes = []
    if not isinstance(notes_source, list) or not notes_source:
        errors.append(f"{label}.music.notes must contain the reference notes")
    else:
        seen_ids = set()
        for note in notes_source:
            if not isinstance(note, dict):
                errors.append(f"{label}.music.notes contains a non-object entry")
                continue
            note_id = note.get("id")
            pitch, start, length, velocity = (note.get("pitch"), note.get("startBeat"),
                                              note.get("durationBeats"), note.get("velocity"))
            if not isinstance(note_id, str) or not note_id or note_id in seen_ids:
                errors.append(f"{label}.music.notes IDs must be non-empty and unique (duplicate or missing: {note_id!r})")
                continue
            seen_ids.add(note_id)
            if (not number(pitch) or pitch < 21 or pitch > 108 or
                    not number(start) or not number(length) or length <= 0 or
                    not number(velocity) or velocity < 1 or velocity > 127 or
                    note.get("hand") not in ("left", "right") or
                    start < loop_start or start + length > loop_end):
                errors.append(f"{label}.music.notes[{note_id}] has an invalid pitch, hand, beat range, duration or velocity")
                continue
            notes.append(note)
    if {note.get("hand") for note in notes} != {"left", "right"}:
        errors.append(f"{label}.music.notes must include both left- and right-hand material")

    review_frames = document.get("reviewFrames")
    expected_frames = (("note-readability", 0.8), ("held-chord-and-impact", 4.1),
                       ("sustain-pedal", 6.1), ("release-and-rest", 11.25))
    if not isinstance(review_frames, list) or len(review_frames) != len(expected_frames):
        errors.append(f"{label}.reviewFrames must keep the four agreed checkpoints at 0.8, 4.1, 6.1 and 11.25 seconds")
        review_frames = []
    else:
        for frame, (expected_id, expected_time) in zip(review_frames, expected_frames):
            if (not isinstance(frame, dict) or frame.get("id") != expected_id or
                    frame.get("timeSeconds") != expected_time or not frame.get("checks")):
                errors.append(f"{label}.reviewFrames must include {expected_id} at {expected_time} seconds with review checks")
    if tempo and notes:
        def beat_to_seconds(beat):
            elapsed = 0.0
            for index, (start, bpm) in enumerate(tempo):
                end = tempo[index + 1][0] if index + 1 < len(tempo) else beat
                if beat <= start:
                    break
                segment_end = min(beat, end)
                elapsed += (segment_end - start) * 60.0 / bpm
                if beat <= end:
                    break
            return elapsed

        loop_seconds = beat_to_seconds(loop_end) - beat_to_seconds(loop_start)
        tail_seconds = duration - loop_seconds if number(duration) else -1
        if round(tail_seconds, 3) != 1.167:
            errors.append(f"{label} must leave the agreed 1.167-second tail after its 16-beat tempo-mapped loop")

        def active_hands_at(time_seconds):
            return {note["hand"] for note in notes
                    if beat_to_seconds(note["startBeat"]) <= time_seconds <
                    beat_to_seconds(note["startBeat"] + note["durationBeats"])}

        if not active_hands_at(0.8):
            errors.append(f"{label} has no note sounding at the 0.8-second readability checkpoint")
        if active_hands_at(4.1) != {"left", "right"}:
            errors.append(f"{label} must show both hands sounding at the 4.1-second held-chord checkpoint")
        if active_hands_at(6.1) != {"left", "right"}:
            errors.append(f"{label} must show sustained left- and right-hand notes at the 6.1-second pedal checkpoint")
        if not beat_to_seconds(8) <= 6.1 < beat_to_seconds(11.75):
            errors.append(f"{label} must keep the pedal down through the 6.1-second sustain checkpoint")
        if active_hands_at(11.25):
            errors.append(f"{label} must be in a true note rest at the 11.25-second release checkpoint")
        if beat_to_seconds(11.75) >= 11.25:
            errors.append(f"{label} must release the sustain pedal before the 11.25-second quiet checkpoint")

    pedal_source = music.get("pedalEvents")
    expected_pedal = ((8, True), (11.75, False))
    if not isinstance(pedal_source, list) or len(pedal_source) != len(expected_pedal):
        errors.append(f"{label}.music.pedalEvents must include pedal down at beat 8 and release at beat 11.75")
    else:
        for event, (expected_beat, expected_down) in zip(pedal_source, expected_pedal):
            if (not isinstance(event, dict) or event.get("beat") != expected_beat or
                    event.get("down") is not expected_down):
                errors.append(f"{label}.music.pedalEvents must retain the ordered down-at-8 / release-at-11.75 cue")

    looks = document.get("looks")
    expected_presets = ("Concert Gold", "Neon Violet", "Two Hands")
    if not isinstance(looks, list) or len(looks) != len(expected_presets):
        errors.append(f"{label}.looks must define the three reference looks without implying runtime presets")
        looks = []
    preset_source = (ROOT / "Stage" / "VisualPresets.cs").read_text(encoding="utf-8")
    built_in_names = set(re.findall(r'new\("([^"]+)"', preset_source))
    default_name = re.search(r'DefaultPresetName\s*=\s*"([^"]+)"', preset_source)
    if default_name:
        built_in_names.add(default_name.group(1))
    if not isinstance(looks, list) or tuple(look.get("referencePreset") for look in looks if isinstance(look, dict)) != expected_presets:
        errors.append(f"{label}.looks must target Concert Gold, Neon Violet and Two Hands in that order")
    for look in looks:
        if not isinstance(look, dict):
            errors.append(f"{label}.looks contains a non-object entry")
            continue
        reference = look.get("referencePreset")
        if not isinstance(reference, str) or reference not in built_in_names:
            errors.append(f"{label}.looks references {reference!r}, which is not a shipped built-in preset")
        if look.get("designTargetOnly") is not True:
            errors.append(f"{label}.looks.{look.get('id', '<unknown>')} must remain explicitly designTargetOnly")
        palette = look.get("palette")
        if not isinstance(palette, dict) or not palette or any(
                not isinstance(color, str) or not re.fullmatch(r"#[0-9A-Fa-f]{6}", color)
                for color in palette.values()):
            errors.append(f"{label}.looks.{look.get('id', '<unknown>')}.palette must use #RRGGBB colors")
        mix = look.get("mix")
        if not isinstance(mix, dict) or not mix or any(
                not number(value) or not 0 <= value <= 1 for value in mix.values()):
            errors.append(f"{label}.looks.{look.get('id', '<unknown>')}.mix values must be normalized from 0 to 1")
    return errors


def scan_preset_shelf():
    """``presets/`` is the community shelf: preset files that ship inside the application.

    A shelf file is an ordinary preset file, so the checks here are the ones a contributor cannot see:
    every settings key of ``PianoVisualSettings`` must be present exactly once (a new setting added in
    code would otherwise quietly fall back to its default in a stale file), the values must already be
    final (``--verify`` loads the shelf in the app and asserts the same thing), the name must match the
    file and must not shadow a built-in look, and the committed file must still be what
    ``tools/make_presets.py`` writes, which is the recipe and the licence for the folder.
    """
    import importlib.util
    import tempfile

    errors = []
    directory = ROOT / "presets"
    files = sorted(directory.glob("*.json")) if directory.exists() else []
    if not files:
        return ["presets/ is empty; the community shelf ships preset files there (tools/make_presets.py writes them)"]
    properties = re.findall(r"^    public [\w<>\[\]]+ (\w+) \{ get; set; \}", (ROOT / "Stage" / "PianoVisualSettings.cs").read_text(encoding="utf-8"), re.M)
    if len(properties) < 100:
        return ["Stage/PianoVisualSettings.cs no longer looks like the settings class; cannot check presets/"]
    built_in = set(re.findall(r'new\("([^"]+)", "', (ROOT / "Stage" / "VisualPresets.cs").read_text(encoding="utf-8")))
    seen = set()
    for path in files:
        label = f"presets/{path.name}"
        try:
            document = json.loads(path.read_text(encoding="utf-8"))
        except json.JSONDecodeError as error:
            errors.append(f"{label} is not valid JSON: {error}")
            continue
        if not isinstance(document, dict) or set(document) != {"Version", "Thumbnail", "Description", "Settings"}:
            errors.append(f"{label} should hold exactly Version, Thumbnail, Description and Settings — the envelope the app writes")
            continue
        if not isinstance(document["Version"], int) or document["Version"] < 1:
            errors.append(f"{label} should carry a version number")
        for field in ("Thumbnail", "Description"):
            if not isinstance(document[field], str):
                errors.append(f"{label} should hold {field} as a string, empty when there is none")
        if not document["Description"].strip():
            errors.append(f"{label} needs a description: it is the second line of the preset in the list")
        settings = document["Settings"]
        if not isinstance(settings, dict):
            errors.append(f"{label} should hold a Settings object")
            continue
        missing = [name for name in properties if name not in settings]
        unknown = [name for name in settings if name not in properties]
        if missing:
            errors.append(f"{label} is missing {len(missing)} setting(s) ({', '.join(missing[:6])}) — regenerate it with `python3 tools/make_presets.py`")
        if unknown:
            errors.append(f"{label} sets {', '.join(unknown[:6])}, which is not a setting of PianoVisualSettings")
        if settings.get("PresetName") != path.stem:
            errors.append(f'{label} is named {settings.get("PresetName")!r}; the shelf uses the file name so the list, the badge and the file agree')
        if settings.get("BackgroundAppearanceVersion", 0) < 2:
            errors.append(f"{label} would be migrated on load; regenerate it with `python3 tools/make_presets.py`")
        if path.stem.lower() in (name.lower() for name in built_in):
            errors.append(f"{label} shadows the built-in preset {path.stem!r}")
        if path.stem.lower() in seen:
            errors.append(f"{label} repeats a shelf name")
        seen.add(path.stem.lower())

    generator = ROOT / "tools" / "make_presets.py"
    if not files or not generator.exists():
        return errors
    spec = importlib.util.spec_from_file_location("make_presets", generator)
    module = importlib.util.module_from_spec(spec)
    try:
        spec.loader.exec_module(module)
        with tempfile.TemporaryDirectory() as scratch:
            module.write_all(Path(scratch), quiet=True)
            for path in files:
                built = Path(scratch) / path.name
                if not built.exists():
                    errors.append(f"presets/{path.name} is not one of the presets tools/make_presets.py writes")
                elif built.read_bytes() != path.read_bytes():
                    errors.append(f"presets/{path.name} does not match tools/make_presets.py — regenerate it with `python3 tools/make_presets.py` instead of editing it by hand")
    except SystemExit as error:
        errors.append(f"tools/make_presets.py refuses to run: {error}")
    return errors

PROJECT_FILES = ("*.csproj", "*.pubxml", "*.props", "*.targets")


def scan_project_files():
    """Every MSBuild file has to parse, because MSBuild refuses to load one that does not.

    The XAML files have been parsed here since the first version of this checker and the C# files are
    scanned for balance, but the project files were read only through regular expressions — and an XML
    comment may not contain a double hyphen, so a comment in ``PianoPath.csproj`` that names a
    command-line switch (``--bench``, ``--verify``) makes the file unparseable: no build, no publish, no
    test project, and this checker still green because nothing in it looked. Found by writing exactly
    that comment while pinning the release version.
    """
    errors = []
    files = sorted({path for pattern in PROJECT_FILES for path in ROOT.glob(f"**/{pattern}")
                    if "obj" not in path.parts and "bin" not in path.parts})
    if not files:
        return ["no MSBuild file was found; PianoPath.csproj is the project"]
    for path in files:
        try:
            ET.parse(path)
        except ET.ParseError as ex:
            errors.append(f"{path.relative_to(ROOT)}: MSBuild cannot load this file — {ex} "
                          "(an XML comment may not contain '--', which is what naming a command-line "
                          "switch inside one does)")
    return errors


# The files a public release branch may hold: the two product pages the generator writes, the changelogs,
# the licences, the document about the two languages, the version marker, and pictures. Anything else is
# either a mistake or a leak, and the release branch is the one place where a mistake is public for ever.
PUBLIC_RELEASE_FILES = {
    "README.md", "README.en.md", "CHANGELOG.md", "CHANGELOG.en.md", "LICENSE",
    "docs/LOCALIZATION.md", "VERSION",
    # The product presentation. It used to live only in the public repository — made there, committed
    # there, mentioned nowhere in this one — which is the one arrangement the release branch cannot
    # keep: the branch is rebuilt from this repository on every release, so the next publish would have
    # deleted it. It travels now (COPIES in tools/make_public_docs.py) and is allowed to.
    "docs/presentation/README.md",
    "docs/presentation/Keyflow-Presentation-v1.0.0.pdf",
    "docs/presentation/Keyflow-Presentation-v1.0.0.docx",
}


def scan_public_release():
    """The source repository is private and the release repository is public, so what crosses the line has
    to be pinned by something other than good intentions.

    Four things can go wrong here, and none of them shows up in a build: the public page can drift from
    the README it is generated from (the drift is invisible unless somebody reads both), a source file can
    be copied into the public tree (a leak nobody notices until a stranger downloads it), the token that
    writes to the public repository can be reachable from more of the workflow than the one step that
    needs it, and the documents can be published *into the source repository* — on 2026-10-03 a run aimed
    at this repository replaced main with them and the sources had to be restored from a branch. So this
    rule compares ``docs/release/`` with its generator byte for byte, keeps the list of files that may
    travel explicit, checks every link on the generated pages, asserts that the token is named in exactly
    one file, and asserts that both places which can push refuse to write main of the source repository.
    """
    import importlib.util
    import tempfile

    errors = []
    generator_path = ROOT / "tools" / "make_public_docs.py"
    target = ROOT / "docs" / "release"
    if not generator_path.exists():
        return ["tools/make_public_docs.py is missing; it writes and explains the public release documents"]
    if not target.exists():
        return ["docs/release/ is missing; run `python3 tools/make_public_docs.py` and commit the result"]

    spec = importlib.util.spec_from_file_location("make_public_docs", generator_path)
    module = importlib.util.module_from_spec(spec)
    try:
        spec.loader.exec_module(module)
    except SystemExit as error:
        return [f"tools/make_public_docs.py refuses to run: {error}"]

    # 1. One public repository, named the same wherever it is typed.
    repository = module.PUBLIC_REPOSITORY
    workflow = ROOT / ".github" / "workflows" / "release.yml"
    workflow_text = workflow.read_text(encoding="utf-8") if workflow.exists() else ""
    installer = (ROOT / "installer" / "Keyflow.iss").read_text(encoding="utf-8")
    names = {
        "tools/make_public_docs.py": re.search(r'PUBLIC_REPOSITORY = "([^"]+)"', generator_path.read_text(encoding="utf-8")),
        ".github/workflows/release.yml": re.search(r"vars\.PUBLIC_RELEASES_REPOSITORY \|\| '([^']+)'", workflow_text),
        "installer/Keyflow.iss": re.search(r"AppPublisherURL=https://github\.com/(\S+)", installer),
        "tools/publish_public.ps1": re.search(r"\$Repository = '([^']+)'",
                                              (ROOT / "tools" / "publish_public.ps1").read_text(encoding="utf-8")
                                              if (ROOT / "tools" / "publish_public.ps1").exists() else ""),
    }
    for name, found in names.items():
        if not found:
            errors.append(f"{name} no longer names the public release repository (expected "
                          f"{repository!r}); a reader of one file would have to guess where releases go")
        elif found.group(1) != repository:
            errors.append(f"{name} points at {found.group(1)}, while tools/make_public_docs.py publishes "
                          f"to {repository} — the installer, the workflow and the public pages disagree")
    for name in ("README.md", "README.en.md", "docs/PRIVATE-SOURCE-PUBLIC-RELEASES.md"):
        path = ROOT / name
        if not path.exists():
            errors.append(f"{name} is missing; it is one of the places a reader is told where releases live")
        elif f"github.com/{repository}" not in path.read_text(encoding="utf-8"):
            errors.append(f"{name} never links to the public release repository {repository}; a reader of "
                          f"the private repository has no way to find where the packages are published")

    # 2. docs/release is exactly what the generator writes.
    try:
        version = module.release_version()
    except SystemExit as error:
        errors.append(f"tools/make_public_docs.py cannot read the release version: {error}")
        return errors
    committed = {path.relative_to(target).as_posix(): path.read_bytes()
                 for path in target.rglob("*") if path.is_file()}
    with tempfile.TemporaryDirectory() as scratch:
        generated_paths = module.write_all(Path(scratch), version)
        generated = {path.relative_to(scratch).as_posix(): path.read_bytes() for path in generated_paths}
    for name in sorted(committed.keys() - generated.keys() - {"docs/previews"}):
        if name.startswith("docs/previews/"):
            continue
        errors.append(f"docs/release/{name} is not something tools/make_public_docs.py writes; the public "
                      f"branch holds documents the project generates and nothing else")
    for name in sorted(generated.keys() - committed.keys()):
        errors.append(f"docs/release/{name} is missing; run `python3 tools/make_public_docs.py`")
    for name in sorted(generated.keys() & committed.keys()):
        if generated[name] != committed[name]:
            errors.append(f"docs/release/{name} does not match tools/make_public_docs.py — regenerate it "
                          f"with `python3 tools/make_public_docs.py` instead of editing it by hand")

    # 3. Nothing on the public branch is source code, and no source file would be copied there either.
    for name in sorted(committed):
        if name == "docs/previews" or name.startswith("docs/previews/"):
            continue
        if name not in PUBLIC_RELEASE_FILES:
            errors.append(f"docs/release/{name} is not in the list of files allowed to be public; if it is "
                          f"meant to travel, add it to PUBLIC_RELEASE_FILES in tools/check_sources.py too")
    for source, destination in module.COPIES:
        source_path = ROOT / source
        if not source_path.exists():
            errors.append(f"tools/make_public_docs.py copies {source}, which is not in the checkout")
        if Path(destination).suffix in module.FORBIDDEN_SUFFIXES:
            errors.append(f"tools/make_public_docs.py would copy {source} to {destination} in the public branch")

    # 4. Every link on the public pages has a destination.
    for name, own in (("README.md", "vi"), ("README.en.md", "en")):
        text = committed.get(name, b"").decode("utf-8")
        covered = set(generated.keys()) | PUBLIC_RELEASE_FILES
        headings = {readme_slug(m.group(2)) for m in re.finditer(r"^(#{1,6})\s+(.*)$", text, re.M)}
        for anchor in re.findall(r"\]\(#([^)\s]+)\)", text):
            if anchor not in headings:
                errors.append(f"docs/release/{name} links to '#{anchor}', which is not a heading there")
        for link in re.findall(r"\]\((?!https?:|#|mailto:)([^)\s]+)\)", text):
            if link in covered:
                continue
            if link.startswith("docs/previews/"):
                # The release step copies the whole docs/previews folder, so any picture CI knows about is
                # there; what still has to hold is the rule the two pages follow in this repository: each
                # edition shows its own language's pictures.
                parts = link.split("/")
                # docs/previews/<lang>/<picture> belongs to one edition; docs/previews/<picture> is shared
                # (the preset gallery is, because it shows both engines side by side).
                if len(parts) >= 4 and parts[2] != own:
                    errors.append(f"docs/release/{name} shows '{link}', which belongs to the other "
                                  f"language's picture set")
                continue
            errors.append(f"docs/release/{name} links to '{link}', which no step copies into the release branch")
    if "docs/previews" not in workflow_text:
        errors.append(".github/workflows/release.yml no longer copies docs/previews into the public branch, "
                      "so every picture on the public pages is a broken link")

    # 5. The public pages say what a downloader has to know, and the workflow still attaches those files.
    attachments = ("publish/*.zip", "publish/SHA256SUMS.txt", "installer/Output/Keyflow-Setup-*.exe")
    for pattern in attachments:
        if pattern not in workflow_text:
            errors.append(f".github/workflows/release.yml no longer attaches {pattern}; the download table "
                          f"on the public pages describes a release that would not carry it")
    for name, language in (("README.md", "vi"), ("README.en.md", "en")):
        text = committed.get(name, b"").decode("utf-8")
        for fragment in ("SHA256SUMS.txt", "Keyflow-Setup-", "win-x64.zip", "win-x64-fd.zip", "win-arm64.zip",
                         "Get-FileHash", "SmartScreen", "LICENSE.txt", "PianoPath.exe",
                         "Salamander Grand Piano V3", "748 MB", "CC BY 3.0"):
            if fragment not in text:
                errors.append(f"docs/release/{name} ({language}) does not mention {fragment!r}; the download "
                              f"section has to name the packages, hashes, MIT licence and optional piano download")
        for obsolete in ("Assets\\ATTRIBUTION.txt", "Assets\\ConcertGrand.sf2", "113 MiB", "Git LFS",
                         "FreePats", "Disklavier Pro"):
            if obsolete in text:
                errors.append(f"docs/release/{name} ({language}) still documents obsolete piano packaging: {obsolete!r}")

    # 6. The token that writes to the public repository is named in one workflow and nothing else.
    token = "PUBLIC_RELEASES_TOKEN"
    holders = []
    for path in sorted(ROOT.glob("**/*")):
        if not path.is_file() or ".git/" in path.as_posix() or "obj" in path.parts or "bin" in path.parts:
            continue
        # The generator is imported by this rule, and this file has to name the token to check it; the
        # scan looks for the token in every other file.
        if path in (generator_path, Path(__file__).resolve()):
            continue
        try:
            if token in path.read_text(encoding="utf-8"):
                holders.append(path.relative_to(ROOT).as_posix())
        except (UnicodeDecodeError, OSError):
            continue
    # Documentation may name it — the runbook tells a maintainer what to configure, and both READMEs
    # explain the model. What must not name it is anything that can act: a workflow, a script, a source
    # file. That is the whole point of the token living in one step of one workflow.
    for name in holders:
        if name.endswith(".md") or name == ".github/workflows/release.yml":
            continue
        errors.append(f"{name} names {token}; the token belongs to the one release step that publishes, "
                      f"and tools/publish_public.ps1 exists so a maintainer needs no stored credential")
    if ".github/workflows/release.yml" not in holders:
        errors.append(f".github/workflows/release.yml no longer names {token}; the automatic publish step "
                      f"cannot be told which credential to use")
    if ".github/workflows/build.yml" in holders:
        errors.append(f".github/workflows/build.yml names {token}; the build job must not be able to reach "
                      f"the public repository at all")

    if "  release:" in workflow_text or 'tags: ["v*"]' in workflow_text:
        errors.append("release.yml must use one tag-creation trigger, not duplicate push/published events")
    if "  create:" not in workflow_text or "--notes $notes --draft" not in workflow_text or "--draft=false" not in workflow_text:
        errors.append("release.yml must trigger on tag creation and upload public assets before publishing its draft")

    if re.search(r"Set-Content[^\n]*-Append", workflow_text, re.I):
        errors.append("release.yml must use Add-Content: PowerShell Set-Content has no -Append parameter")

    # Preparing the public tree must not remove the project or the packages needed by the next step.
    if "$env:GIT_INDEX_FILE" not in workflow_text or "read-tree --empty" not in workflow_text:
        errors.append("release.yml must prepare its public tree with an isolated Git index")
    if any(line.strip().startswith("git checkout --orphan") for line in workflow_text.splitlines()):
        errors.append("release.yml must not replace the source checkout with an orphan documentation branch")
    if "Get-ChildItem -Force | Where-Object { $_.Name -ne '.git' }" in workflow_text:
        errors.append("release.yml must not delete the source worktree before public package publication")

    # 7. Neither place that can push release documents may write main of the source repository. It happened
    # once — on 2026-10-03 `publish_public.ps1 -Repository lxmtuu/PianoPath` replaced main with docs/release
    # and the sources had to be restored from a branch — and the two refusals that came out of it are
    # exactly the kind of thing a later edit deletes because it looks like a special case. It is not: the
    # source repository's main is the product, and no release step has any business writing there.
    guards = (
        ("tools/publish_public.ps1",
         "refusing to push the release documents to refs/heads/main",
         "the script must refuse to write the release documents to main of the source repository"),
        (".github/workflows/release.yml",
         "$repository -ieq $sourceRepository",
         "the publish step must compare the target with this repository before it pushes anything"),
        (".github/workflows/release.yml",
         "$updateMain = $false",
         "the publish step must clear the main update when the target is this repository"),
    )
    for name, marker, why in guards:
        path = ROOT / name
        text = path.read_text(encoding="utf-8") if path.exists() else ""
        if marker not in text:
            errors.append(f"{name} no longer carries the guard {marker!r} ({why}); a release run pointed at "
                          f"this repository would replace main with the release documents again")
    return errors


RELEASE_VERSION = re.compile(r"\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?")
# A changelog entry is "## <version> — <date>"; an "## Unreleased" section is allowed on top and is not
# a version, so it is skipped rather than compared.
CHANGELOG_ENTRY = re.compile(r"^## (?:(Unreleased)|(" + RELEASE_VERSION.pattern + r") — (\d{4}-\d{2}-\d{2}))\s*$")


def scan_release_version(cs_files):
    """One release, one number, and the number has one home.

    ``<Version>`` in ``PianoPath.csproj`` is that home: the SDK stamps it into the assembly, ``AppInfo``
    reads it back for the start-up menu's version label, the About box and the ``--bench`` report. What
    this rule pins are the mirrors that cannot read an assembly — the installer's ``#define AppVersion``
    fallback, the ``git tag`` and ``/DAppVersion=`` examples in both READMEs, and the newest entry of
    both changelogs.

    It exists because the number used to be typed into eight places, the About box being one of them:
    a bump could ship a start-up menu saying one version above an About box saying another, and nothing
    in the repository would have noticed. Two directions, because each one fails differently — every
    mirror has to agree with the project file (a stale mirror prints an old number to exactly the person
    who is checking a download), and no source file may type the number at all (a hardcoded one is a
    mirror no list above would ever name).
    """
    errors = []
    project = ROOT / "PianoPath.csproj"
    if not project.exists():
        return ["PianoPath.csproj is missing; there is no release version to compare anything with"]
    declared = re.search(r"<Version>([^<]+)</Version>", project.read_text(encoding="utf-8"))
    if not declared:
        return ["PianoPath.csproj has no <Version>; publish.ps1, the installer, the About box and both "
                "changelogs all read that one value"]
    version = declared.group(1).strip()
    if not RELEASE_VERSION.fullmatch(version):
        errors.append(f"PianoPath.csproj: <Version>{version}</Version> is not a release number "
                      "(expected MAJOR.MINOR.PATCH, the value git tags as v…)")

    def mirrors(path, pattern, what, flags=0):
        """Every match of ``pattern`` in ``path`` has to be the release version, and there has to be one."""
        if not path.exists():
            errors.append(f"{path.relative_to(ROOT)} is missing; it carries {what}")
            return
        hits = re.findall(pattern, path.read_text(encoding="utf-8"), flags)
        if not hits:
            errors.append(f"{path.relative_to(ROOT)} no longer carries {what}; a reader of that file "
                          f"would have to guess the release number")
            return
        for hit in hits:
            if hit != version:
                errors.append(f"{path.relative_to(ROOT)}: {what} says {hit}, but PianoPath.csproj "
                              f"declares <Version>{version}</Version>")

    script = ROOT / "installer" / "Keyflow.iss"
    mirrors(script, r'#define AppVersion "([^"]+)"', "the installer's AppVersion fallback")
    mirrors(script, r"/DAppVersion=(\S+)", "the /DAppVersion example in the installer's header comment")
    for name, changelog in (("README.md", "CHANGELOG.md"), ("README.en.md", "CHANGELOG.en.md")):
        readme = ROOT / name
        mirrors(readme, r"^git tag v(\S+)$", "the `git tag` example of the release section", re.M)
        mirrors(readme, r"^git push origin v(\S+)$", "the `git push origin` example of the release section", re.M)
        mirrors(readme, r"/DAppVersion=(\S+)", "the /DAppVersion example of the packaging section")
        # A link, not a mention: the point is that a reader who lands on a README can reach the edition's
        # own changelog in one click, and prose naming the file would satisfy a substring test.
        if readme.exists() and f"]({changelog})" not in readme.read_text(encoding="utf-8"):
            errors.append(f"{name} never links to {changelog}; the two language editions each carry their "
                          "own changelog, and a reader who lands on a README has to be able to reach it")

    lists = {}
    for name in ("CHANGELOG.md", "CHANGELOG.en.md"):
        path = ROOT / name
        if not path.exists():
            errors.append(f"{name} is missing; a release has to say what it ships, in both languages")
            continue
        entries = []
        for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            if not line.startswith("## "):
                continue
            entry = CHANGELOG_ENTRY.match(line)
            if not entry:
                errors.append(f"{name}:{number}: “{line}” is not “## <version> — <YYYY-MM-DD>” (or "
                              "“## Unreleased”), so the release number cannot be read out of it")
                continue
            if entry.group(1):
                continue
            entries.append(entry.group(2))
        if not entries:
            errors.append(f"{name} lists no released version")
        elif entries[0] != version:
            errors.append(f"{name}: the newest entry is {entries[0]}, but PianoPath.csproj declares "
                          f"<Version>{version}</Version> — the release and its notes are not the same release")
        lists[name] = entries
    if len(lists) == 2:
        left, right = lists["CHANGELOG.md"], lists["CHANGELOG.en.md"]
        if left != right:
            errors.append(f"CHANGELOG.md lists {left} while CHANGELOG.en.md lists {right}; the two "
                          "editions document the same product, so they list the same releases")

    # No source file types the number. "Keyflow 1.0.0" in a sentence is the About box's old shape: the
    # sentence is a string-table key, so it is printed through Loc.F with {0} and filled from AppInfo.
    # Comments are blanked out first, because the rule is about what the application can print and a
    # doc comment is allowed to name the version a defect used to carry.
    for path in cs_files:
        text = _code(path.read_text(encoding="utf-8"))
        for hit in re.findall(r"Keyflow \d+\.\d+[\d.]*", text):
            errors.append(f"{path.relative_to(ROOT)}: “{hit}” types the release number into a source file; "
                          "print it through AppInfo.Version (a {0} in the string-table key) so the number "
                          "keeps one home")
    info = ROOT / "AppInfo.cs"
    if not info.exists():
        errors.append("AppInfo.cs is missing; it is the one place the running build reads its version from")
    elif "internal static string Version" not in info.read_text(encoding="utf-8"):
        errors.append("AppInfo.cs no longer exposes Version; the start-up menu, the About box and the "
                      "--bench report read the release version from it")
    for name in ("Ui/MainWindow.Menu.cs", "Diagnostics/FrameBenchmark.cs"):
        path = ROOT / name
        if path.exists() and "AppInfo.Version" not in path.read_text(encoding="utf-8"):
            errors.append(f"{name} does not read AppInfo.Version; it is one of the surfaces that print the "
                          "release version, and a version typed here goes stale at the next bump")
    return errors


def scan_media_foundation_keys():
    """The Media Foundation GUIDs that cannot be told apart by eye. The advanced-video-processing key once held the
    GUID of MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS, so the reader never inserted its converter and every camera that
    sends YUY2/NV12/MJPEG failed with 0xC00D5212 — a bug no CI job without a webcam could see."""
    text = (ROOT / "Camera" / "MediaFoundation.cs").read_text(encoding="utf-8")
    found = re.search(r'ReaderAdvancedKey\s*=\s*new\("([0-9A-Fa-f-]+)"\)', text)
    wanted = "0F81DA2C-B537-4672-A8B2-A681B17307A3"   # MF_SOURCE_READER_ENABLE_ADVANCED_VIDEO_PROCESSING
    if not found or found.group(1).upper() != wanted:
        return [f"Camera/MediaFoundation.cs: ReaderAdvancedKey must be {wanted} (MF_SOURCE_READER_ENABLE_ADVANCED_VIDEO_PROCESSING)"]
    return []


def main():
    errors = []
    cs_files = sorted(p for p in ROOT.glob("**/*.cs") if "obj" not in p.parts and "bin" not in p.parts)
    for path in cs_files:
        errors.extend(scan_csharp(path))
    xaml_files = sorted(p for p in ROOT.glob("**/*.xaml") if "obj" not in p.parts and "bin" not in p.parts)
    xaml_errors, keys, names = scan_xaml(xaml_files)
    errors.extend(xaml_errors)
    errors.extend(scan_xaml_bindings(cs_files, xaml_files, names, keys))
    errors.extend(scan_settings_navigation())
    errors.extend(scan_icon_glyphs())
    errors.extend(scan_accessible_names())
    errors.extend(scan_theme_tokens())
    errors.extend(scan_installer())
    errors.extend(scan_readme())
    errors.extend(scan_markdown_tables())
    errors.extend(scan_gpu_overlay_evidence())
    errors.extend(scan_wisp_motion_contract())
    errors.extend(scan_stage_clock_contract())
    errors.extend(scan_flame_motion_contract())
    errors.extend(scan_particle_flow_contract())
    errors.extend(scan_held_emission_parity())
    errors.extend(scan_note_width_parity())
    errors.extend(scan_option_matrix())
    errors.extend(scan_option_numeric())
    errors.extend(scan_option_pose())
    errors.extend(scan_audit_matrix_claims())
    errors.extend(scan_refraction_contract())
    errors.extend(scan_ar_material_review())
    errors.extend(scan_ar_effect_family_review())
    errors.extend(scan_preset_roundtrip())
    errors.extend(scan_review_sheets())
    errors.extend(scan_previews_folder())
    errors.extend(scan_cli_and_samples())
    errors.extend(scan_generated_assets())
    errors.extend(scan_phase1_scene_fixture())
    errors.extend(scan_preset_shelf())
    localization_errors, keys_used, keys_inventory = scan_localization(cs_files)
    errors.extend(localization_errors)
    errors.extend(scan_dead_keys(cs_files, xaml_files))
    errors.extend(scan_release_version(cs_files))
    errors.extend(scan_project_files())
    errors.extend(scan_media_foundation_keys())
    errors.extend(scan_public_release())
    print(f"checked {len(cs_files)} C# files and {len(xaml_files)} XAML files, {len(keys)} resource keys, {len(names)} named elements")
    print("checked the dock navigation catalogue against the XAML tab strip, the icon glyph templates, the theme tokens against App.xaml and every README link")
    print("checked the command-line switches against the README table and the preview workflow, and the generated sample against its script")
    print("checked the Phase 1 scene fixture's frame timing, tempo-map tail, four review checkpoints (readability, two hands, pedal-on, rest) and three design-only preset targets")
    print("checked the community preset shelf against the settings class and against tools/make_presets.py, the script that writes it")
    print("checked the installer language file against the generated Inno Setup message list, the language metadata and the {cm:...} captions")
    print(f"checked the string tables against one another, against the {keys_used} keys the sources print ({keys_inventory} in the inventory) and against the sources that hold them")
    print("checked the release version of PianoPath.csproj against the installer, both READMEs, both changelogs and every source that prints it")
    print("checked every Markdown table's delimiter row, and the claim about what is still a WPF layer over a GPU frame against the engine that draws it")
    print("checked that the wisp sway lives in one curve and that the stage, the AR quads and the alignment review all draw it")
    print("checked that the AR hold-family page drives the contract's five families, one switch and one range per family, and that CI runs and stages it")
    print("checked that slow motion scales the particles on both renderers and leaves the held bar, rings and flashes on the real clock")
    print("checked that the held-key flame keeps the engine's law on the software stage instead of a plume pinned to the key")
    print("checked that W1's flow stays one law called by both integrators, and that the slider gates it instead of replacing the shipped stream")
    print("checked that a held key lets off the engine's amount of embers and wisps on both renderers, ceilings aside")
    print("checked that the falling note and the held bar size themselves from one Note width slider, with no floor of their own")
    print("checked that every option the settings offer is captured by the N12(b) matrix on both renderers, off state included")
    print("checked that the effect audit no longer calls an option untested once the matrix has a picture of it")
    print("checked that the refraction page promises exactly the materials the shader reads NoteB.w in, on both renderers")
    print("checked that the AR material page walks the typed contract for its materials, settings and expected bindings")
    print("checked that every numeric slider the plan shoots is the dock's own range, clamped by the settings class, sampled at both ends")
    print("checked that every shipped preset is saved and reopened through the product's own preset file and applies the dock's own path")
    print("checked that the four N12 reviews still compose one contact sheet each, that a sheet refuses to be missing a capture, and that the workflow commits it")
    print("parsed every MSBuild file (project, test project, publish profiles) the way MSBuild itself would")
    print("checked the public release documents against their generator, their links, the token that "
          "publishes them, and the guards that keep a release run out of main of the source repository")
    if errors:
        print(f"\n{len(errors)} problem(s):")
        for e in errors:
            print("  " + e)
        return 1
    print("no bracket, quote, XML, resource-reference, naming or binding problems found")
    return 0


if __name__ == "__main__":
    sys.exit(main())
