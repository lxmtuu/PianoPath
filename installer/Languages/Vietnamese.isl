; Vietnamese wizard text for Keyflow's installer (installer\Keyflow.iss).
;
; This is a *partial* translation: [Languages] lists it after compiler:Default.isl, and the files are
; read in order, so every name below replaces the English wording of that message while everything
; else (the pages this installer never shows, the shell-extension strings, the disk and component
; pages) keeps falling back to Default.isl. That is the documented way to ship a translation that
; stays maintainable — see "MessagesFile" in the Inno Setup help.
;
; Two things the file must not do:
;   * Contain [LangOptions]. LanguageName/LanguageID/LanguageCodePage live in installer\Keyflow.iss,
;     prefixed with the language name, because a partial file must not rely on overriding the ones
;     Default.isl already set.
;   * Name a message that Default.isl does not define, or put it in the wrong section. Inno Setup only
;     *warns* about an unrecognized message name and then ignores the line, so a typo here would
;     silently ship English text. tools/check_sources.py checks every name below against
;     installer/Languages/messages.txt (valid names, generated from Inno Setup's own Default.isl by
;     tools/inno_messages.py) and compares the placeholders on both sides, and tools/inno_messages.py
;     refuses to regenerate that list when a name in this file no longer exists.
;
; The wording follows the application's own Vietnamese (Localization/Strings.Vietnamese.cs) and the
; README: "Huỷ", "tệp", "thư mục", "shortcut", "Start Menu", "gỡ cài đặt". The file is UTF-8 with a
; BOM, which is how the compiler is told to read the diacritics.

[Messages]

; --- window titles and the language picker ---------------------------------------------------
SetupAppTitle=Trình cài đặt
SetupWindowTitle=Trình cài đặt - %1
SetupLdrStartupMessage=Bản này sẽ cài %1. Bạn muốn tiếp tục chứ?
SelectLanguageTitle=Chọn ngôn ngữ trình cài đặt
SelectLanguageLabel=Chọn ngôn ngữ dùng trong quá trình cài đặt.

; --- welcome page ----------------------------------------------------------------------------
WelcomeLabel1=Chào mừng bạn đến với trình cài đặt [name]
WelcomeLabel2=Trình cài đặt sẽ cài [name/ver] lên máy tính của bạn.%n%nNên đóng mọi ứng dụng khác trước khi tiếp tục.
ClickNext=Bấm Tiếp tục để đi tiếp, hoặc Huỷ để thoát khỏi trình cài đặt.

; --- licence page (the installer shows ..\LICENSE) --------------------------------------------
WizardLicense=Thoả thuận giấy phép
LicenseLabel3=Hãy đọc Thoả thuận giấy phép sau. Bạn phải đồng ý với các điều khoản của thoả thuận này trước khi tiếp tục cài đặt.
LicenseAccepted=Tôi &đồng ý với thoả thuận
LicenseNotAccepted=Tôi &không đồng ý với thoả thuận

; --- destination folder page -----------------------------------------------------------------
WizardSelectDir=Chọn thư mục cài đặt
SelectDirDesc=Cài [name] vào thư mục nào?
SelectDirLabel3=Trình cài đặt sẽ cài [name] vào thư mục sau.
SelectDirBrowseLabel=Để tiếp tục, bấm Tiếp tục. Nếu muốn chọn thư mục khác, bấm Duyệt.
DiskSpaceMBLabel=Cần ít nhất [mb] MB dung lượng trống.
DiskSpaceGBLabel=Cần ít nhất [gb] GB dung lượng trống.

; --- additional tasks page ([Tasks]: desktop shortcut) ---------------------------------------
WizardSelectTasks=Chọn việc làm thêm
SelectTasksDesc=Muốn làm thêm những việc nào?
SelectTasksLabel2=Chọn những việc phụ bạn muốn trình cài đặt làm trong lúc cài [name], rồi bấm Tiếp tục.

; --- Start Menu page -------------------------------------------------------------------------
WizardSelectProgramGroup=Chọn thư mục Start Menu
SelectStartMenuFolderDesc=Đặt shortcut của chương trình ở đâu?
SelectStartMenuFolderLabel3=Trình cài đặt sẽ tạo shortcut của chương trình trong thư mục Start Menu sau.
SelectStartMenuFolderBrowseLabel=Để tiếp tục, bấm Tiếp tục. Nếu muốn chọn thư mục khác, bấm Duyệt.
NoProgramGroupCheck2=&Không tạo thư mục Start Menu

; --- ready page ------------------------------------------------------------------------------
WizardReady=Sẵn sàng cài đặt
ReadyLabel1=Trình cài đặt đã sẵn sàng cài [name] lên máy tính của bạn.
ReadyLabel2a=Bấm Cài đặt để bắt đầu, hoặc bấm Quay lại nếu muốn xem lại hay đổi lựa chọn.
ReadyLabel2b=Bấm Cài đặt để bắt đầu.
ReadyMemoDir=Thư mục cài đặt:
ReadyMemoGroup=Thư mục Start Menu:
ReadyMemoTasks=Việc làm thêm:

; --- progress page ---------------------------------------------------------------------------
WizardPreparing=Đang chuẩn bị cài đặt
PreparingDesc=Trình cài đặt đang chuẩn bị cài [name] lên máy tính của bạn.
WizardInstalling=Đang cài đặt
InstallingLabel=Vui lòng đợi trong lúc trình cài đặt cài [name] lên máy tính của bạn.
StatusClosingApplications=Đang đóng các ứng dụng...
StatusCreateDirs=Đang tạo thư mục...
StatusCreateIcons=Đang tạo shortcut...
StatusExtractFiles=Đang giải nén tệp...
StatusRegisterFiles=Đang đăng ký tệp...
StatusRestartingApplications=Đang mở lại các ứng dụng...
StatusRollback=Đang hoàn tác thay đổi...
StatusRunProgram=Đang hoàn tất cài đặt...
StatusSavingUninstall=Đang lưu thông tin gỡ cài đặt...

; --- finished page ([Run]: open Keyflow) ------------------------------------------------------
FinishedHeadingLabel=Hoàn tất trình cài đặt [name]
FinishedLabel=Trình cài đặt đã cài xong [name] lên máy tính của bạn. Có thể mở ứng dụng từ shortcut vừa tạo.
FinishedLabelNoIcons=Trình cài đặt đã cài xong [name] lên máy tính của bạn.
ClickFinish=Bấm Hoàn tất để thoát khỏi trình cài đặt.

; --- buttons ---------------------------------------------------------------------------------
ButtonNext=&Tiếp tục >
ButtonBack=< &Quay lại
ButtonCancel=Huỷ
ButtonInstall=&Cài đặt
ButtonFinish=&Hoàn tất
ButtonBrowse=&Duyệt...
ButtonWizardBrowse=D&uyệt...
ButtonNewFolder=&Tạo thư mục mới
ButtonYes=&Có
ButtonNo=&Không
ButtonOK=OK

; --- dialogs the user can run into -----------------------------------------------------------
ConfirmTitle=Xác nhận
InformationTitle=Thông tin
ErrorTitle=Lỗi
ExitSetupTitle=Thoát trình cài đặt
ExitSetupMessage=Trình cài đặt chưa xong. Nếu thoát bây giờ, chương trình sẽ không được cài.%n%nBạn có thể chạy lại trình cài đặt sau để hoàn tất.%n%nThoát trình cài đặt?
SetupAborted=Trình cài đặt chưa hoàn tất.%n%nHãy sửa lỗi rồi chạy lại trình cài đặt.
CannotContinue=Trình cài đặt không thể tiếp tục. Hãy bấm Huỷ để thoát.
SetupAlreadyRunning=Trình cài đặt đang chạy.
SetupAppRunningError=Trình cài đặt thấy %1 đang chạy.%n%nHãy đóng mọi cửa sổ của nó rồi bấm OK để tiếp tục, hoặc bấm Huỷ để thoát.
UninstallAppRunningError=Trình gỡ cài đặt thấy %1 đang chạy.%n%nHãy đóng mọi cửa sổ của nó rồi bấm OK để tiếp tục, hoặc bấm Huỷ để thoát.
WindowsVersionNotSupported=Chương trình này không hỗ trợ phiên bản Windows mà máy bạn đang chạy.
DiskSpaceWarningTitle=Không đủ dung lượng
DiskSpaceWarning=Trình cài đặt cần ít nhất %1 KB dung lượng trống, nhưng ổ đĩa đã chọn chỉ còn %2 KB.%n%nBạn vẫn muốn tiếp tục chứ?
DirExistsTitle=Thư mục đã tồn tại
DirExists=Thư mục:%n%n%1%n%nđã tồn tại. Bạn vẫn muốn cài vào đó chứ?
DirDoesntExistTitle=Thư mục không tồn tại
DirDoesntExist=Thư mục:%n%n%1%n%nkhông tồn tại. Bạn có muốn tạo thư mục đó không?
BadDirName32=Tên thư mục không được chứa các ký tự sau:%n%n%1
InvalidDirName=Tên thư mục không hợp lệ.
ErrorCreatingDir=Trình cài đặt không tạo được thư mục "%1"
ErrorCopying=Đã xảy ra lỗi khi sao chép tệp:
ErrorExecutingProgram=Không chạy được tệp:%n%1

; --- uninstaller -----------------------------------------------------------------------------
UninstallAppTitle=Gỡ cài đặt
UninstallAppFullTitle=Gỡ cài đặt %1
WizardUninstalling=Tiến trình gỡ cài đặt
UninstallStatusLabel=Vui lòng đợi trong lúc gỡ %1 khỏi máy tính của bạn.
StatusUninstalling=Đang gỡ %1...
ConfirmUninstall=Bạn có chắc muốn gỡ hoàn toàn %1 và mọi thành phần của nó?
UninstalledAll=Đã gỡ %1 khỏi máy tính của bạn.
UninstalledMost=Đã gỡ xong %1.%n%nMột vài thành phần không gỡ được; bạn có thể xoá thủ công.
UninstalledAndNeedsRestart=Để hoàn tất việc gỡ %1, cần khởi động lại máy tính.%n%nBạn có muốn khởi động lại ngay bây giờ không?
OnlyAdminCanUninstall=Chỉ người dùng có quyền quản trị mới gỡ được bản cài này.
UninstallOnlyOnWin64=Bản cài này chỉ gỡ được trên Windows 64-bit.
UninstallNotFound=Tệp "%1" không tồn tại, không thể gỡ cài đặt.
UninstallOpenError=Không mở được tệp "%1", không thể gỡ cài đặt.
UninstallDataCorrupted=Tệp "%1" bị hỏng, không thể gỡ cài đặt.
UninstallUnsupportedVer=Tệp nhật ký gỡ cài đặt "%1" có định dạng mà bản gỡ cài đặt này không đọc được, nên không thể gỡ.
UninstallUnknownEntry=Gặp một mục lạ (%1) trong nhật ký gỡ cài đặt
UninstallDisplayNameMarkCurrentUser=tài khoản hiện tại
UninstallDisplayNameMarkAllUsers=mọi người dùng
UninstallDisplayNameMark32Bit=32-bit
UninstallDisplayNameMark64Bit=64-bit
UninstallDisplayNameMarks=%1 (%2, %3)

; --- the texts installer\Keyflow.iss asks for with {cm:...} ------------------------------------
; Default.isl defines these three in [CustomMessages], a namespace of its own: keeping them in
; [Messages] above would leave the desktop-shortcut task and the "launch Keyflow" checkbox English.
[CustomMessages]
AdditionalIcons=Shortcut thêm:
CreateDesktopIcon=Tạo shortcut ngoài &màn hình nền
LaunchProgram=Mở %1
UninstallProgram=Gỡ cài đặt %1
