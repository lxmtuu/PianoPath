# Kho nguồn riêng tư, kho phát hành công khai

Cách phát hành Keyflow từ ngày **2026‑10‑02**: mã nguồn nằm trong kho **private**, còn gói cài và trang
sản phẩm nằm trong kho **public**. Tài liệu này là **sổ tay vận hành**: ba việc thiết lập một lần, quy
trình phát hành một bản, cách kiểm sau khi phát hành, và những gì mô hình này đánh đổi.

Nếu chỉ đọc một câu: **mọi thứ đi qua ranh giới đều do máy sinh ra từ một chỗ** — `docs/release/` được
`tools/make_public_docs.py` sinh từ chính hai README này, `tools/check_sources.py` so từng byte với
script đó, và `release.yml` chỉ chép đúng thư mục ấy (cộng `docs/previews/`) sang kho công khai.

## 1. Mô hình

```
lxmtuu/PianoPath  (private — nguồn, lịch sử, LFS, CI, dock, mọi thứ)
        │
        │  tag v1.0.0  ──►  release.yml
        │                      │
        │                      ├── publish 3 gói (ZIP x64, ZIP x64-fd, ZIP arm64) + bộ cài .exe
        │                      ├── SHA256SUMS.txt
        │                      ├── dựng nhánh orphan `release/public-v1.0.0`
        │                      │      = docs/release/ (sinh sẵn, đã commit, đã được kiểm)
        │                      │      + docs/previews/ (ảnh CI vừa render)
        │                      │      + 0 dòng lịch sử của kho nguồn
        │                      └── đẩy nhánh đó sang kho công khai
        ▼
lxmtuu/PianoPath-Releases  (public — chỉ tài liệu + ảnh, và GitHub Release đính kèm 4 gói)
        https://github.com/lxmtuu/PianoPath-Releases
```

Hai đường đẩy sang kho công khai, cùng một kết quả:

| Đường | Dùng khi | Credential |
| --- | --- | --- |
| **Tự động** — bước *Publish to the public release repository* của `release.yml` | đã đặt secret `PUBLIC_RELEASES_TOKEN` | một fine‑grained PAT, chỉ nằm trong secret của kho này và chỉ dùng ở đúng một bước |
| **Thủ công** — `pwsh tools/publish_public.ps1 -Tag v1.0.0` | máy bạn đã `gh auth login`; không muốn lưu token thứ hai | chính đăng nhập `gh` của bạn; script dựng lại đúng nhánh ấy rồi `git push` |

Thiếu secret thì bước tự động **không làm đỏ build**: nó in một cảnh báo kèm đúng câu lệnh cần chạy, rồi
chờ tối đa **5 phút** để xem bản đẩy tay có lên không. Nếu không, lượt chạy vẫn xanh — vì gói và nhánh
phát hành đã nằm đúng chỗ của chúng trong kho nguồn rồi.

## 2. Ba việc thiết lập một lần

Làm một lần, bằng tài khoản `lxmtuu` (token của phiên agent **không** có quyền `administration`, nên ba
việc dưới đây phải do bạn bấm hoặc chạy `gh` trên máy bạn).

**a. Đổi kho nguồn sang private**

```bash
gh repo edit lxmtuu/PianoPath --visibility private --accept-visibility-change-consequences
```

Trước khi bấm, biết trước ba điều: repo hiện có **1 sao và 0 fork** (nên không mất fork nào), **lịch sử
traffic/insights công khai dừng lại**, và mọi liên kết công khai cũ (`github.com/lxmtuu/PianoPath`,
`blob/main`…) bắt đầu trả 404 với người ngoài — kể cả liên kết trong ghi chú phát hành `v1.0.0` đã phát
hành trước đó, nên bản phát hành lại ở kho công khai (bước dưới) là thứ thay thế chúng.

**b. Tạo kho phát hành công khai**

```bash
gh repo create lxmtuu/PianoPath-Releases --public \
  --description "Keyflow — bản dựng cho Windows (kho phát hành; mã nguồn ở kho riêng)"
gh repo edit lxmtuu/PianoPath-Releases --enable-issues        # nơi nhận báo lỗi từ người dùng
```

Đừng tạo README trong kho đó: nhánh `main` sẽ do lần phát hành đầu tiên đặt vào (trang sản phẩm song
ngữ). Sau lần phát hành đầu, vào **Settings → Branches** đặt default branch là `main` (hoặc nhánh
`release/public-<tag>` mới nhất) để trang chủ kho hiện đúng trang sản phẩm.

**c. Secret và variable** (cả hai đều tuỳ chọn — thiếu cũng chỉ là đường thủ công)

```bash
# Fine-grained PAT, scope: chỉ repo PianoPath-Releases, quyền Contents: Read and write
gh secret set PUBLIC_RELEASES_TOKEN --repo lxmtuu/PianoPath
# Chỉ cần khi kho phát hành mang tên khác mặc định
gh variable set PUBLIC_RELEASES_REPOSITORY --repo lxmtuu/PianoPath --body "lxmtuu/PianoPath-Releases"
```

Tên mặc định đã nằm trong `.github/workflows/release.yml` (`PUBLIC_REPOSITORY`), trong
`tools/make_public_docs.py`, trong `AppPublisherURL` của bộ cài và trong cả hai README; lớp kiểm tĩnh
(`scan_public_release`) đòi tất cả nói cùng một tên, nên đổi tên kho thì phải đổi cả năm chỗ — xem §7.

## 3. Phát hành một bản

1. **Sửa số phiên bản ở một chỗ**: `<Version>` trong `PianoPath.csproj`, rồi mục mới nhất của
   `CHANGELOG.md` + `CHANGELOG.en.md`, và mục mới trong cả hai README nếu cần.
2. **Sinh lại tài liệu công khai**: `python3 tools/make_public_docs.py`, rồi commit `docs/release/`.
   Quên bước này là `tools/check_sources.py` đỏ (`docs/release/… does not match tools/make_public_docs.py`)
   và `build.yml` chặn ở job `static` trước cả khi job Windows được xếp lịch.
3. **Đẩy tag**:

   ```bash
   git tag v1.0.0
   git push origin v1.0.0
   ```

4. **Đợi `release.yml`.** Nó publish ba gói + bộ cài, băm `SHA256SUMS.txt`, đính kèm vào Release của
   **kho này**, dựng nhánh `release/public-v1.0.0`, rồi đẩy sang kho công khai:
   * có secret → tự động, xong trong vài giây;
   * không có secret → bước in cảnh báo và **chờ 5 phút**. Trong 5 phút đó, trên máy bạn:

     ```powershell
     pwsh tools/publish_public.ps1 -Tag v1.0.0                       # chỉ tài liệu
     pwsh tools/publish_public.ps1 -Tag v1.0.0 -Packages publish     # kèm 4 gói + SHA256SUMS.txt vào Release
     pwsh tools/publish_public.ps1 -Tag v1.0.0 -DryRun               # xem trước, không đẩy gì
     ```

5. **Nếu lỡ quá 5 phút**: không sao. Nhánh `release/public-v1.0.0` đã nằm trong kho nguồn và chứa đúng
   commit cần đẩy; chạy lệnh ở bước 4 lúc nào cũng được (hoặc bấm **Run workflow** cho `release.yml` với
   `workflow_dispatch` — nó chạy lại toàn bộ, kể cả publish ba gói).

Chạy lại cùng một tag là chuyện bình thường: nhánh phát hành được **force push** (nó là nội dung sinh
tựa, sửa tay sẽ bị ghi đè — trang công khai nói rõ điều đó), và gói thì được tải lên với `--clobber`.

## 4. Kiểm sau khi phát hành

- Nhánh công khai có: `README.md`, `README.en.md`, `CHANGELOG.md`, `CHANGELOG.en.md`, `LICENSE`,
  `Assets/ATTRIBUTION.txt`, `docs/LOCALIZATION.md`, `docs/previews/…`, `VERSION`.
- **Không** có: bất kỳ `*.cs`, `*.xaml`, `*.csproj`, `*.iss`, `*.ps1`, `*.py`, `*.hlsl`, `*.sf2`, `.github/`.
  (Lớp kiểm tĩnh khẳng định danh sách này trên `docs/release/` đã commit; bước dựng nhánh lặp lại phép
  kiểm trên cả `docs/previews/`.)
- Release ở kho công khai có **4 gói + `SHA256SUMS.txt`**, và `SHA256SUMS.txt` khớp với tệp đã tải:
  `Get-FileHash .\Keyflow-1.0.0-win-x64.zip -Algorithm SHA256`.
- Trên một máy Windows sạch: cài bằng bộ cài (hoặc giải nén ZIP) rồi chạy
  `PianoPath.exe --verify --verify-log=%TEMP%\keyflow-verify.log` — mã thoát `0`. Đây vẫn là việc **duy
  nhất** không làm được từ sandbox hay từ CI.
- `python3 tools/check_sources.py` xanh (nó kiểm cả ranh giới riêng/công khai, xem §5).

## 5. Ranh giới được kiểm bằng gì

`scan_public_release` trong `tools/check_sources.py` chạy trên **mọi** push và **mọi** pull request (job
`static`, ~2 giây, không cần Windows), và khẳng định sáu điều:

1. **Một tên duy nhất**: `tools/make_public_docs.py`, `release.yml`, `AppPublisherURL` của bộ cài, cả hai
   README và tài liệu này phải trỏ cùng một kho công khai.
2. **Tài liệu khớp máy sinh ra nó**: `docs/release/` so từng byte với `write_all()` của
   `tools/make_public_docs.py`; thiếu tệp, thừa tệp hay lệch một ký tự đều đỏ, kèm đúng câu lệnh phải chạy.
3. **Danh sách tệp được đi là danh sách đóng**: chỉ 8 đường dẫn ở §4, cộng `docs/previews/`.
4. **Mọi liên kết trên trang công khai có đích**: anchor phải là một tiêu đề của chính trang đó, liên kết
   tệp phải là tệp được chép, ảnh phải đúng bộ ảnh của ngôn ngữ đó (trừ gallery `presets.jpg` dùng chung).
5. **Trang tải phải nói đúng thứ release đính kèm**: bảng gói nhắc đủ ZIP/Setup/SHA256SUMS và
   `release.yml` vẫn phải đính kèm đúng ba mẫu tệp ấy.
6. **Token chỉ được xuất hiện ở đúng một bước**: `PUBLIC_RELEASES_TOKEN` chỉ được có trong
   `release.yml` (và trong tài liệu); `build.yml` **không** được nhắc tới nó, nên job build không có
   đường nào tới kho công khai.

## 6. Mô hình này đánh đổi gì

- **Giấy phép**: mã nguồn theo MIT, và bản MIT đòi giấy phép **đi kèm bản sao**. Mỗi gói đã mang
  `LICENSE.txt` + `Assets/ATTRIBUTION.txt` (CC BY 3.0 của SoundFont), nên nghĩa vụ đó vẫn đủ — nhưng dự
  án **không còn là "source available"**: người ngoài không đọc được mã nguồn nữa, và bất kỳ ai muốn
  dịch/nhúng mã phải xin quyền truy cập. Nếu muốn giữ tinh thần MIT đầy đủ, cách duy nhất là mở lại repo.
- **Sao và fork**: kho private giữ nguyên số sao (1) nhưng 0 fork nghĩa là không mất gì. Kho công khai
  mới bắt đầu từ 0 sao — nếu muốn "ngôi sao" thuộc về trang sản phẩm, hãy để người dùng sao kho phát hành.
- **Chi phí CI**: repo private tính phút Actions theo hạn mức (Windows nhân 2). `build.yml` nay có
  `concurrency: cancel-in-progress`, nên một phiên push liên tục chỉ còn lượt cuối chạy. Muốn tiết kiệm
  hơn nữa: giới hạn job `build` (Windows) chỉ chạy trên `main` và pull request, để push lên `arena/**`
  chỉ chạy `static` + `test` (~1 phút) — đổi `on.push.branches` thành `["main"]` và thêm nhánh vào
  `pull_request` nếu cần.
- **Băng thông LFS**: không đổi — `release.yml` vẫn tải ~113 MiB mỗi lần chạy.
- **Bộ kiểm chứng**: `--verify` chạy trên chính file `.exe` đã phát hành, không cần mã nguồn. `tests/`
  (xUnit) và `tools/` (Python) thì chỉ chạy được khi có mã nguồn — trang công khai nói rõ phần nào cần gì.
- **Nếu secret hết hạn hoặc bị thu hồi**: không có gì hỏng. Bước tự động chỉ cảnh báo, và đường thủ công
  `tools/publish_public.ps1` vẫn chạy được bằng `gh` của bạn.
- **Nếu ruleset của kho chặn push nhánh `release/public-*`**: bước dựng nhánh cũng chỉ **cảnh báo** (nó
  nhận diện GH013, đúng cách `build.yml` xử lý khi bị chặn commit ảnh), và `publish_public.ps1` tự dựng
  nhánh y hệt trên máy bạn — luật của kho không áp cho lần đẩy tay đó.
- **Không dùng GitHub Pages, không có telemetry, không auto-update**: mô hình này không thêm dịch vụ nào.
  Trang sản phẩm là README của kho phát hành.

## 7. Đổi tên kho phát hành (nếu không dùng tên mặc định)

Năm chỗ, và lớp kiểm tĩnh sẽ đỏ cho tới khi đủ cả năm:

| Chỗ | Sửa gì |
| --- | --- |
| `tools/make_public_docs.py` | `PUBLIC_REPOSITORY = "owner/name"` (nguồn của mọi liên kết trên trang công khai) |
| `.github/workflows/release.yml` | hằng dự phòng `vars.PUBLIC_RELEASES_REPOSITORY \|\| 'owner/name'` |
| `installer/Keyflow.iss` | `AppPublisherURL=https://github.com/owner/name` |
| `README.md`, `README.en.md` | liên kết ở khối đầu và trong mục phát hành |
| tài liệu này | tiêu đề mô hình và các lệnh `gh` |

Rồi chạy lại `python3 tools/make_public_docs.py` **hai lần** (một lần để sinh `docs/release/`, một lần để
`scan_public_release` so lại — lần thứ hai là lớp kiểm, không phải script).

## 8. Muốn quay lại mô hình cũ

Đặt kho nguồn về public (`gh repo edit lxmtuu/PianoPath --visibility public`) và bỏ hai bước cuối của
`release.yml` (hoặc để nguyên: thiếu secret thì chúng chỉ cảnh báo). Hai kho vẫn có thể cùng tồn tại —
nhánh phát hành chỉ là một bản sao tài liệu, không có gì riêng tư.
