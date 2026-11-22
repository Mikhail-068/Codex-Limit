# -*- coding: utf-8 -*-
Unicode true
Name "Codex Gauge"
OutFile "${OUTPUT_FILE}"
InstallDir "$LOCALAPPDATA\Programs\CodexGauge"
RequestExecutionLevel user
SetCompressor /SOLID lzma

!include "MUI2.nsh"
!include "WinVer.nsh"
!include "x64.nsh"

!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\CodexGauge"
!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_RUN "$INSTDIR\CodexGauge.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Запустить Codex Gauge"
!define MUI_FINISHPAGE_RUN_NOTCHECKED

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "Russian"

VIProductVersion "${PRODUCT_VERSION}"
VIAddVersionKey /LANG=1049 "ProductName" "Codex Gauge"
VIAddVersionKey /LANG=1049 "ProductVersion" "${APP_VERSION}"
VIAddVersionKey /LANG=1049 "FileVersion" "${APP_VERSION}"
VIAddVersionKey /LANG=1049 "FileDescription" "Установщик Codex Gauge для Windows x64"
VIAddVersionKey /LANG=1049 "LegalCopyright" "Mikhail-068"

!macro InitOperation
    SetRegView 64
    SetShellVarContext current
    ; Keep the mutex handle open until this install or uninstall process exits.
    System::Call 'kernel32::CreateMutexW(p 0, i 0, w "Local\CodexGauge.Setup") p .r0 ?e'
    Pop $1
    ${If} $0 == 0
    ${OrIf} $1 == 183
        MessageBox MB_OK|MB_ICONSTOP "Установка или удаление Codex Gauge уже выполняется." /SD IDOK
        SetErrorLevel 3
        Abort
    ${EndIf}
!macroend

!macro RequireClosedWidget
    System::Call 'kernel32::OpenMutexW(i 0x00100000, i 0, w "Local\CodexGauge.Widget") p .r0'
    ${If} $0 != 0
        System::Call 'kernel32::CloseHandle(p r0)'
        MessageBox MB_OK|MB_ICONSTOP "Закройте Codex Gauge через пункт «Выход» в трее и повторите попытку." /SD IDOK
        SetErrorLevel 3
        Abort
    ${EndIf}
!macroend

Function .onInit
    ${IfNot} ${AtLeastWin10}
    ${OrIfNot} ${RunningX64}
        MessageBox MB_OK|MB_ICONSTOP "Требуется Windows 10 или 11 с поддержкой x64." /SD IDOK
        SetErrorLevel 2
        Abort
    ${EndIf}
    !insertmacro InitOperation

    ; Keep updates in the registered folder and honor /D on a fresh install.
    ReadRegStr $0 HKCU "${UNINSTALL_KEY}" "InstallLocation"
    ${If} $0 != ""
        StrCpy $INSTDIR $0
    ${EndIf}
FunctionEnd

Function un.onInit
    !insertmacro InitOperation
FunctionEnd

Section "Codex Gauge"
    !insertmacro RequireClosedWidget
    ClearErrors
    SetOutPath "$INSTDIR"
    File /oname=CodexGauge.exe.new "${PAYLOAD_DIR}\CodexGauge.exe"
    IfErrors installFailed

    SetOutPath "$INSTDIR\licenses"
    File "${LICENSE_DIR}\LICENSE.txt"
    File "${LICENSE_DIR}\dotnet-runtime-license.txt"
    File "${LICENSE_DIR}\dotnet-desktop-license.txt"
    File "${LICENSE_DIR}\dotnet-third-party-notices.txt"
    File "${LICENSE_DIR}\nsis-license.txt"
    IfErrors installFailed

    SetOutPath "$INSTDIR"
    ; Replace only after the complete new binary has been extracted.
    System::Call 'kernel32::MoveFileExW(w "$INSTDIR\CodexGauge.exe.new", w "$INSTDIR\CodexGauge.exe", i 9) i .r0'
    ${If} $0 == 0
        Goto installFailed
    ${EndIf}

    WriteUninstaller "$INSTDIR\Uninstall.exe"
    CreateShortcut "$SMPROGRAMS\Codex Gauge.lnk" "$INSTDIR\CodexGauge.exe"
    WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayName" "Codex Gauge"
    WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayVersion" "${APP_VERSION}"
    WriteRegStr HKCU "${UNINSTALL_KEY}" "Publisher" "Mikhail-068"
    WriteRegStr HKCU "${UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
    WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\CodexGauge.exe"
    WriteRegStr HKCU "${UNINSTALL_KEY}" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
    WriteRegStr HKCU "${UNINSTALL_KEY}" "QuietUninstallString" '$\"$INSTDIR\Uninstall.exe$\" /S'
    WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoModify" 1
    WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoRepair" 1
    IfErrors installFailed
    Goto installDone

installFailed:
    Delete "$INSTDIR\CodexGauge.exe.new"
    MessageBox MB_OK|MB_ICONSTOP "Не удалось установить Codex Gauge. Проверьте доступ к папке установки и повторите попытку." /SD IDOK
    SetErrorLevel 4
    Abort

installDone:
SectionEnd

Section "Uninstall"
    !insertmacro RequireClosedWidget
    SetOutPath "$TEMP"
    ClearErrors
    Delete "$INSTDIR\CodexGauge.exe"
    IfErrors uninstallFailed
    Delete "$INSTDIR\CodexGauge.exe.new"
    Delete "$INSTDIR\licenses\LICENSE.txt"
    Delete "$INSTDIR\licenses\dotnet-runtime-license.txt"
    Delete "$INSTDIR\licenses\dotnet-desktop-license.txt"
    Delete "$INSTDIR\licenses\dotnet-third-party-notices.txt"
    Delete "$INSTDIR\licenses\nsis-license.txt"
    Delete "$SMPROGRAMS\Codex Gauge.lnk"
    IfErrors uninstallFailed
    DeleteRegKey HKCU "${UNINSTALL_KEY}"
    IfErrors uninstallFailed
    Delete "$INSTDIR\Uninstall.exe"
    RMDir "$INSTDIR\licenses"
    RMDir "$INSTDIR"
    Goto uninstallDone

uninstallFailed:
    MessageBox MB_OK|MB_ICONSTOP "Не удалось удалить Codex Gauge. Проверьте доступ к папке установки и повторите попытку." /SD IDOK
    SetErrorLevel 4
    Abort

uninstallDone:
SectionEnd
