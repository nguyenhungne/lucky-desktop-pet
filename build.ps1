# Builds Lucky.exe with the C# compiler that ships with Windows (.NET Framework 4.x). No SDK needed.
$ErrorActionPreference = 'Stop'
$csc = Get-ChildItem "$env:WINDIR\Microsoft.NET\Framework64\v4*\csc.exe" | Sort-Object FullName | Select-Object -Last 1
if (-not $csc) { throw 'csc.exe not found (.NET Framework 4.x is required).' }
$out = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force $out | Out-Null
& $csc.FullName /nologo /target:winexe /codepage:65001 /optimize+ `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll `
    "/out:$out\Lucky.exe" (Join-Path $PSScriptRoot 'src\Lucky.cs')
if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)" }
"Built $out\Lucky.exe"
