Unicode true
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"

!ifndef APP_VERSION
  !define APP_VERSION "1.0.0"
!endif
!define APP_NAME "Windows System Monitor"
!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\WindowsSystemMonitor"

Name "${APP_NAME}"
OutFile "..\dist\WindowsSystemMonitor-Setup.exe"
InstallDir "$LOCALAPPDATA\Programs\Windows System Monitor"
RequestExecutionLevel user
ManifestDPIAware true
ManifestSupportedOS all
SetCompressor /SOLID lzma
SetCompressorDictSize 32
ShowInstDetails show
ShowUninstDetails show
VIProductVersion "${APP_VERSION}.0"
VIAddVersionKey /LANG=1033 "ProductName" "${APP_NAME}"
VIAddVersionKey /LANG=1033 "FileDescription" "${APP_NAME} installer"
VIAddVersionKey /LANG=1033 "FileVersion" "${APP_VERSION}"
VIAddVersionKey /LANG=1033 "ProductVersion" "${APP_VERSION}"
VIAddVersionKey /LANG=1033 "LegalCopyright" ""

!define MUI_ICON "..\assets\app.ico"
!define MUI_UNICON "..\assets\app.ico"
!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_RUN "$INSTDIR\WindowsSystemMonitor.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Open ${APP_NAME}"
!define MUI_FINISHPAGE_RUN_NOTCHECKED
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH
!insertmacro MUI_LANGUAGE "English"

; Never terminate the application or its Windows-owned maintenance operations.
!macro RequireClosed
  FindWindow $0 "" "${APP_NAME}"
  ${If} $0 != 0
    MessageBox MB_OK|MB_ICONEXCLAMATION "Close ${APP_NAME} before continuing. If a maintenance command is active, use the application's safe exit options first." /SD IDOK
    Abort
  ${EndIf}
!macroend

Function .onInit
  SetShellVarContext current
  ${IfNot} ${RunningX64}
    MessageBox MB_OK|MB_ICONSTOP "This release requires 64-bit Windows 10 22H2 or Windows 11." /SD IDOK
    Abort
  ${EndIf}
  SetRegView 64
  ReadRegStr $2 HKCU "${UNINSTALL_KEY}" "InstallLocation"
  ${If} $2 != ""
    StrCpy $INSTDIR $2
  ${EndIf}
  ReadRegStr $0 HKLM "SOFTWARE\Microsoft\Windows NT\CurrentVersion" "CurrentBuildNumber"
  ${If} $0 < 19045
    MessageBox MB_OK|MB_ICONSTOP "This release requires Windows 10 22H2 (build 19045) or Windows 11." /SD IDOK
    Abort
  ${EndIf}
  ; Windows 10 on ARM has no supported x64 emulation; require Windows 11 there.
  ${If} ${IsNativeARM64}
  ${AndIf} $0 < 22000
    MessageBox MB_OK|MB_ICONSTOP "Running the x64 release on ARM requires Windows 11 x64 emulation." /SD IDOK
    Abort
  ${EndIf}
  !insertmacro RequireClosed
FunctionEnd

Function un.onInit
  SetShellVarContext current
  SetRegView 64
  !insertmacro RequireClosed
FunctionEnd

Section "${APP_NAME}" SEC_APP
  !insertmacro RequireClosed
  SetShellVarContext current
  SetRegView 64
  SetOverwrite on
  ClearErrors
  SetOutPath "$INSTDIR"
  File "..\dist\portable\WindowsSystemMonitor.exe"
  File /oname=README.txt "..\docs\portable-readme.txt"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  ${If} ${Errors}
    MessageBox MB_OK|MB_ICONSTOP "Could not write application files. Choose a writable folder and check that the application is closed, then retry." /SD IDOK
    Abort
  ${EndIf}

  CreateShortcut "$DESKTOP\${APP_NAME}.lnk" "$INSTDIR\WindowsSystemMonitor.exe" "" "$INSTDIR\WindowsSystemMonitor.exe" 0
  CreateShortcut "$SMPROGRAMS\${APP_NAME}.lnk" "$INSTDIR\WindowsSystemMonitor.exe" "" "$INSTDIR\WindowsSystemMonitor.exe" 0
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayName" "${APP_NAME}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayVersion" "${APP_VERSION}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "Publisher" "${APP_NAME}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\WindowsSystemMonitor.exe,0"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKCU "${UNINSTALL_KEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoRepair" 1
  ${If} ${Errors}
    MessageBox MB_OK|MB_ICONSTOP "The application files were copied, but a shortcut or uninstall registration could not be created. Run Uninstall.exe in the installation folder before retrying." /SD IDOK
    Abort
  ${EndIf}
SectionEnd

Section "Uninstall"
  !insertmacro RequireClosed
  SetShellVarContext current
  SetRegView 64
  ClearErrors
  IfFileExists "$INSTDIR\WindowsSystemMonitor.exe" 0 +2
  Delete "$INSTDIR\WindowsSystemMonitor.exe"
  IfFileExists "$INSTDIR\README.txt" 0 +2
  Delete "$INSTDIR\README.txt"
  ${If} ${Errors}
    MessageBox MB_OK|MB_ICONSTOP "Could not remove the application files. Close the application, check file permissions, and run uninstall again." /SD IDOK
    Abort
  ${EndIf}
  Delete "$DESKTOP\${APP_NAME}.lnk"
  Delete "$SMPROGRAMS\${APP_NAME}.lnk"
  DeleteRegKey HKCU "${UNINSTALL_KEY}"
  Delete "$INSTDIR\Uninstall.exe"
  ; Only remove an empty install directory. Never recursively delete user files.
  RMDir "$INSTDIR"
SectionEnd
