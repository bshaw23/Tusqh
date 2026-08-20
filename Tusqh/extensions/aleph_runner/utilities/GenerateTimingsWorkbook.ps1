<#
.SYNOPSIS
Creates an auditable Excel workbook from a Tusqh timing log.

.DESCRIPTION
Parses Grasshopper component timings, the Tusqh Aleph runner output, and
PowerShell Measure-Command output for Sculpt. Configuration and run counts
are discovered from the source file. The Summary sheet uses Excel formulas;
the Data, Audit Trail, and Source Text sheets preserve the underlying values
and their provenance.

.EXAMPLE
.\GenerateTimingsWorkbook.ps1 -SourcePath .\Timings_Data.txt -Domain Dragon

.EXAMPLE
.\GenerateTimingsWorkbook.ps1 -SourcePath .\Timings_Data.txt `
    -OutputPath .\OtherGeometry_Timings.xlsx -Domain OtherGeometry -Force
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$SourcePath,

    [string]$OutputPath,

    [string]$Domain,

    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$culture = [System.Globalization.CultureInfo]::InvariantCulture
$SourcePath = [System.IO.Path]::GetFullPath($SourcePath)
if (-not (Test-Path -LiteralPath $SourcePath -PathType Leaf)) {
    throw "Timing source file does not exist: $SourcePath"
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = [System.IO.Path]::ChangeExtension($SourcePath, '.xlsx')
}
else {
    $OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
}
if ([System.IO.Path]::GetExtension($OutputPath) -ne '.xlsx') {
    throw "OutputPath must have an .xlsx extension: $OutputPath"
}
if (-not (Test-Path -LiteralPath ([System.IO.Path]::GetDirectoryName($OutputPath)) -PathType Container)) {
    throw "Output directory does not exist: $([System.IO.Path]::GetDirectoryName($OutputPath))"
}
if ((Test-Path -LiteralPath $OutputPath) -and -not $Force) {
    throw "Output already exists. Choose another OutputPath or pass -Force: $OutputPath"
}
if ([string]::IsNullOrWhiteSpace($Domain)) {
    $Domain = Split-Path -Leaf (Split-Path -Parent $SourcePath)
}
$lines = [System.IO.File]::ReadAllLines($SourcePath)
$audit = [System.Collections.Generic.List[object]]::new()

function Convert-Number([string]$text, [bool]$integer = $false) {
    $clean = $text.Replace(',', '')
    if ($integer) { return [long]::Parse($clean, $culture) }
    return [double]::Parse($clean, $culture)
}

function Set-ExcelScalar($cell, $value) {
    if ($null -eq $value) { return }
    if ($value -is [byte] -or $value -is [sbyte] -or
        $value -is [int16] -or $value -is [uint16] -or
        $value -is [int32] -or $value -is [uint32] -or
        $value -is [int64] -or $value -is [uint64] -or
        $value -is [single] -or $value -is [double] -or $value -is [decimal]) {
        $cell.Value2 = [double]$value
    }
    elseif ($value -is [bool]) { $cell.Value2 = [bool]$value }
    else { $cell.Value2 = [string]$value }
}

function New-Record($config, [int]$run) {
    return @{
        Values = @{
            ConfigOrder = $config.Order
            Configuration = $config.Name
            Domain = $Domain
            X = $config.X
            Y = $config.Y
            Z = $config.Z
            WindingX = $config.WX
            WindingY = $config.WY
            WindingZ = $config.WZ
            SamplesPerHex = $config.WX * $config.WY * $config.WZ
            Run = $run
            ConfigSourceLine = $config.SourceLine
        }
        Sources = @{}
    }
}

function Set-RecordValue($record, [string]$key, $value, [int]$sourceLine,
                         [string]$sourceText, [string]$process,
                         [string]$metric, [string]$unit) {
    $record.Values[$key] = $value
    if ($sourceLine -gt 0) {
        $record.Sources[$key] = $sourceLine
        $audit.Add([pscustomobject]@{
            Configuration = $record.Values.Configuration
            Run = $record.Values.Run
            Process = $process
            Metric = $metric
            Value = $value
            Unit = $unit
            SourceLine = $sourceLine
            SourceText = $sourceText
            DataKey = $key
        })
    }
}

function Parse-GrasshopperBlock($block, $record) {
    if ($block.Count -eq 0) { return }
    $joined = ($block | ForEach-Object { $_.Text }) -join "`n"
    $first = $block[0].Text

    if ($first -match '^01 inputs:') { $prefix = 'VF'; $process = 'Volume Fraction' }
    elseif ($first -match '^01 inputs \(') { $prefix = 'Compact'; $process = 'Compact Aleph Export' }
    elseif ($joined -match 'direct volume mapping') { $prefix = 'Tri'; $process = '24-Tet Triangulation' }
    elseif ($joined -match 'SPN formatting') { $prefix = 'SPN'; $process = 'SPN Export' }
    else { return }

    foreach ($item in $block) {
        $text = $item.Text
        $lineNumber = $item.Line

        if ($text -match '^(?<order>\d{2}) (?<label>.+?): (?<value>[\d,]+(?:\.\d+)?) ms(?:T)?$') {
            $order = $matches.order
            $label = $matches.label
            $value = Convert-Number $matches.value
            Set-RecordValue $record "${prefix}_${order}_ms" $value $lineNumber $text $process $label 'ms'

            if ($prefix -eq 'VF' -and $label -match '\((?<count>[\d,]+) points\)') {
                Set-RecordValue $record 'WindingPoints' (Convert-Number $matches.count $true) $lineNumber $text $process 'Total winding query points' 'count'
            }
            elseif ($prefix -eq 'Tri' -and $label -match 'direct volume mapping \((?<count>[\d,]+) vertices\)') {
                Set-RecordValue $record 'TriMappingVertices' (Convert-Number $matches.count $true) $lineNumber $text $process 'Direct mapping vertices' 'count'
            }
            elseif ($prefix -eq 'Tri' -and $label -match 'compact 24-tet construction \((?<count>[\d,]+) tets\)') {
                Set-RecordValue $record 'TriTetrahedra' (Convert-Number $matches.count $true) $lineNumber $text $process 'Constructed tetrahedra' 'count'
            }
            elseif ($prefix -eq 'Compact' -and $label -match 'inputs \((?<verts>[\d,]+) verts, (?<tets>[\d,]+) tets\)') {
                Set-RecordValue $record 'CompactInputVertices' (Convert-Number $matches.verts $true) $lineNumber $text $process 'Input vertices' 'count'
                Set-RecordValue $record 'CompactInputTetrahedra' (Convert-Number $matches.tets $true) $lineNumber $text $process 'Input tetrahedra' 'count'
            }
            elseif ($prefix -eq 'SPN' -and $label -match 'SPN formatting \((?<count>[\d,]+) cells\)') {
                Set-RecordValue $record 'SPNCells' (Convert-Number $matches.count $true) $lineNumber $text $process 'SPN cells' 'count'
            }
        }
        elseif ($text -match '^TOTAL(?: \(excluding timing output\))?: (?<value>[\d,]+(?:\.\d+)?) ms$') {
            Set-RecordValue $record "${prefix}_Total_ms" (Convert-Number $matches.value) $lineNumber $text $process 'Recorded total' 'ms'
        }
    }
}

$configMatches = [System.Collections.Generic.List[object]]::new()
for ($i = 0; $i -lt $lines.Length; ++$i) {
    if ($lines[$i] -match '^### x(?<x>\d+) y(?<y>\d+) z(?<z>\d+); (?<wx>\d+)x(?<wy>\d+)x(?<wz>\d+) winding numbers r1 ###$') {
        $configMatches.Add([pscustomobject]@{
            Start = $i
            SourceLine = $i + 1
            X = [int]$matches.x
            Y = [int]$matches.y
            Z = [int]$matches.z
            WX = [int]$matches.wx
            WY = [int]$matches.wy
            WZ = [int]$matches.wz
        })
    }
}

$configs = [System.Collections.Generic.List[object]]::new()
for ($c = 0; $c -lt $configMatches.Count; ++$c) {
    $m = $configMatches[$c]
    $end = if ($c + 1 -lt $configMatches.Count) { $configMatches[$c + 1].Start - 1 } else { $lines.Length - 1 }
    $runCount = 1
    for ($i = $m.Start + 1; $i -le $end; ++$i) {
        if ($lines[$i].Trim() -match '^r(?<run>\d+)\s*$') {
            $runCount = [Math]::Max($runCount, [int]$matches.run)
        }
    }
    $configs.Add([pscustomobject]@{
        Order = $c + 1
        Id = "$($m.X)|$($m.Y)|$($m.Z)|$($m.WX)|$($m.WY)|$($m.WZ)"
        Name = "$($m.X)x, $($m.WX)x$($m.WY)x$($m.WZ)"
        X = $m.X; Y = $m.Y; Z = $m.Z
        WX = $m.WX; WY = $m.WY; WZ = $m.WZ
        Start = $m.Start
        End = $end
        RunCount = $runCount
        SourceLine = $m.SourceLine
    })
}
if ($configs.Count -eq 0) {
    throw 'No configuration headers were found. Expected lines like: ### x30 y13 z21; 4x4x4 winding numbers r1 ###'
}

$recordsByConfig = @{}
foreach ($config in $configs) {
    $records = @()
    1..$config.RunCount | ForEach-Object { $records += New-Record $config $_ }
    $recordsByConfig[$config.Id] = $records

    $alephIndex = -1
    $sculptIndex = -1
    for ($i = $config.Start; $i -le $config.End; ++$i) {
        if ($alephIndex -lt 0 -and $lines[$i] -match '^Aleph:?$') { $alephIndex = $i }
        elseif ($alephIndex -ge 0 -and $lines[$i] -match '^Sculpt:?\s*$') { $sculptIndex = $i; break }
    }
    if ($alephIndex -lt 0 -or $sculptIndex -lt 0) { throw "Missing Aleph/Sculpt section for $($config.Name)" }

    # Grasshopper blocks
    $run = 1
    $block = [System.Collections.Generic.List[object]]::new()
    for ($i = $config.Start + 1; $i -lt $alephIndex; ++$i) {
        $text = $lines[$i].Trim()
        if ($text -match '^r(?<run>\d+)\s*$') {
            Parse-GrasshopperBlock $block $records[$run - 1]
            $block.Clear()
            $run = [int]$matches.run
        }
        elseif ([string]::IsNullOrWhiteSpace($text)) {
            Parse-GrasshopperBlock $block $records[$run - 1]
            $block.Clear()
        }
        elseif ($text -match '^(?:\d{2} |TOTAL)') {
            $block.Add([pscustomobject]@{ Line = $i + 1; Text = $text })
        }
    }
    Parse-GrasshopperBlock $block $records[$run - 1]

    # Aleph section
    $run = 1
    for ($i = $alephIndex + 1; $i -lt $sculptIndex; ++$i) {
        $text = $lines[$i].Trim()
        $record = $records[$run - 1]
        if ($text -match '^r(?<run>\d+)\s*$') { $run = [int]$matches.run; continue }
        $record = $records[$run - 1]

        if ($text -eq 'Aleph run complete') {
            Set-RecordValue $record 'AlephStatus' 'Complete' ($i + 1) $text 'Aleph' 'Status' 'text'
        }
        elseif ($text -match '^(?:<)?Not (?:computed|completed).*$') {
            foreach ($r in $records) {
                Set-RecordValue $r 'AlephStatus' $text ($i + 1) $text 'Aleph' 'Status' 'text'
            }
        }
        elseif ($text -match '^input format: (?<value>.+)$') {
            Set-RecordValue $record 'AlephInputFormat' $matches.value ($i + 1) $text 'Aleph' 'Input format' 'text'
        }
        elseif ($text -match '^(?<metric>vertices|tetrahedra|simplices|persistence pairs): (?<value>[\d,]+)$') {
            $map = @{ vertices='AlephVertices'; tetrahedra='AlephTetrahedra'; simplices='AlephSimplices'; 'persistence pairs'='AlephPersistencePairs' }
            Set-RecordValue $record $map[$matches.metric] (Convert-Number $matches.value $true) ($i + 1) $text 'Aleph' $matches.metric 'count'
        }
        elseif ($text -match '^(?<metric>read_input|seed_complex|restore_faces_and_weights|sort_filtration|build_boundary_matrix|dualize_boundary_matrix|reduce_and_pair|construct_diagrams|write_diagrams|total): (?<value>[\d,]+(?:\.\d+)?) ms$') {
            $map = @{
                read_input='AlephReadInput_ms'; seed_complex='AlephSeedComplex_ms'
                restore_faces_and_weights='AlephRestoreFacesWeights_ms'; sort_filtration='AlephSortFiltration_ms'
                build_boundary_matrix='AlephBuildBoundaryMatrix_ms'; dualize_boundary_matrix='AlephDualizeBoundaryMatrix_ms'
                reduce_and_pair='AlephReducePair_ms'; construct_diagrams='AlephConstructDiagrams_ms'
                write_diagrams='AlephWriteDiagrams_ms'; total='AlephTotal_ms'
            }
            Set-RecordValue $record $map[$matches.metric] (Convert-Number $matches.value) ($i + 1) $text 'Aleph' $matches.metric 'ms'
        }
        elseif ($text -match '^timing file: (?<value>.+)$') {
            Set-RecordValue $record 'AlephTimingFile' $matches.value ($i + 1) $text 'Aleph' 'Timing file' 'text'
        }
        elseif ($text -match '^Note: (?<value>.+)$') {
            Set-RecordValue $record 'AlephNote' $matches.value ($i + 1) $text 'Aleph' 'Note' 'text'
        }
        elseif ($text -match 'aleph_runner\.exe\s+(?<input>\S+)\s+(?<output>\S+)\s*$') {
            Set-RecordValue $record 'AlephInputFile' $matches.input ($i + 1) $text 'Aleph' 'Input file' 'text'
            Set-RecordValue $record 'AlephOutputPrefix' $matches.output ($i + 1) $text 'Aleph' 'Output prefix' 'text'
        }
    }

    # Sculpt section
    $run = 1
    $commandParts = [System.Collections.Generic.List[string]]::new()
    $capturingCommand = $false
    $commandStart = 0
    for ($i = $sculptIndex + 1; $i -le $config.End; ++$i) {
        $text = $lines[$i].Trim()
        if ($text -match '^r(?<run>\d+)\s*$') { $run = [int]$matches.run; continue }
        $record = $records[$run - 1]

        if ($text -match 'Measure-Command \{') {
            $capturingCommand = $true
            $commandParts.Clear()
            $commandStart = $i + 1
        }
        if ($capturingCommand) {
            $commandParts.Add($text)
            if ($text -match '^>> \}$') {
                $command = ($commandParts -join ' ')
                Set-RecordValue $record 'SculptCommand' $command $commandStart $command 'Sculpt' 'Command' 'text'
                foreach ($setting in @(
                    @('SculptJ','j','int'), @('SculptX','x','int'), @('SculptY','y','int'), @('SculptZ','z','int'),
                    @('SculptP','p','int'), @('SculptS','S','int'), @('SculptLI','LI','int'), @('SculptOI','OI','int'),
                    @('SculptGQI','GQI','int'), @('SculptGQT','GQT','double')
                )) {
                    $pattern = '(?:^|\s)-' + [regex]::Escape($setting[1]) + '\s+(?<value>-?[\d.]+)'
                    if ($command -match $pattern) {
                        $value = if ($setting[2] -eq 'int') { [int]$matches.value } else { Convert-Number $matches.value }
                        Set-RecordValue $record $setting[0] $value $commandStart $command 'Sculpt' "-$($setting[1]) setting" 'argument'
                    }
                }
                Set-RecordValue $record 'SculptPSO' ([bool]($command -match '(?:^|\s)-pso(?:\s|$)')) $commandStart $command 'Sculpt' '-pso setting' 'argument'
                $capturingCommand = $false
            }
        }

        if ($text -match '^-?isp\s+"?(?<value>\.\\[^"\s`]+)') {
            Set-RecordValue $record 'SculptInputFile' $matches.value ($i + 1) $text 'Sculpt' 'Input file' 'text'
        }
        elseif ($text -match '^>>\s+-isp\s+"?(?<value>\.\\[^"\s`]+)') {
            Set-RecordValue $record 'SculptInputFile' $matches.value ($i + 1) $text 'Sculpt' 'Input file' 'text'
        }

        if ($text -match '^(?<metric>Days|Hours|Minutes|Seconds|Milliseconds|Ticks|TotalDays|TotalHours|TotalMinutes|TotalSeconds|TotalMilliseconds)\s+:\s+(?<value>[\d.]+)$') {
            $integer = $matches.metric -in @('Days','Hours','Minutes','Seconds','Milliseconds','Ticks')
            $value = Convert-Number $matches.value $integer
            Set-RecordValue $record "Sculpt$($matches.metric)" $value ($i + 1) $text 'Sculpt' $matches.metric $(if ($matches.metric -like 'Total*') {'reported'} else {'component'})
            Set-RecordValue $record 'SculptStatus' 'Complete' ($i + 1) $text 'Sculpt' 'Status' 'text'
        }
    }

}

$allRecords = @()
$dataRanges = @{}
$nextDataRow = 2
foreach ($config in $configs) {
    $records = $recordsByConfig[$config.Id]
    $allRecords += $records
    $dataRanges[$config.Id] = [pscustomobject]@{
        Start = $nextDataRow
        End = $nextDataRow + $records.Count - 1
    }
    $nextDataRow += $records.Count
}

$columns = @(
    @{K='ConfigOrder';H='Configuration order';U='index'}, @{K='Configuration';H='Configuration';U='text'},
    @{K='Domain';H='Domain';U='text'}, @{K='X';H='X divisions';U='count'}, @{K='Y';H='Y divisions';U='count'}, @{K='Z';H='Z divisions';U='count'},
    @{K='WindingX';H='Winding samples X';U='count'}, @{K='WindingY';H='Winding samples Y';U='count'}, @{K='WindingZ';H='Winding samples Z';U='count'},
    @{K='SamplesPerHex';H='Winding samples per hex';U='count'}, @{K='Run';H='Run';U='index'}, @{K='ConfigSourceLine';H='Configuration source line';U='line'},
    @{K='WindingPoints';H='Total winding query points';U='count'},
    @{K='VF_01_ms';H='VF 01 inputs (ms)';U='ms'}, @{K='VF_02_ms';H='VF 02 surface mesh conversion (ms)';U='ms'}, @{K='VF_03_ms';H='VF 03 background mesh/setup (ms)';U='ms'},
    @{K='VF_04_ms';H='VF 04 sample point generation (ms)';U='ms'}, @{K='VF_05_ms';H='VF 05 column-major packing (ms)';U='ms'},
    @{K='VF_06_ms';H='VF 06 native winding numbers (ms)';U='ms'}, @{K='VF_07_ms';H='VF 07 volume-fraction aggregation (ms)';U='ms'},
    @{K='VF_08_ms';H='VF 08 publish outputs (ms)';U='ms'}, @{K='VF_Total_ms';H='VF recorded total (ms)';U='ms'},
    @{K='TriMappingVertices';H='Tri direct-mapping vertices';U='count'}, @{K='TriTetrahedra';H='Tri constructed tetrahedra';U='count'},
    @{K='Tri_01_ms';H='Tri 01 inputs/validation (ms)';U='ms'}, @{K='Tri_02_ms';H='Tri 02 direct volume mapping (ms)';U='ms'},
    @{K='Tri_03_ms';H='Tri 03 compact 24-tet construction (ms)';U='ms'}, @{K='Tri_04_ms';H='Tri 04 weighting/visualization (ms)';U='ms'},
    @{K='Tri_05_ms';H='Tri 05 compact output publication (ms)';U='ms'}, @{K='Tri_Total_ms';H='Tri recorded total (ms)';U='ms'},
    @{K='CompactInputVertices';H='Compact export input vertices';U='count'}, @{K='CompactInputTetrahedra';H='Compact export input tetrahedra';U='count'},
    @{K='Compact_01_ms';H='Compact 01 inputs (ms)';U='ms'}, @{K='Compact_02_ms';H='Compact 02 validation (ms)';U='ms'},
    @{K='Compact_03_ms';H='Compact 03 binary writing (ms)';U='ms'}, @{K='Compact_Total_ms';H='Compact recorded total (ms)';U='ms'},
    @{K='SPNCells';H='SPN cells';U='count'}, @{K='SPN_01_ms';H='SPN 01 inputs/validation (ms)';U='ms'},
    @{K='SPN_02_ms';H='SPN 02 formatting (ms)';U='ms'}, @{K='SPN_03_ms';H='SPN 03 file writing (ms)';U='ms'}, @{K='SPN_Total_ms';H='SPN recorded total (ms)';U='ms'},
    @{K='AlephStatus';H='Aleph status';U='text'}, @{K='AlephInputFormat';H='Aleph input format';U='text'}, @{K='AlephInputFile';H='Aleph input file';U='text'},
    @{K='AlephOutputPrefix';H='Aleph output prefix';U='text'}, @{K='AlephVertices';H='Aleph vertices';U='count'}, @{K='AlephTetrahedra';H='Aleph tetrahedra';U='count'},
    @{K='AlephSimplices';H='Aleph total simplices';U='count'}, @{K='AlephPersistencePairs';H='Aleph persistence pairs';U='count'},
    @{K='AlephReadInput_ms';H='Aleph read input (ms)';U='ms'}, @{K='AlephSeedComplex_ms';H='Aleph seed complex (ms)';U='ms'},
    @{K='AlephRestoreFacesWeights_ms';H='Aleph restore faces/weights (ms)';U='ms'}, @{K='AlephSortFiltration_ms';H='Aleph sort filtration (ms)';U='ms'},
    @{K='AlephBuildBoundaryMatrix_ms';H='Aleph build boundary matrix (ms)';U='ms'}, @{K='AlephDualizeBoundaryMatrix_ms';H='Aleph dualize boundary matrix (ms)';U='ms'},
    @{K='AlephReducePair_ms';H='Aleph reduce and pair (ms)';U='ms'}, @{K='AlephConstructDiagrams_ms';H='Aleph construct diagrams (ms)';U='ms'},
    @{K='AlephWriteDiagrams_ms';H='Aleph write diagrams (ms)';U='ms'}, @{K='AlephTotal_ms';H='Aleph recorded total (ms)';U='ms'},
    @{K='AlephTimingFile';H='Aleph timing file';U='text'}, @{K='AlephNote';H='Aleph note';U='text'},
    @{K='SculptStatus';H='Sculpt status';U='text'}, @{K='SculptCommand';H='Sculpt command';U='text'}, @{K='SculptInputFile';H='Sculpt input file';U='text'},
    @{K='SculptJ';H='Sculpt -j';U='count'}, @{K='SculptX';H='Sculpt -x';U='count'}, @{K='SculptY';H='Sculpt -y';U='count'}, @{K='SculptZ';H='Sculpt -z';U='count'},
    @{K='SculptP';H='Sculpt -p';U='value'}, @{K='SculptS';H='Sculpt -S';U='value'}, @{K='SculptLI';H='Sculpt -LI';U='value'}, @{K='SculptOI';H='Sculpt -OI';U='value'},
    @{K='SculptGQI';H='Sculpt -GQI';U='value'}, @{K='SculptGQT';H='Sculpt -GQT';U='value'}, @{K='SculptPSO';H='Sculpt -pso';U='bool'},
    @{K='SculptDays';H='Sculpt Days';U='component'}, @{K='SculptHours';H='Sculpt Hours';U='component'}, @{K='SculptMinutes';H='Sculpt Minutes';U='component'},
    @{K='SculptSeconds';H='Sculpt Seconds';U='component'}, @{K='SculptMilliseconds';H='Sculpt Milliseconds';U='component'}, @{K='SculptTicks';H='Sculpt Ticks';U='ticks'},
    @{K='SculptTotalDays';H='Sculpt TotalDays';U='days'}, @{K='SculptTotalHours';H='Sculpt TotalHours';U='hours'},
    @{K='SculptTotalMinutes';H='Sculpt TotalMinutes';U='minutes'}, @{K='SculptTotalSeconds';H='Sculpt TotalSeconds';U='seconds'},
    @{K='SculptTotalMilliseconds';H='Sculpt TotalMilliseconds';U='ms'},
    @{K='Formula_GH_Total_ms';H='FORMULA instrumented Grasshopper total (ms)';U='ms'},
    @{K='Formula_Aleph_PreReduction_ms';H='FORMULA Aleph through dualization (ms)';U='ms'},
    @{K='Formula_Aleph_ReductionShare';H='FORMULA Aleph reduction share';U='ratio'},
    @{K='Formula_Combined_s';H='FORMULA measured GH + Aleph + Sculpt (s)';U='seconds'}
)

function Excel-Column([int]$number) {
    $result = ''
    while ($number -gt 0) {
        $number--
        $result = [char](65 + ($number % 26)) + $result
        $number = [math]::Floor($number / 26)
    }
    return $result
}

$columnIndex = @{}
for ($i = 0; $i -lt $columns.Count; ++$i) { $columnIndex[$columns[$i].K] = $i + 1 }

$excel = $null
$workbook = $null
try {
    $excel = New-Object -ComObject Excel.Application
    $excel.Visible = $false
    $excel.DisplayAlerts = $false
    $excel.ScreenUpdating = $false
    $excel.EnableEvents = $false
    $workbook = $excel.Workbooks.Add()

    while ($workbook.Worksheets.Count -lt 5) { [void]$workbook.Worksheets.Add() }
    while ($workbook.Worksheets.Count -gt 5) { $workbook.Worksheets.Item($workbook.Worksheets.Count).Delete() }

    $summary = $workbook.Worksheets.Item(1); $summary.Name = 'Summary'
    $data = $workbook.Worksheets.Item(2); $data.Name = 'Data'
    $auditSheet = $workbook.Worksheets.Item(3); $auditSheet.Name = 'Audit Trail'
    $sourceSheet = $workbook.Worksheets.Item(4); $sourceSheet.Name = 'Source Text'
    $readme = $workbook.Worksheets.Item(5); $readme.Name = 'README'

    # Data sheet
    for ($c = 0; $c -lt $columns.Count; ++$c) { $data.Cells.Item(1, $c + 1).Value2 = $columns[$c].H }
    for ($r = 0; $r -lt $allRecords.Count; ++$r) {
        $excelRow = $r + 2
        $record = $allRecords[$r]
        for ($c = 0; $c -lt $columns.Count; ++$c) {
            $key = $columns[$c].K
            if ($key -like 'Formula_*') { continue }
            if ($record.Values.ContainsKey($key)) { Set-ExcelScalar ($data.Cells.Item($excelRow, $c + 1)) $record.Values[$key] }
        }

        $vf = Excel-Column $columnIndex.VF_Total_ms
        $tri = Excel-Column $columnIndex.Tri_Total_ms
        $compact = Excel-Column $columnIndex.Compact_Total_ms
        $spn = Excel-Column $columnIndex.SPN_Total_ms
        $alephRead = Excel-Column $columnIndex.AlephReadInput_ms
        $alephDual = Excel-Column $columnIndex.AlephDualizeBoundaryMatrix_ms
        $alephReduce = Excel-Column $columnIndex.AlephReducePair_ms
        $alephTotal = Excel-Column $columnIndex.AlephTotal_ms
        $sculptSeconds = Excel-Column $columnIndex.SculptTotalSeconds
        $ghFormulaCol = $columnIndex.Formula_GH_Total_ms
        $preFormulaCol = $columnIndex.Formula_Aleph_PreReduction_ms
        $shareFormulaCol = $columnIndex.Formula_Aleph_ReductionShare
        $combinedFormulaCol = $columnIndex.Formula_Combined_s
        $ghFormulaLetter = Excel-Column $ghFormulaCol

        $data.Cells.Item($excelRow, $ghFormulaCol).Formula = "=IF(COUNT(${vf}${excelRow},${tri}${excelRow},${compact}${excelRow},${spn}${excelRow})=4,SUM(${vf}${excelRow},${tri}${excelRow},${compact}${excelRow},${spn}${excelRow}),`"`")"
        $data.Cells.Item($excelRow, $preFormulaCol).Formula = "=IF(COUNT(${alephRead}${excelRow}:${alephDual}${excelRow})=6,SUM(${alephRead}${excelRow}:${alephDual}${excelRow}),`"`")"
        $data.Cells.Item($excelRow, $shareFormulaCol).Formula = "=IFERROR(${alephReduce}${excelRow}/${alephTotal}${excelRow},`"`")"
        $data.Cells.Item($excelRow, $combinedFormulaCol).Formula = "=IF(OR(${ghFormulaLetter}${excelRow}=`"`",${alephTotal}${excelRow}=`"`",${sculptSeconds}${excelRow}=`"`"),`"`",${ghFormulaLetter}${excelRow}/1000+${alephTotal}${excelRow}/1000+${sculptSeconds}${excelRow})"
    }

    $lastDataRow = $allRecords.Count + 1
    $lastDataCol = $columns.Count
    $data.Range($data.Cells.Item(1,1), $data.Cells.Item(1,$lastDataCol)).Font.Bold = $true
    $data.Range($data.Cells.Item(1,1), $data.Cells.Item(1,$lastDataCol)).Interior.Color = 0x703000
    $data.Range($data.Cells.Item(1,1), $data.Cells.Item(1,$lastDataCol)).Font.Color = 0xFFFFFF
    $data.Range($data.Cells.Item(1,1), $data.Cells.Item($lastDataRow,$lastDataCol)).AutoFilter() | Out-Null
    $data.Activate()
    $data.Application.ActiveWindow.SplitRow = 1
    $data.Application.ActiveWindow.FreezePanes = $true
    $data.UsedRange.Columns.AutoFit() | Out-Null
    for ($c = 1; $c -le $lastDataCol; ++$c) {
        if ($data.Columns.Item($c).ColumnWidth -gt 32) { $data.Columns.Item($c).ColumnWidth = 32 }
    }
    $data.Range($data.Cells.Item(2,1), $data.Cells.Item($lastDataRow,$lastDataCol)).NumberFormat = '0.000'
    foreach ($key in @('Configuration','Domain','AlephStatus','AlephInputFormat','AlephInputFile','AlephOutputPrefix','AlephTimingFile','AlephNote','SculptStatus','SculptCommand','SculptInputFile')) {
        $data.Columns.Item($columnIndex[$key]).NumberFormat = '@'
    }
    $data.Columns.Item($columnIndex.Formula_Aleph_ReductionShare).NumberFormat = '0.0%'

    # Summary sheet
    $summary.Cells.Item(1,1).Value2 = "$Domain timing summary (formula-driven medians)"
    $summary.Range('A1:T1').Merge()
    $summary.Range('A1:T1').Font.Bold = $true
    $summary.Range('A1:T1').Font.Size = 14
    $summary.Range('A1:T1').Interior.Color = 0x703000
    $summary.Range('A1:T1').Font.Color = 0xFFFFFF
    $summaryHeaders = @('Order','Configuration','Grid','Sampling','Samples/hex','VF n','Aleph n','Sculpt n','Median winding queries','Median Aleph simplices',
        'Median VF total (s)','Median triangulation (s)','Median compact export (s)','Median SPN export (s)','Median instrumented GH (s)',
        'Median Aleph (s)','Median Sculpt (s)','Sum of median stages (s)','Median Aleph reduction (s)','Aleph reduction share')
    for ($c=0; $c -lt $summaryHeaders.Count; ++$c) { $summary.Cells.Item(3,$c+1).Value2 = $summaryHeaders[$c] }

    $summaryKeys = @('VF_Total_ms','Tri_Total_ms','Compact_Total_ms','SPN_Total_ms','Formula_GH_Total_ms','AlephTotal_ms','SculptTotalSeconds','AlephReducePair_ms')
    for ($i=0; $i -lt $configs.Count; ++$i) {
        $row = $i + 4
        $dataStart = $dataRanges[$configs[$i].Id].Start
        $dataEnd = $dataRanges[$configs[$i].Id].End
        $summary.Cells.Item($row,1).Formula = "=Data!A${dataStart}"
        $summary.Cells.Item($row,2).Formula = "=Data!B${dataStart}"
        $summary.Cells.Item($row,3).Formula = "=Data!D${dataStart}&`" x `"&Data!E${dataStart}&`" x `"&Data!F${dataStart}"
        $summary.Cells.Item($row,4).Formula = "=Data!G${dataStart}&`" x `"&Data!H${dataStart}&`" x `"&Data!I${dataStart}"
        $summary.Cells.Item($row,5).Formula = "=Data!J${dataStart}"

        $vfCol = Excel-Column $columnIndex.VF_Total_ms
        $alephCol = Excel-Column $columnIndex.AlephTotal_ms
        $sculptCol = Excel-Column $columnIndex.SculptTotalSeconds
        $summary.Cells.Item($row,6).Formula = "=COUNT(Data!${vfCol}${dataStart}:${vfCol}${dataEnd})"
        $summary.Cells.Item($row,7).Formula = "=COUNT(Data!${alephCol}${dataStart}:${alephCol}${dataEnd})"
        $summary.Cells.Item($row,8).Formula = "=COUNT(Data!${sculptCol}${dataStart}:${sculptCol}${dataEnd})"

        foreach ($pair in @(@(9,'WindingPoints',1),@(10,'AlephSimplices',1),@(11,'VF_Total_ms',1000),@(12,'Tri_Total_ms',1000),
                             @(13,'Compact_Total_ms',1000),@(14,'SPN_Total_ms',1000),@(15,'Formula_GH_Total_ms',1000),
                             @(16,'AlephTotal_ms',1000),@(17,'SculptTotalSeconds',1),@(19,'AlephReducePair_ms',1000))) {
            $targetCol = $pair[0]; $key = $pair[1]; $factor = $pair[2]
            $sourceCol = Excel-Column $columnIndex[$key]
            $range = "Data!${sourceCol}${dataStart}:${sourceCol}${dataEnd}"
            $summary.Cells.Item($row,$targetCol).Formula = "=IF(COUNT(${range})=0,`"`",MEDIAN(${range})/${factor})"
        }
        $summary.Cells.Item($row,18).Formula = "=IF(OR(O${row}=`"`",P${row}=`"`",Q${row}=`"`"),`"`",O${row}+P${row}+Q${row})"
        $summary.Cells.Item($row,20).Formula = "=IFERROR(S${row}/P${row},`"`")"
    }

    $summary.Range('A3:T3').Font.Bold = $true
    $summary.Range('A3:T3').Interior.Color = 0xD9EAF7
    $summaryEnd = $configs.Count + 3
    $summary.Range("K4:T${summaryEnd}").NumberFormat = '0.000'
    $summary.Range("T4:T${summaryEnd}").NumberFormat = '0.0%'
    $summary.Range("I4:J${summaryEnd}").NumberFormat = '#,##0'

    $statsStart = $summaryEnd + 4
    $summary.Cells.Item($statsStart,1).Value2 = 'Detailed formula-driven statistics'
    $summary.Range("A${statsStart}:I${statsStart}").Merge()
    $summary.Range("A${statsStart}:I${statsStart}").Font.Bold = $true
    $statsHeaders = @('Configuration','Process','Data range','N','Median (s)','Mean (s)','Std. dev. (s)','Minimum (s)','Maximum (s)')
    for($c=0;$c -lt $statsHeaders.Count;++$c){$summary.Cells.Item($statsStart+1,$c+1).Value2=$statsHeaders[$c]}
    $processes = @(
        @('Volume fraction','VF_Total_ms',1000), @('Triangulation','Tri_Total_ms',1000),
        @('Compact Aleph export','Compact_Total_ms',1000), @('SPN export','SPN_Total_ms',1000),
        @('Instrumented Grasshopper','Formula_GH_Total_ms',1000), @('Aleph total','AlephTotal_ms',1000),
        @('Aleph reduction','AlephReducePair_ms',1000), @('Sculpt total','SculptTotalSeconds',1)
    )
    $statsRow = $statsStart + 2
    for($i=0;$i -lt $configs.Count;++$i){
        $dataStart = $dataRanges[$configs[$i].Id].Start
        $dataEnd = $dataRanges[$configs[$i].Id].End
        foreach($proc in $processes){
            $key=$proc[1]; $factor=$proc[2]; $sourceCol=Excel-Column $columnIndex[$key]
            $range = 'Data!${0}${1}:${0}${2}' -f $sourceCol,$dataStart,$dataEnd
            $summary.Cells.Item($statsRow,1).Formula="=Data!B${dataStart}"
            $summary.Cells.Item($statsRow,2).Value2=$proc[0]
            $summary.Cells.Item($statsRow,3).Value2=$range
            $summary.Cells.Item($statsRow,4).Formula="=COUNT(${range})"
            $summary.Cells.Item($statsRow,5).Formula="=IF(D${statsRow}=0,`"`",MEDIAN(${range})/${factor})"
            $summary.Cells.Item($statsRow,6).Formula="=IF(D${statsRow}=0,`"`",AVERAGE(${range})/${factor})"
            $summary.Cells.Item($statsRow,7).Formula="=IF(D${statsRow}<2,`"`",STDEV.S(${range})/${factor})"
            $summary.Cells.Item($statsRow,8).Formula="=IF(D${statsRow}=0,`"`",MIN(${range})/${factor})"
            $summary.Cells.Item($statsRow,9).Formula="=IF(D${statsRow}=0,`"`",MAX(${range})/${factor})"
            $statsRow++
        }
    }
    $summary.Range("A$($statsStart+1):I$($statsStart+1)").Font.Bold=$true
    $summary.Range("A$($statsStart+1):I$($statsStart+1)").Interior.Color=0xD9EAF7
    $summary.Range("E$($statsStart+2):I$($statsRow-1)").NumberFormat='0.000'
    $summary.UsedRange.Columns.AutoFit() | Out-Null
    for($c=1;$c -le 20;++$c){if($summary.Columns.Item($c).ColumnWidth -gt 30){$summary.Columns.Item($c).ColumnWidth=30}}
    $summary.Activate()
    $summary.Application.ActiveWindow.SplitRow = 3
    $summary.Application.ActiveWindow.FreezePanes = $true

    # Audit trail
    $auditHeaders=@('Configuration','Run','Process','Metric','Value','Unit','Source line','Source text','Data key')
    for($c=0;$c -lt $auditHeaders.Count;++$c){$auditSheet.Cells.Item(1,$c+1).Value2=$auditHeaders[$c]}
    $auditRow=2
    foreach($item in $audit){
        Set-ExcelScalar ($auditSheet.Cells.Item($auditRow,1)) $item.Configuration
        Set-ExcelScalar ($auditSheet.Cells.Item($auditRow,2)) $item.Run
        Set-ExcelScalar ($auditSheet.Cells.Item($auditRow,3)) $item.Process
        Set-ExcelScalar ($auditSheet.Cells.Item($auditRow,4)) $item.Metric
        Set-ExcelScalar ($auditSheet.Cells.Item($auditRow,5)) $item.Value
        Set-ExcelScalar ($auditSheet.Cells.Item($auditRow,6)) $item.Unit
        Set-ExcelScalar ($auditSheet.Cells.Item($auditRow,7)) $item.SourceLine
        $auditSheet.Cells.Item($auditRow,8).Formula = "='Source Text'!B$($item.SourceLine)"
        Set-ExcelScalar ($auditSheet.Cells.Item($auditRow,9)) $item.DataKey
        $auditRow++
    }
    $auditSheet.Range('A1:I1').Font.Bold=$true
    $auditSheet.Range('A1:I1').Interior.Color=0xD9EAF7
    $auditSheet.Range($auditSheet.Cells.Item(1,1),$auditSheet.Cells.Item($auditRow-1,9)).AutoFilter() | Out-Null
    $auditSheet.UsedRange.Columns.AutoFit() | Out-Null
    $auditSheet.Columns.Item(8).ColumnWidth=80
    $auditSheet.Activate()
    $auditSheet.Application.ActiveWindow.SplitRow=1
    $auditSheet.Application.ActiveWindow.FreezePanes=$true

    # Exact source text
    $sourceSheet.Cells.Item(1,1).Value2='Source line'
    $sourceSheet.Cells.Item(1,2).Value2='Exact text from Timings_Data.txt'
    for($i=0;$i -lt $lines.Length;++$i){
        $sourceSheet.Cells.Item($i+2,1).Value2=[double]($i+1)
        $sourceSheet.Cells.Item($i+2,2).Value2=$lines[$i]
    }
    $sourceSheet.Range('A1:B1').Font.Bold=$true
    $sourceSheet.Range('A1:B1').Interior.Color=0xD9EAF7
    $sourceSheet.Columns.Item(1).ColumnWidth=12
    $sourceSheet.Columns.Item(2).ColumnWidth=120
    $sourceSheet.Activate()
    $sourceSheet.Application.ActiveWindow.SplitRow=1
    $sourceSheet.Application.ActiveWindow.FreezePanes=$true

    # README
    $readmeRows=@(
        @('Workbook purpose',"Auditable transcription and formula-driven summary of $Domain runtime measurements."),
        @('Source file',$SourcePath),
        @('Generated',([DateTime]::Now.ToString('yyyy-MM-dd HH:mm:ss'))),
        @('Data sheet','One row per configuration/run. Recorded values are transcribed; columns beginning FORMULA are calculated.'),
        @('Summary sheet','Medians and detailed statistics are Excel formulas referencing each configuration''s discovered run range in Data.'),
        @('Audit Trail','Maps every parsed value to its process, metric, original source line, and exact source text.'),
        @('Source Text',"Verbatim copy of every line in $([System.IO.Path]::GetFileName($SourcePath))."),
        @('Units','Raw component and Aleph timings are milliseconds. Sculpt TotalSeconds is seconds. Summary runtimes are seconds.'),
        @('Missing data','Measurements absent from the source remain blank and are never replaced with zero.'),
        @('Summary row order',(($configs | ForEach-Object { $_.Name }) -join '; ')),
        @('Formula policy','No measured value is recomputed. Data formulas only combine recorded totals or calculate shares. Summary formulas use MEDIAN, AVERAGE, STDEV.S, MIN, MAX, and COUNT.'),
        @('Refresh','Excel calculation mode is automatic. Press Ctrl+Alt+F9 to force a complete recalculation if needed.')
    )
    $readme.Cells.Item(1,1).Value2='Item';$readme.Cells.Item(1,2).Value2='Details'
    for($i=0;$i -lt $readmeRows.Count;++$i){$readme.Cells.Item($i+2,1).Value2=$readmeRows[$i][0];$readme.Cells.Item($i+2,2).Value2=$readmeRows[$i][1]}
    $readme.Range('A1:B1').Font.Bold=$true
    $readme.Range('A1:B1').Interior.Color=0xD9EAF7
    $readme.Columns.Item(1).ColumnWidth=22
    $readme.Columns.Item(2).ColumnWidth=120
    $readme.Range("B1:B$($readmeRows.Count+1)").WrapText=$true

    $excel.Calculation = -4105 # xlCalculationAutomatic
    $workbook.ForceFullCalculation = $true
    $excel.CalculateFullRebuild()
    $workbook.CheckCompatibility = $false

    if (Test-Path -LiteralPath $OutputPath) { Remove-Item -LiteralPath $OutputPath -Force }
    $workbook.SaveAs($OutputPath, 51)
    $workbook.Close($true)
    $excel.Quit()
    $workbook=$null; $excel=$null
}
finally {
    if($null -ne $workbook){try{$workbook.Close($false)}catch{}}
    if($null -ne $excel){try{$excel.Quit()}catch{}}
    [gc]::Collect(); [gc]::WaitForPendingFinalizers()
}

Write-Output "Created $OutputPath"
Write-Output "Records: $($allRecords.Count); audit entries: $($audit.Count); source lines: $($lines.Length)"
