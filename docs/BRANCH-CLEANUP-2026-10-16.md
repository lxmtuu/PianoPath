# Dọn nhánh 2026‑10‑16 — bảng kiểm kê và đường khôi phục

Tệp này tồn tại vì **xoá nhánh là thao tác không thuận nghịch**: sau khi ref bị xoá, commit của nó chỉ
còn sống trong kho dưới dạng *unreachable* cho tới khi GitHub chạy GC (và một lượt `git gc --prune` chủ
động thì xoá ngay). Bởi vậy mọi SHA bị xoá được ghi lại **ở đây, trong Git**, trước khi lệnh xoá chạy.

Đối chiếu bằng `gh api repos/lxmtuu/PianoPath/compare/main...<nhánh>` cho từng nhánh một, không đoán theo
tên. Luật dùng để quyết định: **chỉ xoá nhánh có `ahead_by = 0`** — tức tip của nó là tổ tiên của `main`,
nên mọi commit của nó đã nằm trong `main` và việc xoá không mất một dòng code nào.

> Việc dọn chạy lúc kho đang **private** (sáng 2026‑10‑16); trong ngày visibility đổi qua lại bốn trạng thái
> (public → private → public → private) và lần đo cuối của phiên lại là **private**. Ở trạng thái nào thì 49
> nhánh đã xoá cũng không quay lại, và 13 nhánh giữ lại đều có lý do ghi ở §4. Mặt trái đo được của vòng đổi
> visibility ghi ở §5.

## 0. Kết quả, đếm được bằng `git ls-remote`

| Kho | Trước | Đã xoá | Sau | Ai xoá, bằng gì |
|---|---|---|---|---|
| `lxmtuu/PianoPath` | 63 nhánh | **49** | **14** | phiên agent, `git push origin --delete` |
| `lxmtuu/KeyFlow` | 7 nhánh | **4** | **3** | **chủ repo**, trên máy của mình: `gh auth login` rồi `git push https://github.com/lxmtuu/KeyFlow --delete …` (§3) |

Số ở kho nguồn kiểm lại được: `git ls-remote --heads origin \| wc -l` = 14 = 63 − 49, và đúng bằng
1 (`main`) + 11 (`diverged`) + 2 (`orphan`). Số ở kho công khai: `git ls-remote --heads …/KeyFlow` = 3 =
`main` (`f79c9a70`) + `release/public-v2.0.1` (`e9a5fab6`) + `release/public-v2.1.1` (`f79c9a70`) — đo lại
sau khi chủ repo chạy lệnh, và SHA của `main` trùng SHA của `release/public-v2.1.1` đúng như §4.2 ghi.

Số 49 đã xoá kiểm lại được: `git ls-remote --heads origin \| wc -l` = 14 = 63 − 49, và đúng bằng
1 (`main`) + 11 (`diverged`) + 2 (`orphan`).

## 1. Đã xoá — 49 nhánh mà nội dung đã nằm trọn trong `main`

Mọi nhánh dưới đây có `status=behind` và `ahead_by=0`; cột "trễ" là số commit mà nhánh thua `main`.

| Nhánh | SHA tip (đẩy lại được) | Trễ `main` | PR |
|---|---|---|---|
| `arena/01a0e95c-pianopath` | `b781d871f8d37eff43c9b90a167aff9c12bd0d72` | 958 | 2 |
| `arena/01a0e9af-pianopath` | `4ae56cb405f10bea16d1754b8ef0cb769b9d6c0e` | 955 | 3 |
| `arena/01a0e9fa-pianopath` | `1b226e58d774d675a3650114992bcc3800f72350` | 948 | 4 |
| `arena/01a0ea06-pianopath` | `4fa0dab95b08e90ad33e623c93c8a824f4f1c407` | 949 | 5 |
| `arena/01a0ec05-pianopath` | `43c0677013fd73f682f19211b2a2e09b77fb3e72` | 932 | 8,7 |
| `arena/01a0ec7e-pianopath` | `b87f94278638ba6f4a6be285d53ae60ed67f8af4` | 926 | 9 |
| `arena/01a0ece2-pianopath` | `38ff81285744f2411b5b18b98cd407f51b8c7026` | 925 | 10 |
| `arena/01a0ed18-pianopath` | `29b8eacaa022c98b7b6579b34bab508923a85360` | 914 | 11 |
| `arena/01a0ed61-pianopath` | `6ce560e9ea63b2dae8f6aa7ca485acafd3315492` | 912 | 12 |
| `arena/01a0ed71-pianopath` | `5d4079b4e09682738a902cc5a514e4586266236b` | 903 | 13 |
| `arena/01a0ede3-pianopath` | `5c730a4f1a4a43ca2713f62126a91cc889ad454a` | 888 | 15 |
| `arena/01a0ee11-pianopath` | `35b3d86c46f6e52eca5c5c18b1b86010bb55086f` | 884 | 16 |
| `arena/01a0ee63-pianopath` | `c2585f3670a13396aad2a8150ab62a3a1522d7cc` | 879 | 17 |
| `arena/01a0ee7c-pianopath` | `ee9f7e911354ee6436b1eee9d8edc8687b8f044f` | 864 | 18 |
| `arena/01a0eed5-pianopath` | `284530c5f96579fa6991005888299f8c6ebed7ae` | 692 | 21 |
| `arena/01a0f212-pianopath` | `fa4551a5109c5df881e2ad49785afd652162d077` | 862 | — |
| `arena/01a0f26d-pianopath` | `8e3e501103feb5ef53bd6ae9a21b15c432948142` | 652 | 24 |
| `arena/01a0f34a-pianopath` | `e6583c24774ff8823942ad01158f79923c9b3744` | 645 | 25 |
| `arena/01a0f3f0-pianopath` | `aad324ae5a93ea99f8d7d2c62b51afc884b700d8` | 631 | 27 |
| `arena/01a0f42c-pianopath` | `69cf84a94d22e2edb97fe8469bd0b1e08fd88211` | 627 | 28 |
| `arena/01a0f46c-pianopath` | `3d2712f3e036c4732d85f1ae117c7a9669c0c291` | 621 | 29 |
| `arena/01a0f5e6-pianopath` | `0503ec2377079ca324e7cb8ea68a0681cb84170a` | 617 | 30 |
| `arena/01a0f671-pianopath` | `dfb3a6d8e332e968733bca194f959c99b9643bdf` | 583 | 32 |
| `arena/01a0f82d-pianopath` | `615b36eb35663118ec32dfe2a81380a740e8dac0` | 571 | 33 |
| `arena/01a0f8ec-pianopath` | `b84884e05f27a854bce4d2fc83d08844796edf9b` | 568 | 34 |
| `arena/01a0f921-pianopath` | `a004fe09398c906d5f8dd461797218c524c245a0` | 565 | 35 |
| `arena/01a0fbdf-pianopath` | `cbe551c85b7df350a2f1a5dff019b5d92a204a38` | 562 | 37 |
| `arena/01a1023d-pianopath` | `3e1d9de7d3e84850aea671c48b3ae5ca2e05d6b4` | 549 | 43,42 |
| `arena/01a102ed-pianopath` | `9f7d463c9230e90128d0bf39728eecf782db15d7` | 544 | 45 |
| `arena/01a1032b-pianopath` | `ac389f2e93d8fd1ecb0d42aa944b3ed41031b2c7` | 532 | 46 |
| `arena/01a107ec-pianopath` | `43de3b5a35ed298740c97403aa088374c292f747` | 526 | 48 |
| `arena/01a10895-pianopath` | `3efe2163d762405d79414b7823baa4e2bc846cfb` | 514 | 52 |
| `arena/1d7fd862-pianopath` | `81dc93777f1adb91cfa763d96ddb7ded14eec25d` | 167 | 65 |
| `arena/1fccb0ec-pianopath` | `347fd7acb36206ef40614987bbdceb7885412e03` | 287 | 58 |
| `arena/603c479b-pianopath` | `ec8feb90fda24e15fa2803f4c5f302858ad99c95` | 254 | 59 |
| `arena/892ab0a0-pianopath` | `472d501b0271b68b97c9d3648277c5b7f5f68754` | 185 | 63 |
| `arena/94906038-pianopath` | `3ba1e03180ca32665c4bb2252bc39afbe3bfe66c` | 304 | 55 |
| `arena/a6cd38dd-pianopath` | `12c97238bb49fe1ef19d7885c2351e2b2694e0a8` | 162 | 66 |
| `arena/cb709cbe-pianopath` | `efd03ed94f30604a281e1dbd647bb14b254c4c46` | 180 | 64 |
| `arena/d670261a-pianopath` | `0eb344d6601f2797ea6efb24537cb6202d95243d` | 346 | 54 |
| `arena/d9f17f72-pianopath` | `afea9131bbac763a0492e239ca6a5685ad0974d2` | 2 | 70 |
| `arena/db337b9a-pianopath` | `c4f5571ae4d392d956b6a225cf89f8ac4673e330` | 293 | 57 |
| `arena/e44d81f8-pianopath` | `cdfdd8498820d8d4e7271ac70436c84279576bb7` | 297 | 56 |
| `arena/feac9e1c-pianopath` | `57de641163c689d86903046d947f37a7935d9d7f` | 9 | 69,68,67 |
| `docs/readme-audit-p0` | `b4b05c7bac9d5aef44059979674efb292c98694f` | 690 | 22 |
| `feature/settings-ui` | `74014da8c44b6cb36f1d76229b89e7aa16b77661` | 910 | 14 |
| `fix/avi-4k-freeze` | `138b0af45df4486ba12efd0c6f1222ba70249747` | 555 | 40,39 |
| `fix/recording-freeze` | `c342e476d457e40d1baca47db8a1020ba686cfa9` | 560 | 38 |
| `fix/ui-inputs-and-async-key-tiles` | `a417b57e23fbe15852e79789ae2d3c55cca7d385` | 683 | 23,19 |

## 2. **Chưa xoá** — 2 nhánh mồ côi `release/public-*` trong kho nguồn, vì một phát hiện ngoài kế hoạch

`gh api compare` trả `404 No common ancestor` cho cả hai: chúng là nhánh *orphan* của **cách phát hành cũ**,
khi `release.yml` còn đẩy nhánh tài liệu vào chính kho nguồn. Cách đó đã bị bỏ sau tai nạn 2026‑10‑03
(tài liệu phát hành đè lên `main`, xem `docs/PRIVATE-SOURCE-PUBLIC-RELEASES.md` §9); `release.yml` nay dựng
cây tài liệu trong một Git index riêng và **không tạo nhánh nào ở kho này**
(`.github/workflows/release.yml:56‑110`, và chính sổ tay dòng 203 xác nhận).

| Nhánh | SHA tip | Ngày | Nội dung |
|---|---|---|---|
| `release/public-v2.0.0` | `44a716bdc3259aaa7172f0f713d3ac01d721ddbc` | 2026‑10‑10 | 45 mục, **0 tệp mã nguồn**: `CHANGELOG{,.en}.md` · `LICENSE` · `README{,.en}.md` · `VERSION` · `docs/` |
| `release/public-v2.0.1` | `c84b9acac4d6b4cd19c4410961e2c57f21024696` | 2026‑10‑13 | như trên |

Kế hoạch ban đầu là xoá cả hai. Khi kiểm "có ai đọc tới chúng không" thì hoá ra **có**, và kiểm tra đó lộ
ra một lỗi thật của lần phát hành v2.0.0:

| Nhánh | Bản sao bên `lxmtuu/KeyFlow` | Ghi chú release của tag trỏ vào đâu |
|---|---|---|
| `release/public-v2.0.0` | ❌ **không có** (đã kiểm `git ls-remote --heads`) | ghi chú `v2.0.0` link `https://github.com/lxmtuu/KeyFlow/blob/release/public-v2.0.0/CHANGELOG{,.en}.md` → API trả **404 `No commit found for the ref`** ⇒ **hai link gãy** ngay chỗ người tải bấm "What changed" |
| `release/public-v2.0.1` | ✅ còn, `VERSION=2.0.1` | ghi chú `v2.0.1` link đúng nhánh đó ⇒ **sống** |

Đối chiếu để thấy đây là lỗ hổng của riêng v2.0.0: `release/public-v2.1.1` tồn tại và link của v2.1.1 sống;
`release/public-v2.1.0` không tồn tại nhưng **không gãy** vì v2.1.0 là pre-release viết tay ("no downloadable
build"), không link CHANGELOG nào.

**Quyết định: giữ cả hai nhánh lại**, vì chúng là đầu mối ngắn nhất để vá link gãy của v2.0.0 — nhánh ở kho
nguồn còn đúng cây tài liệu mà trang công khai đáng lẽ phải nhận. Ba cách vá, chủ repo chọn:

1. **Đăng lại cây đó sang kho công khai** (khôi phục đúng link, không sửa gì release):
   ```bash
   git fetch origin release/public-v2.0.0
   git push git@github.com:lxmtuu/KeyFlow.git FETCH_HEAD:refs/heads/release/public-v2.0.0
   ```
   Cần credential có quyền ghi lên `lxmtuu/KeyFlow` — token của phiên agent **không có** (xem §3), và
   `PUBLIC_RELEASES_TOKEN` trong secret của repo thì có.
2. **Sửa ghi chú release v2.0.0** trỏ vào thứ tồn tại (`.../blob/v2.0.0/CHANGELOG.md` của chính kho nguồn,
   hoặc `release/public-v2.0.1`): `gh release edit v2.0.0 --notes-file <tệp>`. Lưu ý sổ tay
   `docs/PRIVATE-SOURCE-PUBLIC-RELEASES.md` có luật "không sửa/tái sử dụng tag bất biến" — luật đó nói về
   **tag và gói**, còn đây chỉ là văn bản ghi chú của một bản đã bị 2.1.1 thay thế; vẫn nên ghi lại nếu sửa.
3. **Chấp nhận link gãy** của một bản pre‑2.1 đã bị thay thế, rồi xoá cả hai nhánh (SHA ở bảng trên).

**Đã chọn (2026‑10‑17, chủ repo): cách 1 — đăng lại cây sang KeyFlow.** Kiểm tra lần cuối trước khi
chuyển lệnh cho chủ repo: `44a716b` là **root commit, 0 cha**, 40 tệp thuần tài liệu, có đúng
`CHANGELOG.md` + `CHANGELOG.en.md` **ở gốc cây** — khớp từng ký tự với đường dẫn của hai link gãy, và
đẩy nhánh này sang KeyFlow không mang theo bất kỳ commit nguồn nào.

Phiên agent thử đẩy một lần nữa ngày 2026‑10‑17 và **vẫn 403**, với một chi tiết đáng ghi: trường
`permissions` của `GET /repos/lxmtuu/KeyFlow` báo `push:true, admin:true`, nhưng **cả hai đường viết
thật** đều bị từ chối — REST `POST /repos/lxmtuu/KeyFlow/git/refs` → 403 `Resource not accessible by
integration`, và `git push` HTTPS (kể cả với username `x-access-token`) → 403 `Permission to
lxmtuu/KeyFlow.git denied`. Bài học: metadata `permissions` không phải bằng chứng; phép thử viết thật
mới là bằng chứng — giống hệt lần xoá 4 nhánh ở §3.

**Trạng thái: chờ chủ repo chạy lệnh của cách 1 trên máy của mình** (một lệnh, như lần §3). Sau khi
nhánh tồn tại bên KeyFlow — kiểm bằng `GET /repos/lxmtuu/KeyFlow/contents/CHANGELOG.md?ref=release/public-v2.0.0`
và `VERSION` phải đọc ra `2.0.0` — thì **xoá được cả hai nhánh mồ côi bên kho nguồn mà không mất gì**:
cây v2.0.0 sống trên KeyFlow dưới **đúng SHA `44a716b`**; cây v2.0.1 đã sống trên KeyFlow dưới
`e9a5fab6` (trước khi xoá `c84b9aca` thì so cây của hai commit đó một lần cho chắc).

## 3. Đã xoá — 4 nhánh bản nháp trong kho **phát hành công khai** `lxmtuu/KeyFlow`

Bốn nhánh này là **bản nháp sửa tay** trang sản phẩm thời 1.0.0/2.0.0, tạo ra trước khi mô hình "trang công
khai chỉ do máy sinh" được siết (`tools/make_public_docs.py` + `scan_public_release` so từng byte). Chúng cũng
là commit gốc nên không so ancestry được; mình so **cây nội dung** với `main` của kho đó
(`f79c9a70`, VERSION 2.1.1, 404 tệp):

> **Đã xoá 2026‑10‑16 bởi chủ repo, trên máy của mình** — và đây chính là bằng chứng cho ràng buộc quyền
> ghi ở kho công khai: phiên agent thử trước và **thất bại đúng như dự đoán** —
> `gh api -X DELETE repos/lxmtuu/KeyFlow/git/refs/heads/<nhánh>` → **403 `Resource not accessible by
> integration`**, `git push --delete` → **`remote: Permission to lxmtuu/KeyFlow.git denied to lxmtuu`** — vì
> token của phiên chỉ có quyền trên kho nguồn; quyền ghi lên kho công khai nằm ở secret
> `PUBLIC_RELEASES_TOKEN` (đúng như `docs/PRIVATE-SOURCE-PUBLIC-RELEASES.md` §2c thiết kế: token đó *scoped
> chỉ repo KeyFlow*). Chủ repo chạy `gh auth login` rồi một lệnh một dòng:
> ```bash
> git push https://github.com/lxmtuu/KeyFlow --delete arena/01a0fc21-keyflow arena/01a0fc63-keyflow arena/01a102b7-keyflow arena/75990fb0-keyflow
> ```
> Cả bốn dòng trả `[deleted]`; `git ls-remote --heads` ngay sau đó còn đúng ba nhánh.
> Kết luận kiểm tra giữ nguyên: **không có gì đáng giữ** trong bốn nhánh đó (xem bảng và đoạn về
> `Assets/ATTRIBUTION.txt` bên dưới).

| Nhánh | SHA tip | VERSION | Số tệp | Khác `main` ở chỗ nào |
|---|---|---|---|---|
| `arena/01a0fc21-keyflow` | `b30b1c97ee6ba7cef2fa75c49206f132e37530b1` | 1.0.0 | 37–40 | chỉ có `Assets/ATTRIBUTION.txt` mà `main` không có (xem dưới) |
| `arena/01a0fc63-keyflow` | `28f1ceb6d0b885972c6dc2b0deeaec5ccfdc857a` | 1.0.0 | 37–40 | chỉ có `Assets/ATTRIBUTION.txt` mà `main` không có (xem dưới) |
| `arena/01a102b7-keyflow` | `9620b57bfb9f7d99455d3004a6634f081a2b6a87` | 1.0.0 | 37–40 | chỉ có `Assets/ATTRIBUTION.txt` mà `main` không có (xem dưới) |
| `arena/75990fb0-keyflow` | `e792b8c77400ae8d095b0ae9a00fc1c8214e6f1d` | 2.0.0 | 37–40 | chỉ có `Assets/ATTRIBUTION.txt` mà `main` không có (xem dưới) |

**Về `Assets/ATTRIBUTION.txt`** — tệp duy nhất không còn ở `main` công khai. Đọc nội dung thì nó là ghi công
của **`ConcertGrand.sf2` (YDP Grand Piano, FreePats/Zenph, CC BY 3.0)**, tức nhạc cụ đóng gói **đã bị bỏ hẳn
từ 2.0.0** (2026‑10‑10) khi app chuyển sang Salamander Grand Piano V3 tải riêng. Ghi công của nhạc cụ *hiện
hành* không nằm ở tệp đó: `Audio/SalamanderPiano.cs` và `tools/build_installer.ps1` sinh/kiểm attribution
CC BY 3.0 **cạnh các tệp mẫu sau khi tải xong** (`README.md` mục *Giới hạn hiện tại* mô tả đúng như vậy, và
gói phát hành cố ý không chứa mẫu). Vậy tệp trong 4 nhánh nháp là **văn bản ghi công đã hết hiệu lực**,
không phải thứ cần giữ trên trang sản phẩm; nội dung của nó được chép nguyên văn vào §6 để không mất chữ nào.

## 4. Giữ lại — 13 nhánh, có lý do

### 4.1 Mười một nhánh `diverged` trong kho nguồn — còn commit **chưa từng vào `main`**

Xoá những nhánh này là mất commit thật (dù phần lớn nội dung đã được viết lại và vào `main` bằng PR khác).
Không có lệnh nào trong đợt dọn này chạm tới chúng; bảng này là để chủ repo tự quyết.

| Nhánh | Commit riêng | Mới nhất | PR | Nhận định |
|---|---|---|---|---|
| `arena/01a0e9f0-pianopath` | 8 | 2026-09-28 | không có | 8 commit dựng lại stage renderer thành shader partials + trang settings màu/shader/lens/film — hướng này sau đó vào `main` bằng đường khác (GPU engine); **không có PR nào**, nên đây là bản duy nhất |
| `arena/01a105c8-pianopath` | 7 | 2026-10-04 | #47 MERGED | 7 commit, có `feat(audio): switch to Salamander SFZ and improve output UX` — nội dung đã vào `main` qua PR #47 nhưng **bằng SHA khác**; commit cuối là preview refresh của CI |
| `arena/01a0ea2d-pianopath` | 6 | 2026-09-28 | #6 CLOSED | 6 commit nền shader PBR Direct3D + biên dịch bytecode trong CI, kèm 1 cặp commit/revert bloom — PR #6 |
| `arena/5812ee79-pianopath` | 6 | 2026-10-05 | không có | 6 commit AR‑6.2a/6.2b (`ArSceneMap`, note pass vẽ qua scene map) — hướng AR cũ đã bị thiết kế lại §14 thay thế, nhưng **đây là nơi duy nhất còn mã đó**; không có PR |
| `arena/01a0f630-pianopath` | 2 | 2026-10-01 | #31 MERGED | 2 commit GPU atmosphere/hit-line + sửa kiểm chứng timing hạt — PR #31 đã merge, nhánh còn 2 commit khác SHA |
| `arena/01a0e8eb-pianopath` | 1 | 2026-09-28 | #1 MERGED | 1 commit `Redesign the UI into a professional settings dock` — PR #1, bản sớm của dock; cùng tên commit với một nhánh `behind` đã xoá |
| `arena/01a0f39a-pianopath` | 1 | 2026-09-30 | #26 MERGED | 1 commit, là preview refresh của CI — nội dung ảnh đã bị các lần render sau ghi đè |
| `arena/01a102c2-pianopath` | 1 | 2026-10-03 | #44 MERGED | 1 commit `Fix public changelog link to private roadmap` — PR #44 |
| `arena/01a108bd-pianopath` | 1 | 2026-10-05 | #53 MERGED | 1 commit AR‑6.2a `ArSceneMap` — cùng nội dung với commit đầu của `arena/5812ee79`; PR #53 |
| `arena/41464266-pianopath` | 1 | 2026-10-10 | #60 MERGED | 1 commit, là preview refresh của CI — PR #60 (phát hành 2.0.0 + ký mã SignPath) |
| `arena/a983bbdb-pianopath` | 1 | 2026-10-10 | #62 CLOSED, #61 MERGED | 1 commit `Keep native stderr from ending the release steps` — PR #62/#61 (release 2.0.0 unsigned) |

### 4.2 Những nhánh **không** đụng tới

| Nhánh | Vì sao giữ |
|---|---|
| `main` | nhánh chính |
| `arena/0e081d37-pianopath` | nhánh của phiên làm việc đang tạo ra tệp này |
| `release/public-v2.0.1`, `release/public-v2.1.1` (ở `lxmtuu/KeyFlow`) | **đang được dùng**: ghi chú phát hành của mỗi tag link CHANGELOG vào đúng nhánh `release/public-<tag>` (`release.yml` bước *Decide where the release notes' changelog links point*). Đã kiểm link của v2.1.1 trỏ tới nhánh có thật; v2.1.0 là pre-release viết tay, không link nên không gãy |

## 5. Một hệ quả của vòng đổi visibility, phát hiện trong lúc dọn

`gh api repos/lxmtuu/PianoPath/rulesets` được đo **bốn lần** trong phiên, ở cả hai trạng thái visibility:

| Lần đo | Trạng thái kho | Kết quả |
|---|---|---|
| sáng 2026‑10‑16 (khi dọn nhánh) | private | **403** "Upgrade to GitHub Pro or make this repository public to enable this feature" — gói Free không có ruleset cho kho private |
| giữa ngày | public | **mảng rỗng** — 0 ruleset |
| cuối phiên | **private lại** | 403 lại |

Kết luận: hai ruleset `24265624` (*Protect Branch*) và `24233546` (*Protect Main Branch*, có `pull_request` +
code-owner review) mà `docs/REPO-AUDIT.md` §6.2/§8.1 từng đo **đã không sống sót qua vòng private→public**, và
không tự quay lại khi đổi visibility lần nữa. `main` vì thế hiện **không còn gì bảo vệ** — không buộc pull
request, không code-owner review — với người có quyền push thì push thẳng vào được. Kèm theo, sao công khai
**3 → 2** vĩnh viễn: sao của người ngoài bị gỡ lúc kho private lần đầu và không trả lại (hai sao còn đọc được
bằng tên: `jin3107`, `lxmtuu`).

Việc này **không đụng vào bất kỳ nhánh nào đã dọn** (49 nhánh xoá vẫn xoá, 13 nhánh giữ vẫn giữ), nhưng nó
đổi nghĩa của mấy câu trong hai workflow: các nhánh xử lý **GH013** ("nhánh chỉ nhận pull request nên bước
commit ảnh chỉ cảnh báo") nay mô tả một hoàn cảnh không tồn tại. Chúng vô hại nên giữ; còn protection của
`main` là quyết định của chủ repo, và có một ràng buộc phải nói thẳng: **gói Free chỉ có ruleset cho kho
public** — muốn protection mà vẫn private thì cần GitHub Pro. Nếu dựng lại, **đừng chép nguyên bản cũ**:
`bypass_actors: null` của nó chặn cả `GITHUB_TOKEN`, tức chặn luôn bước commit ảnh preview của CI
(`docs/REPO-AUDIT.md` §6.2 đã ghi bài học đó).

## 6. Nội dung `Assets/ATTRIBUTION.txt` của thời SF2 (chép lại để không mất)

```text
Built-in SoundFont: YDP Grand Piano SF2 (2016-08-04)

SoundFont assembly and sample editing: Roberto, FreePats project.
Recorded source: Yamaha Disklavier Pro piano multisamples from Zenph Studios;
the source performances were recorded for OLPC by Dr. Mikhail Krishtal and team.

License: Creative Commons Attribution 3.0 (CC BY 3.0)
https://creativecommons.org/licenses/by/3.0/

Original source and attribution details:
https://freepats.zenvoid.org/Piano/acoustic-grand-piano.html

The included SF2 sample data is unchanged from the upstream YDP Grand Piano
SoundFont. Only its filename was changed to ConcertGrand.sf2 so Keyflow can
load it as its bundled piano. Keyflow adds optional real-time hall reverb while
playing; the SoundFont itself is not modified. Yamaha, Zenph Studios, and OLPC
are not affiliated with or endorsing Keyflow.
```

## 7. Cách khôi phục

Trong thời gian GitHub còn giữ object (mặc định object không tới được bị dọn sau ~90 ngày, và sớm hơn nếu có
lượt GC chủ động):

```bash
# một nhánh của kho nguồn, ví dụ nhánh đầu bảng §1 (SHA lấy ở cột 2)
git push origin b781d871f8d37eff43c9b90a167aff9c12bd0d72:refs/heads/arena/01a0e95c-pianopath
```
Bốn nhánh nháp ở §3 đã bị xoá **bằng lệnh của chủ repo**, nên đường khôi phục của chúng (nếu cần) là
`git push https://github.com/lxmtuu/KeyFlow <SHA>:refs/heads/<nhánh>` với SHA ở bảng §3 — nhưng lưu ý nội dung
duy nhất không còn ở `main` công khai là tệp ghi công đã hết hiệu lực chép lại ở §6.
Nếu SHA đã bị GC: nội dung của 49 nhánh ở §1 **vẫn còn nguyên trong `main`** (đó chính là lý do chúng được
xoá), và lịch sử PR của từng nhánh vẫn xem được trên GitHub sau khi nhánh bị xoá.

