$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe"
}
if (-not (Test-Path $csc)) {
    Write-Error "csc.exe not found (.NET Framework 4 required)."
    exit 1
}

$sources = Get-ChildItem -Path $PSScriptRoot -Filter "*.cs" | ForEach-Object { $_.FullName }
$out = Join-Path $PSScriptRoot "CapsIndicator.exe"

& $csc /nologo /target:winexe /out:$out `
    /reference:System.dll `
    /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll `
    $sources

if ($LASTEXITCODE -eq 0) {
    Write-Host "Built: $out"
} else {
    Write-Error "Build failed."
}
