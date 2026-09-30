#!/usr/bin/env python3
"""Regenerate ``installer/Languages/messages.txt`` from Inno Setup's own ``Default.isl``.

``installer/Languages/Vietnamese.isl`` is a *partial* translation: it replaces the wording of the
messages the installer actually shows and lets every other message fall back to English. Inno Setup
only prints a *warning* for a message name it does not recognize ("Message name "X" in "Y" is not
recognized by this version of Inno Setup. Ignoring.") and then drops that line — a typo therefore
ships English text where Vietnamese was intended, with a green build. The compiler is just as quiet
about a name that is right but in the wrong section, since ``[Messages]`` and ``[CustomMessages]``
are separate namespaces. The list of names that really exist therefore has to be a checked-in file
next to the translation it guards.

    python3 tools/inno_messages.py            # rewrite installer/Languages/messages.txt

The script fetches ``Files/Default.isl`` from the official Inno Setup repository with the GitHub API
(the only host this sandbox can reach) for the tags below and writes the *intersection* of their
message names per section, so every name is valid for the oldest Inno Setup the installer script
claims to support and for the newest release. When Inno Setup 7.2 is out, add ``is-7_2_0`` to
``TAGS`` and run this again: a message that disappeared upstream (or was renamed, or moved section)
shows up as a diff instead of as English text in a Vietnamese dialog.

Each line is ``name=placeholders``, grouped under the same ``[Messages]``/``[CustomMessages]``
headers the compiler reads. The placeholders are the ``%1``/``%n``/``[name]`` tokens the English
message uses, which is what ``tools/check_sources.py`` compares the translation against, so a
translation cannot silently drop ``%1`` or ``[name]``. A leading ``!`` marks the names
``installer/Languages/Vietnamese.isl`` translates; the script fails loudly — instead of quietly
keeping a dead line — when one of those names no longer exists upstream.
"""
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
TARGET = ROOT / "installer" / "Languages" / "messages.txt"
TRANSLATION = ROOT / "installer" / "Languages" / "Vietnamese.isl"

# The oldest Inno Setup the installer script supports, the newest 6.x release and the newest release
# overall. Keep this list short: it is the contract between the repository and the compiler.
TAGS = ["is-6_3_3", "is-6_7_3", "is-7_1_0"]

# The two sections that hold text: [Messages] is Inno Setup's own wizard text, [CustomMessages] the
# `{cm:...}` texts the script asks for (the shortcut task and the "launch" checkbox). [LangOptions]
# is not wizard text and lives in installer/Keyflow.iss.
SECTIONS = ["Messages", "CustomMessages"]

# Matches the argument placeholders Inno Setup substitutes: %1 … and the [name] / [name/ver] style
# constants. Everything else in a message is literal text.
PLACEHOLDER = re.compile(r"%\d+|%n|\[[a-z/]+\]")


def read_isl(source) -> dict:
    """Return {section: {name: text}} for an .isl file, given its path or its bytes."""
    raw = source.read_bytes() if isinstance(source, Path) else source
    sections, current = {}, None
    for line in raw.decode("utf-8-sig").splitlines():
        line = line.strip()
        if not line or line.startswith(";"):
            continue
        header = re.match(r"^\[(\w+)\]$", line)
        if header:
            current = header.group(1)
            sections.setdefault(current, {})
            continue
        if "=" in line and current is not None:
            name, value = line.split("=", 1)
            sections[current][name.strip()] = value.strip()
    return sections


def default_isl(tag: str) -> dict:
    """Return {section: {message name: english text}} for Files/Default.isl at ``tag``."""
    raw = subprocess.run(
        ["gh", "api", "-H", "Accept: application/vnd.github.raw",
         f"repos/jrsoftware/issrc/contents/Files/Default.isl?ref={tag}"],
        check=True, capture_output=True).stdout
    return read_isl(raw)


def main() -> int:
    per_tag = {tag: default_isl(tag) for tag in TAGS}
    ours = read_isl(TRANSLATION)

    # A name is valid when every tag defines it in the same section: a partial translation has to work
    # with all of them, and a message that moved from [Messages] to [CustomMessages] on the way would
    # silently stop overriding.
    valid = {}
    for section in SECTIONS:
        shared = set.intersection(*(set(per_tag[tag].get(section, {})) for tag in TAGS))
        valid[section] = {name: per_tag[TAGS[-1]][section][name] for name in sorted(shared)}

    unknown, misplaced = [], []
    for section, messages in ours.items():
        for name in messages:
            if section not in SECTIONS:
                misplaced.append(f"{name} (in [{section}])")
            elif name not in valid[section]:
                hit = next((other for other in SECTIONS if name in valid[other]), None)
                (misplaced if hit else unknown).append(f"{name} ({section} → {hit})" if hit else f"{name} ({section})")
    if unknown or misplaced:
        print(f"{TARGET.relative_to(ROOT)} was not written. {TRANSLATION.name} translates messages"
              " Default.isl does not define there:")
        for item in unknown:
            print(f"  unknown  {item}")
        for item in misplaced:
            print(f"  section  {item}")
        print("Inno Setup would only warn about the unknown ones and then ship English text, and a name"
              " in the wrong section never overrides anything — fix the name/section, or drop the line"
              " if the message really is gone.")
        return 1

    lines = [
        "; Valid wizard-text names and placeholders for the installer, per section.",
        ";",
        "; Generated by tools/inno_messages.py — do not edit by hand. Source: Files/Default.isl at",
        f"; {', '.join(TAGS)} in https://github.com/jrsoftware/issrc; this is the intersection, so every",
        "; name below is valid whatever Inno Setup version compiles the script.",
        ";",
        "; name=placeholders  placeholders are the %1/%n/[name]-style tokens the English message uses",
        "; !name=             installer/Languages/Vietnamese.isl translates this message",
        ";",
    ]
    for section in SECTIONS:
        lines.append(f"[{section}]")
        for name, english in valid[section].items():
            mark = "!" if name in ours.get(section, {}) else ""
            signature = ",".join(sorted(set(PLACEHOLDER.findall(english))))
            lines.append(f"{mark}{name}={signature}")
        lines.append("")
    TARGET.write_text("\n".join(lines).rstrip("\n") + "\n", encoding="utf-8")

    counts = ", ".join(f"{len(valid[section])} in [{section}]"
                       f" ({len(ours.get(section, {}))} translated)" for section in SECTIONS)
    print(f"wrote {TARGET.relative_to(ROOT)}: {counts}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
