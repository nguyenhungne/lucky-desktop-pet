# Builds Lucky.exe with the C# compiler that ships with Windows (.NET Framework 4.x). No SDK needed.
# Sprite sheets (assets/*.png) and their timing table (assets/anims.txt) are embedded into the exe.
# Regenerate sprites with:
#   python art/render_sprites.py assets          # base poses
#   python art/actions.py assets                 # feature animations (parallel)
$ErrorActionPreference = 'Stop'
$csc = Get-ChildItem "$env:WINDIR\Microsoft.NET\Framework64\v4*\csc.exe" | Sort-Object FullName | Select-Object -Last 1
if (-not $csc) { throw 'csc.exe not found (.NET Framework 4.x is required).' }
$out = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force $out | Out-Null
$assets = Join-Path $PSScriptRoot 'assets'
$res = @(Get-ChildItem (Join-Path $assets '*.png') | ForEach-Object { "/resource:$($_.FullName),$($_.Name)" })
if (-not $res) { throw 'No sprites in assets/. Run the art scripts first.' }
if (Test-Path (Join-Path $assets 'anims.txt')) { $res += "/resource:$(Join-Path $assets 'anims.txt'),anims.txt" }
$src = Get-ChildItem (Join-Path $PSScriptRoot 'src') -Recurse -Filter *.cs | ForEach-Object { $_.FullName }
& $csc.FullName /nologo /target:winexe /codepage:65001 /optimize+ /warn:1 `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll @res `
    "/out:$out\Lucky.exe" @src
if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)" }
"Built $out\Lucky.exe"
