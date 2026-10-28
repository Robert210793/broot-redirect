#Requires -Version 7.2
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)]
    [string] $ExcelPath,

    [Parameter(Mandatory = $true)]
    [string] $SiteUrl,

    [Parameter(Mandatory = $false)]
    [string] $ClientId,

    [Parameter(Mandatory = $false)]
    [switch] $UseExistingConnection,

    [Parameter(Mandatory = $false)]
    [string] $WorksheetName = 'Umleitungsservice',

    [Parameter(Mandatory = $false)]
    [string[]] $Status = @('beauftragt zur Umleitung'),

    [Parameter(Mandatory = $false)]
    [switch] $IncludeManuallyRedirected,

    [Parameter(Mandatory = $false)]
    [switch] $IncludeDocumentUrls,

    [Parameter(Mandatory = $false)]
    [switch] $ParseOnly
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression

$ListUrl = 'Lists/brtqmRedirectRules'
$SchemeAndAuthority = [regex]::new('^[a-z][a-z0-9+.-]*://[^/?#]*', 'IgnoreCase')
$PercentEncodedSequence = [regex]::new('(%[0-9a-f]{2})+', 'IgnoreCase')
$StrictUtf8 = [System.Text.UTF8Encoding]::new($false, $true)
$JsonEscapes = @{ 0x22 = '\"'; 0x5C = '\\'; 0x08 = '\b'; 0x0C = '\f'; 0x0A = '\n'; 0x0D = '\r'; 0x09 = '\t' }
$Columns = [ordered]@{
    Status       = 'Status'
    Area         = 'Bereich'
    ViewUrl      = 'Quell-URL View'
    ViewManual   = 'Manueller Smartredirect Eintrag für Quell-URL-View'
    FolderUrl    = 'Quell-URL Ordner'
    FolderManual = 'Manueller Smartredirect Eintrag'
    DocumentUrl  = 'Quell-URL Dokument (Test)'
    LiDokPath    = 'LiDok Pfad'
    TargetUrl    = 'LiDok-URL'
}

function ConvertFrom-PercentEncoding {
    param([string] $Value)

    return $PercentEncodedSequence.Replace($Value, {
            param($match)
            $bytes = [byte[]]::new([int] ($match.Value.Length / 3))
            for ($index = 0; $index -lt $bytes.Length; $index++) {
                $bytes[$index] = [Convert]::ToByte($match.Value.Substring($index * 3 + 1, 2), 16)
            }
            try { return $StrictUtf8.GetString($bytes) } catch { return $match.Value }
        })
}

function ConvertTo-NormalizedPart {
    param([string] $Value)

    return (ConvertFrom-PercentEncoding -Value $Value.ToLowerInvariant()).ToLowerInvariant()
}

function ConvertTo-JsonString {
    param([string] $Value)

    $builder = [System.Text.StringBuilder]::new('"')
    for ($index = 0; $index -lt $Value.Length; $index++) {
        $char = $Value[$index]
        $code = [int] $char
        $escape = $JsonEscapes[$code]
        if ($null -ne $escape) {
            [void] $builder.Append($escape)
        }
        elseif ($code -lt 0x20) {
            [void] $builder.Append(('\u{0:x4}' -f $code))
        }
        elseif ([char]::IsHighSurrogate($char) -and $index + 1 -lt $Value.Length -and [char]::IsLowSurrogate($Value[$index + 1])) {
            [void] $builder.Append($char).Append($Value[$index + 1])
            $index++
        }
        elseif ([char]::IsSurrogate($char)) {
            [void] $builder.Append(('\u{0:x4}' -f $code))
        }
        else {
            [void] $builder.Append($char)
        }
    }
    return $builder.Append('"').ToString()
}

function Get-CanonicalSourceUrl {
    param([string] $SourceUrl)

    $withoutAuthority = $SchemeAndAuthority.Replace($SourceUrl.Trim(), '', 1)
    $fragmentIndex = $withoutAuthority.IndexOf('#')
    $withoutFragment = if ($fragmentIndex -ge 0) { $withoutAuthority.Substring(0, $fragmentIndex) } else { $withoutAuthority }
    $queryIndex = $withoutFragment.IndexOf('?')
    $path = if ($queryIndex -ge 0) { $withoutFragment.Substring(0, $queryIndex) } else { $withoutFragment }
    $query = if ($queryIndex -ge 0) { $withoutFragment.Substring($queryIndex + 1) } else { '' }

    $segments = @($path.Split('/') | Where-Object { $_.Length -gt 0 } | ForEach-Object { ConvertTo-JsonString (ConvertTo-NormalizedPart $_) })

    $pairs = [System.Collections.Generic.List[string]]::new()
    foreach ($part in $query.Split('&')) {
        if ($part.Length -eq 0) { continue }
        $separatorIndex = $part.IndexOf('=')
        $key = ConvertTo-NormalizedPart $(if ($separatorIndex -ge 0) { $part.Substring(0, $separatorIndex) } else { $part })
        $value = ConvertTo-NormalizedPart $(if ($separatorIndex -ge 0) { $part.Substring($separatorIndex + 1) } else { '' })
        $pair = '[' + (ConvertTo-JsonString $key) + ',' + (ConvertTo-JsonString $value) + ']'
        if (-not $pairs.Contains($pair)) { $pairs.Add($pair) }
    }
    $pairs.Sort([System.StringComparer]::Ordinal)

    return '{"path":[' + ($segments -join ',') + '],"query":[' + ($pairs -join ',') + ']}'
}

function Get-SourceKey {
    param([string] $SourceUrl)

    $bytes = [System.Text.Encoding]::UTF8.GetBytes((Get-CanonicalSourceUrl -SourceUrl $SourceUrl))
    $hash = [System.Security.Cryptography.SHA256]::HashData($bytes)
    return 'v1:' + [Convert]::ToHexString($hash).ToLowerInvariant()
}

function Test-AbsoluteHttpUrl {
    param([string] $Value)

    if ($Value -notmatch '^https?://[^\s/?#\\]+') { return $false }
    $uri = $null
    return [Uri]::TryCreate($Value, [UriKind]::Absolute, [ref] $uri) -and $uri.Scheme -in @('http', 'https') -and $uri.Host.Length -gt 0
}

function Read-ZipEntryXml {
    param([System.IO.Compression.ZipArchive] $Archive, [string] $Path)

    $entry = $Archive.GetEntry($Path)
    if ($null -eq $entry) { return $null }
    $reader = [System.IO.StreamReader]::new($entry.Open())
    try { return [xml] $reader.ReadToEnd() } finally { $reader.Dispose() }
}

function Get-ColumnIndex {
    param([string] $CellReference)

    $letters = ($CellReference -replace '\d', '').ToUpperInvariant()
    $index = 0
    foreach ($letter in $letters.ToCharArray()) { $index = $index * 26 + ([int] $letter - 64) }
    return $index - 1
}

function Read-WorksheetRows {
    param([string] $Path, [string] $SheetName)

    $stream = [System.IO.File]::Open($Path, 'Open', 'Read', 'ReadWrite')
    $archive = [System.IO.Compression.ZipArchive]::new($stream, 'Read')
    try {
        $sharedStrings = @()
        $sst = Read-ZipEntryXml -Archive $archive -Path 'xl/sharedStrings.xml'
        if ($null -ne $sst) {
            $sharedStrings = @($sst.sst.si | ForEach-Object { ($_.SelectNodes('.//*[local-name()="t"]') | ForEach-Object { $_.InnerText }) -join '' })
        }

        $workbook = Read-ZipEntryXml -Archive $archive -Path 'xl/workbook.xml'
        $sheet = $workbook.workbook.sheets.sheet | Where-Object { $_.name -eq $SheetName } | Select-Object -First 1
        if ($null -eq $sheet) { throw "Tabellenblatt '$SheetName' fehlt in '$Path'." }
        $relationshipId = $sheet.GetAttribute('id', 'http://schemas.openxmlformats.org/officeDocument/2006/relationships')
        $relationships = Read-ZipEntryXml -Archive $archive -Path 'xl/_rels/workbook.xml.rels'
        $target = ($relationships.Relationships.Relationship | Where-Object { $_.Id -eq $relationshipId }).Target
        $sheetPath = if ($target.StartsWith('/')) { $target.TrimStart('/') } else { 'xl/' + $target }
        $worksheet = Read-ZipEntryXml -Archive $archive -Path $sheetPath

        foreach ($row in $worksheet.worksheet.sheetData.row) {
            $cells = @{}
            foreach ($cell in @($row.c)) {
                $value = switch ($cell.t) {
                    's' { $sharedStrings[[int] $cell.v] }
                    'inlineStr' { ($cell.SelectNodes('.//*[local-name()="t"]') | ForEach-Object { $_.InnerText }) -join '' }
                    default { [string] $cell.v }
                }
                $cells[(Get-ColumnIndex $cell.r)] = [string] $value
            }
            [pscustomobject]@{ Row = [int] $row.r; Cells = $cells }
        }
    }
    finally {
        $archive.Dispose()
        $stream.Dispose()
    }
}

function Get-AreaName {
    param([string] $LiDokPath, [string] $FallbackArea)

    $lastSegment = ($LiDokPath -split '>')[-1].Trim()
    if ($lastSegment.Length -gt 0 -and $lastSegment -notmatch '^(tbd|\?)$' -and $LiDokPath -notmatch '\r|\n') { return $lastSegment }
    return $FallbackArea.Trim()
}

$rows = @(Read-WorksheetRows -Path (Resolve-Path $ExcelPath).Path -SheetName $WorksheetName)
$headerRow = $rows | Where-Object { $_.Cells.Values | Where-Object { $_.Trim() -eq $Columns.Area } } | Select-Object -First 1
if ($null -eq $headerRow) { throw "Kopfzeile mit der Spalte '$($Columns.Area)' fehlt im Tabellenblatt '$WorksheetName'." }

$columnIndex = @{}
foreach ($name in $Columns.Keys) {
    $match = $headerRow.Cells.GetEnumerator() | Where-Object { $_.Value.Trim() -eq $Columns[$name] } | Select-Object -First 1
    if ($null -eq $match) { throw "Spalte '$($Columns[$name])' fehlt in der Kopfzeile (Zeile $($headerRow.Row))." }
    $columnIndex[$name] = $match.Key
}

$sourceColumns = @('ViewUrl', 'FolderUrl') + $(if ($IncludeDocumentUrls) { @('DocumentUrl') } else { @() })
$candidates = [System.Collections.Generic.List[object]]::new()
$report = [System.Collections.Generic.List[object]]::new()

function Add-Report {
    param([int] $Row, [string] $Area, [string] $SourceUrl, [string] $Action, [string] $Reason)
    $report.Add([pscustomobject]@{ Row = $Row; Area = $Area; Action = $Action; Reason = $Reason; SourceUrl = $SourceUrl })
}

foreach ($row in $rows | Where-Object { $_.Row -gt $headerRow.Row }) {
    $cell = { param($name) $value = $row.Cells[$columnIndex[$name]]; if ($null -eq $value) { '' } else { $value } }
    $excelArea = (& $cell 'Area').Trim()
    if ($excelArea.Length -eq 0) { continue }

    $rowStatus = (& $cell 'Status').Trim()
    $area = Get-AreaName -LiDokPath (& $cell 'LiDokPath') -FallbackArea $excelArea
    $targetUrl = (& $cell 'TargetUrl').Trim()

    if (-not ($Status | Where-Object { $rowStatus -like $_ })) {
        Add-Report $row.Row $area '' 'Übersprungen' "Status '$rowStatus' nicht ausgewählt"
        continue
    }
    if (-not $IncludeManuallyRedirected -and ((& $cell 'ViewManual') -match 'ok' -or (& $cell 'FolderManual') -match 'ok')) {
        Add-Report $row.Row $area '' 'Übersprungen' 'Bereits manuell in broot-redirect umgeleitet'
        continue
    }
    if (-not (Test-AbsoluteHttpUrl $targetUrl)) {
        Add-Report $row.Row $area '' 'Übersprungen' "Ziel-URL ungültig: '$targetUrl'"
        continue
    }

    foreach ($column in $sourceColumns) {
        $sourceUrl = (& $cell $column).Trim()
        if ($sourceUrl.Length -eq 0) { continue }
        if (-not (Test-AbsoluteHttpUrl $sourceUrl)) {
            Add-Report $row.Row $area $sourceUrl 'Übersprungen' "Keine absolute http(s)-URL ($($Columns[$column]))"
            continue
        }
        if ($column -eq 'FolderUrl' -and $sourceUrl -notmatch '[?#]' -and -not $sourceUrl.EndsWith('/')) {
            $sourceUrl += '/'
            Add-Report $row.Row $area $sourceUrl 'Hinweis' "'/' am Ende der Ordner-URL ergänzt, damit der Ordner samt Inhalt weitergeleitet wird"
        }
        $candidates.Add([pscustomobject]@{
                Row = $row.Row; Area = $area; AreaKey = $area.ToLowerInvariant(); TargetUrl = $targetUrl
                SourceUrl = $sourceUrl; SourceKey = Get-SourceKey -SourceUrl $sourceUrl
            })
    }
}

if ($ParseOnly) {
    $candidates | Select-Object Row, Area, SourceKey, SourceUrl, TargetUrl
    $report
    return
}

if (-not $UseExistingConnection) {
    if ([string]::IsNullOrWhiteSpace($ClientId)) {
        throw 'ClientId fehlt: Für die interaktive Anmeldung ist die Client-ID der Entra-App-Registrierung für PnP.PowerShell nötig.'
    }
    Connect-PnPOnline -Url $SiteUrl -Interactive -ClientId $ClientId
}

$existingItems = @(Get-PnPListItem -List $ListUrl -PageSize 2000 -Fields 'ID', 'brtqmArea', 'brtqmTargetUrl', 'brtqmSourceKey')
$existingKeys = @{}
$existingAreaTargets = @{}
foreach ($item in $existingItems | Sort-Object Id) {
    $itemArea = [string] $item['brtqmArea']
    $itemKey = [string] $item['brtqmSourceKey']
    if ($itemKey) { $existingKeys[$itemKey] = $itemArea.Trim() }
    $areaKey = $itemArea.Trim().ToLowerInvariant()
    if ($areaKey -and -not $existingAreaTargets.ContainsKey($areaKey)) { $existingAreaTargets[$areaKey] = [string] $item['brtqmTargetUrl'] }
}
Write-Host "Liste '$ListUrl': $($existingItems.Count) bestehende Einträge."

$plannedKeys = @{}
$plannedAreaTargets = @{}
foreach ($candidate in $candidates) {
    if ($existingAreaTargets.ContainsKey($candidate.AreaKey) -and $existingAreaTargets[$candidate.AreaKey] -ne $candidate.TargetUrl) {
        Add-Report $candidate.Row $candidate.Area $candidate.SourceUrl 'Übersprungen' "Bereich existiert bereits mit anderer Ziel-URL: $($existingAreaTargets[$candidate.AreaKey])"
        continue
    }
    if ($plannedAreaTargets.ContainsKey($candidate.AreaKey) -and $plannedAreaTargets[$candidate.AreaKey] -ne $candidate.TargetUrl) {
        Add-Report $candidate.Row $candidate.Area $candidate.SourceUrl 'Übersprungen' "Bereichsname kommt in der Excel-Datei mit anderer Ziel-URL vor: $($plannedAreaTargets[$candidate.AreaKey])"
        continue
    }
    if ($existingKeys.ContainsKey($candidate.SourceKey)) {
        Add-Report $candidate.Row $candidate.Area $candidate.SourceUrl 'Übersprungen' "Quell-URL bereits im Bereich '$($existingKeys[$candidate.SourceKey])' erfasst"
        continue
    }
    if ($plannedKeys.ContainsKey($candidate.SourceKey)) {
        Add-Report $candidate.Row $candidate.Area $candidate.SourceUrl 'Übersprungen' "Gleichwertige Quell-URL bereits in Zeile $($plannedKeys[$candidate.SourceKey]) der Excel-Datei"
        continue
    }

    $plannedKeys[$candidate.SourceKey] = $candidate.Row
    $plannedAreaTargets[$candidate.AreaKey] = $candidate.TargetUrl

    if (-not $PSCmdlet.ShouldProcess("$($candidate.Area): $($candidate.SourceUrl)", 'Listeneintrag anlegen')) {
        Add-Report $candidate.Row $candidate.Area $candidate.SourceUrl 'Geplant' ''
        continue
    }

    try {
        Add-PnPListItem -List $ListUrl -Values @{
            brtqmArea      = $candidate.Area
            brtqmSourceUrl = $candidate.SourceUrl
            brtqmTargetUrl = $candidate.TargetUrl
            brtqmSourceKey = $candidate.SourceKey
        } | Out-Null
        Add-Report $candidate.Row $candidate.Area $candidate.SourceUrl 'Angelegt' ''
    }
    catch {
        Add-Report $candidate.Row $candidate.Area $candidate.SourceUrl 'Fehler' $_.Exception.Message
    }
}

$report | Sort-Object Row
$report | Group-Object Action | ForEach-Object { Write-Host ("{0}: {1}" -f $_.Name, $_.Count) }
