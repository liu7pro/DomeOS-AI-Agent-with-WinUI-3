Unicode true
!include "MUI2.nsh"
!include "FileFunc.nsh"
!include "WinMessages.nsh"
!include "LogicLib.nsh"

; DomeOS AI —— 安装包脚本（NSIS 3.x）
;
; 打包前先发布一份自包含的产物到 setup-src，安装包就带上 .NET 运行时，
; 目标机器不需要预装 .NET 也能直接跑：
;   dotnet publish -c Release --self-contained -o setup-src
;
; 然后编译安装包：
;   $env:NSISDIR="%LOCALAPPDATA%\DomeOSBuild\nsis"
;   & "$env:NSISDIR\makensis.exe" "C:\Users\wslaoliu7a\Desktop\DomeOS AI Agent Dev\build.nsi"

!define APP_NAME       "DomeOS AI"
; 已经有另一个「DomeOS AI」时，安装显示名和默认目录都加上这个后缀
!define APP_SUFFIX     "（WinUI 3）"
!define APP_EXE        "DomeOS AI.exe"
; 改名之前用的名字，升级时拿来清掉旧目录和旧快捷方式
!define OLD_NAME       "DomeOS AI Agent Dev"
!define OLD_EXE        "DomeOS AI Agent Dev.exe"
!define APP_DESC       "DomeOS Team AI Agent"
!define APP_PUBLISHER  "DomeOS Team"
; 卸载项在注册表里的标识，换个软件就要换一个，别和别的程序撞上
!define APP_GUID       "d41f6e2a-9c73-4b18-8f5d-2a6c4e90317b"
!define SRC            "C:\Users\wslaoliu7a\Desktop\DomeOS AI Agent Dev\setup-src"
!define CONF_DIR       "DomeOS AI Agent Dev"

Name              "${APP_NAME}"

; 安装包默认落在项目目录；外部（比如打包脚本）可以用 /DOUT_DIR=... 指到别处去
!ifndef OUT_DIR
  !define OUT_DIR "C:\Users\wslaoliu7a\Desktop\DomeOS AI Agent Dev"
!endif
OutFile           "${OUT_DIR}\DomeOS-AI-Setup.exe"
; 正式包固定装到 Program Files；自检时可以用 /DINSTALL_DIR=... 指到临时目录，不动真环境
!ifndef INSTALL_DIR
  !define INSTALL_DIR "$PROGRAMFILES64\${APP_NAME}"
!endif
InstallDir        "${INSTALL_DIR}"
; 正式包要装到 Program Files 并写 HKLM，所以必须提权；
; 想单独看一眼向导长什么样时可以用 /DREQ_LEVEL=asInvoker 编一份不用提权的副本。
!ifndef REQ_LEVEL
  !define REQ_LEVEL "admin"
!endif
RequestExecutionLevel ${REQ_LEVEL}
SetCompressor /SOLID lzma

; 运行时才算得出来的名字：装了别的 DomeOS AI 就带「（WinUI 3）」
Var DISPLAY_NAME
Var OTHER_FOUND
Var OLD_DIR

; 安装过程中不铺那串文件名，连「显示详细信息」那个按钮也不留
ShowInstDetails nevershow
ShowUninstDetails show

; 让安装包在「属性 → 详细信息」里有像样的名字
VIProductVersion "1.0.0.0"
VIAddVersionKey "ProductName"     "${APP_NAME}"
VIAddVersionKey "FileDescription" "${APP_NAME} 安装程序"
VIAddVersionKey "CompanyName"     "${APP_PUBLISHER}"
VIAddVersionKey "LegalCopyright"  "DomeOS Team"
VIAddVersionKey "FileVersion"     "1.0.0.0"

; 向导左下角那行（默认是「Nullsoft Install System v3.03」），换成自己的名字
BrandingText "${APP_NAME}"

!define MUI_ICON   "${SRC}\Assets\app.ico"
!define MUI_UNICON "${SRC}\Assets\app.ico"
!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_NOAUTOCLOSE

; 和 Soto Player-Community 的安装程序摆成同一套页面：第一页就是授权条款，
; 底下那对按钮是「我同意(I)」「取消(C)」——MUI_LICENSEPAGE_BUTTON 就是干这个的。
; 页面上下两行说明也照着写，不然会多出一句「按 [PgDn] 往下卷动」的默认提示。
!define MUI_LICENSEPAGE_BUTTON
!define MUI_LICENSEPAGE_TEXT_TOP "在安装 ${APP_NAME} 之前，请检阅授权条款。"
!define MUI_LICENSEPAGE_TEXT_BOTTOM "如果你接受协议中的条款，单击 [我同意(I)] 继续安装。必须要接受协议才能安装 ${APP_NAME}。"
!define MUI_PAGE_CUSTOMFUNCTION_SHOW LicenseShow
!insertmacro MUI_PAGE_LICENSE "C:\Users\wslaoliu7a\Desktop\DomeOS AI Agent Dev\install-license.txt"
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

; MUI 的中文语言文件把同意键写成「我接受(I)」，Soto 那边是「我同意(I)」，
; 这里在页面显示后把那个按钮的文字改过来，两边就对得上了。
Function LicenseShow
  GetDlgItem $0 $HWNDPARENT 1
  SendMessage $0 ${WM_SETTEXT} 0 "STR:我同意(I)"
FunctionEnd

; 扫一遍卸载列表，看有没有另一个显示名正好是「DomeOS AI」的程序（自己的键要跳过）
!macro ScanOtherDomeOS ROOT
  StrCpy $9 0
  ${Do}
    EnumRegKey $8 ${ROOT} "Software\Microsoft\Windows\CurrentVersion\Uninstall" $9
    ${If} $8 == ""
      ${ExitDo}
    ${EndIf}
    ${If} $8 != "${APP_GUID}"
      ReadRegStr $7 ${ROOT} "Software\Microsoft\Windows\CurrentVersion\Uninstall\$8" "DisplayName"
      ${If} $7 == "${APP_NAME}"
        StrCpy $OTHER_FOUND "1"
      ${EndIf}
    ${EndIf}
    IntOp $9 $9 + 1
  ${Loop}
!macroend

Function .onInit
  StrCpy $OTHER_FOUND "0"
  StrCpy $OLD_DIR ""

  ; 自己的卸载项写在 WOW6432Node 下，64 位视图也扫一遍，避免漏掉别的安装
  SetRegView 64
  !insertmacro ScanOtherDomeOS HKLM
  !insertmacro ScanOtherDomeOS HKCU
  SetRegView 32
  !insertmacro ScanOtherDomeOS HKLM
  !insertmacro ScanOtherDomeOS HKCU

  StrCpy $DISPLAY_NAME "${APP_NAME}"
  ${If} $OTHER_FOUND == "1"
    StrCpy $DISPLAY_NAME "${APP_NAME}${APP_SUFFIX}"
    ; 默认目录跟着名字走；用 /D 指定过目录的就不动
    ${If} $INSTDIR == "$PROGRAMFILES64\${APP_NAME}"
      StrCpy $INSTDIR "$PROGRAMFILES64\${APP_NAME}${APP_SUFFIX}"
    ${EndIf}
  ${EndIf}

  ; 旧版（改名前）装在哪：装完把那个目录清掉，别在硬盘上留两份
  ReadRegStr $OLD_DIR HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_GUID}" "InstallLocation"
FunctionEnd

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "SimpChinese"
!insertmacro MUI_LANGUAGE "English"

Section "${APP_NAME}" SecMain
  SectionIn RO

  ; 连进度条上方那行「抽取: xxx.dll」也不要，安装页只留一条进度条
  SetDetailsPrint none

  SetShellVarContext all
  SetOutPath "$INSTDIR"

  ; 先把正在跑的旧版本关掉，否则文件换不掉（改名前后的 exe 名都试一次）
  nsExec::ExecToLog 'taskkill /IM "${APP_EXE}" /F'
  nsExec::ExecToLog 'taskkill /IM "${OLD_EXE}" /F'

  File /r "${SRC}\*.*"

  WriteUninstaller "$INSTDIR\Uninstall $DISPLAY_NAME.exe"

  WriteRegStr   HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_GUID}" "DisplayName"     "$DISPLAY_NAME"
  WriteRegStr   HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_GUID}" "DisplayIcon"     "$INSTDIR\${APP_EXE}"
  WriteRegStr   HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_GUID}" "Publisher"       "${APP_PUBLISHER}"
  WriteRegStr   HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_GUID}" "InstallLocation" "$INSTDIR"
  WriteRegStr   HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_GUID}" "UninstallString" '"$INSTDIR\Uninstall $DISPLAY_NAME.exe" /allusers'
  WriteRegStr   HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_GUID}" "QuietUninstallString" '"$INSTDIR\Uninstall $DISPLAY_NAME.exe" /allusers /S'
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_GUID}" "NoModify" 1
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_GUID}" "NoRepair" 1
  ; 版本号故意不写，那一列空着就好
  DeleteRegValue HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_GUID}" "DisplayVersion"

  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  IntFmt $0 "0x%08X" $0
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_GUID}" "EstimatedSize" "$0"

  ; 改名后旧快捷方式也要换掉，先删掉两种旧名字再建新的
  Delete "$SMPROGRAMS\${OLD_NAME}.lnk"
  Delete "$SMPROGRAMS\${APP_NAME}${APP_SUFFIX}.lnk"

  ; 所有人可见的开始菜单项
  CreateShortCut "$SMPROGRAMS\$DISPLAY_NAME.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}" 0 SW_SHOWNORMAL "" "${APP_DESC}"

  ; 旧版本装在别的目录时，把那份连旧 exe 一起清掉，不占两份空间
  ${If} $OLD_DIR != ""
  ${AndIf} $OLD_DIR != $INSTDIR
    RMDir /r "$OLD_DIR"
  ${EndIf}

  DetailPrint "配置会存放在 %APPDATA%\${CONF_DIR}，升级和卸载都不会动它。"

  ; 装完自动走到「正在完成」那一页；自动关闭由 MUI_FINISHPAGE_NOAUTOCLOSE 挡住
  SetAutoClose true
  SetDetailsPrint both

  System::Call 'shell32::SHChangeNotify(i 0x08000000, i 0, i 0, i 0)'
SectionEnd

Section "Uninstall"
  SetShellVarContext all
  nsExec::ExecToLog 'taskkill /IM "${APP_EXE}" /F'

  ; 安装时按当时的显示名建了快捷方式和卸载程序，这里从注册表读回来
  ReadRegStr $0 HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_GUID}" "DisplayName"
  ${If} $0 == ""
    StrCpy $0 "${APP_NAME}"
  ${EndIf}

  Delete "$SMPROGRAMS\$0.lnk"
  Delete "$SMPROGRAMS\${OLD_NAME}.lnk"
  RMDir /r "$INSTDIR"
  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_GUID}"

  ; 主人的配置、密钥和聊天存档留在 %APPDATA% 里，卸载不删
  DetailPrint "%APPDATA%\${CONF_DIR} 里的配置和聊天记录已保留。"

  System::Call 'shell32::SHChangeNotify(i 0x08000000, i 0, i 0, i 0)'
SectionEnd
