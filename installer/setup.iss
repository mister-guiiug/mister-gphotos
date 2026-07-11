; Script Inno Setup pour Google Photos Local Uploader.
; Prérequis :
;   1. Installer Inno Setup 6 : https://jrsoftware.org/isdl.php
;   2. Publier l'application :  .\build\publish.ps1
;   3. Compiler ce script :     iscc installer\setup.iss
; L'installeur produit est écrit dans dist\installer\.

#define MyAppName "Google Photos Local Uploader"
; La version peut être surchargée par la CI : iscc /DMyAppVersion=1.2.3 installer\setup.iss
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#define MyAppExeName "GooglePhotosLocalUploader.exe"

[Setup]
AppId={{7E9D2C4A-1F5B-4E83-9A6C-2B8D0E4F6153}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\dist\installer
OutputBaseFilename=GooglePhotosLocalUploader-Setup-{#MyAppVersion}
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

; Les données locales (%APPDATA%\GooglePhotosLocalUploader) et les secrets du
; Gestionnaire d'identifiants ne sont volontairement PAS supprimés à la
; désinstallation : utilisez le bouton « Supprimer les données locales » dans
; l'application avant de désinstaller si vous voulez tout effacer.
