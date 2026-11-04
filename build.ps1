# Builds Lucky.exe with the C# compiler that ships with Windows (.NET Framework 4.x). No SDK needed.
# Sprite sheets in assets/ are embedded into the exe. Regenerate them with:
#   python art/render_sprites.py assets --preview docs/sprites.png
$ErrorActionPreference = 'Stop'
$csc = Get-ChildItem "$env:WINDIR\Microsoft.NET\Framework64\v4*\csc.exe" | Sort-Object FullName | Select-Object -Last 1
if (-not $csc) { throw 'csc.exe not found (.NET Framework 4.x is required).' }
$out = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force $out | Out-Null
$res = Get-ChildItem (Join-Path $PSScriptRoot 'assets\*.png') | ForEach-Object { "/resource:$($_.FullName),$($_.Name)" }
if (-not $res) { throw 'No sprites in assets/. Run art/render_sprites.py first.' }
& $csc.FullName /nologo /target:winexe /codepage:65001 /optimize+ `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll @res `
    "/out:$out\Lucky.exe" (Join-Path $PSScriptRoot 'src\Lucky.cs')
if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)" }
"Built $out\Lucky.exe"
