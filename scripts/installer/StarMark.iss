; StarMark 安装包定义（批次 VB；✅P-166 的落地：setup.exe **只作首装入口**）
;
; 三处口径都由 scripts\make-installer.ps1 用 /D 传进来，这里一律不自己算：
;   版本   = Directory.Build.props 那一颗 <Version>（与 zip 载荷名同源，不带 v）
;   载荷树 = scripts\publish-tree.ps1 产出的同一棵树（**不重新 publish**：两处编译必漂移）
;   输出   = dist\StarMark-<版本>-setup.exe
;
; ⚠ 自更新**不走这颗包**：升级仍是 zip + update-manifest.json + update-manifest.sig 那套
;   （验签 → 对载荷名 → 逐文件对字节 → 事务替换 + 回滚）。这里不许出现第二套"检查更新"。
;
; 编码：这颗 .iss 必须是 **UTF-8 带 BOM ＋ CRLF**——Inno Setup 对无 BOM 的 UTF-8 按 ANSI 读，
; 结果是把中文提示写成乱码编进 setup.exe，构建 0 警告、测试全绿，只有用户看得见（#152 那一族）。

#define MyAppName      "StarMark"
#define MyAppPublisher "StarMark"
#define MyAppURL       "https://github.com/AH23333/StarMarkDesktop"
#define MyAppExeName   "StarMark.UI.exe"

#ifndef MyAppVersion
  #error 必须由 make-installer 传 /DMyAppVersion=<版本不带 v>（空版本会编出一个不知道是谁的包）
#endif
#ifndef SourceDir
  #error 必须由 make-installer 传 /DSourceDir=<publish-tree 产出的树>（空路径会让 Files 段指向盘根）
#endif
#ifndef OutputDir
  #error 必须由 make-installer 传 /DOutputDir=<输出目录，一般是 dist>
#endif

[Setup]
; ⚠ AppId 一经发布**不许改**：改了就是"另一个产品"——用户机器上会留下两个安装项，
;   升级变并存，卸载面板里那两条互相顶不掉。
AppId={{7E3C9A41-52D8-4C0F-9F61-2A6D7B5E8C14}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
; ⚠ 默认落**每用户可写**的位置，不是 Program Files（批次 VC，他裁的）。理由是一条实测读数，不是偏好：
;   C:\Program Files 的 ACL 给 BUILTIN\Users 只有 RX（子项继承 GR,GE），而自更新那条链要
;   "把旧树挪开 + 把新树写进去"，跑的是**普通权限**（永不自我提权）⇒ 装在 Program Files 时
;   更新器必然失败，只能回那句"这一版没装上"。本机 D:\StarMark 恰好宽 ACL（Authenticated Users 有 M）
;   才躲过——那是运气，不是设计。（2026-10-04 实测：icacls ＋ 只读写入/改名探针。）
;   仍然允许用户在向导里明确选"为所有用户装"（下面 PrivilegesRequiredOverridesAllowed=dialog），
;   那条路我们**照旧支持**，只是不再当默认，也不假装它还能自动更新。
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
DisableWelcomePage=no
OutputDir={#OutputDir}
OutputBaseFilename={#MyAppName}-{#MyAppVersion}-setup
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
; 仓库里没有 .ico（图标走 Windows 应用图标那套），所以不设 SetupIconFile：
; 指一个不存在的文件是**编译期就报错**，比悄悄用默认图标更难查的是反过来。
Compression=lzma2/max
SolidCompression=yes
; 载荷就是 publish-tree 那一棵 x64 树，没有 x86 分支可发。
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; 默认**每用户安装**（不弹 UAC、装完普通权限就能自更新）；仍把"为所有用户装"留给用户明确选。
; ⛔ 程序自己永远不为了写机身而提权（✅P-161／XP 改判）：所以这里不是"想办法绕过 UAC"，
;   而是把默认位置选在一个不需要绕过就能写的地方。
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
WizardStyle=modern
ShowLanguageDialog=no

[Languages]
; 中文语言包**不是 Inno 自带的那一颗**（官方只带 20 余种，简体中文属于另行下载的翻译）。
; 所以有就用、没有就退回英文向导——而不是构建失败：发布机不该因为少一颗可选文件就产不出包。
; 有没有用得上，由 make-installer 在收尾时打印出来（"没中文语言包"是可看到的降级，不许静默）。
#define HasChinese FileExists(AddBackslash(CompilerPath) + "Languages\ChineseSimplified.isl")
#if HasChinese
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
#endif
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; 整棵搬进去，**逐颗不过滤**：过滤（比如不装 Updater\）会静默砍掉自更新能力，
; 那比"包里多一颗文件"严重得多。
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; 只登记安装器自己写进去的那颗说明文件——不删它，卸载完 {app} 会留一个空不干净的空目录。
; ⚠ 这里**刻意不列** %LOCALAPPDATA%\StarMark：那下面是用户的全部内容（库、日志、缓存、剪贴板图片），
;   "卸载一个程序"与它的不可逆性完全不成比例。清理要从程序内的数据维护入口做，手必须落在看得见数据的地方。
Type: files; Name: "{app}\安装信息.txt"

[Code]
// ===== .NET 9 桌面运行时：缺了就地补上，不叫用户自己动手（P-54 在安装器这一侧同样成立）=====
// 载荷是 framework-dependent（✅P-166 第二条）⇒ 这是唯一一处"安装期依赖"。
// 顺序：① 共享框架目录里有 9+ ⇒ 直接过；② 有 winget ⇒ 静默装，然后**以重新检测为准**（不信 winget 自报的成功）；
// ③ 两条都不成 ⇒ 说清缺什么，给一条能点的出口（打开官方下载页），并允许"仍要继续"或取消。
//
// ⚠ 判定按**目录**不按注册表。这一条是被实测纠出来的：原本写的是查
//   HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedframework\Microsoft.WindowsDesktop.App 的子键，
//   而这台装了 9.0.12／9.0.19 的机器上，InstalledVersions\x64 底下**只有 sharedhost 一颗**，
//   sharedframework 整棵不存在（2026-10-04 现查）——按那条键查就会稳定误判"没装"，
//   于是每一个其实有运行时的用户都要被问一次。症状不是崩，是白问，测试全绿没人红。
//   C:\Program Files\dotnet\shared\<框架>\<版本> 才是宿主自己找运行时的地方，两侧同一颗事实。
//
// ⚠ 这一刀放在 PrepareToInstall 而不是 InitializeSetup：那里已经在"要装什么"的阶段，且
//   **权限模型已经定了**——InitializeSetup 跑在权限决定之前，读不到 IsAdminInstallMode。
// ⚠ 机器级装运行时那一次 MSI 必须要管理员，而默认形状现在是**每用户安装（不提升）**
//   ⇒ 非提升时根本不去跑那发注定失败的 winget，直接走分支 ③ 给一条能点的出口。
//   只有用户明确选了"为所有用户装"（dialog 覆盖 ⇒ IsAdminInstallMode=True）才试 winget。
// ⚠ 已知够不到的形状：按用户装的 dotnet（%LOCALAPPDATA%\Microsoft\dotnet）——这一支读的是
//   {commonpf64}（机器级那一棵），所以那种机器会走到分支 ③，而那里给的是"否＝照样装"，不会被锁死。

const
  SharedRoot       = '{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App';
  RequiredMajor    = 9;      // 只认这一档主版本，见下面那句注释：写成 >= 会把"只有 10"读成"能跑"
  RuntimePageUrl   = 'https://dotnet.microsoft.com/zh-cn/download/dotnet/9.0';
  MaxRedetectTries = 12;

function DesktopRuntimeInstalled: Boolean;
var
  Find: TFindRec;
  Root, Name: String;
  Dot, Major: Integer;
begin
  Result := False;
  Root := ExpandConstant(SharedRoot);
  if not DirExists(Root) then
  begin
    Log('[runtime] 没有共享框架目录：' + Root);
    Exit;
  end;
  if FindFirst(Root + '\*', Find) then
  try
    repeat
      Name := Find.Name;
      if ((Find.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0)
         and (Name <> '.') and (Name <> '..') then
      begin
        Dot := Pos('.', Name);
        if Dot > 1 then
        begin
          Major := StrToIntDef(Copy(Name, 1, Dot - 1), 0);
          // 判的是"有没有我们要的那一档主版本"，不是"有没有一个更高的"。
          // 载荷按 9.0 编，机器上只有一颗 10.x 时宿主会不会跨大档回退**不在这台的证据里**（本机 9.0.12／9.0.19 与 10.0.1 并存，
          // 永远测不出那条路），所以这里保守：认 9，其余一律走"自动补装 9"那一档——side-by-side 装上不打架，
          // 反过来赌一次回退不成，代价是用户双击没反应。
          if Major = RequiredMajor then
          begin
            Log('[runtime] 找到 ' + Root + '\' + Name);
            Result := True;
            Exit;                     // finally 照样走：FindClose 不漏
          end;
        end;
      end;
    until not FindNext(Find);
  finally
    FindClose(Find);
  end;
  Log('[runtime] 目录在，但没有主版本 ' + IntToStr(RequiredMajor) + ' 的那一档');
end;

function TryInstallRuntimeWithWinget: Boolean;
var
  ResultCode: Integer;
  Candidate: String;
begin
  Result := False;
  Candidate := ExpandConstant('{sys}\winget.exe');
  if not FileExists(Candidate) then
    Candidate := 'winget.exe';            // 交给 PATH；两条都不成时 Exec 返回 False
  if Exec(Candidate,
      'install --id Microsoft.DotNet.DesktopRuntime.9 -e --silent --accept-package-agreements ' +
      '--accept-source-agreements --disable-interactivity',
      '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0) then
    Result := DesktopRuntimeInstalled;    // 成不成由重新检测说了算，不由退出码说了算
end;

procedure OpenRuntimeDownloadPage;
var
  ResultCode: Integer;
begin
  if not ShellExec('open', RuntimePageUrl, '', '', SW_SHOWNORMAL, ewNoWait, ResultCode) then
    Log('[runtime] 打不开下载页（' + IntToStr(ResultCode) + '），只能把地址念出来了：' + RuntimePageUrl);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Choice: Integer;
  Tries: Integer;
  Why: String;
begin
  Result := '';
  if DesktopRuntimeInstalled then
  begin
    Log('[runtime] 找到 .NET 桌面运行时（主版本 = ' + IntToStr(RequiredMajor) + '），不装任何东西');
    Exit;
  end;

  // 机器级装运行时那一次 MSI 必须要管理员，而默认形状现在是**每用户安装（不提升）**
  // ⇒ 非提升时不去跑那发注定失败的 winget。白跑一次的代价不是慢，是用户看见"自动安装没成功"
  //   而把它读成 winget 坏了——那句谎话的成本我们付过（坑表 #261：别在没有读数之前替失败原因下结论）。
  //   只有他明确选了"为所有用户装"才进机器级那一支。
  if IsAdminInstallMode then
  begin
    Log('[runtime] 没找到 .NET 9 桌面运行时；这是机器级安装 ⇒ 试 winget');
    if TryInstallRuntimeWithWinget then
    begin
      Log('[runtime] winget 装好了（重新检测通过）');
      Exit;
    end;
    Why := '自动安装没成功——多半是没有 winget，或它被组织策略挡住了。';
  end
  else
  begin
    Log('[runtime] 没找到 .NET 9 桌面运行时；这是每用户安装（无管理员权限）⇒ 跳过 winget，直接给出口');
    Why := '这一份是「只给我装」，没有管理员权限，所以不替你机器级装运行时。';
  end;

  while True do
  begin
    Choice := MsgBox(
      'StarMark 需要 .NET 9 桌面运行时（Microsoft.WindowsDesktop.App），这台机器上没检测到。' + Why + #13#10#13#10 +
      '"是"：打开官方下载页。装好后回到这里它会自己重新检测，不用重跑整个向导。' + #13#10 +
      '"否"：不装运行时，先把程序装上（若它其实存在、只是不在我们读的那棵目录下，程序照样能起）。' + #13#10 +
      '"取消"：现在什么都不做。',
      mbInformation, MB_YESNOCANCEL);
    if Choice = mrCancel then
    begin
      Log('[runtime] 用户取消：运行时缺失，安装没有开始');
      Result := '没有安装：这台机器上缺少 .NET 9 桌面运行时，而你选择了取消。';
      Exit;
    end;
    if Choice = mrNo then
    begin
      Log('[runtime] 用户选择在缺运行时的情况下继续安装');
      Exit;
    end;
    Inc(Tries);
    if Tries > MaxRedetectTries then
    begin
      // 有界重试：这不是"请稍后再试"，是防止一个坏掉的注册表把向导钉成死循环。
      Log('[runtime] 重新检测次数到上限，停止等待');
      Exit;
    end;
    OpenRuntimeDownloadPage;
    if DesktopRuntimeInstalled then
    begin
      Log('[runtime] 重新检测到了，继续安装');
      Exit;
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    SaveStringToFile(ExpandConstant('{app}\安装信息.txt'),
      '版本 {#MyAppVersion}' + #13#10 +
      '这次是 setup.exe 首装；之后的升级请走应用内的"立即更新"（zip 载荷 + 签名清单 + 事务替换），' +
      '不必重复运行本安装包。' + #13#10 +
      '用户数据在 %LOCALAPPDATA%\StarMark，卸载不会删除它。' + #13#10, False);
end;
