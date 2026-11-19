#define RepoRoot ".."
#define Payload "out\payload"

[Setup]
AppId={{3F56AE2E-A958-47D3-A1B8-5D12664D39E8}
AppName=Arkedo Series - 03 PIXEL!
AppVersion=1.0.0
DefaultDirName={localappdata}\Programs\Arkedo Series - 03 PIXEL!
DisableDirPage=yes
DefaultGroupName=Arkedo Series - 03 PIXEL!
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=out
OutputBaseFilename=PIXEL-1.0.0
Compression=lzma2/fast
SolidCompression=no
WizardStyle=modern
DisableWelcomePage=no
SetupIconFile={#RepoRoot}\assets\icon\pixel.ico
UninstallDisplayIcon={app}\pixel.ico
UninstallDisplaySize=180_000_000
WizardSmallImageFile={#RepoRoot}\assets\icon\pixel.png
WizardImageFile={#RepoRoot}\assets\installer\wizard-large-*.bmp
DisableProgramGroupPage=yes
UsePreviousAppDir=yes
AllowNoIcons=yes

[Messages]
WelcomeLabel1=Welcome to Arkedo Series - 03 PIXEL! setup
WelcomeLabel2=This fan-made port brings Arkedo Series - 03 PIXEL! to your PC with keyboard, mouse, controller, and ultrawide gameplay support.%n%nYou will need your own copy of the Xbox 360 LIVE package. Setup prepares it locally.
WizardSelectTasks=Shortcuts
SelectTasksDesc=Choose shortcuts
SelectTasksLabel2=Select the shortcuts to create:

[Files]
Source: "{#RepoRoot}\assets\icon\pixel.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}\helper\Port.SetupHelper.exe"; Flags: dontcopy
Source: "{#Payload}\runtime\*"; DestDir: "{tmp}\port-payload\runtime"; Flags: recursesubdirs createallsubdirs dontcopy
Source: "{#Payload}\tools\*"; DestDir: "{tmp}\port-payload\tools"; Flags: recursesubdirs createallsubdirs dontcopy
Source: "{#Payload}\config\*"; DestDir: "{tmp}\port-payload\config"; Flags: recursesubdirs createallsubdirs dontcopy

[Tasks]
Name: "startmenu"; Description: "Start Menu shortcut"; Flags: checkedonce
Name: "desktop"; Description: "Desktop shortcut"; Flags: unchecked

[Icons]
Name: "{autoprograms}\Arkedo Series - 03 PIXEL!"; Filename: "{app}\current\run\584E07D1\PIXEL.exe"; Parameters: "--run ""--save-dir"" ""{code:SaveDirectory}"""; WorkingDir: "{app}\current\run\584E07D1"; Tasks: startmenu
Name: "{autodesktop}\Arkedo Series - 03 PIXEL!"; Filename: "{app}\current\run\584E07D1\PIXEL.exe"; Parameters: "--run ""--save-dir"" ""{code:SaveDirectory}"""; WorkingDir: "{app}\current\run\584E07D1"; Tasks: desktop

[Run]
Filename: "{app}\current\run\584E07D1\PIXEL.exe"; Parameters: "--run ""--save-dir"" ""{code:SaveDirectory}"""; Description: "Launch Arkedo Series - 03 PIXEL!"; Flags: nowait postinstall skipifsilent unchecked

[UninstallDelete]
Type: filesandordirs; Name: "{app}\current"
Type: files; Name: "{app}\locations.ini"

[Code]
var
  LocationsPage: TInputDirWizardPage;
  PackagePage: TInputFileWizardPage;
  InstallCommitted: Boolean;
  SetupCompleted: Boolean;
  LastHelperError: String;
  HelperExtracted: Boolean;
  PreparingGame: Boolean;
  CancellationRequested: Boolean;
  CancelFile: String;

procedure EnsureHelperExtracted;
begin
  if HelperExtracted then Exit;
  ExtractTemporaryFile('Port.SetupHelper.exe');
  HelperExtracted := True;
end;

function DefaultSaveDirectory: String;
begin
  Result := AddBackslash(ExpandConstant('{usersavedgames}')) + 'Arkedo Series - 03 PIXEL!';
end;

function SaveDirectory(Param: String): String;
begin
  if WizardSilent then Result := ExpandConstant('{param:SAVEDIR|' +
    GetIniString('Folders', 'Saves', DefaultSaveDirectory, AddBackslash(WizardDirValue) + 'locations.ini') + '}')
  else Result := LocationsPage.Values[1];
end;

function CheckLocations: String;
var
  InstallPath, SavesPath: String;
begin
  Result := '';
  InstallPath := AddBackslash(ExpandFileName(WizardDirValue));
  SavesPath := ExpandFileName(SaveDirectory(''));
  if Trim(WizardDirValue) = '' then Result := 'Choose an installation folder.'
  else if Trim(SaveDirectory('')) = '' then Result := 'Choose a saves folder.'
  else if CompareText(Copy(AddBackslash(SavesPath), 1, Length(InstallPath)), InstallPath) = 0 then
    Result := 'Keep saves outside the installation folder so uninstalling does not erase them.';
end;

function Quoted(const S: String): String;
begin
  Result := '"' + S + '"';
end;

procedure PackageFileChanged(Sender: TObject);
var
  Path: String;
begin
  if WizardSilent or (WizardForm.CurPageID <> PackagePage.ID) then Exit;
  Path := PackagePage.Values[0];
  WizardForm.NextButton.Enabled := FileExists(Path);
  if FileExists(Path) then
    PackagePage.SubCaptionLabel.Caption := 'Click Next to check the selected game package.'
  else
    PackagePage.SubCaptionLabel.Caption := 'Choose a readable LIVE package file.';
end;

function CheckPackagePage: Boolean;
var
  Path: String;
begin
  Result := False;
  Path := PackagePage.Values[0];
  if not FileExists(Path) then Exit;
  PackagePage.SubCaptionLabel.Caption := 'Checking game package...';
  PackagePage.SubCaptionLabel.Refresh;
  try
    if CompareText(GetSHA256OfFile(Path),
       '64b5bb62e8cf16c44c04c1b9d2ac5f1c780db1cd28e031cd4315788ab3af4104') <> 0 then
    begin
      PackagePage.SubCaptionLabel.Caption := 'Unsupported or damaged game package.';
      Exit;
    end;
  except
    PackagePage.SubCaptionLabel.Caption := 'The game package could not be read.';
    Exit;
  end;
  PackagePage.SubCaptionLabel.Caption := 'Supported game version detected.';
  Result := True;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = PackagePage.ID then PackageFileChanged(nil);
end;

procedure InitializeWizard;
begin
  LocationsPage := CreateInputDirPage(wpWelcome, 'Folders',
    'Choose where the game and saves live.',
    'Your saves are kept outside the installation folder and preserved when uninstalling.', False, '');
  LocationsPage.Add('Installation:');
  LocationsPage.Add('Saves:');
  LocationsPage.Values[0] := WizardDirValue;
  LocationsPage.Values[1] := ExpandConstant('{param:SAVEDIR|}');
  if LocationsPage.Values[1] = '' then
    LocationsPage.Values[1] := GetIniString('Folders', 'Saves', DefaultSaveDirectory,
      AddBackslash(WizardDirValue) + 'locations.ini');
  PackagePage := CreateInputFilePage(LocationsPage.ID, 'Game package',
    'Select your Arkedo Series - 03 PIXEL! Xbox 360 package.',
    'Choose your own supported LIVE package. Setup verifies and prepares it locally.');
  PackagePage.Add('Xbox 360 LIVE package:', 'LIVE packages|*.*', '');
  PackagePage.Values[0] := ExpandConstant('{param:PACKAGE|}');
  PackagePage.Edits[0].OnChange := @PackageFileChanged;
end;

function UpdateReadyMemo(const Space, NewLine, MemoUserInfoInfo, MemoDirInfo,
  MemoTypeInfo, MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  Result := 'Installation: ' + WizardDirValue + NewLine +
    'Saves: ' + SaveDirectory('') + NewLine + NewLine +
    'Game: Arkedo Series - 03 PIXEL! (Xbox 360)' + NewLine +
    'Game package: ' + PackagePage.Values[0] + NewLine + NewLine +
    'Setup will prepare and verify the game locally before installing it.' +
    NewLine + NewLine + MemoTasksInfo;
end;

procedure AdvanceProgress(Value: Integer);
begin
  if Value > 1000 then Value := 1000;
  if Value <= WizardForm.ProgressGauge.Position then Exit;
  WizardForm.ProgressGauge.Position := Value;
  WizardForm.ProgressGauge.Refresh;
end;

procedure InstallLog(const S: String; const Error, FirstLine: Boolean);
var
  Stage: String;
  Position: Integer;
begin
  if Pos('PROGRESS ', S) = 1 then
  begin
    Position := StrToIntDef(Copy(S, 10, Length(S)), -1);
    if (Position >= 0) and (Position <= 1000) then
      AdvanceProgress(200 + Position * 8 div 10);
    Exit;
  end;
  Log(S);
  if Error then LastHelperError := S;
  if Pos('STAGE ', S) <> 1 then Exit;
  Stage := Copy(S, 7, Length(S));
  if Stage = 'runtime' then WizardForm.StatusLabel.Caption := 'Copying runtime files...'
  else if Stage = 'reuse-check' then WizardForm.StatusLabel.Caption := 'Checking installed game files...'
  else if Stage = 'reuse' then WizardForm.StatusLabel.Caption := 'Copying prepared game data...'
  else if Stage = 'extract' then WizardForm.StatusLabel.Caption := 'Extracting game package...'
  else if Stage = 'inspect' then WizardForm.StatusLabel.Caption := 'Inspecting game assembly...'
  else if Stage = 'abi' then WizardForm.StatusLabel.Caption := 'Checking runtime compatibility...'
  else if Stage = 'rewrite' then WizardForm.StatusLabel.Caption := 'Preparing game code...'
  else if Stage = 'content' then WizardForm.StatusLabel.Caption := 'Preparing game content and music...'
  else if Stage = 'audio' then WizardForm.StatusLabel.Caption := 'Preparing game audio...'
  else if Stage = 'verify' then WizardForm.StatusLabel.Caption := 'Verifying prepared game files...'
  else if Stage = 'commit' then WizardForm.StatusLabel.Caption := 'Finishing installation...';
  WizardForm.StatusLabel.Refresh;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := CheckLocations;
  if Result <> '' then Exit;
  if not ForceDirectories(SaveDirectory('')) then Result := 'Could not create the saves folder.';
end;

function InstallGame: String;
var
  Code: Integer;
  SelectedPackage: String;
begin
  Result := '';
  SelectedPackage := PackagePage.Values[0];
  if not FileExists(SelectedPackage) then
  begin
    Result := 'Select a readable game package before installing.';
    Exit;
  end;
  CancelFile := ExpandConstant('{tmp}\port-cancel-requested');
  if FileExists(CancelFile) then DeleteFile(CancelFile);
  CancellationRequested := False;
  PreparingGame := True;
  WizardForm.ProgressGauge.Max := 1000;
  WizardForm.ProgressGauge.Position := 0;
  WizardForm.StatusLabel.Caption := 'Loading setup helper...';
  WizardForm.StatusLabel.Refresh;
  try
    EnsureHelperExtracted;
    AdvanceProgress(20);
    WizardForm.StatusLabel.Caption := 'Unpacking game runtime...';
    WizardForm.StatusLabel.Refresh;
    ExtractTemporaryFiles('{tmp}\port-payload\runtime\*');
    AdvanceProgress(100);
    ExtractTemporaryFiles('{tmp}\port-payload\config\*');
    AdvanceProgress(120);
    WizardForm.StatusLabel.Caption := 'Unpacking preparation tools...';
    WizardForm.StatusLabel.Refresh;
    ExtractTemporaryFiles('{tmp}\port-payload\tools\*');
    AdvanceProgress(200);
    if CancellationRequested then Exit;
    LastHelperError := '';
    if not ExecAndLogOutput(ExpandConstant('{tmp}\Port.SetupHelper.exe'),
       'install --destination ' + Quoted(ExpandConstant('{app}\current')) +
       ' --payload ' + Quoted(ExpandConstant('{tmp}\port-payload')) +
       ' --package ' + Quoted(SelectedPackage) +
       ' --cancel-file ' + Quoted(CancelFile), '', SW_HIDE,
       ewWaitUntilTerminated, Code, @InstallLog) then
      Result := 'Could not start game preparation.'
    else if Code <> 0 then
    begin
      Result := 'Game preparation failed. The previous installation is preserved.';
      if LastHelperError <> '' then Result := Result + #13#10 + LastHelperError;
    end
    else
    begin
      InstallCommitted := True;
      AdvanceProgress(1000);
    end;
  finally
    PreparingGame := False;
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if WizardSilent then Exit;
  if CurPageID = LocationsPage.ID then
  begin
    WizardForm.DirEdit.Text := LocationsPage.Values[0];
    Result := CheckLocations = '';
    if not Result then MsgBox(CheckLocations, mbInformation, MB_OK);
  end
  else if CurPageID = PackagePage.ID then Result := CheckPackagePage;
end;

procedure CancelButtonClick(CurPageID: Integer; var Cancel, Confirm: Boolean);
begin
  if not PreparingGame then Exit;
  Cancel := False;
  Confirm := False;
  if CancellationRequested then Exit;
  CancellationRequested := True;
  SaveStringToFile(CancelFile, 'cancel', False);
  WizardForm.StatusLabel.Caption := 'Cancelling after the current step...';
  WizardForm.StatusLabel.Refresh;
end;

function InstalledBytes(const Directory: String; var Total: Int64): Boolean;
var
  Entry: TFindRec;
  Path: String;
  Size: Int64;
begin
  Result := True;
  if not FindFirst(AddBackslash(Directory) + '*', Entry) then Exit;
  try
    repeat
      if (Entry.Name <> '.') and (Entry.Name <> '..') and
         ((Entry.Attributes and FILE_ATTRIBUTE_REPARSE_POINT) = 0) then
      begin
        Path := AddBackslash(Directory) + Entry.Name;
        if (Entry.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
          Result := InstalledBytes(Path, Total)
        else if FileSize64(Path, Size) then
          Total := Total + Size
        else
          Result := False;
        if not Result then Exit;
      end;
    until not FindNext(Entry);
  finally
    FindClose(Entry);
  end;
end;

procedure UpdateInstalledSize;
var
  Total: Int64;
  Key: String;
begin
  Total := 0;
  Key := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{3F56AE2E-A958-47D3-A1B8-5D12664D39E8}_is1';
  if not InstalledBytes(ExpandConstant('{app}'), Total) or
     (Total div 1024 > 2147483647) or not RegKeyExists(HKCU64, Key) or
     not RegWriteDWordValue(HKCU64, Key, 'EstimatedSize', Cardinal((Total + 1023) div 1024)) then
    Log('Could not update the installed size in Control Panel.')
  else
    Log('INSTALL_SIZE_BYTES ' + Int64ToStr(Total));
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Code: Integer;
  ErrorText: String;
begin
  if CurStep = ssInstall then
  begin
    ErrorText := InstallGame;
    if CancellationRequested then Abort;
    if ErrorText <> '' then
    begin
      if WizardSilent then Log(ErrorText)
      else MsgBox(ErrorText, mbError, MB_OK);
      Abort;
    end;
    Exit;
  end;
  if CurStep <> ssDone then Exit;
  SetupCompleted := True;
  Code := -1;
  if not Exec(ExpandConstant('{tmp}\Port.SetupHelper.exe'),
     'finalize --destination ' + Quoted(ExpandConstant('{app}\current')),
     '', SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then
    Log('INSTALL_FINALIZE_FAILED; previous install retained for recovery.')
  else
  begin
    SetIniString('Folders', 'Saves', SaveDirectory(''), ExpandConstant('{app}\locations.ini'));
    Log('INSTALL_FINALIZED');
  end;
  UpdateInstalledSize;
end;

procedure DeinitializeSetup;
var
  Code: Integer;
begin
  if not InstallCommitted or SetupCompleted then Exit;
  Code := -1;
  if not Exec(ExpandConstant('{tmp}\Port.SetupHelper.exe'),
     'rollback --destination ' + Quoted(ExpandConstant('{app}\current')),
     '', SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then
    Log('INSTALL_ROLLBACK_FAILED; previous install retained in current.previous.')
  else Log('INSTALL_ROLLED_BACK');
end;
