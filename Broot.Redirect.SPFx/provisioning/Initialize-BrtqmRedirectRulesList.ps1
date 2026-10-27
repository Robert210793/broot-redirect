#Requires -Modules PnP.PowerShell
#Requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $SiteUrl,

    [Parameter(Mandatory = $false)]
    [string] $ClientId,

    [Parameter(Mandatory = $false)]
    [switch] $UseExistingConnection
)

$ErrorActionPreference = 'Stop'

$ListUrl = 'Lists/brtqmRedirectRules'
$ListTitle = 'Redirect Regeln'
$ViewFields = @('brtqmArea', 'brtqmSourceUrl', 'brtqmTargetUrl')

$FieldDefinitions = @(
    [pscustomobject]@{
        InternalName = 'brtqmArea'; DisplayName = 'Bereich'; Id = '9fc3ad53-128d-4c09-bc2b-967728aef122'
        Type = 'Text'; Required = $true; Indexed = $true; HiddenInForms = $false
    },
    [pscustomobject]@{
        InternalName = 'brtqmSourceUrl'; DisplayName = 'Quell-URL'; Id = 'a735257b-0b3c-4769-96c3-cce82f6a75a8'
        Type = 'Note'; Required = $true; Indexed = $false; HiddenInForms = $false
    },
    [pscustomobject]@{
        InternalName = 'brtqmTargetUrl'; DisplayName = 'Ziel-URL'; Id = '3ea93438-0360-4cb2-a6d4-4f710c19e677'
        Type = 'Note'; Required = $true; Indexed = $false; HiddenInForms = $false
    },
    [pscustomobject]@{
        InternalName = 'brtqmSourceKey'; DisplayName = 'Quell-Schlüssel'; Id = '495bb033-9f26-4537-9bef-5232709201b1'
        Type = 'Text'; Required = $false; Indexed = $true; HiddenInForms = $true
    }
)

function Get-FieldXml {
    param([pscustomobject] $Definition)

    $required = if ($Definition.Required) { 'TRUE' } else { 'FALSE' }
    $shown = if ($Definition.HiddenInForms) { 'FALSE' } else { 'TRUE' }
    $common = "ID=`"{$($Definition.Id)}`" Name=`"$($Definition.InternalName)`" StaticName=`"$($Definition.InternalName)`" " +
        "DisplayName=`"$($Definition.InternalName)`" Required=`"$required`" " +
        "ShowInNewForm=`"$shown`" ShowInEditForm=`"$shown`" ShowInDisplayForm=`"$shown`""

    if ($Definition.Type -eq 'Note') {
        return "<Field Type=`"Note`" $common NumLines=`"6`" RichText=`"FALSE`" RichTextMode=`"Compatible`" AppendOnly=`"FALSE`" />"
    }

    return "<Field Type=`"Text`" $common MaxLength=`"255`" />"
}

function Test-FieldMatchesDefinition {
    param($Field, [pscustomobject] $Definition)

    $issues = @()

    if ($Field.TypeAsString -ne $Definition.Type) {
        $issues += "Feld '$($Definition.InternalName)' hat den Typ '$($Field.TypeAsString)' statt '$($Definition.Type)'."
    }

    if ($Definition.Type -eq 'Note') {
        $schema = [xml] $Field.SchemaXml
        if ($schema.Field.RichText -eq 'TRUE') {
            $issues += "Feld '$($Definition.InternalName)' ist formatiert (Rich Text) statt nur Text."
        }
    }

    return $issues
}

if (-not $UseExistingConnection) {
    if ([string]::IsNullOrWhiteSpace($ClientId)) {
        throw 'ClientId fehlt: Für die interaktive Anmeldung ist die Client-ID der Entra-App-Registrierung für PnP.PowerShell nötig.'
    }
    Connect-PnPOnline -Url $SiteUrl -Interactive -ClientId $ClientId
}

$deviations = @()
$list = Get-PnPList -Identity $ListUrl -ErrorAction SilentlyContinue
$listCreated = $false

if ($null -eq $list) {
    Write-Host "Liste '$ListTitle' ($ListUrl) wird angelegt."
    $list = New-PnPList -Title $ListTitle -Url $ListUrl -Template GenericList
    $listCreated = $true

    $titleField = Get-PnPField -List $list -Identity 'Title'
    $titleField.Required = $false
    $titleField.SetShowInNewForm($false)
    $titleField.SetShowInEditForm($false)
    $titleField.SetShowInDisplayForm($false)
    $titleField.Update()
    Invoke-PnPQuery
}
else {
    Write-Host "Liste '$ListTitle' ($ListUrl) existiert bereits und wird weiterverwendet."
    if ($list.BaseTemplate -ne 100) {
        $deviations += "Liste '$ListUrl' basiert auf der Vorlage $($list.BaseTemplate) statt auf einer benutzerdefinierten Liste (100)."
    }
}

foreach ($definition in $FieldDefinitions) {
    $field = Get-PnPField -List $list -Identity $definition.InternalName -ErrorAction SilentlyContinue

    if ($null -eq $field) {
        Write-Host "Feld '$($definition.InternalName)' wird angelegt."
        $field = Add-PnPFieldFromXml -List $list -FieldXml (Get-FieldXml -Definition $definition)
        Set-PnPField -List $list -Identity $definition.InternalName -Values @{ Title = $definition.DisplayName } | Out-Null
    }
    else {
        $issues = @(Test-FieldMatchesDefinition -Field $field -Definition $definition)
        if ($issues.Count -gt 0) {
            $deviations += $issues
            continue
        }
        if ($field.Id.ToString() -ne $definition.Id) {
            Write-Warning "Feld '$($definition.InternalName)' existiert mit der ID $($field.Id) statt $($definition.Id); es wird weiterverwendet."
        }
        Write-Host "Feld '$($definition.InternalName)' existiert bereits."
    }

    if ($definition.Indexed -and -not $field.Indexed) {
        Write-Host "Index für Feld '$($definition.InternalName)' wird angelegt."
        Set-PnPField -List $list -Identity $definition.InternalName -Values @{ Indexed = $true } | Out-Null
    }
}

if ($listCreated) {
    $defaultView = Get-PnPView -List $list | Where-Object { $_.DefaultView } | Select-Object -First 1
    Write-Host "Standardansicht '$($defaultView.Title)' wird eingerichtet."
    Set-PnPView -List $list -Identity $defaultView.Id -Fields $ViewFields | Out-Null
}

if ($deviations.Count -gt 0) {
    $deviations | ForEach-Object { Write-Error $_ -ErrorAction Continue }
    throw "Die Liste '$ListUrl' weicht vom erwarteten Schema ab ($($deviations.Count) Abweichung(en)); es wurde nichts korrigiert."
}

Write-Host "Liste '$ListTitle' ist bereitgestellt."
