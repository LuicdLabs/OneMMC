<#
.SYNOPSIS
    Aggregates MSBuild AOT/trim analysis warnings into markdown summary tables.
.DESCRIPTION
    Parses a WarningsOnly file-logger output produced by an OneMMCAotAnalysis build
    (see doc/NativeAotAssessment.md) and emits two markdown tables: warning counts
    by diagnostic code, and warning counts by feature area. Identical file(line)+code
    tuples are de-duplicated because MSBuild repeats warnings across restore/build
    passes and multi-project references.
.EXAMPLE
    pwsh eng/Get-AotWarningSummary.ps1 -LogFile eng/aot-logs/aot-build-warn.log -OutFile eng/aot-logs/aot-build-summary.md
#>
param(
    [Parameter(Mandatory)] [string] $LogFile,
    [string] $OutFile
)

if (-not (Test-Path $LogFile)) { throw "Log file not found: $LogFile" }

$codeMeanings = @{
    'IL2026'      = 'RequiresUnreferencedCode member called (trim-unsafe)'
    'IL2045'      = 'DynamicallyAccessedMembers mismatch on attribute'
    'IL2046'      = 'RequiresUnreferencedCode mismatch on override/interface'
    'IL2050'      = 'P/Invoke with COM marshalling (trimmer cannot analyze)'
    'IL2067'      = 'Argument does not satisfy DynamicallyAccessedMembers'
    'IL2070'      = 'this argument does not satisfy DynamicallyAccessedMembers'
    'IL2072'      = 'Return value does not satisfy DynamicallyAccessedMembers'
    'IL2075'      = 'Unknown reflected type (GetProperty/GetMethod on unannotated Type)'
    'IL2080'      = 'Field value does not satisfy DynamicallyAccessedMembers'
    'IL2091'      = 'Generic parameter does not satisfy DynamicallyAccessedMembers'
    'IL2104'      = 'Assembly produced trim warnings (collapsed)'
    'IL3000'      = 'Assembly.Location returns empty in single-file'
    'IL3002'      = 'RequiresAssemblyFiles member called (single-file-unsafe)'
    'IL3050'      = 'RequiresDynamicCode member called (AOT-unsafe)'
    'IL3053'      = 'Assembly produced AOT warnings (collapsed)'
    'IL3056'      = 'RequiresDynamicCode mismatch on override/interface'
    'CsWinRT1028' = 'Class implements WinRT interfaces but is not partial'
    'CsWinRT1029' = 'Class implements WinRT-mapped .NET interface; needs partial for AOT'
    'CsWinRT1030' = 'Project needs unsafe blocks enabled for CsWinRT AOT codegen'
    'MVVMTK0045'  = '[ObservableProperty] field not AOT-safe for WinRT (use partial property)'
}

# Typical shapes:
#   C:\...\File.cs(12,34): warning IL2026: message [C:\...\proj.csproj]
#   ILC/ILLink warnings without a file location: "warning IL3053: ..."
$siteRegex = '(?<file>[^(\s][^(]*)\((?<line>\d+)(,\d+)?\):\s+warning\s+(?<code>(IL\d{4}|CsWinRT\d{4}|MVVMTK\d{4}|CA\d{4}|CS\d{4}))\s*:'
$bareRegex = 'warning\s+(?<code>(IL\d{4}|CsWinRT\d{4}|MVVMTK\d{4}))\s*:'

$sites = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$records = foreach ($line in Get-Content $LogFile) {
    if ($line -match $siteRegex) {
        $key = '{0}({1}):{2}' -f $Matches.file.Trim(), $Matches.line, $Matches.code
        if ($sites.Add($key)) {
            [pscustomobject]@{ Code = $Matches.code; File = $Matches.file.Trim() }
        }
    }
    elseif ($line -match $bareRegex) {
        # No file location (e.g. ILC assembly-level warnings): dedupe on whole line.
        if ($sites.Add($line.Trim())) {
            [pscustomobject]@{ Code = $Matches.code; File = '(no location)' }
        }
    }
}

function Get-FeatureArea([string] $path) {
    if ($path -match 'src[\\/]OneMMC\.Core[\\/]Features[\\/](?<a>[^\\/]+)[\\/](?<b>[^\\/]+)') { return "Core/Features/$($Matches.a)/$($Matches.b)" }
    if ($path -match 'src[\\/]OneMMC\.Core[\\/](?<a>[^\\/]+)')                                { return "Core/$($Matches.a)" }
    if ($path -match 'src[\\/]OneMMC[\\/]Views[\\/](?<a>[^\\/]+)[\\/]?(?<b>[^\\/]*)')          { return "App/Views/$($Matches.a)" }
    if ($path -match 'src[\\/]OneMMC[\\/](?<a>[^\\/]+)')                                       { return "App/$($Matches.a)" }
    return '(other/generated)'
}

$byCode = $records | Group-Object Code | Sort-Object Count -Descending
$byArea = $records | Group-Object { Get-FeatureArea $_.File } | Sort-Object Count -Descending

$sb = [System.Text.StringBuilder]::new()
[void]$sb.AppendLine("## AOT warning summary: $(Split-Path $LogFile -Leaf)")
[void]$sb.AppendLine()
[void]$sb.AppendLine("Total distinct warning sites: **$($records.Count)**")
[void]$sb.AppendLine()
[void]$sb.AppendLine('### By diagnostic code')
[void]$sb.AppendLine()
[void]$sb.AppendLine('| Code | Count | Meaning |')
[void]$sb.AppendLine('|---|---:|---|')
foreach ($g in $byCode) {
    $meaning = if ($codeMeanings.ContainsKey($g.Name)) { $codeMeanings[$g.Name] } else { '' }
    [void]$sb.AppendLine("| $($g.Name) | $($g.Count) | $meaning |")
}
[void]$sb.AppendLine()
[void]$sb.AppendLine('### By feature area')
[void]$sb.AppendLine()
[void]$sb.AppendLine('| Area | Count |')
[void]$sb.AppendLine('|---|---:|')
foreach ($g in $byArea) {
    [void]$sb.AppendLine("| $($g.Name) | $($g.Count) |")
}

$output = $sb.ToString()
$output
if ($OutFile) { Set-Content -Path $OutFile -Value $output -Encoding utf8 }
