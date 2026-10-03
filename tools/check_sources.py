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
  * the installer: every message name the Vietnamese wizard text overrides exists in the Inno Setup
    the script claims to support, keeps the placeholders of the English message, and is reachable
    from the ``[Languages]``/``[LangOptions]`` sections that Inno Setup reads (see
    ``tools/inno_messages.py``);
  * the generated documentation assets: ``docs/samples`` still matches the script that builds it.

Run it from the repository root (``python tools/check_sources.py``); CI runs it before the Windows
build so a typo is caught in seconds instead of in a full Windows job. A .NET SDK is not needed.
"""
import json
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
    rendered = set(re.findall(r"Name\s*=\s*'([^']+\.png)'", workflow.read_text(encoding="utf-8"))) if workflow.exists() else set()
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
                if relative.split("/")[2] != own:
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
# folder may hold pictures only; diagnostics go outside the repository (see ``App.ReportPreview``).
PREVIEW_PICTURES = {".png", ".jpg", ".jpeg"}


def scan_previews_folder():
    folder = ROOT / "docs" / "previews"
    if not folder.exists():
        return []
    return [f"{path.relative_to(ROOT).as_posix()}: docs/previews may hold pictures only, because CI commits everything in it on every run; "
            "write diagnostics outside the repository (see App.ReportPreview)"
            for path in sorted(folder.rglob("*")) if path.is_file() and path.suffix.lower() not in PREVIEW_PICTURES]


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
    shots = re.search(r"\$shots = @\((.*?)\n\s*\)\n", workflow.read_text(encoding="utf-8"), re.S)
    if not shots:
        return errors + ["build.yml lost the $shots list that renders the README previews"]
    block = shots.group(1)
    # The preset gallery renders every built-in look by name (spaces dropped, as --preset accepts them).
    gallery = re.search(r"\$presets = @\(([^)]*)\)", workflow.read_text(encoding="utf-8"))
    presets_source = ROOT / "Stage" / "VisualPresets.cs"
    if gallery and presets_source.exists():
        text = presets_source.read_text(encoding="utf-8")
        default = re.search(r'DefaultPresetName = "([^"]+)"', text)
        names = re.findall(r'^\s*new\("([^"]+)", "', text, re.M) + ([default.group(1)] if default else [])
        wanted = {name.replace(" ", "") for name in names}
        listed = set(re.findall(r"'([^']+)'", gallery.group(1)))
        for name in sorted(wanted - listed):
            errors.append(f"build.yml's preset gallery leaves out the built-in preset {name}")
        for name in sorted(listed - wanted):
            errors.append(f"build.yml's preset gallery renders {name}, which is not a built-in preset")
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
    script = ROOT / "tools" / "make_stage_background.py"
    target = ROOT / "docs" / "samples" / "stage-backdrop.png"
    if not script.exists():
        return [str(script.relative_to(ROOT)) + " is missing; it is the licence and the recipe of the sample backdrop"]
    if not target.exists():
        return [str(target.relative_to(ROOT)) + " is missing; regenerate it with `python3 tools/make_stage_background.py`"]
    width, height = (int(n) for n in re.search(r"^W, H = (\d+), (\d+)", script.read_text(encoding="utf-8"), re.M).groups())
    head = target.read_bytes()
    if not head.startswith(b"\x89PNG\r\n\x1a\n"):
        return [str(target.relative_to(ROOT)) + " is not a PNG"]
    fields = struct.unpack(">IIBBBBB", head[16:16 + 13])
    if (fields[0], fields[1], fields[2], fields[3]) != (width, height, 8, 2):
        errors.append(f"{target.relative_to(ROOT)} is {fields[0]}x{fields[1]} at {fields[2]} bit colour type {fields[3]}, "
                      f"but tools/make_stage_background.py writes {width}x{height} at 8 bit colour type 2 (RGB) — "
                      f"regenerate it instead of editing the picture by hand")
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
    "Assets/ATTRIBUTION.txt", "docs/LOCALIZATION.md", "VERSION",
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
                         "Get-FileHash", "SmartScreen", "LICENSE.txt", "Assets\\ATTRIBUTION.txt", "Assets\\"):
            if fragment not in text:
                errors.append(f"docs/release/{name} ({language}) does not mention {fragment!r}; the download "
                              f"section has to name the packages, the hashes and the files that travel with them")

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
    errors.extend(scan_previews_folder())
    errors.extend(scan_cli_and_samples())
    errors.extend(scan_generated_assets())
    errors.extend(scan_preset_shelf())
    localization_errors, keys_used, keys_inventory = scan_localization(cs_files)
    errors.extend(localization_errors)
    errors.extend(scan_dead_keys(cs_files, xaml_files))
    errors.extend(scan_release_version(cs_files))
    errors.extend(scan_project_files())
    errors.extend(scan_public_release())
    print(f"checked {len(cs_files)} C# files and {len(xaml_files)} XAML files, {len(keys)} resource keys, {len(names)} named elements")
    print("checked the dock navigation catalogue against the XAML tab strip, the icon glyph templates, the theme tokens against App.xaml and every README link")
    print("checked the command-line switches against the README table and the preview workflow, and the generated sample against its script")
    print("checked the community preset shelf against the settings class and against tools/make_presets.py, the script that writes it")
    print("checked the installer language file against the generated Inno Setup message list, the language metadata and the {cm:...} captions")
    print(f"checked the string tables against one another, against the {keys_used} keys the sources print ({keys_inventory} in the inventory) and against the sources that hold them")
    print("checked the release version of PianoPath.csproj against the installer, both READMEs, both changelogs and every source that prints it")
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
