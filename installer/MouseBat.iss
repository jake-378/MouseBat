#define AppName "MouseBat"
#ifndef AppVersion
#define AppVersion "1.1.3"
#endif
#define AppPublisher "Jake Taylor"
#define AppExeName "MouseBat.exe"

[Setup]
AppId={{C7471AB7-D2A0-4F8A-B13F-4B7509EA45F4}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL=https://github.com/jake-378/MouseBat
AppSupportURL=https://github.com/jake-378/MouseBat/issues
DefaultDirName={localappdata}\Programs\MouseBat
DefaultGroupName=MouseBat
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\installer-output
OutputBaseFilename=MouseBat-Setup-{#AppVersion}
SetupIconFile=..\res\mousebat.ico
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "startup"; Description: "Start MouseBat when I sign in"; GroupDescription: "Startup:"

[Files]
Source: "..\publish\MouseBat.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\publish\HidSharp.dll"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\MouseBat"; Filename: "{app}\{#AppExeName}"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "MouseBat"; ValueData: """{app}\{#AppExeName}"""; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch MouseBat"; Flags: nowait postinstall skipifsilent
