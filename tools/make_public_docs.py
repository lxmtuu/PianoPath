#!/usr/bin/env python3
"""Writes the documents of the public release repository: ``docs/release/``.

The source repository is private and the release repository is public, but the product page must exist
only once. If somebody copied README.md across by hand, the two would drift on the first edit — the very
thing ``tools/check_sources.py`` exists to prevent. So the public page is **generated** from this
repository's README, the generated files are committed under ``docs/release/``, and the static check
compares every byte with what this script writes (the same arrangement ``presets/`` has with
``tools/make_presets.py``).

One public page is assembled like this:

* the head of the README (title, introduction, pictures) is kept as it is;
* a banner is added saying this is the release repository and the source lives elsewhere;
* a **Downloading a release** section is added: the four packages, ``SHA256SUMS.txt``, SmartScreen, and
  the files that have to stay next to ``PianoPath.exe``;
* the table of contents is rebuilt from the sections that survive;
* the sections that describe the *product* survive (system requirements, first-time setup, features,
  languages, interface map, limitations, testing, licence) and the ones that only make sense with the
  source in front of you are dropped (installing tools, getting the source, building, re-rendering
  pictures, packaging, technical documentation, repository layout). Sections are dropped **by title**,
  so renaming one in the source README stops this script instead of silently losing a whole subject.

What is deliberately *not* generated here: ``docs/previews``. CI renders fresh pictures on every push,
so they are copied into the release branch by ``release.yml`` itself at release time; the static check
only asserts that the step doing it exists.

Run from the repository root:

    python3 tools/make_public_docs.py            # writes docs/release/
    python3 tools/make_public_docs.py --out /tmp/x
"""

from __future__ import annotations

import argparse
import re
import shutil
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

# The public release repository. This is the third place that types its name; the other two are the
# PUBLIC_REPOSITORY default in .github/workflows/release.yml and the installer's AppPublisherURL, and
# scan_public_release in tools/check_sources.py makes those (plus both READMEs and the runbook under
# docs/) agree on one name.
PUBLIC_REPOSITORY = "lxmtuu/PianoPath-Releases"
RELEASES_URL = f"https://github.com/{PUBLIC_REPOSITORY}/releases"
LATEST_URL = f"{RELEASES_URL}/latest"
ISSUES_URL = f"https://github.com/{PUBLIC_REPOSITORY}/issues"

# The sections that survive on the public page, by their exact ``##`` titles in the source README.
# Subsections (``###``) travel with their parent. The list is the written-down version of one decision:
# somebody downloading a package needs to know whether their PC runs it, how to set it up, what it does,
# which languages it speaks, what the interface looks like, what is still missing, that the verification
# suite runs from the very .exe they downloaded, and what the licence is.
KEPT_SECTIONS = {
    "vi": (
        "Yêu cầu hệ thống",
        "Thiết lập lần đầu",
        "Bắt đầu sử dụng",
        "Bàn phím & thao tác nhanh",
        "Chức năng",
        "Đa ngôn ngữ",
        "Bản đồ giao diện",
        "Giới hạn hiện tại",
        "Kiểm thử",
        "Giấy phép",
    ),
    "en": (
        "System requirements",
        "First-time setup",
        "Getting started",
        "Keyboard & shortcuts",
        "Features",
        "Languages",
        "Interface map",
        "Current limitations",
        "Testing",
        "Licence",
    ),
}

DOCUMENTS = {
    "vi": "README.md",
    "en": "README.en.md",
}

# Files copied across verbatim. Deliberately short: everything here is something a person who downloaded
# a package needs to read (the licence, the attribution, the changelog, the document about the two
# languages), and nothing here is source code.
COPIES = (
    ("CHANGELOG.md", "CHANGELOG.md"),
    ("CHANGELOG.en.md", "CHANGELOG.en.md"),
    ("LICENSE", "LICENSE"),
    ("Assets/ATTRIBUTION.txt", "Assets/ATTRIBUTION.txt"),
    ("docs/LOCALIZATION.md", "docs/LOCALIZATION.md"),
)

# Subsections dropped from a section that is kept. The *Testing* section stays because it says the
# verification suite runs from the very .exe a user just downloaded; its three subsections only make
# sense with the source in front of you (measuring frame times, running xUnit, running the Python
# checker), so they go. These titles are matched character for character on purpose.
DROPPED_SUBSECTIONS = {
    "vi": {
        "Kiểm thử": (
            "Cổng hiệu năng (`--bench`)",
            "Bộ test chạy trên mọi máy (`tests/PianoPath.Tests`)",
            "Kiểm tra tĩnh (chạy được trên mọi máy, kể cả không có .NET SDK)",
        ),
    },
    "en": {
        "Testing": (
            "The frame budget gate (`--bench`)",
            "The test suite that runs on any machine (`tests/PianoPath.Tests`)",
            "Static checks (run anywhere, even without the .NET SDK)",
        ),
    },
}

# Passages that have to be rewritten because they point at a section the public page does not carry.
# They are replaced rather than dropped: ``](anchor)`` aimed at a removed section is a dead link, and
# check_links() below refuses to write one, so forgetting a rewrite turns red before a release. The
# strings are matched character for character — when the source README rewords one of them this script
# stops, and whoever edits the README decides what the sentence should say now.
REWRITES = {
    "vi": (
        ("Kho bạn đang đọc là **kho mã nguồn riêng tư**; gói phát hành và trang sản phẩm chỉ nằm ở kho kia\n"
         "> (xem [`docs/PRIVATE-SOURCE-PUBLIC-RELEASES.md`](docs/PRIVATE-SOURCE-PUBLIC-RELEASES.md)).",
         "Kho bạn đang đọc là **kho mã nguồn riêng tư**: đây là nơi giữ mã nguồn, còn gói phát hành và\n"
         "> trang sản phẩm chỉ nằm ở kho kia."),
        ("Xem [Đóng gói và xuất file .exe](#đóng-gói-và-xuất-file-exe).",
         "Xem [Tải bản phát hành](#tải-bản-phát-hành)."),
        ("Muốn làm mới sau khi sửa\n> giao diện: xem [Tạo lại ảnh giao diện](#tạo-lại-ảnh-giao-diện).",
         "Ảnh được render lại mỗi khi giao diện đổi,\n> rồi chép sang kho này ở mỗi lần phát hành."),
        ("cùng một tài liệu, cùng ảnh (do CI render) và cùng bảng tham số dòng lệnh; `tools/check_sources.py`\n"
         "> kiểm cả hai nên không bản nào lệch khỏi bản kia.",
         "cùng một tài liệu và cùng ảnh (do CI render); cả hai được sinh từ một bản gốc nên\n"
         "> không bản nào lệch khỏi bản kia."),
    ),
    "en": (
        ("The repository you are reading is the **private source repository**; packages and the product\n"
         "> page live in that other one (see [`docs/PRIVATE-SOURCE-PUBLIC-RELEASES.md`](docs/PRIVATE-SOURCE-PUBLIC-RELEASES.md)).",
         "The repository you are reading is the **private source repository**: the source lives here, while\n"
         "> the packages and the product page live in that other one."),
        ("See [Packaging and shipping the .exe](#packaging-and-shipping-the-exe).",
         "See [Downloading a release](#downloading-a-release)."),
        ("after a UI change, see [Rendering the interface pictures again](#rendering-the-interface-pictures-again).",
         "the pictures are rendered again whenever the\n> interface changes and copied here on every release."),
        ("This is the English translation of the same document. Every\n"
         "> screenshot below is rendered by the application itself, so the two files always show the same build.",
         "Both language editions are generated from one\n"
         "> source, and every screenshot below is rendered by the application itself, so the two files always\n"
         "> show the same build."),
    ),
}

# A generated folder may never hold source code: the release branch is public, so a source file landing
# there is an unintended publication. scan_public_release() asserts the same thing about the committed
# docs/release/ tree.
FORBIDDEN_SUFFIXES = (".cs", ".xaml", ".csproj", ".sln", ".pubxml", ".iss", ".ps1", ".py", ".hlsl", ".sf2", ".yml")


def read(relative: str) -> str:
    return (ROOT / relative).read_text(encoding="utf-8")


def release_version() -> str:
    """``<Version>`` of ``PianoPath.csproj`` — the one home every mirror of the release number reads."""
    declared = re.search(r"<Version>([^<]+)</Version>", read("PianoPath.csproj"))
    if not declared:
        raise SystemExit("PianoPath.csproj has no <Version>; there is no release to document")
    return declared.group(1).strip()


def split_sections(text: str):
    """Split a README into (head, [(title, whole section), …]) on the ``## `` lines.

    ``###`` subsections stay inside their parent's text, which is what lets a section be kept or dropped
    as one unit and a subsection be removed from a kept parent.
    """
    lines = text.splitlines(keepends=True)
    head: list[str] = []
    blocks: list[list[str]] = []
    current: list[str] | None = None
    for line in lines:
        if line.startswith("## "):
            if current is not None:
                blocks.append(current)
            current = [line]
        elif current is None:
            head.append(line)
        else:
            current.append(line)
    if current is not None:
        blocks.append(current)
    return "".join(head), [(block[0][3:].strip(), "".join(block)) for block in blocks]


def drop_subsections(language: str, heading: str, block: str) -> str:
    """Remove the subsections listed in ``DROPPED_SUBSECTIONS`` from a section that is kept."""
    wanted = DROPPED_SUBSECTIONS.get(language, {}).get(heading)
    if not wanted:
        return block
    kept: list[str] = []
    dropping = False
    seen: set[str] = set()
    for line in block.splitlines(keepends=True):
        if line.startswith("### "):
            title = line[4:].strip()
            dropping = title in wanted
            if dropping:
                seen.add(title)
        if not dropping:
            kept.append(line)
    missing = [title for title in wanted if title not in seen]
    if missing:
        raise SystemExit(
            f"{DOCUMENTS[language]}: subsection(s) {missing} of '{heading}' are not in the file any more. "
            f"Update DROPPED_SUBSECTIONS in tools/make_public_docs.py in the same change.")
    return "".join(kept)


def slug(heading: str) -> str:
    """GitHub's heading anchor: lower case, punctuation dropped, spaces to dashes."""
    text = heading.strip().lower()
    text = re.sub(r"[^\w\s\-À-ỹ]", "", text, flags=re.UNICODE)
    return re.sub(r"\s", "-", text)


def download_title(language: str) -> str:
    """The download section's title — the one place the banner, the table of contents and the anchor
    check all read the name from."""
    return "Tải bản phát hành" if language == "vi" else "Downloading a release"


def banner(language: str, version: str) -> str:
    """The first thing a reader sees: this is the release repository, and the source is not public."""
    tag = f"v{version}"
    if language == "vi":
        return (
            f"> **Đây là kho phát hành, không phải kho mã nguồn.** Kho này chỉ chứa bản dựng và tài liệu\n"
            f"> người dùng của Keyflow; mã nguồn nằm ở một kho riêng và không được công khai. Mọi tệp ở đây\n"
            f"> do quy trình phát hành của kho nguồn sinh ra tại đúng tag `{tag}` — xem\n"
            f"> **[Tải bản phát hành](#tải-bản-phát-hành)** bên dưới, hoặc\n"
            f"> **[Releases]({LATEST_URL})** cho bản mới nhất, và **[Issues]({ISSUES_URL})** để báo lỗi.\n"
            f">\n"
            f"> *Đừng sửa tệp trong kho này bằng tay*: mỗi lần phát hành, nhánh này được dựng lại từ kho\n"
            f"> nguồn nên mọi thay đổi viết tay sẽ bị ghi đè.\n"
        )
    return (
        f"> **This is the release repository, not the source repository.** It carries only Keyflow's built\n"
        f"> packages and its user documentation; the source lives in a private repository. Everything here\n"
        f"> is produced by the source repository's release pipeline at exactly tag `{tag}` — see\n"
        f"> **[Downloading a release](#downloading-a-release)** below, **[Releases]({LATEST_URL})** for the\n"
        f"> newest one, and **[Issues]({ISSUES_URL})** to report a defect.\n"
        f">\n"
        f"> *Do not edit files in this repository by hand*: the branch is rebuilt from the source repository\n"
        f"> on every release, so hand edits are overwritten.\n"
    )


def download_section(language: str, version: str) -> str:
    """The download section: four packages, how to check a hash, and what has to travel with the .exe.

    The package names carry the release version, which is also why regenerating this folder is part of
    bumping ``<Version>``: a stale copy would name last release's files.
    """
    if language == "vi":
        return f"""## {download_title(language)}

Mỗi mốc phát hành (`v{version}` và các bản sau) là một mục trong **[Releases]({RELEASES_URL})**, kèm
`SHA256SUMS.txt` của mọi tệp đính kèm. Bản mới nhất: **[{LATEST_URL}]({LATEST_URL})**.

| Tệp | Là gì | Máy đích cần gì |
| --- | --- | --- |
| `Keyflow-{version}-win-x64.zip` | Bản portable **self-contained**: giải nén là chạy `PianoPath.exe` | Không cần cài gì thêm |
| `Keyflow-Setup-{version}.exe` | **Bộ cài** cho Windows x64: shortcut Start Menu/Desktop, mục gỡ cài đặt, wizard Anh/Việt | Không cần cài gì thêm |
| `Keyflow-{version}-win-x64-fd.zip` | Bản **framework-dependent**, nhẹ hơn nhiều | [.NET 10 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/10.0) |
| `Keyflow-{version}-win-arm64.zip` | Windows on ARM, self-contained (CI publish; chưa máy ARM nào chạy thử) | Không cần cài gì thêm |

Cài bằng bộ cài thì xong; dùng bản ZIP thì **giải nén cả thư mục rồi chạy `PianoPath.exe`** — đừng
tách `.exe` ra khỏi thư mục của nó:

```
Keyflow-{version}-win-x64\\
├── PianoPath.exe              ← file chạy duy nhất
├── LICENSE.txt                ← giấy phép MIT, phải đi kèm bản sao
└── Assets\\
    ├── ConcertGrand.sf2       ← SoundFont ~113 MiB, phải nằm cạnh .exe
    └── ATTRIBUTION.txt        ← ghi công FreePats (CC BY 3.0)
```

- **Kiểm tệp tải về**: mỗi release kèm `SHA256SUMS.txt`; đối chiếu bằng
  `Get-FileHash .\\Keyflow-{version}-win-x64.zip -Algorithm SHA256`. Các gói **chưa ký số** nên đây là
  cách duy nhất để chắc tệp nhận được đúng là tệp quy trình trên build ra.
- **SmartScreen**: vì chưa ký số, lần chạy đầu Windows hiện *"Windows protected your PC"* — chọn
  **More info → Run anyway**.
- **Giữ giấy phép**: `LICENSE.txt` (MIT) và `Assets\\ATTRIBUTION.txt` (CC BY 3.0 của SoundFont) đã nằm
  trong mỗi gói; đừng xoá chúng khi chia sẻ lại.
- **Nhật ký thay đổi**: [CHANGELOG.md](CHANGELOG.md) (bản tiếng Anh: [CHANGELOG.en.md](CHANGELOG.en.md))
  — mỗi bản một mục, ghi những gì người dùng nhìn thấy.
- **Bộ kiểm chứng**: `PianoPath.exe --verify --verify-log=%TEMP%\\keyflow-verify.log` chạy ngay trên máy
  bạn vừa tải về và in từng mục PASS/FAIL (mục cần phần cứng không có sẽ ghi SKIP, không phải lỗi).
"""

    return f"""## {download_title(language)}

Every release (`v{version}` and later) is a GitHub **Release** holding four packages and a
`SHA256SUMS.txt` covering all of them: **[{LATEST_URL}]({LATEST_URL})**.

| File | What it is | What the target PC needs |
| --- | --- | --- |
| `Keyflow-{version}-win-x64.zip` | Portable **self-contained** build: unzip and run `PianoPath.exe` | Nothing else |
| `Keyflow-Setup-{version}.exe` | **Installer** for Windows x64: Start Menu/Desktop shortcuts, an uninstall entry, an English/Vietnamese wizard | Nothing else |
| `Keyflow-{version}-win-x64-fd.zip` | **Framework-dependent** build, far smaller | [.NET 10 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/10.0) |
| `Keyflow-{version}-win-arm64.zip` | Windows on ARM, self-contained (published by CI; no ARM machine has run it yet) | Nothing else |

Run the installer and you are done; for a ZIP, **unzip the whole folder and run `PianoPath.exe`** — never
separate the `.exe` from its folder:

```
Keyflow-{version}-win-x64\\
├── PianoPath.exe              ← the single executable
├── LICENSE.txt                ← the MIT licence, which has to travel with copies
└── Assets\\
    ├── ConcertGrand.sf2       ← the ~113 MiB SoundFont; it must sit next to the .exe
    └── ATTRIBUTION.txt        ← FreePats credit (CC BY 3.0)
```

- **Checking a download**: each release carries `SHA256SUMS.txt`; compare with
  `Get-FileHash .\\Keyflow-{version}-win-x64.zip -Algorithm SHA256`. The packages are **not code-signed**,
  so this is the only way to be sure the file that arrived is the file the pipeline built.
- **SmartScreen**: because nothing is signed, the first launch shows *"Windows protected your PC"* —
  choose **More info → Run anyway**.
- **Keep the licences**: `LICENSE.txt` (MIT) and `Assets\\ATTRIBUTION.txt` (the SoundFont's CC BY 3.0)
  ship inside every package; do not remove them when passing a copy on.
- **What changed**: [CHANGELOG.en.md](CHANGELOG.en.md) (Vietnamese edition: [CHANGELOG.md](CHANGELOG.md))
  — one entry per release, listing everything a user can see.
- **The verification suite**: `PianoPath.exe --verify --verify-log=%TEMP%\\keyflow-verify.log` runs on the
  machine you just downloaded to and prints PASS/FAIL per subject (a subject that needs hardware you do
  not have reports SKIP, which is not a failure).
"""


def toc(language: str, headings) -> str:
    """The table of contents, rebuilt from the headings that survive — a stale entry is a dead link."""
    title = "Mục lục" if language == "vi" else "Contents"
    lines = [f"## {title}", ""]
    for level, text in headings:
        lines.append(f"{'  ' if level == 3 else ''}- [{text}](#{slug(text)})")
    return "\n".join(lines) + "\n"


def apply_rewrites(language: str, text: str) -> str:
    for old, new in REWRITES[language]:
        if old not in text:
            raise SystemExit(
                f"{DOCUMENTS[language]}: the passage to rewrite was not found:\n  {old!r}\n"
                f"The source README changed; check whether the new wording still points at a section the "
                f"public page does not carry, and update REWRITES in tools/make_public_docs.py.")
        text = text.replace(old, new, 1)
    return text


def check_links(language: str, text: str, headings: set[str]) -> None:
    """Every link on the public page has to have a destination: an anchor in the page, or a file the
    release branch carries.

    Preview pictures are the one exception — CI re-renders them after every push and ``release.yml``
    copies them into the release branch, so the static check only asserts that the copying step exists.
    """
    for anchor in sorted(set(re.findall(r"\]\(#([^)\s]+)\)", text))):
        if anchor not in headings:
            raise SystemExit(
                f"{DOCUMENTS[language]} would link to '#{anchor}', which is not a heading of the public "
                f"page. Add the section to KEPT_SECTIONS, or rewrite the sentence in REWRITES.")
    copied = {name for _, name in COPIES} | set(DOCUMENTS.values()) | {"VERSION"}
    for target in sorted(set(re.findall(r"\]\((?!https?:|#|mailto:)([^)\s]+)\)", text))):
        if target in copied or target.startswith("docs/previews/"):
            continue
        raise SystemExit(
            f"{DOCUMENTS[language]} would link to '{target}', which no step copies into the release branch. "
            f"Either add it to COPIES (if it may be published) or rewrite the sentence in REWRITES.")


def build_readme(language: str, version: str) -> str:
    """Assemble one public page: keep the introduction, add the download section, drop the dev sections."""
    head_text, sections = split_sections(read(DOCUMENTS[language]))
    kept_names = KEPT_SECTIONS[language]
    found = {heading for heading, _ in sections}
    missing = [name for name in kept_names if name not in found]
    if missing:
        raise SystemExit(
            f"{DOCUMENTS[language]}: section(s) {missing} are not in the file any more. A section that "
            f"moved or was renamed has to be deleted from KEPT_SECTIONS in tools/make_public_docs.py on "
            f"purpose — otherwise the public page would silently lose a whole subject.")

    # The head of the source README is the title, the introduction and the first pictures. The banner
    # goes right under the title, before the introduction: it is the first sentence a reader needs.
    lines = head_text.splitlines(keepends=True)
    title_at = next((i for i, line in enumerate(lines) if line.startswith("# ")), None)
    if title_at is None:
        raise SystemExit(f"{DOCUMENTS[language]} has no '# ' title line; the public page would start mid-sentence")
    head = "".join(lines[: title_at + 1])
    intro = "".join(lines[title_at + 1 :])

    kept_blocks = [(heading, drop_subsections(language, heading, block))
                   for heading, block in sections if heading in kept_names]

    # The table of contents lists what survives, in document order, with the download section first
    # because that is the first heading a reader meets after the introduction.
    ordered: list[tuple[int, str]] = [(2, download_title(language))]
    for heading, block in kept_blocks:
        ordered.append((2, heading))
        for line in block.splitlines():
            if line.startswith("### "):
                ordered.append((3, line[4:].strip()))

    parts = [
        head, "\n",
        banner(language, version), "\n",
        intro, "\n",
        download_section(language, version), "\n",
        toc(language, ordered), "\n",
    ]
    parts.extend(block for _, block in kept_blocks)
    text = "".join(parts)
    # Inserting those blocks leaves runs of three or more blank lines behind; tidy them so the public
    # page reads like every other document in the project.
    text = re.sub(r"\n{3,}", "\n\n", text)
    text = apply_rewrites(language, text)
    headings = {slug(title) for _, title in ordered}
    # The two headings this script writes itself are valid anchors too.
    headings |= {slug("Mục lục" if language == "vi" else "Contents")}
    check_links(language, text, headings)
    return text if text.endswith("\n") else text + "\n"


def write_all(out: Path, version: str) -> list[Path]:
    """Write the whole release repository into ``out`` and return what was written."""
    if out.exists():
        shutil.rmtree(out)
    written: list[Path] = []
    out.mkdir(parents=True)
    for language, name in DOCUMENTS.items():
        path = out / name
        path.write_text(build_readme(language, version), encoding="utf-8")
        written.append(path)
    for source, target in COPIES:
        path = out / target
        path.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(ROOT / source, path)
        written.append(path)
    # The branch says which release it is, so nobody has to open the README to find out.
    version_file = out / "VERSION"
    version_file.write_text(version + "\n", encoding="utf-8")
    written.append(version_file)
    for path in sorted(written):
        if path.suffix in FORBIDDEN_SUFFIXES:
            raise SystemExit(f"{path.name} is a source file; the release repository carries documents only")
    return written


def main() -> int:
    parser = argparse.ArgumentParser(description="Write the public-release documents (docs/release/).")
    parser.add_argument("--out", default=str(ROOT / "docs" / "release"),
                        help="output folder (default: docs/release)")
    parser.add_argument("--quiet", action="store_true")
    arguments = parser.parse_args()
    version = release_version()
    written = write_all(Path(arguments.out), version)
    if not arguments.quiet:
        print(f"wrote {len(written)} file(s) for Keyflow {version} into {arguments.out}")
        for path in written:
            print(f"  {path}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
