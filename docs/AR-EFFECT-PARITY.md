# Family hiệu ứng trong lớp AR: mười bảy family `Unverified` (N17)

> **Trạng thái:** đợt 1 + đợt 2 + đợt 3, 2026-10-23 · **Mã:** `Diagnostics/ArEffectFamilyReview.cs` · **Chốt chặn:**
> `tools/check_sources.py` (`scan_ar_effect_family_review`) · **Ảnh:** `docs/evidence/ar-effect-sheets/{held,approach,impact}.png`

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

## 5. Kết quả đợt 1: năm family hold

Năm family, cùng một cảnh, cùng một lượt chạy. Số là **pixel đổi** so với khung `rest` của chính cột đó, dung sai
2/255. Nguồn số: **đọc lại từ tờ ảnh đã commit** — `docs/evidence/ar-effect-sheets/held.png`, 3 890 × 3 802 px, lưới
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

## 6. Đợt 2: hai chỗ trống còn lại

### 6.1 Vì sao đúng hai family này

Đợt 1 chọn nhóm hold vì bốn family đã có phép đo và arc thì chưa. Rà lại toàn bộ 17 family (bảng §3) thì chỉ còn
đúng hai chỗ mà **không** có phép đo riêng nào:

| Family | Trước đợt 2 |
|---|---|
| `falling.pulse` | chỉ xuất hiện bên trong một look "busy" dùng để thử composite camera; không có assert nào cho riêng nó |
| `hold.arc` | *(đã xong ở đợt 1)* |

Còn `impact.burst` được đưa vào cùng đợt vì nó là family duy nhất trong nhóm impact **không** có assert riêng — nó
chỉ nằm trong look Live Stage đặt sáu family cùng lúc, nên con số `effectPixels > 100` không quy được cho nó.

### 6.2 Mỗi family một cảnh, vì vòng đời khác nhau

Đây là điều đợt 1 chưa phải làm: cả năm family hold sống trên cùng một cặp phím đang giữ. Hai family này thì không.

| Family | Cảnh | Vì sao không dùng cảnh khác |
|---|---|---|
| `falling.pulse` | **Approach** — một nốt C4 đang bay, chưa vang | `AddSongNote` chỉ nhân `pulse` vào alpha khi `!sounding`. Cảnh nốt rơi của N16 có nốt **đang vang** (start 0, duration 2, chụp ở 1,5 s) ⇒ pulse không bao giờ được áp. Cảnh ở đây: nốt `Start = 0,9 s`, `Duration = 0,5 s`, chụp ở `0,5 s` ⇒ đầu nốt còn cách phím 0,4 s, thân nốt 0,735 đơn vị (~18% tầm với AR). Review **assert** điều này trước khi render. |
| `impact.burst` | **Impact** — một cú đánh vào C4 ở đường hit, không nốt, không ngón | Family là *burst tại điểm chạm*. Dùng cảnh giữ phím sẽ đo emitter sustain chứ không đo cú nổ; `feed.Impact(60, 1)` cho đúng cú nổ. |

**Hệ quả cho trang:** mỗi cảnh một tờ ảnh riêng (`held`, `approach`, `impact`), vì hai khung từ hai camera-thời điểm
khác nhau không so được với nhau, còn trên cùng một tờ thì người đọc buộc phải hiểu ngầm rằng chúng cùng cảnh. Ba tờ
nhỏ cũng dễ đọc hơn một tờ 7 hàng × 1280 px.

### 6.3 Thiết lập thứ hai có thể là một *choice*

`impact.burst` không có phần trăm: nó có `ShowEmbers` (bool) và `ImpactBurst` (choice). Vì vậy trang định nghĩa
"đầu xa" chung cho cả hai kiểu:

- **Percentage** → `Maximum` mà contract khai báo;
- **Choice** → **giá trị cuối cùng** trong danh sách mà chính settings model sở hữu (`PianoVisualSettings.ImpactBursts`)
  khác với giá trị sản phẩm đang giữ. Với `ImpactBurst` mặc định `Embers` thì đầu xa là `Dust`.

Danh sách giá trị hợp lệ vẫn thuộc về settings model — review chỉ đọc. Và vì `Clamp()` **âm thầm** đổi một giá trị
không hợp lệ về mặc định, review đọc lại giá trị sau khi clamp và ném lỗi nếu nó khác giá trị định chụp; nếu không,
tấm ảnh sẽ mang chú thích `Dust` trong khi thực chất là `Embers`.

### 6.4 Kết quả

Ba tờ ảnh đã commit: `docs/evidence/ar-effect-sheets/held.png` (3 890 × 3 802, lưới 3 cột × 5 hàng — số của phần
này đã báo ở §5), `approach.png` (3 890 × 794, 3 cột × 1 hàng) và `impact.png` (3 890 × 794, 3 cột × 1 hàng). Mỗi ô
là ảnh gốc 1280 × 720 **không hạ mẫu**; cột 1 là family ở giá trị sản phẩm, cột 2 là công tắc **tắt**, cột 3 là
**đầu xa** của contract. Số là pixel đổi so với khung `rest` của chính cột đó, dung sai 2/255, **đọc lại từ tờ ảnh
đã commit** — manifest của lượt chạy nằm trong artifact mà sandbox này không tải được, cùng cách §5 đã làm.

| Family | Cảnh | Công tắc **tắt** | Đầu xa |
|---|---|---:|---:|
| `falling.pulse` | Approach | `FallingPulse` off → **4 504 px** | `FallingPulseRate` 100 → **4 439 px** |
| `impact.burst` | Impact | `ShowEmbers` off → **986 px** | `ImpactBurst` `Dust` → **2 219 px** |

**Đọc bảng:**

1. **`falling.pulse` đo đúng thân nốt.** Hộp bao của phần đổi là **58 × 105 px**, đúng bằng thân một nốt C4 ở cỡ
   đó, và phép so phóng 8× cho thấy phần đổi là **một vòng kín quanh thân nốt** (một phép dịch vị trí sẽ ra hai
   lưỡi liềm ở hai bên đối nhau, không ra một vòng kín): family đổi **độ sáng và quầng của nốt đang bay**, không
   đẩy nốt sang chỗ khác. Delta lớn nhất chỉ **19** (tắt) và **28** (đầu trên) trên thang 255 — đây là thứ thấy
   được khi đặt hai khung cạnh nhau, không phải một đốm sáng mới. Hai đầu gần bằng nhau (4 504 / 4 439) là đúng
   bản chất: tắt `FallingPulse` bỏ hẳn phần nhân alpha, còn đẩy `FallingPulseRate` lên 100 chỉ đổi pha ở thời điểm
   chụp — cả hai đều là **cùng một thân nốt** đổi sáng.
2. **`impact.burst` thấy bằng mắt thường.** Hộp bao **128 × 43 px** khi tắt và **128 × 53 px** khi `Dust`, nằm ngay
   trên phím bị đánh, delta lớn nhất **224**. `Embers` là chùm tia toả ra còn `Dust` là đám mây mờ: nhìn ba ô cạnh
   nhau là thấy, đúng thứ một trang kiểm chứng phải đưa ra. Đây cũng là lý do `impact.burst` phải chụp ở cảnh
   *impact* — trong cảnh giữ phím, con số sẽ đo emitter sustain chứ không đo cú nổ.
3. **Cả 21 ô đều qua cổng.** Review tự ném lỗi nếu khung gốc đè dưới 500 px lên hình camera, nếu layer không phát
   quad riêng nào, hoặc nếu tắt family không đổi đủ 12 px; bước 6 của CI xanh nghĩa là **không ô nào** bị từ chối,
   kể cả hai ô nhỏ nhất (`hold.arc` 124 / 87 px và `impact.burst` 986 px). Con số nhỏ ở đây là hình học của hiệu
   ứng, không phải binding hỏng.
4. **Tất định.** Tờ `held.png` của lượt này cho lại **đúng từng con số** của đợt 1 (7 948 / 6 915 / 7 824 / 11 469 /
   124 và 14 625 / 9 463 / 8 357 / 13 319 / 87) dù là một lượt CI mới trên runner mới: cùng seed, cùng pose, cùng
   12 khung thì ra cùng một ảnh. Hai tờ mới sinh trong cùng lượt đó nên dùng chung chuẩn ấy.

**Không có gì được lật.** Contract vẫn `Unverified` cho cả bảy family. Bảng trên là bằng chứng rằng bảy family
**tới được** lớp phủ AR và **đổi ảnh** ở cả hai đầu thiết lập; việc lật trạng thái là một bước riêng, cần ảnh
chứng minh **và** người duyệt.

## 7. Đợt 3: ba family `particles.*`

### 7.1 "Tách" nghĩa là gì ở đây

Ba family này không thiếu ảnh vì khó chụp, mà vì **một con số không quy được cho cái nào**. Suite hôm nay đặt cả ba
cùng lúc rồi đòi `effectPixels > 100`; nếu phép đo đó đổi thì không biết family nào đổi. Cách tách đã dùng cho nhóm
hold ở đợt 1 — mỗi cột chỉ đổi **một** thiết lập so với khung của chính nó — vẫn đúng ở đây, nhưng có hai chỗ mới
phải xử lý mà hai đợt trước không gặp.

### 7.2 Family khai nhiều hơn hai thiết lập

Đợt 1 và đợt 2 chỉ chụp những family khai **đúng hai** thiết lập, và trang **từ chối chạy** nếu một family khai
nhiều hơn — với lý do "cái thứ ba sẽ rơi khỏi trang mà không ai thấy". Ba family `particles.*` khai **sáu, năm và
ba** thiết lập, nên lý do đó phải được trả lời chứ không phải nới ra:

| Family | Thiết lập contract khai | Cột chụp | Không chụp ở đợt này |
|---|---|---|---|
| `particles.sparks` | `ShowEmbers`, `ParticleAmount`, `ParticleSpeed`, `ParticleSize`, `ParticleGlow`, `ParticleLife` | `ShowEmbers` + `ParticleAmount` 120 | 4 thiết lập còn lại |
| `particles.wisps` | `ShowWisps`, `WispAmount`, `WispSpeed`, `WispTurbulence`, `WispGlow` | `ShowWisps` + `WispAmount` 150 | 3 thiết lập còn lại |
| `particles.flames` | `ShowFlame`, `FlameIntensity`, `FlameHeight` | `ShowFlame` + `FlameIntensity` 100 | `FlameHeight` |

Quy tắc chọn thiết lập thứ hai là **máy đọc từ contract**, không phải người chọn: công tắc là tham số boolean đầu
tiên. Family khai **đúng hai** thiết lập thì giữ nguyên luật cũ — thiết lập còn lại là đầu xa, bất kể loại, nên
`impact.burst` vẫn đẩy `ImpactBurst` tới giá trị cuối trong danh sách. Family khai **nhiều hơn hai** thì đầu xa là
**phần trăm đầu tiên sau công tắc** theo đúng thứ tự contract khai báo, lấy `Maximum` mà contract ghi. Mọi thiết lập
còn lại được ghi vào manifest dưới khoá `unshot` và hiện thành một cột **"Không chụp ở đợt
này"** ngay trên trang HTML. Như vậy cột này không thể bị đọc thành "cả family đã được phủ": nó nói rõ nó phủ cái
gì, và phần còn lại là việc của một đợt sau.

### 7.3 Hai family nằm sau cùng một công tắc

`impact.burst` và `particles.sparks` cùng khai `ShowEmbers` làm công tắc, cùng cảnh *impact*. Đó không phải lỗi của
trang mà là cấu trúc của contract: **hình dạng cú nổ** (`ImpactBurst`: Embers/Splash/Fireworks/Confetti/Dust) và
**mật độ hạt** (`ParticleAmount`) là hai thiết lập khác nhau nằm sau cùng một công tắc. Hệ quả đo được, và phải nói
trước: hai cột này **trùng nhau từng pixel ở hai trong ba khung** (khung `rest` và khung `off`), chỉ khác ở khung
đầu xa. Nếu tấm ảnh trình bày hai cột đó như hai phép đo độc lập thì nó đang nói quá — nên trang ghi đúng như vậy.

### 7.4 Cảnh

`particles.wisps` và `particles.flames` là family **giữ phím** (`NoteHeld`, `WhileNoteHeld`) nên chụp trong cảnh
*Held* đã có: hai phím C3 và C5 đang giữ. Tờ `held.png` vì thế thành **7 hàng**. `particles.sparks` là family
**impact** (`NoteOn`, `FixedDuration`, neo `HitPoint`) nên chụp trong cảnh *Impact* đã có, cạnh `impact.burst`;
tờ `impact.png` thành **2 hàng**. Tổng: **10 family × 3 khung = 30 ảnh**. Tờ `approach.png` không đổi.

### 7.5 Kết quả

<!-- N17C:RESULTS -->

## 8. Việc tiếp theo

| Đợt | Nội dung | Vì sao đứng ở thứ tự này |
|---|---|---|
| ~~2~~ | ~~`falling.pulse` + `impact.burst`~~ | **xong** (§6) |
| ~~3~~ | ~~ba family `particles.*`~~ | **mã + guard xong**; §7.2 ghi rõ phần chưa phủ |
| 4 | `release.fx`, `atmosphere.beams`, `impact.wave/flash/morph` | đã có phép đo riêng; đợt này chỉ là đưa ảnh vào repo |

Không đợt nào trong bốn đợt trên được phép lật binding. Muốn lật thì phải phủ **giá trị và vòng đời**, không phải
thêm một pose nữa — và đó là việc phải làm trên máy có GPU thật, không phải trong sandbox này.
