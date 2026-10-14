# Official portable Inno Setup compiler, pinned by SHA-256 and publisher.
$ErrorActionPreference = 'Stop'
$cache = Join-Path $PSScriptRoot 'cache\inno\7.1.0'
$download = Join-Path $cache 'innosetup-7.1.0-x64.exe'
$compilerDirectory = Join-Path $cache 'compiler'
$expectedHash = '0362A383ED217D4C4239B5933866DD96D3EB2102737DA92F80F6057A4B40DF2F'
New-Item -ItemType Directory -Force -Path $cache | Out-Null
if (!(Test-Path -LiteralPath $download)) {
    Invoke-WebRequest -Uri 'https://github.com/jrsoftware/issrc/releases/download/is-7_1_0/innosetup-7.1.0-x64.exe' -OutFile $download
}
if ((Get-FileHash -LiteralPath $download).Hash -ne $expectedHash) { throw 'Pinned compiler hash mismatch.' }
$signature = Get-AuthenticodeSignature -LiteralPath $download
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch '^CN=Pyrsys B.V.,') { throw 'Unexpected installer compiler publisher.' }
if (!(Test-Path -LiteralPath (Join-Path $compilerDirectory 'ISCC.exe'))) {
    $process = Start-Process -FilePath $download -ArgumentList @('/PORTABLE=1','/CURRENTUSER','/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/DIR="'+$compilerDirectory+'"')) -WindowStyle Hidden -PassThru
    if (!$process.WaitForExit(30000) -or $process.ExitCode -ne 0) { throw 'Portable compiler extraction failed.' }
}
foreach ($file in @('ISCC.exe','ISCmplr.dll','ISPP.dll')) {
    $signature = Get-AuthenticodeSignature -LiteralPath (Join-Path $compilerDirectory $file)
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch '^CN=Pyrsys B.V.,') { throw "Unexpected compiler signature: $file" }
}
Write-Output (Join-Path $compilerDirectory 'ISCC.exe')
