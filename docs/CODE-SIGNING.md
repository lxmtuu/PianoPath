# Ký mã (code signing) · SignPath — cách Keyflow hết bảng "Windows protected your PC"

> **Trạng thái (2026-10-10): chưa ký mã.** SignPath Foundation đã từ chối đơn đăng ký dự án mã nguồn mở vì
> dự án chưa đủ độ phủ công khai (sao, fork, người đóng góp, bài viết bên ngoài). Vì vậy `release.yml` **không
> còn bước ký**: các gói 2.0.0 phát hành chưa ký số, và trang phát hành hướng dẫn "More info → Run anyway".
> Gói trả phí của SignPath (từ 500 USD/năm) chỉ cấp chứng chỉ EV cho tổ chức đã đăng ký pháp lý. Phần thiết lập
> bên dưới giữ lại để tham khảo khi quay lại ký mã — khi đó phải thêm lại bước ký vào `release.yml`.

> **Status (2026-10-10): not code-signed.** SignPath Foundation declined the open-source application, so
> `release.yml` no longer has a signing step. The 2.0.0 packages are unsigned. The setup below is kept for
> reference for when signing is added back.

> *English summary (historical design, not active since 2026-10-10): the release packages were to be
> Authenticode-signed through [SignPath](https://signpath.io/) inside `release.yml`, **before** the ZIPs
> are packed and `SHA256SUMS.txt` is computed, and the workflow would refuse to publish an unsigned package.
> This document is the one-time setup a maintainer does on the SignPath side and in this repository's
> settings; the pipeline itself needs no change after that.*

## Vì sao cần ký mã

Windows SmartScreen chặn tệp thực thi không có chữ ký đáng tin bằng bảng **"Windows protected your PC"**
mọi người vẫn gọi là *window protect* — người tải phải bấm *More info → Run anyway*, và nhiều máy chính
sách chặt hơn thì chặn hẳn. Chỉ có **chữ ký Authenticode từ chứng chỉ code signing** mới gỡ bảng này;
hash `SHA256SUMS.txt` chỉ kiểm được tính toàn vẹn, không thay chữ ký được. Từ 2.0.0, `release.yml` ký mọi
gói qua SignPath và **dừng lượt phát hành nếu chưa cấu hình xong** — không bao giờ phát hành gói chưa ký.

## Đăng ký SignPath (miễn phí cho open source)

1. Đăng ký dự án tại **[signpath.org](https://signpath.org/)** (SignPath Foundation — chứng chỉ code signing
   miễn phí cho dự án open source; quy trình duyệt thường cần maintainer xác minh danh tính). Khi apply,
   dùng kho phát hành công khai [lxmtuu/KeyFlow](https://github.com/lxmtuu/KeyFlow) làm đại diện dự án;
   lưu ý Foundation yêu cầu dự án **open source** — nếu reviewer cần xem mã nguồn, hãy sẵn sàng cho xem
   hoặc cân nhắc gói trả phí của SignPath / Azure Trusted Signing.
2. Trong SignPath: tạo **Project** (gợi ý slug `keyflow`), liên kết **Trusted Build System → GitHub.com**,
   và cài **[SignPath GitHub App](https://github.com/apps/signpath)** cho kho này.
3. Tạo **Artifact Configuration** (đặt làm **default**, hoặc nhớ slug đã đặt). Artifact mà workflow gửi lên
   là một ZIP gồm đúng bốn tệp thực thi, nên config ký cả bốn:

   ```xml
   <artifact-configuration xmlns="http://signpath.io/artifact-configuration/v1">
     <zip-file>
       <pe-file path="PianoPath-win-x64.exe"><authenticode-sign/></pe-file>
       <pe-file path="PianoPath-win-x64-fd.exe"><authenticode-sign/></pe-file>
       <pe-file path="PianoPath-win-arm64.exe"><authenticode-sign/></pe-file>
       <pe-file path="Keyflow-Setup-*.exe"><authenticode-sign/></pe-file>
     </zip-file>
   </artifact-configuration>
   ```

   (Cách nhanh hơn: chọn *Upload an artifact sample* với ZIP do một lượt `release.yml` chạy thử tạo ra —
   SignPath tự sinh config — rồi chỉnh lại như trên.)
4. Tạo **API token** (quyền *submitter* trên project) — giá trị này là **secret**, không commit vào repo.

## Cấu hình trong kho GitHub `lxmtuu/PianoPath`

Settings → *Secrets and variables* → Actions:

| Tên | Loại | Giá trị |
| --- | --- | --- |
| `SIGNPATH_API_TOKEN` | **Secret** | API token vừa tạo |
| `SIGNPATH_ORGANIZATION_ID` | Variable | Organization ID (GUID) của SignPath |
| `SIGNPATH_PROJECT_SLUG` | Variable | Slug của project — mặc định `keyflow` |
| `SIGNPATH_SIGNING_POLICY_SLUG` | Variable | Chính sách ký — mặc định `release-signing` |
| `SIGNPATH_ARTIFACT_CONFIGURATION_SLUG` | Variable | Slug artifact configuration — mặc định `default` |

## `release.yml` làm gì

1. **Validate signing configuration** — thiếu secret/variable nào thì run đỏ ngay kèm hướng dẫn, *trước*
   khi build bất cứ thứ gì.
2. Publish ba gói (`win-x64` self-contained, `win-x64-fd`, `win-arm64`) — **chưa** đóng ZIP.
3. Smoke test `--verify` trên bản `win-x64`, rồi biên dịch bộ cài Inno Setup.
4. Gom bốn tệp `.exe` vào artifact `unsigned-executables` (đổi tên phân biệt kiến trúc), gửi
   `signpath/github-action-submit-signing-request@v3` ký, tải bản đã ký về.
5. Chép bản đã ký về chỗ cũ và **kiểm tra chữ ký** bằng `Get-AuthenticodeSignature` — không `Valid` là run đỏ.
6. **Sau khi ký mới** đóng ZIP và tính `SHA256SUMS.txt`, rồi đính kèm vào GitHub Release.

Tóm lại: người tải chỉ nhận được bản đã ký, nên khi cài đặt hoặc chạy lần đầu Windows không hiện bảng
*Windows protected your PC*. Nếu Windows hỏi tường lửa khi dùng MIDI/camera mạng nội bộ thì đó là câu hỏi
bình thường của mọi ứng dụng, không phải bảng protect.
