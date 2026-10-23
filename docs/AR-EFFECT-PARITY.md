# Family hiệu ứng trong lớp AR: mười bảy family `Unverified` (N17)

> **Trạng thái:** đợt 1 làm 2026-10-23 · **Mã:** `Diagnostics/ArEffectFamilyReview.cs` · **Chốt chặn:**
> `tools/check_sources.py` (`scan_ar_effect_family_review`) · **Ảnh:** `docs/evidence/ar-effect-sheets/hold.png`

## 1. Câu hỏi, và câu trả lời ngắn

**Câu hỏi:** mười bảy family hiệu ứng mà `EffectContracts` để `ArOverlay = Unverified` có thật sự đi tới lớp phủ
AR không, và nếu có thì cái gì trong ảnh AR đổi?

**Câu trả lời ngắn, và nó không phải câu mình chờ đợi:** *phần lớn đã được đo rồi* — nhưng bằng **assertion**, không
bằng ảnh. `VerifyArNoteProjection` bật/tắt bốn family hold và đẩy hai đầu phần trăm của chúng, đòi hơn 20 px mỗi
lần; nó cũng đo trail, shimmer, ghost, landing glow, vật liệu nốt và cả một look "busy" cho composite camera. Những
gì **chưa** có phép đo riêng chỉ còn hai chỗ: **`hold.arc`** (chỉ được *đếm*, chưa lần nào so hai ảnh) và
**`falling.pulse`** (chỉ xuất hiện trong look "busy", không có assert riêng). Đó là lý do đợt 1 của N17 chọn đúng
nhóm hold: nó vừa đóng chỗ trống của arc, vừa biến bốn phép đo đang nằm trong log thành **một trang ảnh có manifest**
mà người đọc nhìn được.

Nói cách khác: N17 không phải một tuyên bố mới. Nó là **cùng phép đo, lần đầu có ảnh**.

## 2. Vì sao contract vẫn `Unverified` dù đã đo

`VerificationSuite.Ar.cs` ghi lý do ngay trong assert: *"Sampled S5 WARP checks do not justify Native family support
or Applied bindings for every option/value."* Một khung, một pose, một giá trị mỗi thiết lập không phải là độ phủ
giá trị/vòng đời mà một claim `Applied` đòi. Vì vậy trang này **không đổi một binding nào** — nó chỉ ghi lại số và
ảnh, để lần sau ai muốn mở claim thì có sẵn thứ để đọc.

Điều đó khác N16: ở đó contract tự viết *"remain Unverified until an AR-specific style/value matrix is reviewed"*,
nên một ma trận là đủ để lật binding. Ở đây câu đó không tồn tại, nên lật binding sẽ là nói quá.

## 3. Hiện trạng: mỗi family đang có bằng chứng gì

Đọc `Diagnostics/VerificationSuite.Ar.cs` trên tip `869ab68`:

| Nhóm | Family | Suite đo hôm nay |
|---|---|---|
| hold | `hold.bar`, `hold.breath`, `hold.vibration`, `hold.color-cycle` | bật/tắt **và** hai đầu phần trăm, mỗi phép assert **> 20 px** (dòng ~1860–1882) |
| hold | `hold.arc` | **chỉ đếm**: `ArHoldArcCount == 0` ở view overhead 90°, `> 0` ở Ultra (dòng ~1949, ~2004). **Chưa từng so hai ảnh.** |
| falling | `falling.trail`, `falling.shimmer` | quad + pixel: Glow/Stream phải khác nhau, shimmer nhân đôi quad, mỗi phép **> 20 px** |
| falling | `falling.pulse` | **không có assert riêng** — chỉ nằm trong look "busy" dùng cho composite camera |
| impact | `impact.wave`, `impact.flash`, `impact.morph` | "pixel-checked" trong dòng PASS: ring Shockwave/Absorb, flash Plasma/Morph |
| impact | `impact.burst` | chỉ nằm trong look Live Stage đặt nhiều family cùng lúc; không có assert riêng |
| particles | `particles.sparks`, `particles.wisps`, `particles.flames` | đếm quad **và** `effectPixels > 100` ở Ultra — nhưng cùng lúc cho cả ba |
| release | `release.fx` | đếm `ArReleaseEffectCount` |
| atmosphere | `atmosphere.beams` | đếm quad; pixel chỉ trong look "busy" |

Hai hệ quả cho thứ tự làm tiếp:

1. **`hold.arc` và `falling.pulse` là hai chỗ trống thật.** Một cái đã có trong đợt 1; cái còn lại nên vào đợt 2.
2. **Nhóm đo "cùng lúc" (`impact.burst`, `particles.*`, `atmosphere.beams`, `release.fx`) không quy được phép đo
   cho từng family** — một khung có cả sáu thứ thì `effectPixels > 100` không nói cái nào đóng góp. Đó là việc của
   các đợt sau, và nó là việc **tách** chứ không phải **đo thêm**: mỗi family phải có một khung mà nó là thứ duy nhất
   được bật.

## 4. Đợt 1: năm family hold

### 4.1 Cảnh

Một cặp phím **C3 + C5 giữ nguyên suốt lượt chạy**, không có bài hát, không có nền, không có family nào khác:

- hai **thanh live bar** dưới hai ngón tay → mang `hold.bar`, `hold.breath`, `hold.color-cycle`, `hold.vibration`;
- hai pitch đang hoạt động → `BuildHoldArcs` nối đúng **một cung** giữa chúng → mang `hold.arc`.

Hai phím cách nhau **hai quãng tám** là kết quả của hai lượt đo hỏng. Lượt đầu giữ C4 + E4 (bốn nửa cung): cung giữa
chúng chỉ là một vệt **46 px** ngang — đúng về hình học, vô dụng để đo. Cung được làm từ *sải tay*, nên sải tay phải
dài; C3 + C5 cho cung khoảng 350 px.

Mỗi **cột** của trang bật **đúng một** family và để bốn cái kia tắt. Đây là chỗ chặt hơn N16: ở N16 mọi thiết lập
khác của chất liệu vẫn đang bật, nên một con số phải được đọc là "thiết lập này, trên nền chất liệu kia". Ở đây một
khác biệt **không thể** đến từ family mà cột đó không nói tới.

### 4.2 Ba hàng

| Hàng | Chụp gì | Vì sao |
|---|---|---|
| `rest` | family đang bật, ở **giá trị của sản phẩm** | khung tham chiếu của cột |
| `toggle` | **công tắc tắt** | câu hỏi "family có vẽ không" — mọi khung khác của cột đều bật nó |
| `amount` | **phần trăm ở đầu trên** của khoảng contract khai báo | câu hỏi "tham số có tới ảnh không", mà không lặp lại chuyện tắt family |

Hàng giữa luôn là **tắt**, không phải "lật so với mặc định của sản phẩm": cả năm công tắc này mặc định đều
**tắt**, nên lật theo mặc định sẽ bật family lên và hàng đó thành bản sao của khung `rest`. Lượt chạy đầu tiên đo
đúng như vậy — cả năm hàng `toggle` ra **0 px** — và đó là lỗi của phép đo, không phải phát hiện về sản phẩm.

Đầu trên chứ không phải "đầu xa mặc định nhất": với `HoldBarIntensity` (mặc định 60) và `HoldArcIntensity` (70),
đầu xa mặc định là **0**, mà 0 chính là tắt family — hàng `toggle` đã chứng minh điều đó rồi. Đẩy lên 100 thì hàng
thứ ba hỏi được một câu khác.

### 4.3 Hai cổng chống "xanh vì không có gì để đổi"

- **Cổng nền:** mỗi khung phải tô đè **≥ 500 px** lên ảnh camera. Ảnh AR là ảnh ghép trên camera nên "pixel sáng"
  luôn bằng cả khung hình (921 600 px) và chẳng chứng minh gì; thứ đáng đếm là phần lớp phủ vẽ thêm.
- **Cổng nhân chứng:** mỗi cột phải có **counter quad riêng của layer** khác 0 ở khung `rest` — `ArLiveBarCount` cho
  bốn family thanh, `ArHoldArcCount` cho cung. Không có cổng này, một cảnh hỏng (mất thanh, mất cung) sẽ khiến cả ba
  khung của cột giống nhau và trang sẽ ghi "family này không tới AR" — đúng câu trả lời sai theo cách nguy hiểm nhất.
  Bảng counter là thứ duy nhất trong review viết tay, nên `scan_ar_effect_family_review` bắt nó phải khớp **đúng**
  danh sách family của contract.
- **Cổng dấu chân:** counter khác 0 mới chỉ nói layer *phát* quad; nó không nói quad có nhìn thấy được. Vì vậy hàng
  `toggle` — hàng duy nhất lấy cả family ra khỏi ảnh — phải đổi **≥ 12 px**. Cung ở pose nhìn thẳng đã phát đủ 10
  segment và vẽ gần như không có gì; cổng này bắt được đúng trường hợp đó và báo là *cảnh* sai, thay vì ghi ba khung
  gần giống nhau thành một kết quả.

### 4.4 Phạm vi

Pose **`ar-oblique`** (camera nghiêng 55°), camera tổng hợp `ar-keyboard-topdown.png`, 1280×720, 30 fps, 12 khung,
seed `20261021`, WARP. Pose này không phải pose chuẩn của các review nốt (`ar-reference`) vì một lý do đo được: ở
`ar-reference` camera nhìn thẳng xuống bàn phím, còn cung giữ phím là một dải **dựng đứng** — bật cung lên ở 70% rồi
tắt hẳn chỉ đổi **18 px** (và 39 px với cặp C4 + E4 ở chính pose nghiêng). Đó là một sự thật về pose, không phải về thiết lập, và một cột đo ở pose đó không trả lời
được câu hỏi của nó.
Một giá trị mỗi thiết lập, một pose, một tier. **Không** phải duyệt mỹ thuật, không phủ mọi giá trị, không phải camera
thật hay khung hình phần cứng.

## 5. Kết quả

Năm family, cùng một cảnh, cùng một lượt chạy. Số là **pixel đổi** so với khung `rest` của chính cột đó, dung sai
2/255. Nguồn số: **đọc lại từ tờ ảnh đã commit** — `docs/evidence/ar-effect-sheets/hold.png`, 3 890 × 3 802 px, lưới
3 cột × 5 hàng, mỗi hàng một family, mỗi ô là ảnh gốc 1280 × 720 **không hạ mẫu** (cùng cách N16 đã đọc số của nó).
Manifest đầy đủ nằm trong artifact của lượt chạy, mà sandbox này không tải được; vì ô ảnh là bản sao 1:1 của khung
chụp nên phép đếm trên tờ ảnh cho đúng con số manifest ghi.

| Family | Công tắc **tắt** | Phần trăm ở **đầu trên** |
|---|---:|---:|
| `hold.bar` | 7 948 px | `HoldBarIntensity` 100 → **14 625 px** |
| `hold.breath` | 6 915 px | `HoldBreathRate` 100 → **9 463 px** |
| `hold.vibration` | 7 824 px | `HoldVibrationAmount` 100 → **8 357 px** |
| `hold.color-cycle` | 11 469 px | `HoldColorCycleSpeed` 100 → **13 319 px** |
| `hold.arc` | **124 px** | `HoldArcIntensity` 100 → **87 px** |

**Đọc bảng:**

1. **Bốn family thanh đều vượt ngưỡng rất xa.** Tắt family đổi 6 900–11 500 px, đẩy phần trăm lên đầu trên đổi
   8 400–14 600 px. Mỗi thiết lập tới được quad AR, không phải chỉ tới shader trên giấy.
2. **`hold.arc` đúng nhưng nhỏ — và nhỏ vì hình, không vì binding.** Cung là một sợi chỉ: hộp bao của phần đổi là
   **295 × 64 px** nhưng chỉ **~130 px** thật sự đổi, vì dải ruy-băng mỏng cỡ một pixel và bị jitter làm thành nét
   đứt. Con số 124 và 87 vì thế là *đúng* và vẫn trên ngưỡng 12 px, nhưng nó đo một sợi chỉ, không đo một khối sáng.
   Phóng to ô đó lên ba lần (ngoài repo, chỉ để đọc) thì thấy đúng thứ đó: một **đường chấm** chạy từ thanh bar xanh
   bên trái sang thanh bar hồng bên phải, mảnh tới mức mắt thường phải nhìn kỹ mới thấy.
3. **Ba lượt đo để có một cột.** `ar-reference` + C4/E4: **18 px**; `ar-oblique` + C4/E4: **39 px**; `ar-oblique` +
   C3/C5: **124 px**. Hai lần đầu không phải lỗi của sản phẩm mà là lỗi của phép đo — pose nhìn thẳng làm cung nằm
   nghiêng theo cạnh, và bốn nửa cung thì không có sải tay. Cả hai con số được giữ lại ở đây vì chúng là lý do
   `PoseId` và `HeldKeys` mang giá trị hiện tại.

**Không có gì được lật.** Contract vẫn `Unverified` cho cả năm family, và bảng trên là lý do nó *có thể* được lật
sau này — không phải lý do để lật bây giờ.

## 6. Việc tiếp theo

| Đợt | Nội dung | Vì sao đứng ở thứ tự này |
|---|---|---|
| 2 | `falling.pulse` + `impact.burst` | hai chỗ trống thật còn lại; cùng một cảnh nốt rơi nên dùng lại được khung của N16 |
| 3 | ba family `particles.*` | phải tách khỏi nhau trước khi đo: hôm nay chúng chỉ có một con số chung |
| 4 | `release.fx`, `atmosphere.beams`, `impact.wave/flash/morph` | đã có phép đo riêng; đợt này chỉ là đưa ảnh vào repo |

Không đợt nào trong bốn đợt trên được phép lật binding. Muốn lật thì phải phủ **giá trị và vòng đời**, không phải
thêm một pose nữa — và đó là việc phải làm trên máy có GPU thật, không phải trong sandbox này.
