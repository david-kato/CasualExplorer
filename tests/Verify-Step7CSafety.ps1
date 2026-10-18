param([string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
$ceRoot = (Resolve-Path -LiteralPath $ProjectRoot).Path
$ceSource = Join-Path $ceRoot 'src/CasualExplorer.App'
$ceFiles = @(Get-ChildItem -LiteralPath $ceSource -Recurse -File | Where-Object {
    $_.Extension -in '.cs','.xaml' -and $_.FullName -notmatch '[\\/](bin|obj)[\\/]'
})
$ceText = ($ceFiles | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }) -join "`n"
$ceForbidden = @(
    '\b(?:File|Directory)\.(?:Move|Copy|Delete|Create|Write\w*|Append\w*|Set\w*)\s*\(',
    '\bFile\.(?:Open\w*|ReadAll\w*|ReadLines)\s*\(',
    '\b(?:FileStream|StreamReader|StreamWriter|HttpClient|WebClient|Socket|ProcessStartInfo)\s*\(',
    'SearchOption\.AllDirectories', 'ResolveLinkTarget\s*\(',
    '\b(?:WriteFile|DeleteFile\w*|MoveFile\w*|CopyFile\w*|SetFile\w*|CreateDirectory\w*)\s*\(',
    'ApplicationData\.Current', 'FileOpenPicker', 'FileSavePicker',
    '\bProcess\.Start\s*\(', 'Launch(?:Uri|File|Folder)Async\s*\(',
    'AllowDrop\s*=\s*(?:true|"True")', '\bClipboard\.', 'SetSecurityDescriptor|SetAccessControl'
)
foreach ($cePattern in $ceForbidden) {
    if ($ceText -match $cePattern) { throw "Forbidden production API: $cePattern" }
}
$ceCatalog = Get-Content -LiteralPath (Join-Path $ceSource 'Services/SelectedFolderCatalog.cs') -Raw
$ceUi = Get-Content -LiteralPath (Join-Path $ceSource 'MainPage.FolderSelection.cs') -Raw
$ceRequired = @(
    'OpenExistingDirectory(path, 0x81, 1, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero)',
    'ReadAttributeTag(handle, 9, out AttributeTag tag, 8)',
    'DriveType.Fixed or DriveType.Removable', 'Path.IsPathFullyQualified(path)',
    'parts.Length > 64', 'SearchOption.TopDirectoryOnly',
    'FileAttributes.ReparsePoint', 'FileAttributes.Offline',
    '(FileAttributes)0x40000', '(FileAttributes)0x400000',
    'ScanLimit = 10000', 'ScanBudgetMilliseconds = 2500',
    'cancellationToken.ThrowIfCancellationRequested()',
    'visible.Count > ReadOnlyFileCatalog.DisplayLimit', 'leases[i].Dispose()'
)
foreach ($ceRequiredText in $ceRequired) {
    if (-not $ceCatalog.Contains($ceRequiredText)) { throw "Missing safety boundary: $ceRequiredText" }
}
if ([regex]::Matches($ceText, 'new FolderPicker\(').Count -ne 1 -or
    -not $ceUi.Contains('new FolderPicker(XamlRoot.ContentIslandEnvironment.AppWindowId)') -or
    -not $ceUi.Contains('await Task.Run(() => SelectedFolderCatalog.Load(path, token), token)') -or
    -not $ceUi.Contains('if (_closed) return;') -or
    -not $ceUi.Contains('token.ThrowIfCancellationRequested();')) { throw 'Picker/worker/closed-page boundary changed.' }
$ceImports = [regex]::Matches($ceText, '\[DllImport\([^\r\n]+').Value
if ($ceImports.Count -ne 2 -or
    @($ceImports | Where-Object { $_ -notmatch 'EntryPoint = "(?:CreateFileW|GetFileInformationByHandleEx)"' }).Count -ne 0) {
    throw 'Native API allowlist changed; review before accepting.'
}
[pscustomobject]@{
    Result='PASS'; ProductionSourceFiles=$ceFiles.Count; ForbiddenPatterns=$ceForbidden.Count
    NativeImports=@('CreateFileW: read-only directory lease 0x81/share-read/open-existing/no-follow', 'GetFileInformationByHandleEx: attributes only')
    Selection='One explicit local folder; metadata only; no recursion/content/persistence/network/write APIs'
    Limits='30 displayed, 10000 scanned, 2500ms checked between entries, cancellation, depth 64'
    Limitation='Static source checks supplement runtime tests; OS picker/provider behavior is outside app guarantees.'
} | ConvertTo-Json -Depth 4
