$ErrorActionPreference = 'Stop'

# Send a bounded HTTP/1.1 request to the isolated local review host.
function Send-ProbeRequest([string]$Path, [byte[]]$Body, [bool]$Chunked) {
    $connection = [System.Net.Sockets.TcpClient]::new('127.0.0.1', 5129)
    try {
        $stream = $connection.GetStream()
        $stream.ReadTimeout = 15000
        $lengthHeader = if ($Chunked) { 'Transfer-Encoding: chunked' } else { 'Content-Length: ' + $Body.Length }
        $headers = "POST $Path HTTP/1.1`r`nHost: 127.0.0.1:5129`r`nConnection: close`r`nContent-Type: multipart/form-data; boundary=probe-boundary`r`n$lengthHeader`r`n`r`n"
        $stream.Write([Text.Encoding]::ASCII.GetBytes($headers))
        if ($Chunked) { $stream.Write([Text.Encoding]::ASCII.GetBytes($Body.Length.ToString('x') + "`r`n")) }
        $stream.Write($Body)
        if ($Chunked) { $stream.Write([Text.Encoding]::ASCII.GetBytes("`r`n0`r`n`r`n")) }
        $stream.Flush()
        $reader = [IO.StreamReader]::new($stream)
        $response = $reader.ReadToEnd()
        return [ordered]@{ path = $Path; chunked = $Chunked; requestBytes = $Body.Length; response = $response }
    }
    finally { $connection.Dispose() }
}

# Build a small two-field export payload while keeping Office bytes binary.
function New-ExportBody([byte[]]$OfficeBytes) {
    $buffer = [IO.MemoryStream]::new()
    try {
        $buffer.Write([Text.Encoding]::ASCII.GetBytes("--probe-boundary`r`nContent-Disposition: form-data; name=`"file`"; filename=`"source.docx`"`r`nContent-Type: application/octet-stream`r`n`r`n"))
        $buffer.Write($OfficeBytes)
        $buffer.Write([Text.Encoding]::ASCII.GetBytes("`r`n--probe-boundary`r`nContent-Disposition: form-data; name=`"translatedTexts`"`r`n`r`n[`"Changed`",`"Two`"]`r`n--probe-boundary--`r`n"))
        return ,$buffer.ToArray()
    }
    finally { $buffer.Dispose() }
}

$largePart = 'x' * 2600
$multipart = "--probe-boundary`r`nContent-Disposition: form-data; name=`"file`"; filename=`"source.txt`"`r`n`r`nHello`r`n--probe-boundary`r`nContent-Disposition: form-data; name=`"extra1`"`r`n`r`n$largePart`r`n--probe-boundary`r`nContent-Disposition: form-data; name=`"extra2`"`r`n`r`n$largePart`r`n--probe-boundary--`r`n"
$payload = [Text.Encoding]::ASCII.GetBytes($multipart)
$observations = [Collections.Generic.List[object]]::new()
$observations.Add((Send-ProbeRequest '/import' $payload $false))
$observations.Add((Send-ProbeRequest '/import' $payload $true))
$office = [IO.File]::ReadAllBytes((Join-Path $PSScriptRoot 'artifacts/http-source.docx'))
$observations.Add((Send-ProbeRequest '/export' (New-ExportBody $office) $false))

# Duplicate only a synthetic relationship identifier to test error redaction.
$corrupt = [IO.MemoryStream]::new()
$corrupt.Write($office)
$archive = [IO.Compression.ZipArchive]::new($corrupt, [IO.Compression.ZipArchiveMode]::Update, $true)
$entry = $archive.GetEntry('_rels/.rels')
$entryReader = [IO.StreamReader]::new($entry.Open())
[xml]$rels = $entryReader.ReadToEnd()
$entryReader.Dispose()
$relationship = $rels.DocumentElement.FirstChild
$relationship.SetAttribute('Id', 'SENSITIVE_SENTINEL')
$null = $rels.DocumentElement.AppendChild($relationship.CloneNode($true))
$entry.Delete()
$newEntry = $archive.CreateEntry('_rels/.rels')
$writer = [IO.StreamWriter]::new($newEntry.Open(), [Text.UTF8Encoding]::new($false))
$writer.Write($rels.OuterXml)
$writer.Dispose()
$archive.Dispose()
$observations.Add((Send-ProbeRequest '/export' (New-ExportBody $corrupt.ToArray()) $false))
$corrupt.Dispose()
$observations | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'http-observations.json') -Encoding utf8
$observations | ConvertTo-Json -Depth 6
