# Phase 1 visual prototype

This is a **design-only HTML canvas mock**, not PianoPath UI and not an app renderer. It loads the shared deterministic scene from `docs/samples/visual-effects-phase1-scene.json`; slider values do not write settings/presets. The canvas and its colors are not evidence of WPF/GPU/AR behavior.

From the repository root, run:

```bash
python3 -m http.server 8000 --bind 0.0.0.0 --directory docs
```

Then open `http://localhost:8000/prototypes/visual-effects-phase1/`. The page has three target looks, a fixed scene scrubber, play/pause, and a same-frame A/B comparison. Selecting **AR · future** is intentionally disabled; it does not open a camera or imply calibration support.

Design decisions and approval checklist: [`../../VISUAL-EFFECTS-DESIGN-BIBLE.md`](../../VISUAL-EFFECTS-DESIGN-BIBLE.md) and [`../../VISUAL-EFFECTS-STUDIO-WIREFRAME.md`](../../VISUAL-EFFECTS-STUDIO-WIREFRAME.md).
