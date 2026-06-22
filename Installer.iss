[Setup]
AppName=OverWatch ELD
AppVersion=2.0.5
DefaultDirName={pf}\OverWatchELD
DefaultGroupName=OverWatch ELD
OutputBaseFilename=OverWatchELD_Setup
Compression=lzma
SolidCompression=yes

[Files]
Source: "F:\ELD.Desktop\*"; DestDir: "{app}"; Flags: recursesubdirs

[Icons]
Name: "{group}\OverWatch ELD"; Filename: "{app}\OverWatchELD.exe"
Name: "{autodesktop}\OverWatch ELD"; Filename: "{app}\OverWatchELD.exe"

[Run]
Filename: "{app}\OverWatchELD.exe"; Description: "Launch OverWatch ELD"; Flags: nowait postinstall skipifsilent