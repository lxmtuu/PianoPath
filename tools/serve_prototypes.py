#!/usr/bin/env python3
"""Serve the browser prototypes in docs/prototypes from one port.

    python3 tools/serve_prototypes.py            # http://localhost:8080/
    python3 tools/serve_prototypes.py --port 8090

The prototypes are plain files (no build, no dependency), so `python3 -m http.server`
is enough to look at one of them. This script is the one address for all of them,
because the pages link to each other and to the Markdown analyses with relative
paths that only resolve when the server's root is the repository:

  /                       → the particle lab's A/B page (the page 8080 used to show)
  /particle-motion-lab/   → docs/prototypes/particle-motion-lab/
  /note-view-lab/         → docs/prototypes/note-view-lab/
  /damper.html            → the damper page, at the URL DAMPER-BAR-LIGHT-ANALYSIS.md quotes
  /DAMPER-BAR-LIGHT-ANALYSIS.md, /NOTE-EFFECTS-ACTION-PLAN.md, …
                          → the analysis documents those pages link to
  anything else           → the repository, so evidence paths and images work too

Two things are deliberate: every response is `Cache-Control: no-store`, because a
prototype is edited and reloaded in the same minute and a stale `note-view.js` looks
exactly like a bug in the prototype; and a path is only ever read from inside the
repository, so `/../../etc/passwd` is a 404 rather than a file.

Nothing here is product code or evidence: the pages simulate the renderers in
JavaScript and say so themselves.
"""
from __future__ import annotations

import argparse
import mimetypes
import os
import posixpath
import urllib.parse
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PROTOTYPES = ROOT / "docs" / "prototypes"

# Directories that are served from docs/prototypes instead of the repository root.
LABS = ("particle-motion-lab", "note-view-lab")
# Names the analysis documents quote (`http://localhost:8080/damper.html`) and the
# files those pages load without a directory part.
ALIASES = {
    "/damper.html": PROTOTYPES / "particle-motion-lab" / "damper.html",
    "/seemusic.html": PROTOTYPES / "particle-motion-lab" / "seemusic.html",
    "/damper.js": PROTOTYPES / "particle-motion-lab" / "damper.js",
    "/seemusic.js": PROTOTYPES / "particle-motion-lab" / "seemusic.js",
    "/lab.js": PROTOTYPES / "particle-motion-lab" / "lab.js",
}

# Markdown is a document to read here, not a download.
mimetypes.add_type("text/markdown; charset=utf-8", ".md")
mimetypes.add_type("text/javascript; charset=utf-8", ".js")
mimetypes.add_type("text/css; charset=utf-8", ".css")


class Handler(SimpleHTTPRequestHandler):
    """Repository-rooted files, with the labs and the quoted URLs mapped onto them."""

    server_version = "KeyflowPrototypeLabs"

    def end_headers(self):
        # A prototype edited between two reloads must never come back from cache.
        self.send_header("Cache-Control", "no-store")
        self.send_header("X-Content-Type-Options", "nosniff")
        super().end_headers()

    def _redirect_root(self) -> bool:
        if self.path.split("?", 1)[0] not in ("/", "/index.html"):
            return False
        # Redirect rather than serve: the lab's own links climb two directories
        # (`../../DAMPER-BAR-LIGHT-ANALYSIS.md`), and they only resolve from a
        # directory URL. Serving the file at "/" makes every such link a 404.
        self.send_response(302)
        self.send_header("Location", "/particle-motion-lab/")
        self.send_header("Content-Length", "0")
        self.end_headers()
        return True

    def do_GET(self):
        if not self._redirect_root():
            super().do_GET()

    def do_HEAD(self):
        # A preview proxy may ask with HEAD before it shows the page; answering 200
        # here and 302 there would describe the same URL two ways.
        if not self._redirect_root():
            super().do_HEAD()

    def translate_path(self, path):
        clean = urllib.parse.unquote(urllib.parse.urlsplit(path).path)
        # Collapse "." and ".." before mapping, so the result can be checked once.
        clean = posixpath.normpath("/" + clean.lstrip("/"))
        if clean == "/":
            clean = "/particle-motion-lab/"

        target = None
        for lab in LABS:
            if clean == f"/{lab}" or clean.startswith(f"/{lab}/"):
                target = PROTOTYPES / clean.lstrip("/")
                break
        if target is None:
            target = ALIASES.get(clean)
        if target is None:
            target = ROOT / clean.lstrip("/")
            if not target.exists() and clean.endswith(".md"):
                # The pages link to `../../NAME.md`; once the ".." is collapsed that
                # is `/NAME.md`, while the file lives in docs/.
                target = ROOT / "docs" / clean.lstrip("/")

        try:
            resolved = target.resolve()
            resolved.relative_to(ROOT)
        except (OSError, ValueError):
            resolved = ROOT / "__not_found__"
        return str(resolved)

    def log_message(self, format, *args):  # noqa: A002 - the base class names it `format`
        # One line per request is noise for a page that reloads a dozen files; keep
        # only what is not a 200/206/304, which is what a broken link looks like.
        status = args[1] if len(args) > 1 else ""
        if str(status).startswith(("2", "3")):
            return
        super().log_message(format, *args)


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description="Serve the Keyflow browser prototypes from the repository root.")
    parser.add_argument("--port", type=int, default=8080, help="port to listen on (default 8080)")
    parser.add_argument("--bind", default="0.0.0.0", help="interface to bind (default 0.0.0.0, so the preview proxy can reach it)")
    parser.add_argument("--quiet", action="store_true", help="only print the first line")
    args = parser.parse_args(argv)

    if not (PROTOTYPES / "particle-motion-lab" / "index.html").is_file():
        parser.error(f"no prototypes at {PROTOTYPES}; run this from a checkout of the repository")

    server = ThreadingHTTPServer((args.bind, args.port), partial(Handler, directory=str(ROOT)))
    print(f"Serving Keyflow prototype labs on http://{args.bind}:{args.port}/", flush=True)
    if not args.quiet:
        print("  /                    particle lab: current vs v2, GPU vs WPF", flush=True)
        print("  /note-view-lab/      note view lab: WPF, GPU and AR side by side", flush=True)
        print("  /damper.html         damper light A/B (the URL the analysis quotes)", flush=True)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
