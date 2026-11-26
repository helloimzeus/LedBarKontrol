$ErrorActionPreference = 'Stop'
$sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\UnionMetadata'
$facades = Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\Facades'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$gac = Join-Path $env:WINDIR 'Microsoft.NET\assembly\GAC_MSIL\System.Runtime.WindowsRuntime'

# Birden fazla SDK kuruluysa Windows.winmd iceren en yeni surumu kullan.
$metadata = Get-ChildItem -LiteralPath $sdkRoot -Directory |
    Where-Object { $_.Name -match '^\d+(\.\d+){3}$' } |
    Sort-Object { [version]$_.Name } -Descending |
    ForEach-Object { Join-Path $_.FullName 'Windows.winmd' } |
    Where-Object { Test-Path -LiteralPath $_ } |
    Select-Object -First 1
$runtime = Get-ChildItem -LiteralPath $gac -Recurse -Filter System.Runtime.WindowsRuntime.dll |
    Select-Object -First 1
if (!$metadata -or !$runtime -or !(Test-Path -LiteralPath $facades)) {
    throw 'Windows SDK ve .NET Framework 4.8 Developer Pack kurulumunu kontrol edin.'
}

Push-Location $PSScriptRoot
try {
    New-Item -Path build -ItemType Directory -Force | Out-Null
    $references = @(
        $metadata
        $runtime.FullName
        (Join-Path $facades 'System.Runtime.dll')
        (Join-Path $facades 'System.Runtime.InteropServices.WindowsRuntime.dll')
        (Join-Path $facades 'System.Collections.dll')
        'Accessibility.dll'
    ) | ForEach-Object { "/reference:$_" }
    $sources = 'LedKumanda.cs', 'Studio.cs', 'Spectrum.cs', 'Crystal.cs', 'LedOutput.cs'
    & $compiler /nologo /codepage:65001 /target:winexe /optimize+ /win32icon:LedKumanda.ico /out:build\LedKumanda.exe @references @sources
    if ($LASTEXITCODE -ne 0) { throw 'Derleme tamamlanamadi.' }
} finally {
    Pop-Location
}
