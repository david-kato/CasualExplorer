param(
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$failures = [System.Collections.Generic.List[string]]::new()

function Confirm-True {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        $failures.Add($Message)
    }
}

$sampleFolder = Join-Path $ProjectRoot 'samples\TestData'
$sourceFolder = Join-Path $ProjectRoot 'src\CasualExplorer.App'
$mainPagePath = Join-Path $sourceFolder 'MainPage.xaml'
$catalogPath = Join-Path $sourceFolder 'Services\ReadOnlyFileCatalog.cs'

Confirm-True (Test-Path -LiteralPath $sampleFolder -PathType Container) '同梱ダミーフォルダがありません。'
Confirm-True (Test-Path -LiteralPath $mainPagePath -PathType Leaf) 'MainPage.xamlがありません。'
Confirm-True (Test-Path -LiteralPath $catalogPath -PathType Leaf) '読取り処理がありません。'

if (Test-Path -LiteralPath $sampleFolder -PathType Container) {
    $allFiles = @(Get-ChildItem -LiteralPath $sampleFolder -File)
    $sortRules = @(
        @{ Expression = 'LastWriteTime'; Descending = $true },
        @{ Expression = 'Name'; Descending = $false }
    )
    $visibleFiles = @($allFiles | Sort-Object -Property $sortRules | Select-Object -First 30)

    Confirm-True ($allFiles.Count -gt 30) '31件以上の制限試験に必要なダミーファイルが不足しています。'
    Confirm-True ($visibleFiles.Count -eq 30) '先頭30件の選択に失敗しました。'
    Confirm-True (($visibleFiles | Where-Object { [string]::IsNullOrWhiteSpace($_.Name) }).Count -eq 0) '名前を表示できない項目があります。'
}

if (Test-Path -LiteralPath $mainPagePath -PathType Leaf) {
    $xaml = Get-Content -LiteralPath $mainPagePath -Raw
    foreach ($requiredText in @('読取り専用', 'ファイル名', '種類', '更新日時', 'ファイル変更は行いません')) {
        Confirm-True ($xaml.Contains($requiredText)) "画面に必要な表示がありません: $requiredText"
    }

    [xml]$ui = $xaml
    # 7C adds only explicit read-only source selection, bundle return and cancellation.
    $allowedButtons = @{ ListButton='List_Click'; GravityButton='Gravity_Click'; PauseButton='Pause_Click'; ResetButton='Reset_Click'; ChooseFolderButton='ChooseFolder_Click'; SamplesButton='Samples_Click'; CancelReadButton='CancelRead_Click' }
    $buttons = @($ui.SelectNodes("//*[local-name()='Button']"))
    Confirm-True ($buttons.Count -eq 7) '読取り・表示専用ボタンの数が許可リストと異なります。'
    foreach ($button in $buttons) {
        $name = $button.GetAttribute('Name', 'http://schemas.microsoft.com/winfx/2006/xaml')
        Confirm-True ($allowedButtons.ContainsKey($name) -and $button.Click -eq $allowedButtons[$name]) "未許可のボタンまたは操作があります: $name"
    }
    $gravityUi = Get-Content -LiteralPath (Join-Path $sourceFolder 'MainPage.Gravity.cs') -Raw
    Confirm-True ($gravityUi.Contains('new Button') -and $gravityUi.Contains('card.Click += (_, _) => SelectCard(index)')) 'カードボタンは表示選択専用である必要があります。'
    Confirm-True (-not $gravityUi.Contains('AllowDrop = true')) '外部ドロップの入口があります。'
}

$sourceFiles = @(Get-ChildItem -LiteralPath $sourceFolder -File -Recurse | Where-Object { $_.Extension -in '.cs','.xaml' -and $_.FullName -notmatch '[\\/](bin|obj)[\\/]' })
$sourceText = ($sourceFiles | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }) -join [Environment]::NewLine
$dangerPatterns = @(
    'File.Move(',
    'File.Copy(',
    'File.Delete(',
    'File.WriteAll',
    'Directory.Move(',
    'Directory.Delete(',
    'Directory.CreateDirectory(',
    'FileStream('
)

foreach ($pattern in $dangerPatterns) {
    Confirm-True (-not $sourceText.Contains($pattern)) "書込みにつながる処理候補を検出しました: $pattern"
}

if ($failures.Count -gt 0) {
    Write-Host 'CasualExplorer v0.1 骨格確認: 失敗'
    $failures | ForEach-Object { Write-Host "- $_" }
    exit 1
}

Write-Host 'CasualExplorer v0.1 骨格確認: 成功'
Write-Host '- 同梱ダミーファイル: 31件以上'
Write-Host '- 画面表示: 読取り専用、3列、安全表示あり'
Write-Host '- 一覧制限: 先頭30件'
Write-Host '- 書込み処理候補: 未検出'
