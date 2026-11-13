# Workbook helpers used only on temporary export inputs. Editable workbooks stay untouched.
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Copy-SharedFile([string]$Source, [string]$Destination) {
    $inputStream = [IO.File]::Open($Source, 'Open', 'Read', ([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
    try {
        $outputStream = [IO.File]::Create($Destination)
        try { $inputStream.CopyTo($outputStream) } finally { $outputStream.Dispose() }
    } finally { $inputStream.Dispose() }
}

function Read-WorkbookXml($Book, [string]$Name) {
    $entry = $Book.Zip.GetEntry($Name)
    if ($null -eq $entry) { throw "Missing workbook part: $Name" }
    $reader = [IO.StreamReader]::new($entry.Open())
    try { return [xml]$reader.ReadToEnd() } finally { $reader.Dispose() }
}

function Open-ConfigWorkbook([string]$Path) {
    $book = @{ Zip = [IO.Compression.ZipFile]::Open($Path, 'Update'); Shared = @(); Sheets = @{} }
    try {
        if ($null -ne $book.Zip.GetEntry('xl/sharedStrings.xml')) {
            $strings = Read-WorkbookXml $book 'xl/sharedStrings.xml'
            $book.Shared = @($strings.SelectNodes('//*[local-name()="si"]') | ForEach-Object {
                ($_.SelectNodes('.//*[local-name()="t"]') | ForEach-Object { $_.InnerText }) -join ''
            })
        }
        $xml = Read-WorkbookXml $book 'xl/workbook.xml'
        $rels = Read-WorkbookXml $book 'xl/_rels/workbook.xml.rels'
        foreach ($sheet in $xml.SelectNodes('//*[local-name()="sheet"]')) {
            $rid = $sheet.GetAttribute('id', 'http://schemas.openxmlformats.org/officeDocument/2006/relationships')
            $rel = $rels.SelectSingleNode('//*[local-name()="Relationship"][@Id="' + $rid + '"]')
            $target = $rel.GetAttribute('Target')
            $book.Sheets[$sheet.GetAttribute('name')] = if ($target.StartsWith('/')) { $target.TrimStart('/') } else { 'xl/' + $target }
        }
        return $book
    } catch { $book.Zip.Dispose(); throw }
}

function Write-WorkbookXml($Book, [string]$Name, $Xml) {
    $Book.Zip.GetEntry($Name).Delete()
    $entry = $Book.Zip.CreateEntry($Name, [IO.Compression.CompressionLevel]::Optimal)
    $settings = [Xml.XmlWriterSettings]::new()
    $settings.Encoding = [Text.UTF8Encoding]::new($false)
    $stream = $entry.Open()
    $writer = [Xml.XmlWriter]::Create($stream, $settings)
    try { $Xml.Save($writer) } finally { $writer.Dispose(); $stream.Dispose() }
}

function Get-ColumnIndex([string]$Address) {
    $n = 0
    foreach ($c in ($Address -replace '\d', '').ToCharArray()) { $n = $n * 26 + [int]$c - 64 }
    return $n
}

function Get-ColumnName([int]$Index) {
    $name = ''
    while ($Index -gt 0) { $Index--; $name = [char](65 + $Index % 26) + $name; $Index = [int][Math]::Floor($Index / 26) }
    return $name
}

function Get-CellText($Book, $Cell) {
    if ($null -eq $Cell) { return '' }
    if ($Cell.GetAttribute('t') -eq 'inlineStr') {
        return (($Cell.SelectNodes('.//*[local-name()="t"]') | ForEach-Object { $_.InnerText }) -join '')
    }
    $value = $Cell.SelectSingleNode('./*[local-name()="v"]')
    if ($null -eq $value) { return '' }
    if ($Cell.GetAttribute('t') -eq 's') { return $Book.Shared[[int]$value.InnerText] }
    return $value.InnerText
}

function New-TextCell($Xml, [string]$Address, $Value) {
    $ns = 'http://schemas.openxmlformats.org/spreadsheetml/2006/main'
    $cell = $Xml.CreateElement('c', $ns)
    $cell.SetAttribute('r', $Address)
    if ($Value -is [bool]) {
        $cell.SetAttribute('t', 'b')
        $node = $Xml.CreateElement('v', $ns)
        $node.InnerText = if ($Value) { '1' } else { '0' }
        [void]$cell.AppendChild($node)
    } else {
        $cell.SetAttribute('t', 'inlineStr')
        $node = $Xml.CreateElement('is', $ns)
        $textNode = $Xml.CreateElement('t', $ns)
        $textNode.InnerText = [string]$Value
        [void]$node.AppendChild($textNode)
        [void]$cell.AppendChild($node)
    }
    return $cell
}

function Get-FieldMap($Book, $Sheet) {
    $map = [ordered]@{}
    foreach ($cell in $Sheet.SelectNodes('//*[local-name()="row"][@r="1"]/*[local-name()="c"]')) {
        $column = Get-ColumnIndex $cell.GetAttribute('r')
        $name = Get-CellText $Book $cell
        if ($column -gt 1 -and $name -ne '') {
            if ($map.Contains($name)) { throw "Duplicate field: $name" }
            $map[$name] = $column
        }
    }
    return $map
}

function Get-RowValues($Book, $Row, $Fields) {
    $values = @{}
    foreach ($name in $Fields.Keys) {
        $address = (Get-ColumnName $Fields[$name]) + $Row.GetAttribute('r')
        $values[$name] = Get-CellText $Book $Row.SelectSingleNode('./*[local-name()="c"][@r="' + $address + '"]')
    }
    return $values
}

function Join-LevelValue($Value, [int]$Count) {
    $text = if ($Value -is [bool]) { $Value.ToString().ToLowerInvariant() } else { [string]$Value }
    if ($text -eq '') { return '' }
    return ((1..$Count | ForEach-Object { $text }) -join '|')
}

function Get-ShopFixedValues($Values) {
    # Program-owned rules from the seven Buff specifications, never editable Excel columns.
    $rules = @{
        SpawnTile = @('ColoredGlassBreak', 'HeldBuff', $true, 'NormalBall', $false, $false, $false, $false, '', 'BattleEnd')
        OpeningBallSpeedBonus = @('BattleStart', 'HeldBuff', $true, 'NormalBall', $false, $false, $false, $false, 'Additive', 'Duration')
        ComboBallSpeedBonus = @('BattleStart', 'HeldBuff', $true, 'NormalBall', $true, $false, $false, $false, 'Additive', 'ComboClear')
        ComboRobotDamageBonus = @('BattleStart', 'HeldBuff', $true, 'RobotDamage', $true, $true, $true, $false, '', 'ComboClear')
        GlassDurabilityOverride = @('BattleStart', 'HeldBuff', $true, 'NormalBall', $false, $false, $false, $false, '', 'BattleEnd')
        ExtraBerserkBall = @('BattleStart', 'HeldBuff', $true, 'BerserkBall', $false, $false, $false, $false, '', 'BerserkEnd')
        AddStamina = @('OnPurchase', 'InstantReward', $false, 'PlayerStamina', $false, $false, $false, $true, '', 'Immediate')
    }
    $key = $Values['behaviorKey']
    if (!$rules.ContainsKey($key)) { throw "Unknown shop behaviorKey: $key" }
    $count = [int]$Values['maxLevel']
    if ($count -lt 1 -or $count -gt 3) { throw "Unsupported shop maxLevel: $count ($key)" }
    $rule = $rules[$key]
    $caps = ''
    if ($key -in @('ComboBallSpeedBonus', 'ComboRobotDamageBonus')) {
        # Only this bonus is capped. This does not cap final ball speed or final damage.
        $caps = (@('0.3', '0.6', '0.9')[0..($count - 1)]) -join '|'
    }
    return @{
        activationType = $rule[0]; grantType = $rule[1]; occupiesAssistSlot = $rule[2]; targetType = $rule[3]
        resetOnComboClear = (Join-LevelValue $rule[4] $count); affectsSkillDamage = (Join-LevelValue $rule[5] $count)
        affectsDotDamage = (Join-LevelValue $rule[6] $count); allowStaminaOverflow = (Join-LevelValue $rule[7] $count)
        speedStackRules = (Join-LevelValue $rule[8] $count); endConditions = (Join-LevelValue $rule[9] $count)
        comboBonusCaps = $caps
    }
}

function Expand-ConfigSheet($Book, [string]$SchemaSheet, [string]$InputSheet, [scriptblock]$FixedValues) {
    if (!$Book.Sheets.ContainsKey($SchemaSheet) -or !$Book.Sheets.ContainsKey($InputSheet)) { throw "Missing sheet: $SchemaSheet / $InputSheet" }
    $schema = Read-WorkbookXml $Book $Book.Sheets[$SchemaSheet]
    $sheet = Read-WorkbookXml $Book $Book.Sheets[$InputSheet]
    $fields = Get-FieldMap $Book $schema
    $inputFields = Get-FieldMap $Book $sheet
    foreach ($name in $inputFields.Keys) { if (!$fields.Contains($name)) { throw "Unknown $InputSheet field: $name" } }
    $data = $sheet.SelectSingleNode('//*[local-name()="sheetData"]')
    foreach ($row in @($data.SelectNodes('./*[local-name()="row"]'))) {
        $rowNumber = [int]$row.GetAttribute('r')
        $marker = Get-CellText $Book $row.SelectSingleNode('./*[local-name()="c"][@r="A' + $rowNumber + '"]')
        if ($rowNumber -le 4) {
            # Robot's first sheet has an extra empty ##var row. Match metadata by marker.
            $template = @($schema.SelectNodes('//*[local-name()="sheetData"]/*[local-name()="row"]') | Where-Object {
                $firstCell = $_.SelectSingleNode('./*[local-name()="c"][1]')
                (Get-CellText $Book $firstCell) -eq $marker
            })[0]
            $replacement = $sheet.ImportNode($template, $true)
            $replacement.SetAttribute('r', [string]$rowNumber)
            foreach ($cell in $replacement.SelectNodes('./*[local-name()="c"]')) {
                $cell.SetAttribute('r', (($cell.GetAttribute('r') -replace '\d', '') + $rowNumber))
            }
            [void]$data.ReplaceChild($replacement, $row)
            continue
        }
        $values = Get-RowValues $Book $row $inputFields
        if ($marker.StartsWith('##') -or $values['id'] -eq '') { continue }
        $fixed = & $FixedValues $values
        $cells = @{}
        foreach ($name in $inputFields.Keys) {
            $address = (Get-ColumnName $inputFields[$name]) + $rowNumber
            $cells[$name] = $row.SelectSingleNode('./*[local-name()="c"][@r="' + $address + '"]')
        }
        foreach ($cell in @($row.SelectNodes('./*[local-name()="c"]'))) { [void]$row.RemoveChild($cell) }
        foreach ($name in $fields.Keys) {
            $address = (Get-ColumnName $fields[$name]) + $rowNumber
            if ($fixed.ContainsKey($name)) { $cell = New-TextCell $sheet $address $fixed[$name] }
            elseif ($inputFields.Contains($name)) {
                $cell = $cells[$name]
                if ($null -eq $cell) { $cell = New-TextCell $sheet $address '' }
                $cell.SetAttribute('r', $address)
            } else { throw "No export default for $InputSheet.$name" }
            [void]$row.AppendChild($cell)
        }
    }
    $lastRow = ($data.SelectNodes('./*[local-name()="row"]') | ForEach-Object { [int]$_.GetAttribute('r') } | Measure-Object -Maximum).Maximum
    foreach ($element in @('dimension', 'autoFilter')) {
        $node = $sheet.SelectSingleNode('//*[local-name()="' + $element + '"]')
        if ($null -ne $node) { $node.SetAttribute('ref', 'A1:' + (Get-ColumnName ($fields.Count + 1)) + $lastRow) }
    }
    Write-WorkbookXml $Book $Book.Sheets[$InputSheet] $sheet
}

function Prepare-ConfigWorkbooks([string]$DataDir) {
    $buff = Open-ConfigWorkbook (Join-Path $DataDir 'buff.xlsx')
    try { Expand-ConfigSheet $buff '技能Buff' '商店效果' { param($values) Get-ShopFixedValues $values } }
    finally { $buff.Zip.Dispose() }

    $shapes = @{}
    $shapeBook = Open-ConfigWorkbook (Join-Path $DataDir 'robot_shape.xlsx')
    try {
        foreach ($part in $shapeBook.Sheets.Values) {
            $sheet = Read-WorkbookXml $shapeBook $part
            $fields = Get-FieldMap $shapeBook $sheet
            foreach ($row in $sheet.SelectNodes('//*[local-name()="row"]')) {
                if ([int]$row.GetAttribute('r') -le 5) { continue }
                $values = Get-RowValues $shapeBook $row $fields
                if ($values['id'] -eq '') { continue }
                $xs = @(); $ys = @()
                foreach ($pair in $values['cellOffsets'].Split(';')) {
                    $xy = $pair.Split(',')
                    if ($xy.Count -ne 2) { throw "Invalid shape coordinates: $pair" }
                    $xs += [int]$xy[0]; $ys += [int]$xy[1]
                }
                $x = $xs | Measure-Object -Minimum -Maximum; $y = $ys | Measure-Object -Minimum -Maximum
                $shapes[$values['id']] = @([int]($x.Maximum - $x.Minimum + 1), [int]($y.Maximum - $y.Minimum + 1))
            }
        }
    } finally { $shapeBook.Zip.Dispose() }

    $robot = Open-ConfigWorkbook (Join-Path $DataDir 'robot.xlsx')
    try {
        Expand-ConfigSheet $robot 'Sheet1' 'Sheet2' {
            param($values)
            $shape = $shapes[$values['shapeId']]
            if ($null -eq $shape) { throw "Missing tile shape: $($values['shapeId'])" }
            # Sheet2 is the speed-strip catalog. Classification is independent of numeric IDs.
            return @{
                qualityId = 0; skillId = 0; minTriggerInterval = 0; attackDamage = 0; damagePerLevel = 0; robotPortrait = ''
                entityType = 'EffectTile'; tileCanRobotCover = $false; tileEffectKey = 'ball_speed_bonus'
                tileEndCondition = 'Duration'; tileStackRule = 'Refresh'; tileEnabled = $false
                tileTargetType = 'NormalBall'; tileSpeedStackRule = 'Additive'; tileLifetimeEndCondition = 'BattleEnd'
                tileSpawnLocation = 'BrokenGlassCell'; tileWidth = $shape[0]; tileHeight = $shape[1]; tilePassThroughBall = $true
            }
        }
    } finally { $robot.Zip.Dispose() }
}
