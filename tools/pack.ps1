# 把编译好的插件打成 cipx（ClassIsland 插件包）
# 用法：在仓库根目录执行  pwowershell -File tools/pack.ps1

$ErrorActionPreference = 'Stop'

# icon.png 由 CI 从 base64 恢复；本地如果没有就从 base64 生成一份
if (-not (Test-Path -LiteralPath 'icon.png') -and (Test-Path -LiteralPath 'assets/icon.png.b64')) {
    $b64 = Get-Content -Raw 'assets/icon.png.b64'
    [IO.File]::WriteAllBytes('icon.png', [Convert]::FromBase64String($b64.Trim()))
}

dotnet build -c Release

$out = 'bin/Release/net8.0-windows'
$dist = 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null

Copy-Item "$out/ButtonIsland.dll" $dist -Force
Copy-Item "$out/ButtonIsland.deps.json" $dist -Force
Copy-Item 'manifest.yml' $dist -Force
Copy-Item 'icon.png' $dist -Force

Compress-Archive -Path "$dist/*" -DestinationPath 'ButtonIsland.cipx' -Force
Write-Output ('已生成 ButtonIsland.cipx（' + (Get-Item 'ButtonIsland.cipx').Length + ' 字节）')
