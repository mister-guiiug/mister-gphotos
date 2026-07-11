; Inno Setup script for Google Photos Local Uploader.
; Prerequisites:
;   1. Install Inno Setup 6: https://jrsoftware.org/isdl.php
;   2. Publish the application: .\build\publish.ps1
;   3. Compile this script:     iscc installer\setup.iss
; The produced installer is written to dist\installer\.

#define MyAppName "Google Photos Local Uploader"
; The version can be overridden by CI: iscc /DMyAppVersion=1.2.3 installer\setup.iss
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#define MyAppExeName "MisterGPhotos.exe"

[Setup]
AppId={{7E9D2C4A-1F5B-4E83-9A6C-2B8D0E4F6153}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\dist\installer
OutputBaseFilename=mister-gphotos-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\dist\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

; The local data (%APPDATA%\GooglePhotosLocalUploader) and the Credential
; Manager secrets are intentionally NOT removed on uninstall: use the
; "Remove local data" button in the application before uninstalling if you
; want to erase everything.
