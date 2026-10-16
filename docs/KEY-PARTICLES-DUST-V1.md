# Dust V1 — soft pigment motes in Stage / AR

## Status

Native CI and published-pixel/browser checks pass. **Default-off, review-only; Stage held visibility is not yet acceptable as a settled direction.** Previous candidates are not promoted. Accepted Wisps/Flames stay frozen; auxiliary morph/release follows; settings last.

## Material contract

- Internal `DustArtForReview` is not bound to settings, presets or menu. Eligibility additionally requires current selected base family `ImpactBurst == "Dust"`, non-wisp primitive 3. Zone-selected Dust under another base family and live family switches are not covered by this candidate gate. Tag 29; one sprite / one AR quad per selected physical particle, unchanged order.
- No changes to emitter/RNG, cap, selection, positions, size, velocity, drift, gravity, drag or lifetime. Stage keeps its old kind-0 square (half extent `size × 2.4 × 1.8`); AR retains its original `size × 2.4` square, including projection clamps. The diagnostic restores only candidate tags and intended particle RGBA, not any position/axis/weight/phase.
- Shared bounded, birth-hue-derived linear pigment. Actual RGBA is identity-linked before canonicalisation and compared between Stage and three AR poses. Dust retains its own soft amplitude `.35 × clamp(birthGlow × ParticleGlow,0,1) × (1-age/life)`. This unifies old Stage RGB fading and AR alpha fading; it is an intentional material/opacity change, **not** a promise of unchanged tail brightness.
- A shared smooth Gaussian surface with a compact radial cutoff; no white-hot centre, grain animation, screen-time flicker, glitter or additional age fade. Uses raw particle-size coordinates, so different old quad extents do not arbitrarily stretch the surface. Radius ends at 2.2 raw-size units, fitting inside both unchanged quads. AR's existing world-size clamp can still affect projected apparent size.
- This is a soft material, not new 3D depth geometry, a new depth-sorting model or a new motion style. Overlapping bursts remain possible because emission/spread is deliberately untouched. Legacy Stage's white contribution could dominate low-energy dust; candidate may be substantially dimmer, especially on camera backgrounds. No visibility improvement is assumed before native inspection.

## Verification plan

192 native statics (off/classic/candidate × two palettes × two pitches × four views × burst/held/released/expired), 64 paired raw/ordered geometry/count guards; 720 original motion crops (Stage/reference AR × burst/held-release × 60 frames × off/classic/candidate). Candidate actual particle RGBA parity is checked against Stage at all static AR poses. Active candidate statics must contribute a nonzero native off-delta; motion must contribute at least six nonblank candidate frames, not merely differ from classic by disappearing. This is a nonblank guard, not proof of good visibility. Motion preserves physical release before step 12; pedal must not keep the emitter running.

Publication cap **2.5 MiB**: two exact native ROI quartets, four lossless BGRA XOR+gzip 60-frame bundles, gzip metadata. No quantisation, resampling or frame dropping. Full original/off frames stay artifact-only. No new PNG compaction is planned: existing snapshot headroom must be measured again after publication under the cumulative 128 MiB cap.

## Verified native evidence

- [CI **38770323618**](https://github.com/lxmtuu/PianoPath/actions/runs/38770323618) **SUCCESS**, source `9fa8a032dd73ca66b844f1338048fcb8a8f0d944`; preview publication `a877534`. Static and portable tests, Windows build, native capture generation, publication validation, frame budget, galleries and package passed. Earlier run `38770075304` was superseded/cancelled during setup; it is not counted as a native success. The follow-up added nonblank controls, not a visual retune.
- **192 statics / 64 paired raw-state, ordered geometry/selection/count guards / 720 native motion crops** passed on Windows WARP. Actual candidate intrinsic RGBA, linked to raw identities, matches Stage and all three AR poses in the static matrix. Native nonblank statics and at least six nonblank candidate motion frames pass too; this is not a visibility/quality score.
- Local published subset: **19 source hashes, 8 exact static panes, 480 exact motion halves** pass. Four tracks retain all 60 frames at manifest 30 FPS; full original/off frames were checked in CI, not locally downloaded/revalidated.
- **Real headless Chromium 153**: all 480 decoded native BGRA halves match metadata; four mode release/step/seek controls, play/pause, no page errors and 390px static-page mobile overflow check pass. This is actual browser decoding/control QA, not a mocked DOM, native WPF GUI, realtime FPS, camera or physical GPU result.
- **80 local Python tests**, source checker (322 C# / 2 XAML / 119 resources / 161 named elements), Python/JavaScript syntax and diff checks pass. C#/HLSL execution was on Windows CI, not a local synthetic renderer.
- Seven regenerated historical families match frozen native pixel/frame digests: **320 fields each for Wisps/Flames** (64 full statics + 16 sheets + 240 motion frames), **1,642 each for Embers V1/V2, Splash V1, Fireworks V1 and Confetti V1** (192 statics + 720 crop hashes + 720 full-frame hashes + 2 sheets + 8 panes). **8,850 digest fields unchanged**, excluding source provenance; these are not counts of distinct captures. No prior art is promoted or changed.
- Published Dust payload **560,122 bytes / 0.5342 MiB**, below 2.5 MiB. Cumulative session changed-file payload approximately **125.87 MiB**, below 128 MiB. No additional compaction, frame deletion, resampling or quantisation was needed.

### Same-scene native signal · Prism / pitch 60

| View / phase | Packed classic / V1 | RGB L1 off-delta classic → V1 | Pixels max ΔRGB > 4 classic → V1 |
|---|---|---|---|
| Stage / burst | 35 / 35 | 67,528 → 21,897 | 142 → 50 |
| Stage / held | 14 / 14 | 81,920 → 12,446 | 272 → 140 |
| AR reference / burst | 35 / 35 | 12,503 → 3,356 | 79 → 43 |
| AR reference / held | 14 / 14 | 7,947 → 6,617 | 173 → 124 |

L1 is **absolute** RGB off-difference, including darkening/occlusion, not emitted energy, luminance or quality. Smaller AR L1 does not imply visually dimmer dust: V1 can be a brighter warm patch than classic on a bright key or later above the keys. Compare the actual native pair, not the metric alone. Intrinsic parity does not imply equal composited appearance across modes.

### Inspection and next decision

Inspected both native static quartets at 8× nearest-neighbour enlargement, sixteen same-time samples across each of the four motion tracks, plus the real Chromium player screenshot. V1 replaces Stage's white-hot core with a narrower warm, soft cloud. The original overlapping births/low-speed paths still merge into a compact cloud/arc, not fine separated motes. This iteration does **not** establish a new 3D depth/sorting model.

**Stage held is too faint to call a settled improvement.** The sampled held off-delta falls to about 15% of classic and later candidate tails can be hard to read against black, even while nonblank checks pass. Native AR burst can instead look brighter/less darkening than classic at some frames because of compositing and narrower coverage. Do not compensate by blindly raising a global brightness constant from L1 alone; both modes and press/held need another native comparison.

Next priority is a bounded material-only Dust visibility pass, preserving zero-glow control, smooth colour/fade, simulation/geometry and intrinsic Stage/AR parity. It must be independently rendered/reviewed before promotion, then auxiliary morph/release; settings remains last. This V1 is preserved as a review checkpoint, **not enabled by default or aesthetically approved**.

Real camera/MIDI, physical GPU performance, dense chords, Zone routes/live preset switches, every palette/tier/delay, bloom/tint/spill/recording, realtime cadence and **user art acceptance remain open**. Native motion here covers reference AR and Stage only; oblique/near-overhead are static checks. Local contract tests do not substitute for native rendering.

Evidence: [burst quartet](previews/dust-art-v1/burst.png), [held quartet](previews/dust-art-v1/held.png), [gzip manifest](evidence/dust-art-v1/manifest.json.gz). Classic left / V1 right; Stage above / reference AR below.

```bash
# Pillow required; validate the native subset and serve on 0.0.0.0.
python3 tools/serve_dust_particle_art.py --output /tmp/dust-review --port 8775
CHROME_BIN=/path/to/chrome NODE_PATH=/path/to/deps/node_modules \
  node tools/check_burst_particle_review_browser.cjs http://127.0.0.1:8775
```
