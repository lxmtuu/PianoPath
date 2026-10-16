# Rà soát toàn kho — 2026‑10‑16 (sau phát hành 2.1.1)

> Trả lời câu hỏi **"còn gì chưa xong?"**. Đây là bản rà soát mới, không thay `docs/REPO-AUDIT.md`
> (bản đó tự ghi là *ảnh chụp lịch sử*). Mọi kết luận dưới đây kèm bằng chứng lấy tại chỗ trong phiên
> này: `git`/`gh` trên HEAD `18b10d8`, `python3 tools/check_sources.py` chạy thật, và annotation của lần
> CI mới nhất trên `main`. Những gì sandbox này **không** kiểm được thì nói rõ ở §8, không đoán.

## 0. Kết luận một câu

**Code và pipeline đang lành; thứ chưa xong nằm ở ba chỗ:** (1) tài liệu **nói sai về chính kho này và về
pipeline** — hai README mô tả một bước `release.yml` đã bị bỏ, sổ tay vận hành có tham chiếu treo và bảng
trạng thái cũ (riêng câu "kho này private" đã được chủ repo đóng bằng cách **đổi kho sang private thật**
ngay trong phiên này), (2) **tài liệu kế hoạch lệch pha với code** (`docs/ROADMAP.md` còn ghi WebM "chưa
có" trong khi `Video/WebmRecorder.cs` đã chạy), và (3) **nhóm việc cần máy thật** — mọi bằng chứng hiện có
là WARP/synthetic, chưa có GPU thật, webcam thật, OBS, ASIO hay ARM. Ngoài ra: 1 hiệu ứng `Planned`, slice
**AR‑S8 (HUD)** chưa có code, và 62 nhánh remote chưa dọn.

**Đã sửa ngay trong phiên này** (6 tệp, `tools/check_sources.py` xanh, `docs/release/` không đổi một byte):
hai README viết lại đúng hành vi thật của `release.yml`; sổ tay `docs/PRIVATE-SOURCE-PUBLIC-RELEASES.md` cập
nhật trạng thái private + kết quả kiểm secret/cây kho công khai; `docs/ROADMAP.md`,
`docs/EFFECT-COVERAGE-AUDIT.md` và `docs/REPO-AUDIT.md` được gắn mốc/đo lại để hết lệch pha với code
(§3 bên dưới nay chỉ còn là **bản ghi những gì đã sửa**, không còn việc mở). Xem §9.

## 1. Đã kiểm và **xanh** (không còn việc gì ở đây)

| Việc kiểm | Kết quả đo tại chỗ |
|---|---|
| `python3 tools/check_sources.py` trên HEAD | **xanh**, exit 0, ~12 s: 349 tệp C#, 2 XAML, 119 resource key, 174 named element, 1482 khoá nguồn in ra (1653 trong inventory); chuỗi/installer/phiên bản/README/MSBuild/trang công khai đều khớp |
| CI trên `main` (run **38900310173**, commit `5caafd3`, merge PR #70) | **success** cả `static` · `test` · `build` · `keyboard-light`; `ar-budget`/`key-ensemble`/`app-build` **skipped** vì là job opt-in theo cờ `[ar-budget-native]`/`[ensemble-native]`/`[app-build]` trong commit message (`build.yml:27`) — không phải lỗi |
| Bộ kiểm chứng trong app | `Verification passed: **7910** assertions, **1** skipped check group(s)`; `PianoPath.exe exited with code 0 after 232 log line(s)` (annotation của job `build` 116970794533) |
| Bộ test tách riêng | `tests/PianoPath.Tests`: 110 tệp, **687 `[Fact]` + 128 `[Theory]`**, 109 `<Compile Include>`; job `test` trên `ubuntu-latest` xanh |
| Ảnh README | bốn bước `Render README previews` → `Upload` → `Commit refreshed previews` → `Report preview drift` đều **success**; commit cuối trên `main` chính là `18b10d8 Refresh the README previews from CI [skip ci]` ⇒ **không còn lệch preview** (căn bệnh của đợt rà soát 2026‑10‑01 đã đóng) |
| Phát hành 2.1.1 | tag `v2.1.1`; release **published** (không draft, không pre-release) 2026‑10‑16T09:15:19Z với đủ 5 asset: `Keyflow-2.1.1-win-x64.zip` (71,3 MB), `-win-x64-fd.zip` (3,1 MB), `-win-arm64.zip` (55,9 MB), `Keyflow-Setup-2.1.1.exe` (52,0 MB), `SHA256SUMS.txt` |
| Trang phát hành công khai | `lxmtuu/KeyFlow` **có** release `Keyflow 2.1.1` (latest, 2026‑10‑16T09:15:53Z) ⇒ đường publish công khai đã chạy thật, không còn là "chờ lần tag kế tiếp" như `docs/REPO-AUDIT.md` §7.4 từng ghi |
| Phiên bản nhất quán | `<Version>2.1.1</Version>` (`PianoPath.csproj:20`) = installer = hai README = hai CHANGELOG (checker ghim bằng `scan_release_version`) |
| Nợ trong mã nguồn | **0** `TODO`/`FIXME`/`HACK`/`XXX`/`NotImplementedException` trong `.cs`/`.xaml`/`.hlsl`/`.py`/`.ps1`/`.yml` (grep toàn kho; chỉ 4 lần nhắc tới chúng trong văn bản `docs/REPO-AUDIT.md`) |
| Issue / PR | `gh issue list --state open` rỗng, `gh pr list --state open` rỗng |
| Dọn dẹp tức thời | `git status` sạch; `CHANGELOG.md` mục `## Unreleased` **rỗng** ⇒ không có thay đổi đã merge mà chưa ghi nhật ký |

## 2. Chưa xong #1 — README mâu thuẫn với chính kho này *(đã đóng một phần ngay trong phiên này)*

> **Cập nhật 2026‑10‑16, sau khi bản rà soát này được đọc:** chủ repo đã **đổi kho nguồn sang private**,
> nên dòng 1 của bảng dưới **không còn đúng** — câu "Kho này là private" trong hai README nay khớp thực tế
> (`gh api repos/lxmtuu/PianoPath --jq .private` → `true`). Ba dòng còn lại **vẫn là việc có thật** và đã
> được sửa trong phiên này: mô tả nhánh orphan trong hai README, tham chiếu treo `§2a`, và bảng trạng thái
> đã cũ của sổ tay. Chi tiết từng chỗ sửa ở §9.

Đây là lần đầu một đợt rà soát thấy **tài liệu người dùng nói sai sự thật kiểm được bằng một lệnh API**,
và `tools/check_sources.py` **không có luật nào** nhìn thấy nó.

| Phát hiện | Bằng chứng |
|---|---|
| ~~**README ghi kho này là private — nó đang public.**~~ **Đã đóng 2026‑10‑16** bằng cách đổi visibility thật của kho, không phải bằng cách sửa câu văn | `README.md:108` "Kho này là **private**…"; `README.en.md:121` cùng nội dung. Lúc rà soát: `gh api … --jq .private` → `false`. Sau khi chủ repo đổi: → **`true`**, 0 fork, 2 sao |
| **Tham chiếu treo**: sơ đồ ở `docs/PRIVATE-SOURCE-PUBLIC-RELEASES.md:22` viết "public từ 2026-10-03, **xem §2a**" — tệp **không có** mục `§2a` nào | `grep -n "^## \|^### " docs/PRIVATE-SOURCE-PUBLIC-RELEASES.md` không có mục 2a; grep "2a" chỉ ra đúng một lần nhắc đó |
| **Hai README mô tả một bước workflow đã bị bỏ.** `README.md:486` / `README.en.md:532`: "workflow còn **dựng nhánh phát hành công khai**: một nhánh *orphan* `release/public-<tag>` **trong kho này**… rồi đẩy nhánh đó sang lxmtuu/KeyFlow" | `release.yml:56‑110` nay dựng tree tài liệu trong **Git index riêng** (`GIT_INDEX_FILE`), comment ghi rõ *"No auxiliary branch is created or pushed in the private source repository"*; `docs/PRIVATE-SOURCE-PUBLIC-RELEASES.md:203` cũng xác nhận "workflow **không còn** push nhánh tài liệu `release/public-*` vào kho nguồn". Hai nhánh `release/public-v2.0.0` / `-v2.0.1` còn trên remote là **tàn dư** của cách làm cũ (API compare trả `404 No common ancestor` — đúng nghĩa nhánh orphan) |
| Vì sao checker không bắt được | `scan_public_release` so `docs/release/` với **máy sinh** (`tools/make_public_docs.py`) và kiểm liên kết/anchor/token — nó không đối chiếu *tuyên bố về trạng thái kho* hay *hành vi workflow* với thực tế. Đây là đúng loại lỗ hổng mà repo đã vá một lần cho ảnh preview (bước `Report preview drift`, `docs/REPO-AUDIT.md` §6.6) |

**Phạm vi may mắn là hẹp:** `tools/make_public_docs.py` **cắt bỏ** cả hai mục chứa câu sai
(`## Tải mã nguồn` và mục workflow/"Hai kho, một sản phẩm") khỏi trang công khai — grep
`docs/release/README.md` + `README.en.md` cho `private`/`orphan`/`release/public-` trả **0 kết quả**. Vậy
người dùng cuối không đọc phải câu sai; chỉ người đóng góp đọc README trong kho nguồn mới gặp.

**Việc cần làm:** sửa 4 chỗ trong `README.md`/`README.en.md`, viết **§2a** cho sổ tay
`docs/PRIVATE-SOURCE-PUBLIC-RELEASES.md` hoặc bỏ tham chiếu treo đó, và — nếu muốn giữ luật "mỗi tuyên bố
một lớp kiểm" — thêm vào `tools/check_sources.py` một luật nhỏ đối chiếu câu "kho này private/public"
với một nguồn sự thật nằm trong repo thay vì để nó trôi. Sau khi sửa README vẫn nên chạy
`python3 tools/make_public_docs.py` để xác nhận `docs/release/` không đổi (checker so từng byte).

## 3. Chưa xong #2 — tài liệu kế hoạch lệch pha với code *(✅ đã đóng trong phiên này)*

Không có dòng nào dưới đây làm hỏng build; chúng làm **hỏng việc đọc repo để biết nên làm gì tiếp**,
mà chính `docs/ROADMAP.md` §0 coi là luật ("một việc chỉ xong khi tài liệu viết lại").

> **Đã sửa hết bảng dưới**, theo một nguyên tắc: tài liệu *lịch sử* (`docs/REPO-AUDIT.md`) thì **không viết
> lại** — chỉ thêm lời dẫn nói rõ loại câu nào đã hết hiệu lực và số hiện hành là bao nhiêu; tài liệu *kế
> hoạch* (`docs/ROADMAP.md`, `docs/EFFECT-COVERAGE-AUDIT.md`) thì sửa thẳng vào dòng, kèm ngày đối chiếu và
> bằng chứng đo tại chỗ. Mọi con số mới đều được **đo lại** trong phiên này chứ không chép từ tài liệu cũ.

| Tệp | Câu đã cũ | Thực tế đo được |
|---|---|---|
| `docs/ROADMAP.md:37` (P1 #2) | "*Còn lại*: WebM/VP9 ghi trực tiếp (cần sidecar ffmpeg)" | **đã có**: `Video/WebmRecorder.cs` + `Video/WebmEncoderPlan.cs` (+ test `WebmEncoderPlanTests.cs`), và `README.md` mô tả "WebM (VP9 + Opus) cũng ghi trực tiếp… bật Transparent background thì chính tệp WebM giữ kênh alpha (yuva420p)" |
| `docs/ROADMAP.md:93` (§6 Khối lượng ước đoán) | bảng v0.5 → v1.0, "v1.0 … 4–6 tuần" | repo đang ở **2.1.1**; bảng này là kế hoạch của một năm phiên bản trước, không còn mốc nào đối chiếu được |
| `docs/REPO-AUDIT.md` §6.3 (dòng 173–178) | 5 mục P3 đánh dấu "⬜ chưa làm" | chính §6.11/§6.14 của **cùng tệp** ghi #2 và #3 đã xong; #4/#5/#7 cũng đã xong/một phần theo `docs/ROADMAP.md` §4. Tệp có nhãn "ảnh chụp lịch sử" ở đầu, nhưng bảng ⬜ vẫn là thứ người đọc lướt gặp |
| `docs/REPO-AUDIT.md:118, 156, 458, 466` | nói về con trỏ LFS `Assets/ConcertGrand.sf2` (134 byte) và `publish.ps1 -AllowLfsPointer` | `Assets/` nay chỉ có `Keyflow.ico` + `KeyflowLogo.png`; không có `.gitattributes`, không có `git lfs`; mô hình đã đổi sang Salamander tải riêng (đúng như lời cảnh báo ở đầu tệp — nhưng các dòng cụ thể chưa được đánh dấu là đã hết hiệu lực) |
| `docs/EFFECT-COVERAGE-AUDIT.md` (khối "Ngày audit gốc") | "lần chạy hiện tại rà **219** file C#" | checker in ra **349** tệp C# |
| `docs/EFFECT-COVERAGE-AUDIT.md` (khối "Delta local chờ kiểm thử 2026‑10‑10") | `ArRisingMidiNotes` "đang ở working tree, nhưng không có .NET/Windows toolchain" | setting đã **vào mã và vào verify**: `Diagnostics/VerificationSuite.cs:1049, 1055‑1057, 1628` (5 tệp tham chiếu) |
| `docs/ROADMAP.md` §3 nhóm **Hiệu ứng** | "Hoàn thành các mục `EffectStatus.Planned` trong `Stage/Effects/EffectCatalog.cs`" (số nhiều) | chỉ còn **1** mục `Planned` trong 88: `falling.color-gradient` (`EffectCatalog.cs:37`), đã chốt đưa vào Phase 4 |

## 4. Chưa xong #3 — tính năng thật sự còn thiếu trong sản phẩm

Xếp theo "có code thiếu" chứ không theo "tài liệu nói thiếu".

| # | Việc | Bằng chứng còn thiếu |
|---|---|---|
| 1 | **AR‑S8 — HUD** (đếm nốt, pedal, tên nốt, vạch quãng tám, light bars) | `ArElement.Hud` và `ArElement.OctaveLines` **đã có trong catalogue** (`Camera/ArElementCatalog.cs:47, 51`) kèm tier và family, nhưng grep toàn kho **không có** `ArHud*.cs`, không có setting/toggle/UI nào cho HUD hay octave lines ⇒ catalogue khai báo phần tử chưa tồn tại. `docs/AR-OVERLAY-ROADMAP.md` §14.10 để AR‑S8 không dấu ✅ |
| 2 | **AR‑S9** — AR‑2 dò bàn phím tự động, AR‑2b camera từ phía người chơi, AR‑4 che tay + ánh sáng/bóng, AR‑5 theo dõi từng ngón (ONNX), AR‑6.5 webcam ảo | §13 của lộ trình AR: cả năm dòng là ❌; §14.10 AR‑S9 chưa có dấu ✅ |
| 3 | **Ma trận AR‑S5** — 14 family/parameter ở mức `Unverified` | `docs/EFFECT-COVERAGE-AUDIT.md` có **47** lần `Unverified`/`Unsupported`/`Unmeasured`; WARP mới sample "selected paths", chưa phủ mọi style/value/lifecycle |
| 4 | **`falling.color-gradient`** — chuyển màu đầu→đuôi thân nốt | mục `Planned` duy nhất của catalogue (`Stage/Effects/EffectCatalog.cs:37`); `ColorMode=Gradient` hiện là gradient **theo pitch**, không thay được |
| 5 | **Khuông nhạc vẽ bằng GPU** | `README.md` *Giới hạn hiện tại*: "trên sân khấu GPU, khuông nhạc, điểm đánh dấu bàn tay, hình camera và bộ đếm/FPS **vẫn là lớp vector WPF** phủ lên khung GPU"; `docs/ROADMAP.md` §3 liệt kê là "còn lại" |
| 6 | **Particle compute shader · depth of field · motion blur từ velocity buffer · camera keyframe** | cùng dòng giới hạn của README + `docs/ROADMAP.md` §3; grep không thấy compute shader nào trong `Gpu/StageShaders.hlsl` |
| 7 | **MusicXML nâng cao**: dấu nghỉ nhiều tầng (mỗi bè một lớp như bản khắc), repeat, nhiều bè ghép phách | `README.md` *Giới hạn hiện tại* ghi thẳng "phần còn thiếu của lớp nhập bài là MusicXML nâng cao"; `docs/ROADMAP.md` P1 #6 "*Còn lại*: dấu nghỉ nhiều tầng" |
| 8 | **Stream mẫu Salamander từ đĩa** (hiện giữ mẫu đã giải mã trong RAM) | `README.md`: "mẫu giải nén được **giữ trong RAM**; cần đủ bộ nhớ cho bộ đàn sau khi tải"; `docs/ROADMAP.md` §3 Âm thanh: "**Còn mở**: streaming sample Salamander từ đĩa để giảm RAM" |
| 9 | **Audio-reactive bằng FFT thật** (low/mid/high spectrum) | `Audio/Fft.cs` hiện **chỉ** được `Audio/ConvolutionReverb.cs` dùng (grep `Fft` ngoài tệp định nghĩa: chỉ reverb + test); `docs/EFFECT-COVERAGE-AUDIT.md` §5 P1 #9: "tách `AudioReactive` envelope hiện tại khỏi mục tiêu FFT thật; chỉ quảng bá spectrum sau khi đường PCM → analyzer → smoothing → render đã được kiểm thử" |
| 10 | **`IStageRenderer` / tách nhánh portability phần hình** | `IAudioSink` **đã có** (`Audio/AudioSink.cs` + 4 sink WASAPI/ASIO/waveOut), nhưng `IStageRenderer` grep **0 kết quả**; `MainWindow` vẫn tạo thẳng `PianoAudioEngine`, `PianoStage` giữ nhánh GPU nội bộ (`docs/ROADMAP.md` §4 #6 🟡) |
| 11 | **Chuẩn hoá ID hiệu ứng (Phase 2)** + `Spotlights` đang là choice ngoài 87 ID | `docs/EFFECT-COVERAGE-AUDIT.md` §5 P1 #6; `EffectCatalog` vẫn là inventory/metadata, **không** phải nguồn runtime (quyết định Phase 0, §5 P0 #1) |
| 12 | **Ký mã · MSIX · manifest winget · auto-update cài thật** | `docs/CODE-SIGNING.md`: "**chưa ký mã**", SignPath Foundation đã từ chối đơn (2026‑10‑10), `release.yml` không còn bước ký ⇒ người tải vẫn gặp SmartScreen; `docs/ROADMAP.md` §4 #5 🟡 "chưa có MSIX, manifest winget… cần tài khoản/chứng chỉ"; kiểm tra cập nhật trong app **chỉ đọc số phiên bản**, không tải/không cài |
| 13 | **Theo dõi bàn tay = thị giác cổ điển** | `Camera/HandTracker.cs`: lưới 32×24 + luật màu da YCbCr, chỉ theo **một** bàn tay (nhóm liền kề lớn nhất); chưa có mô hình học máy, nền trùng màu da sẽ lẫn (`README.md` + `docs/ROADMAP.md` P1 #8) |

## 5. Chưa xong #4 — mọi thứ còn chờ **máy thật**

Đây là nhóm "chưa xong" lớn nhất về mặt *bằng chứng*, và sandbox/CI **không** đóng được: cả hai đều chỉ
có WARP (adapter phần mềm) và không có camera, đàn MIDI, driver ASIO hay máy ARM.

| Việc chờ máy thật | Trạng thái ghi trong repo |
|---|---|
| **AR‑S4 trên webcam thật** | `docs/AR-OVERLAY-ROADMAP.md` §14.10: "AR‑S4 vẫn **Unverified** cho tới khi webcam thật chạy đúng checklist" — code/WARP/`--verify` đã xanh trên CI |
| **Cổng OBS** (ghép Geometry + Glow: Glow ở chế độ Add/Screen) | AR‑S6: "manual OBS gate *Unverified*"; WARP readback đồng bộ đã PASS |
| **Ngân sách khung p95 8/16 ms trên card thật** | `docs/ROADMAP.md` §4 #3: "chưa có một con số p95 đo trên máy có card đồ hoạ thật"; WARP đo p95 ≈ 154 ms (§13 AR), nên ngưỡng chưa được xác nhận; trên WARP `--bench` cố ý chỉ in `NOTE`, không phán |
| **Adaptive Quality sau thay đổi** | `docs/VISUAL-EFFECTS-MASTER-ROADMAP.md` §11.6: "chưa có kết quả hardware sau thay đổi… kết quả vật lý vẫn **Unmeasured**"; lần đo iGPU AMD 2026‑10‑07 **fail 3/3** ở default (Concert Gold), vượt 20–43% |
| **Một tệp MP4 ghi trên máy Windows bình thường** | `docs/REPO-AUDIT.md` §3.2/§7.4: runner CI treo trong `IMFSample::SetSampleTime`; lần này annotation của run 38900310173 cho thấy encoder probe **đã mở được** sink writer ("NOTE MP4 encoder: the MP4 sink writer is open"), tức còn đúng một việc: ghi một take thật trên máy người dùng |
| **Độ trễ/ổn định audio trên ASIO + Voicemeeter + nhiều thiết bị** | `docs/ROADMAP.md` P1 #10: cần checklist cold start, đổi thiết bị khi đang chạy, buffer negotiation, 30–60 phút MIDI liên tục, CPU/UI stall, shutdown không treo; "không suy từ nhãn target" |
| **Rà soát UX cuối** | P1 #11: đường đi toàn bộ dialog, co giãn **100–200% DPI**, bàn phím/Tab/Esc, nội dung EN/VI trên Windows thật |
| **`win-arm64`** | chưa máy ARM nào chạy; leg đó tắt ReadyToRun; `release.yml` không smoke test vì runner là x64 |
| **Cài thử trên máy Windows sạch** | `docs/REPO-AUDIT.md` §7.4: "việc đầu tiên sau khi đợt này vào main nên là… cài thử bộ cài và giải nén thử ZIP trên một máy Windows sạch" — 2.1.1 đã phát hành, chưa thấy ghi nhận kết quả ở đâu trong repo |

## 6. Chưa xong #5 — vệ sinh kho

| Việc | Số liệu |
|---|---|
| ~~**62 nhánh remote ngoài `main` chưa dọn**~~ ✅ **đã dọn 49 trong phiên này** | Phân loại từng nhánh bằng `gh api compare/main...<b>` chứ không đoán theo tên: **49 `behind` với `ahead_by=0`** (tip là tổ tiên của `main` ⇒ xoá không mất một dòng code), **11 `diverged`** (35 commit chưa từng vào `main` — **giữ lại**, có bảng nhận định từng nhánh), **2 nhánh orphan** `release/public-v2.0.x` (`404 No common ancestor`). `git ls-remote --heads origin`: **63 → 14**. Toàn bộ SHA của nhánh đã xoá + lý do + đường khôi phục ghi ở [`docs/BRANCH-CLEANUP-2026-10-16.md`](BRANCH-CLEANUP-2026-10-16.md) |
| **4 nhánh nháp trong kho phát hành công khai `lxmtuu/KeyFlow`** — ✅ **chủ repo đã xoá 2026‑10‑16** | `arena/01a0fc21-keyflow`, `arena/01a0fc63-keyflow`, `arena/01a102b7-keyflow`, `arena/75990fb0-keyflow`: bản nháp **sửa tay** trang sản phẩm thời 1.0.0/2.0.0 (VERSION 1.0.0/2.0.0, 37–40 tệp), trước khi mô hình "trang công khai chỉ do máy sinh" được siết. So cây nội dung với `main` của kho đó: tệp duy nhất không còn ở `main` là `Assets/ATTRIBUTION.txt` — mà đọc ra là ghi công của **`ConcertGrand.sf2` đã bỏ từ 2.0.0** (ghi công Salamander hiện hành do `Audio/SalamanderPiano.cs` sinh cạnh tệp mẫu). Phiên agent **không xoá được vì quyền** (`DELETE …/git/refs/heads/…` → `403 Resource not accessible by integration`; `git push --delete` → `Permission to lxmtuu/KeyFlow.git denied to lxmtuu` — token phiên chỉ scoped kho nguồn), nên lệnh được đưa cho chủ repo và chủ repo chạy một dòng trên máy mình: cả bốn `[deleted]`, `git ls-remote --heads` còn đúng `main` · `release/public-v2.0.1` · `release/public-v2.1.1`. Ghi lại ở §3 của tệp trên |
| **Kho phình do ảnh trong Git** | `.git` = **125 MB**; `docs/previews` = **130 MB / 394 tệp** được track; `docs/presentation` 7,5 MB **bị nhân đôi** vì `docs/release/docs/presentation/` giữ bản sao (PDF 4,3 MB + DOCX 3,3 MB mỗi nơi). Repo không có `.gitattributes`/LFS. Không sai (checker so `docs/release` từng byte với máy sinh), nhưng là chi phí clone vĩnh viễn |
| **Vùng mù duy nhất của bộ kiểm vẫn mở** | `publish.ps1` và `tools/build_installer.ps1` không có lớp kiểm tĩnh nào (`docs/REPO-AUDIT.md` §5.3, §7.4): sandbox không có `pwsh`, nên hai tệp này chỉ được đọc tay; lớp xác nhận thật là job Windows của `build.yml` |
| **Bộ kiểm YAML vẫn nằm ngoài repo** | §7.4: "muốn giữ thì phải chuyển vào `tools/` kèm mutation test của chính nó" — chưa chuyển |

## 7. Tín hiệu hạ tầng/CI nên nhìn (không đỏ, nhưng đáng dọn)

| Tín hiệu | Chi tiết |
|---|---|
| **14 warning** trong job `build` của lần chạy `main` mới nhất | 10 warning là các dòng `NOTE MP4 encoder: …` do chính `--verify` in (mức warning, không phải lỗi biên dịch); **4 warning biên dịch thật** đều là `CS8625 Possible null reference argument for parameter 'Calibration' in 'GpuArInput.GpuArInput(...)'` tại **`Ui/MainWindow.ArExport.cs:69`**. Đợt 2026‑10‑01 đã dọn sạch 10 CS8602 trong `VerificationSuite.cs` bằng cách tìm *nguyên nhân gốc*; đây là phần còn lại, ở mã sản phẩm chứ không ở bộ kiểm |
| **`ubuntu-latest` sẽ chuyển sang Ubuntu 26 từ 19/10/2026** | notice trên cả hai job Linux của run 38900310173 (`actions/runner-images#14748`) — tức **3 ngày nữa**; job `static` và `test` chạy Python/.NET trên đó |
| **Một lần chạy ~59 phút** | `build` trên `main` 59m34s; `concurrency` đã có `cancel-in-progress` cho nhánh không phải `main`. Chi phí thật nằm ở khung đầu tiên trên thiết bị WARP mới (§6.13 của audit cũ) |
| **Ruleset không còn hiệu lực vì kho thành private** | `gh api repos/lxmtuu/PianoPath/rulesets` → **403 "Upgrade to GitHub Pro or make this repository public to enable this feature"**: GitHub không bật ruleset cho kho private trên gói Free. Hai ruleset từng được `docs/REPO-AUDIT.md` §6.2/§8.1 đo (`24265624`, `24233546` với `pull_request` + code-owner review, `bypass_actors: null`) vì thế **không còn chặn gì**. Hệ quả: `main` không còn buộc nhận pull request, và mấy nhánh xử lý **GH013** trong `build.yml`/`release.yml` mất hoàn cảnh tồn tại (vẫn vô hại, nên giữ). `branches/main/protection` cũng 403 với token phiên này ⇒ **chủ repo cần xem lại Settings → Rules/Branches** |
| **Ghi chú release `v2.0.0` có hai link gãy** | Link `https://github.com/lxmtuu/KeyFlow/blob/release/public-v2.0.0/CHANGELOG{,.en}.md` → API trả `404 No commit found for the ref`: nhánh đó **chưa từng tồn tại** bên kho công khai (đã kiểm `git ls-remote --heads`). Đối chiếu: `release/public-v2.0.1` và `-v2.1.1` **có** thật nên link của hai tag đó sống; `v2.1.0` là pre-release viết tay, không link nên không gãy. Bản sao của cây tài liệu v2.0.0 **còn** ở nhánh orphan trong kho nguồn (`44a716bd`, 45 mục, 0 tệp nguồn) — đó là lý do đợt dọn nhánh **không xoá** hai nhánh đó. Ba cách vá ở [`docs/BRANCH-CLEANUP-2026-10-16.md`](BRANCH-CLEANUP-2026-10-16.md) §2 |
| **Đường phát hành đã va chạm trong ngày 2.1** | cùng ngày 16/10: `release` **failure** cho tag `v2.1.0` (release trống, sau đó đánh dấu pre-release "no downloadable build"), `release` **failure** một lần cho `v2.1.1` trước khi success, và `build` **failure** trên `main` sau merge PR #69 (PR #70 sửa). Quy trình immutable + draft-first đã ghi vào `docs/PRIVATE-SOURCE-PUBLIC-RELEASES.md` ("Lưu ý vận hành 2.1.1") — đáng đọc lại trước lần tag kế tiếp |

## 8. Không kiểm được trong phiên này (nói rõ, không đoán)

* **Không biên dịch được C#**: sandbox này không có `dotnet` lẫn `pwsh` (`which dotnet pwsh` rỗng). Mọi
  kết luận về build/`--verify`/test là **đọc từ annotation của run 38900310173**, không phải chạy lại.
* **Không chạy được `--verify`, `--bench`, installer hay app**: cần Windows + GPU/camera/đàn MIDI.
* **Không tải được log đầy đủ của job**: chỉ đọc annotation qua `gh api check-runs/{id}/annotations`
  (đúng kinh nghiệm §6.11 của audit cũ: kết luận của một lượt CI đã qua phải lấy từ annotation).
* **`git lfs` không có** trong sandbox, nhưng repo cũng không còn đối tượng LFS nào nên không ảnh hưởng.

## 9. Việc nên làm tiếp, theo thứ tự

| Ưu tiên | Việc | Vì sao đứng ở đó |
|---|---|---|
| ~~**P0**~~ ✅ **đã làm trong phiên này** | Sửa tuyên bố về visibility: chủ repo **đổi kho sang private** (câu README nay đúng); phần mô tả `release.yml` (bỏ "nhánh orphan trong kho này") đã viết lại ở **cả hai** README; tham chiếu `§2a` nay trỏ đúng mục *a* của §2 và mục đó đã ghi kết quả + ngày làm | `git diff README.md README.en.md docs/PRIVATE-SOURCE-PUBLIC-RELEASES.md`; `python3 tools/check_sources.py` exit 0; `python3 tools/make_public_docs.py` ghi 10 tệp mà `git status -- docs/release` **rỗng** ⇒ trang công khai không đổi |
| **P0 còn lại** | Thêm luật tĩnh cho *tuyên bố trạng thái kho/pipeline* vào `tools/check_sources.py` (kèm mutation test), để lần lệch này không lặp lại | Đúng cách repo đã vá chuyện ảnh preview bằng bước `Report preview drift` |
| **P1** | Dọn 49 nhánh `behind` + 2 nhánh `release/public-*` mồ côi; xem riêng 11 nhánh `diverged` | Rẻ, không rủi ro cho nhánh đã merge; lệnh ở `docs/REPO-AUDIT.md` §8.4 mục 5 |
| **P1** | Dọn 4 warning `CS8625` ở `Ui/MainWindow.ArExport.cs:69` | Warning biên dịch trong mã sản phẩm là thứ audit cũ đã coi là "lần đầu ghi nhận" và dọn sạch ở bộ kiểm |
| ~~**P1**~~ ✅ **đã làm trong phiên này** | Cập nhật tài liệu kế hoạch: `docs/ROADMAP.md` (P1 #2 WebM → ✅ xong kèm mô tả đúng cơ chế, §3 Hiệu ứng → 88 mục/1 `Planned`, §4 preamble → không còn mục "chưa làm", P3 #2 → số test đo lại, §6 → bảng ước đoán đối chiếu với **lịch sử phát hành thật** 1.0.0→2.1.1); `docs/EFFECT-COVERAGE-AUDIT.md` (219 → **349** tệp C#, delta `ArRisingMidiNotes` → ✅ đã merge + đã CI chạy, kèm bằng chứng `EffectContractCatalog` **không** đổi); `docs/REPO-AUDIT.md` (không viết lại các bảng lịch sử — thêm bảng "ba loại câu đã hết hiệu lực" ở đầu tệp + lời dẫn cho §6.3) | `git diff docs/ROADMAP.md docs/EFFECT-COVERAGE-AUDIT.md docs/REPO-AUDIT.md`; `tools/check_sources.py` exit 0; `tools/make_public_docs.py` ghi 10 tệp mà `docs/release/` **không đổi một byte** |
| ~~**P1**~~ ✅ **đã xong** | Dọn nhánh: kho nguồn **49/62** đã xoá (63 → 14; giữ 11 nhánh `diverged` có 35 commit chưa vào `main`, và **2 nhánh `release/public-v2.0.x`** vì chúng là đầu mối vá link gãy của v2.0.0); kho công khai **4/4 nhánh nháp đã xoá** bởi chủ repo (7 → 3). Hai kho nay còn tổng cộng **17 nhánh**, nhánh nào cũng có lý do tồn tại được ghi tên | `git ls-remote --heads origin` = 14; `git ls-remote --heads …/KeyFlow` = 3; hồ sơ + SHA + đường khôi phục ở `docs/BRANCH-CLEANUP-2026-10-16.md` |
| **P1 mới** | Vá **hai link gãy** trong ghi chú release `v2.0.0` (chọn 1 trong 3 cách ở §2 của hồ sơ dọn nhánh), rồi mới xoá 2 nhánh orphan | Lỗi nhìn thấy được bởi người tải bản 2.0.0; rẻ nhất là đăng lại cây tài liệu sang kho công khai |
| **P1 mới** | Xem lại **Rules → Branches** của kho nguồn: ruleset tắt theo gói Free kể từ khi kho private. Quyết định: nâng gói, hay chấp nhận `main` không còn buộc PR (và ghi điều đó vào sổ tay) | Hai workflow vẫn còn mã xử lý GH013 cho một hoàn cảnh không còn tồn tại |
| **P2** | Làm **AR‑S8 (HUD)**: `ArElement.Hud`/`OctaveLines` đã có trong catalogue, thiếu setting + renderer + verify | Slice nhỏ, thuần hình học, catalogue đã giữ chỗ sẵn |
| **P3** | Nhóm máy thật (§5): webcam + OBS + GPU thật + ASIO + ARM + cài trên Windows sạch | Chỉ chủ repo làm được; nên ghi kết quả vào một mục mới của `docs/REPO-AUDIT.md` thay vì để rời rạc |
| **P3** | `falling.color-gradient`, GPU sheet layer, streaming sample, FFT audio-reactive, `IStageRenderer` | Việc tính năng thật, mỗi việc cần một lớp kiểm chứng mới theo luật §0 |
