# Kho nguồn riêng tư, kho phát hành công khai

Cách phát hành Keyflow từ ngày **2026‑10‑02**: mã nguồn nằm trong kho **private**, còn gói cài và trang
sản phẩm nằm trong kho **public**. Tài liệu này là **sổ tay vận hành**: ba việc thiết lập một lần, quy
trình phát hành một bản, cách kiểm sau khi phát hành, và những gì mô hình này đánh đổi.

Nếu chỉ đọc một câu: **mọi thứ đi qua ranh giới đều do máy sinh ra từ một chỗ** — `docs/release/` được
`tools/make_public_docs.py` sinh từ chính hai README này, `tools/check_sources.py` so từng byte với
script đó, và `release.yml` chỉ chép đúng thư mục ấy (cộng `docs/previews/`) sang kho công khai.

## 1. Mô hình

```
lxmtuu/PianoPath  (nguồn, lịch sử, LFS, CI, dock, mọi thứ — public từ 2026-10-03, xem §2a)
        │
        │  tag v1.0.0  ──►  release.yml
        │                      │
        │                      ├── publish 3 gói (ZIP x64, ZIP x64-fd, ZIP arm64) + bộ cài .exe
        │                      ├── SHA256SUMS.txt
        │                      ├── đính 4 gói + SHA256SUMS.txt vào Release của **chính kho này**
        │                      ├── dựng nhánh orphan `release/public-v1.0.0`
        │                      │      = docs/release/ (sinh sẵn, đã commit, đã được kiểm)
        │                      │      + docs/previews/ (ảnh CI vừa render)
        │                      │      + docs/presentation/ (bộ slide PDF + DOCX + README)
        │                      │      + 0 dòng lịch sử của kho nguồn
        │                      └── đẩy nhánh đó sang kho công khai, và đính cùng 4 gói ấy
        ▼                         vào Release ở đó
lxmtuu/KeyFlow  (public — chỉ tài liệu + ảnh + bộ slide, và GitHub Release đính kèm 4 gói)
        https://github.com/lxmtuu/KeyFlow
```

Kho công khai **đã tồn tại** (từ 2026-10-02) và `main` của nó đang giữ tài liệu `v1.0.0`; điều đã sai
không phải là mô hình mà là **tên**: năm tệp trong kho nguồn cùng gõ `lxmtuu/PianoPath-Releases`, một kho
chưa bao giờ tồn tại, trong khi kho thật là `lxmtuu/KeyFlow`. Từ 2026-10-03 cả năm chỗ gõ `lxmtuu/KeyFlow`,
và lớp kiểm tĩnh giữ chúng khớp nhau như cũ.

Hai đường đẩy sang kho công khai, cùng một kết quả:

| Đường | Dùng khi | Credential |
| --- | --- | --- |
| **Tự động** — bước *Publish to the public release repository* của `release.yml` | đã đặt secret `PUBLIC_RELEASES_TOKEN` | một fine‑grained PAT, chỉ nằm trong secret của kho này và chỉ dùng ở đúng một bước |
| **Thủ công** — `pwsh tools/publish_public.ps1 -Tag v1.0.0` | máy bạn đã `gh auth login`; không muốn lưu token thứ hai | chính đăng nhập `gh` của bạn; script dựng lại đúng nhánh ấy rồi `git push` |

Thiếu secret thì bước tự động **không làm đỏ build**: nó in một cảnh báo kèm đúng câu lệnh cần chạy, rồi
chờ tối đa **5 phút** để xem bản đẩy tay có lên không. Nếu không, lượt chạy vẫn xanh — vì gói và nhánh
phát hành đã nằm đúng chỗ của chúng trong kho nguồn rồi.

## 2. Trạng thái hiện tại, và những gì còn phải cấu hình

| Kho | Vai trò | Trạng thái |
| --- | --- | --- |
| `lxmtuu/PianoPath` | **nguồn**: mã, lịch sử, LFS, CI, và một bản sao các gói trong Release của chính nó | **public** (mô hình này giả định private — xem *a*) |
| `lxmtuu/KeyFlow` | **phát hành**: trang sản phẩm song ngữ, ảnh giao diện, bộ slide, các gói trong GitHub Release | public; `main` đang giữ tài liệu `v1.0.0` |

**a. (Tuỳ chọn, nhưng là mô hình) đưa kho nguồn về private**

```bash
gh repo edit lxmtuu/PianoPath --visibility private --accept-visibility-change-consequences
```

Trước khi bấm, biết trước ba điều: repo hiện có **1 sao và 0 fork** (nên không mất fork nào), **lịch sử
traffic/insights công khai dừng lại**, và mọi liên kết công khai cũ (`github.com/lxmtuu/PianoPath`,
`blob/main`…) bắt đầu trả 404 với người ngoài — kể cả liên kết trong ghi chú phát hành `v1.0.0` đã phát
hành trước đó, nên bản phát hành ở kho công khai là thứ thay thế chúng.

Chừng nào kho nguồn còn public thì **không có gì hỏng**: cả hai README và banner của trang công khai chỉ
nói *mã nguồn nằm ở một kho riêng*, không nói kho đó công khai hay không — nên trang công khai không nói
sai ở trạng thái nào.

**b. Kho phát hành `lxmtuu/KeyFlow` đã có sẵn** — không tạo lại. Ba thứ nên kiểm một lần:

```bash
gh repo view lxmtuu/KeyFlow                     # default branch phải là main (trang sản phẩm nằm ở đó)
gh repo edit lxmtuu/KeyFlow --enable-issues     # nơi nhận báo lỗi từ người dùng
gh api repos/lxmtuu/KeyFlow/contents \
  --jq '.[].name'                               # chỉ tài liệu; không được có mã nguồn
```

Nhánh `main` của kho đó **do phát hành sinh ra** — đừng sửa tay, lần phát hành sau sẽ ghi đè. Một hệ quả
đáng nhớ: tệp nào chỉ tồn tại ở kho công khai mà **không** có trong kho nguồn thì sẽ bị xoá ở lần ghi đè
kế tiếp. Bộ slide `docs/presentation/` từng như vậy; nay nó nằm trong kho nguồn và đi theo mỗi bản phát
hành (xem §5).

**c. Secret và variable** (cả hai đều tuỳ chọn — thiếu cũng chỉ là đường thủ công)

```bash
# Fine-grained PAT, scope: chỉ repo KeyFlow, quyền Contents: Read and write (đẩy nhánh + tạo Release)
gh secret set PUBLIC_RELEASES_TOKEN --repo lxmtuu/PianoPath
# CHỈ đặt nếu kho phát hành mang tên khác mặc định. Nếu biến này đang mang tên cũ
# (lxmtuu/PianoPath-Releases) thì sửa lại hoặc xoá hẳn — biến thắng giá trị mặc định trong workflow.
gh variable list --repo lxmtuu/PianoPath
gh variable set PUBLIC_RELEASES_REPOSITORY --repo lxmtuu/PianoPath --body "lxmtuu/KeyFlow"
gh variable delete PUBLIC_RELEASES_REPOSITORY --repo lxmtuu/PianoPath   # hoặc xoá, dùng mặc định
```

Tên mặc định nằm ở **sáu** chỗ — `PUBLIC_REPOSITORY` của `tools/make_public_docs.py`, hằng dự phòng của
`.github/workflows/release.yml`, `AppPublisherURL` của bộ cài, `-Repository` mặc định của
`tools/publish_public.ps1`, cả hai README và tài liệu này — và `scan_public_release` đòi tất cả nói cùng
một tên, xem §7.

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
   **kho này**, dựng nhánh `release/public-v1.0.0`, rồi đẩy sang kho công khai — và đính **cùng bốn gói +
   `SHA256SUMS.txt`** vào Release ở kho công khai đó (một lượt tải 370 MB hai lần; đổi lại, người tải
   không phải sang kho nguồn):
   * có secret → tự động, xong trong vài phút;
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
  `Assets/ATTRIBUTION.txt`, `docs/LOCALIZATION.md`, `docs/presentation/…` (bộ slide), `docs/previews/…`,
  `VERSION`.
- **Không** có: bất kỳ `*.cs`, `*.xaml`, `*.csproj`, `*.iss`, `*.ps1`, `*.py`, `*.hlsl`, `*.sf2`, `.github/`.
  (Lớp kiểm tĩnh khẳng định danh sách này trên `docs/release/` đã commit; bước dựng nhánh lặp lại phép
  kiểm trên cả `docs/previews/`.)
- Release ở **cả hai** kho có **4 gói + `SHA256SUMS.txt`** (`gh release view v1.0.0 --repo lxmtuu/KeyFlow`),
  và `SHA256SUMS.txt` khớp với tệp đã tải:
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
3. **Danh sách tệp được đi là danh sách đóng**: chỉ 11 đường dẫn (8 tài liệu ở §4, bộ slide 3 tệp),
   cộng `docs/previews/`.
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
| `tools/publish_public.ps1` | `-Repository` mặc định (`lxmtuu/KeyFlow`) |
| `.github/workflows/release.yml` | hằng dự phòng `vars.PUBLIC_RELEASES_REPOSITORY \|\| 'owner/name'` |
| `installer/Keyflow.iss` | `AppPublisherURL=https://github.com/owner/name` |
| `README.md`, `README.en.md` | liên kết ở khối đầu và trong mục phát hành |
| tài liệu này | tiêu đề mô hình và các lệnh `gh` |

Rồi chạy `python3 tools/make_public_docs.py` (sinh lại `docs/release/`) và `python3 tools/check_sources.py`
(lớp kiểm: `scan_public_release` so từng byte, đọc tên kho từ **cả sáu** chỗ và đỏ nếu chúng lệch nhau).

## 8. Muốn quay lại mô hình cũ

Đặt kho nguồn về public (`gh repo edit lxmtuu/PianoPath --visibility public`) và bỏ hai bước cuối của
`release.yml` (hoặc để nguyên: thiếu secret, hoặc kho đích không tồn tại, thì chúng chỉ cảnh báo — xem §9).
Hai kho vẫn có thể cùng tồn tại — nhánh phát hành chỉ là một bản sao tài liệu, không có gì riêng tư. Kho
nguồn **đang public** (từ 2026-10-03): chừng nào còn như vậy, đặt `PUBLIC_REPOSITORY` trỏ vào chính kho này
là hợp lệ — bước publish tự bỏ qua, và **không bao giờ** ghi vào `main` của kho nguồn nữa.

## 9. Tai nạn 2026-10-03: tài liệu phát hành đè lên `main`

**Chuyện gì đã xảy ra.** Một lượt `pwsh tools/publish_public.ps1 -Tag v1.0.0 -Repository lxmtuu/PianoPath`
được chạy với kho đích **là chính kho nguồn**. Script ghi tài liệu vào nhánh `release/public-<tag>`, nhưng
khi tag ấy đang là bản mới nhất của kho (`gh api repos/<kho>/releases/latest`) thì nó *còn* đẩy cùng commit
đó vào `refs/heads/main` — và với kho nguồn thì `main` là sản phẩm, không phải trang tài liệu. Kết quả:
`main` mang cây `docs/release` trong khoảng nửa giờ (commit gốc `692841fe`), rồi được cứu lại bằng cách đẩy
nhánh `fix/avi-4k-freeze` (`138b0af`) lên `main`, sau đó CI thêm lượt làm mới ảnh xem trước.

**Mất gì, không mất gì.** Không mất một dòng mã nào: cây của `main` trước và sau tai nạn **trùng nhau từng
byte** (`765f5791`). Thứ bị mất là **hai commit merge** của PR #39 và #40 trong lịch sử. Chúng vẫn nằm
nguyên trên GitHub và đã lấy lại được bằng `git fetch origin <sha đầy đủ>`, rồi `main` được đẩy về đúng
commit trước tai nạn (`8e6a706c`) — một commit không còn nhánh nào trỏ tới **vẫn fetch được** theo SHA đầy
đủ, nên "lỡ force-push mất rồi" không có nghĩa là hết đường.

**Hai hàng rào từ nay** (cả hai đều bị `scan_public_release` trong `tools/check_sources.py` kiểm, nên
không thể gỡ lặng lẽ):

| Chỗ | Hàng rào |
| --- | --- |
| `tools/publish_public.ps1` | Nếu kho đích **chính là kho nguồn** (`-Repository` trùng slug của `origin`): script **từ chối chạy** khi `$updateMain` đang bật, và từ chối mọi `-Branch main`/`master`; nhánh `release/public-*` vẫn đẩy được, kèm cảnh báo rằng `main` sẽ không bao giờ bị ghi. |
| `release.yml`, bước *Publish to the public release repository* | `PUBLIC_REPOSITORY` trùng `github.repository` → bước **không đẩy đi đâu cả** (nhánh tài liệu đã nằm trong kho này rồi) và `$updateMain` bị đặt về `$false`. Kho đích **không tồn tại** → chỉ **cảnh báo**, không làm đỏ lượt phát hành: gói, GitHub Release và nhánh tài liệu đều đã nằm đúng chỗ trong kho này. |

**Ghi chú phát hành nay tự chọn chỗ trỏ.** Nếu `PUBLIC_REPOSITORY` là một kho khác, link CHANGELOG trỏ vào
`release/public-<tag>` của kho đó; nếu không — hoặc kho đích chính là kho này — chúng trỏ vào chính tag
trong kho này, vì một liên kết 404 trong ghi chú phát hành là thứ người tải đọc đầu tiên.
