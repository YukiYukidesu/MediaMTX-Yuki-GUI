<#
    MediaMTX-Yuki-GUI 打包脚本

    产出两个 zip，解压即用：
      full   —— 自带 .NET 运行时，目标机什么都不用装
      light  —— 外挂 .NET 运行时，目标机要先装 .NET 10 运行时（Runtime，不是 Desktop Runtime）

    两个包里的应用文件完全相同（exe / dll / deps.json 逐个比对哈希），
    差别只有目录里带不带 runtime.dotnet\。

    用法：
      .\build-release.ps1                     正式包，完整跑一遍（含 restore）
      .\build-release.ps1 -NoRestore          正式包，依赖包没变化时跳过 restore
      .\build-release.ps1 -NoRestore -Label test
                                               临时测试包，文件名带 -test

    -Label 是给「自己临时验一下」用的：包名变成
    MediaMTX-Yuki-GUI-v0.1.0-test-full.zip，并且**只清自己那一份旧产物**，
    不会顺手把 dist\ 里的正式包删掉。不带 -Label 时行为与以前完全一样
    （整目录清空后重出两个正式包）。
#>

param(
    [switch]$NoRestore,
    [string]$Label = ''
)

$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot


# ====== 路径与工具 ======

$projectDir = Join-Path $PSScriptRoot 'src\MediaMTX-Yuki-GUI'
$csprojPath = Join-Path $projectDir 'MediaMTX-Yuki-GUI.csproj'
$distDir    = Join-Path $PSScriptRoot 'dist'
$stageRoot  = Join-Path $distDir $(if ($Label) { '_staging_' + $Label } else { '_staging' })

# dotnet 不一定在 PATH 里（从任务计划、资源管理器双击、别的 IDE 里调过来都可能没有），
# 先按 PATH 找，找不到再试默认安装位置。
$dotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnetCmd) {
    $fallback = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
    if (Test-Path -LiteralPath $fallback) {
        $dotnetCmd = $fallback
    }
}

if (-not $dotnetCmd) {
    throw '找不到 dotnet，请确认 .NET SDK 已安装'
}

$dotnetExe = $dotnetCmd.Source
if (-not $dotnetExe) { $dotnetExe = [string]$dotnetCmd }


# ====== 版本号（只从 csproj 读，避免两处维护）======

$versionNode = Select-Xml -LiteralPath $csprojPath -XPath '//Version'
if (-not $versionNode) { throw 'csproj 里没有 <Version>，无法确定版本号' }

$version     = $versionNode.Node.InnerText.Trim()
$versionText = 'v' + $version
$packageName = 'MediaMTX-Yuki-GUI-' + $versionText

# -Label 只影响文件名与清理范围，不动 exe 内容：临时包和正式包是同一份程序，
# 区别仅在于「你知道它是临时打的」。这样临时验完之后，dist\ 里的正式包
# 还是上一次正式打包的那个，不会被顺手覆盖掉。
if ($Label) {
    if ($Label -notmatch '^[A-Za-z0-9._-]+$') {
        throw ('-Label 只能是字母、数字、点、下划线、连字符：' + $Label)
    }
    $packageName = $packageName + '-' + $Label
    Write-Host ('版本号 ' + $versionText + '（临时测试包 -' + $Label + '）') -ForegroundColor Cyan
}
else {
    Write-Host ('版本号 ' + $versionText) -ForegroundColor Cyan
}


# ====== 收掉旧进程与旧产物 ======

# 程序在跑的时候 exe 是锁着的，publish 会报 MSB3027
Get-Process -Name 'MediaMTX-Yuki-GUI' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 300

if ($Label) {
    # 临时包只清自己那一份：dist\ 里可能已经躺着正式包，绝不能一起删掉
    if (-not (Test-Path -LiteralPath $distDir)) {
        New-Item -ItemType Directory -Path $distDir -Force | Out-Null
    }
    Get-ChildItem -LiteralPath $distDir -Filter ($packageName + '-*.zip') -File -ErrorAction SilentlyContinue |
        Remove-Item -Force
}
elseif (Test-Path -LiteralPath $distDir) {
    Remove-Item -LiteralPath $distDir -Recurse -Force
}

New-Item -ItemType Directory -Path $stageRoot -Force | Out-Null


# ====== restore ======

# 必须带 RID：publish -r win-x64 需要 net10.0/win-x64 那一份还原结果
if (-not $NoRestore) {
    Write-Host 'restore (win-x64)' -ForegroundColor Cyan
    & $dotnetExe restore $csprojPath -r win-x64 -v minimal
    if ($LASTEXITCODE -ne 0) { throw 'restore 失败' }
}


# ====== publish ======
#
# 发的是单文件版本：托管 dll 与 native 库全部焊进 MediaMTX-Yuki-GUI.exe，
# 根目录只剩一个 exe，加上 Languages\。
#
# 曾经发过多文件版（33 个 dll 摊在根目录，或收进 runtime.core\ 再改写
# deps.json 的 localPath），最后换成单文件，理由是文件树干净——多文件省不下内存：
# 空窗关闭后实测单文件 3.5 MB、多文件 4.3 MB，空闲回收（MemoryTrimmer）一跑
# 两者没有区别，之前「单文件更吃内存」的结论是没有回收时的现象。
#
# 单文件的代价：首次启动要把 native 库解压到 %TEMP%\.net\<应用名>\<哈希>\，
# 每次 exe 变化都会多留一份 18 MB 的缓存，首启比二启慢一秒左右。
# -p:IncludeNativeLibrariesForSelfExtract=true 是必须的，否则 libSkiaSharp.dll
# 这类 native 库会散落在 exe 旁边，文件树又乱回去了。
#
# 一次 publish 就够——两个包共用同一份 exe，差别只在
# 「旁边的 runtime.dotnet\ 在不在」。

$publishBase = @(
    'publish', $csprojPath,
    '-c', 'Release',
    '-r', 'win-x64',
    '--no-restore',
    '-v', 'minimal'
)

$publishArgs = $publishBase + @(
    '--self-contained', 'false',
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true'
)

$appDir   = Join-Path $stageRoot '_app'
$fullDir  = Join-Path $stageRoot ($packageName + '-full')
$lightDir = Join-Path $stageRoot ($packageName + '-light')

Write-Host 'publish app（单文件，依赖运行时）' -ForegroundColor Cyan
& $dotnetExe @($publishArgs + @('-o', $appDir))
if ($LASTEXITCODE -ne 0) { throw 'publish 失败' }


# ====== 组装两个包 ======
#
# _app 里只有 MediaMTX-Yuki-GUI.exe 与 Languages\ 两项，直接复制两遍。
# MediaMTX\ 不在包里：引擎由程序自己去 GitHub 下载，目录也在那时才建。
# full 版之后会多一个 runtime.dotnet\（下一段），light 版没有。

Write-Host '组装两个包' -ForegroundColor Cyan
New-Item -ItemType Directory -Path $fullDir  -Force | Out-Null
New-Item -ItemType Directory -Path $lightDir -Force | Out-Null
Copy-Item -Path (Join-Path $appDir '*') -Destination $lightDir -Recurse -Force
Copy-Item -Path (Join-Path $appDir '*') -Destination $fullDir  -Recurse -Force

$appItems = @(Get-ChildItem -LiteralPath $appDir -Force | ForEach-Object { $_.Name })
Write-Host ('  publish 输出 ' + $appItems.Count + ' 项：' + ($appItems -join ' / ')) -ForegroundColor Green


# ====== 把 .NET 运行时收进 runtime.dotnet\（只给 full 版）======
#
# apphost 在 exe 旁边找 runtime.dotnet\（csproj 的 AppHostRelativeDotNet），
# 认的是标准 .NET 安装布局：runtime.dotnet\host\fxr\<版本>\hostfxr.dll 与
# runtime.dotnet\shared\Microsoft.NETCore.App\<版本>\*。
# 找不到就按 AppHostDotNetSearch 回落到系统装的 .NET——light 版正是靠这一点。
#
# 运行时文件从「本机真实安装的 .NET」整目录拷。原因：hostfxr 判断一个目录
# 是不是「有效框架版本」要看三个元数据文件——.version、
# Microsoft.NETCore.App.deps.json、Microsoft.NETCore.App.runtimeconfig.json，
# 而这三个东西既不在 NuGet 包里，也不在 self-contained 的 publish 输出里。
# 少了它们 apphost 会报 No frameworks were found.，然后弹
# 「You must install or update .NET」——装了 .NET 也起不来，还看不出来为什么。

$rtProbe = Join-Path $stageRoot '_probe'
& $dotnetExe @($publishBase + @('--self-contained', 'true', '-o', $rtProbe))
if ($LASTEXITCODE -ne 0) { throw 'publish（探针）失败' }

$probeJson  = Get-Content -LiteralPath (Join-Path $rtProbe 'MediaMTX-Yuki-GUI.runtimeconfig.json') -Raw | ConvertFrom-Json
$runtimeVer = $probeJson.runtimeOptions.includedFrameworks[0].version
$tfm        = $probeJson.runtimeOptions.tfm
Remove-Item -LiteralPath $rtProbe -Recurse -Force

$dotnetRoot = if ($env:ProgramFiles) { Join-Path $env:ProgramFiles 'dotnet' } else { 'C:\Program Files\dotnet' }

# 探针报出来的那个补丁号，本机不一定正好装着一份。
# 典型场景：装了 SDK 11 的预览版，它把 10.0.15 的 hostfxr 带来、却顺手删了 10.0.14 的，
# 而 shared 下两个补丁号都还在——于是 fxr 缺、shared 有。
# 这时往上找一个「host\fxr 与 shared 都有、且主版本相同」的最高补丁号顶上去：
# 目标机上的 framework-dependent 应用默认允许补丁级前滚（rollForward=latestPatch），
# 拷进去的运行时比 exe 期望的新一点没关系，反过来（拷旧的给新的用）才不行。
$fxrSrc    = Join-Path $dotnetRoot ('host\fxr\' + $runtimeVer)
$sharedSrc = Join-Path $dotnetRoot ('shared\Microsoft.NETCore.App\' + $runtimeVer)

if (-not (Test-Path -LiteralPath $fxrSrc) -or -not (Test-Path -LiteralPath $sharedSrc)) {
    $wantPrefix = ($runtimeVer -split '\.')[0] + '.'
    $candidates = @()

    $fxrRoot    = Join-Path $dotnetRoot 'host\fxr'
    $sharedRoot = Join-Path $dotnetRoot 'shared\Microsoft.NETCore.App'

    if ((Test-Path -LiteralPath $fxrRoot) -and (Test-Path -LiteralPath $sharedRoot)) {
        $candidates = @(
            Get-ChildItem -LiteralPath $fxrRoot -Directory |
                Where-Object { $_.Name.StartsWith($wantPrefix) -and (Test-Path -LiteralPath (Join-Path $sharedRoot $_.Name)) } |
                ForEach-Object { $_.Name } |
                Sort-Object { [version]$_ } -Descending
        )
    }

    if ($candidates.Count -eq 0) {
        throw ('打包机没有可用运行时 ' + $runtimeVer + '，也没有可替代的 ' + $wantPrefix + 'x 版本：' + $dotnetRoot)
    }

    $fallback = $candidates[0]
    Write-Host ('  本机没有 ' + $runtimeVer + ' 的 hostfxr，改用 ' + $fallback + '（同为 ' + $wantPrefix + 'x）') -ForegroundColor Yellow
    $runtimeVer = $fallback
    $fxrSrc     = Join-Path $dotnetRoot ('host\fxr\' + $runtimeVer)
    $sharedSrc  = Join-Path $dotnetRoot ('shared\Microsoft.NETCore.App\' + $runtimeVer)
}

$rtRoot    = Join-Path $fullDir 'runtime.dotnet'
$sharedDir = Join-Path $rtRoot ('shared\Microsoft.NETCore.App\' + $runtimeVer)
$fxrDir    = Join-Path $rtRoot ('host\fxr\' + $runtimeVer)
New-Item -ItemType Directory -Path $sharedDir -Force | Out-Null
New-Item -ItemType Directory -Path $fxrDir    -Force | Out-Null

# -Force 也要给：.version 是隐藏属性的点文件，不加会被跳过
Copy-Item -Path (Join-Path $sharedSrc '*') -Destination $sharedDir -Recurse -Force
Copy-Item -Path (Join-Path $fxrSrc '*')    -Destination $fxrDir    -Recurse -Force

$sharedCount = (Get-ChildItem -LiteralPath $sharedDir -File -Force).Count

foreach ($meta in @('.version', 'Microsoft.NETCore.App.deps.json', 'Microsoft.NETCore.App.runtimeconfig.json')) {
    if (-not (Test-Path -LiteralPath (Join-Path $sharedDir $meta))) {
        throw ('runtime.dotnet\shared 里缺框架元数据：' + $meta)
    }
}

Write-Host ('  运行时 ' + $runtimeVer + '（' + $tfm + '）：' + $sharedCount + ' 个文件进 runtime.dotnet\shared，hostfxr 进 runtime.dotnet\host\fxr') -ForegroundColor Green


# ====== 关于 ANGLE 库（现在必须一起带上）======
#
# av_libglesv2.dll / av_libegl.dll 是 ANGLE 的两个 native 库（合计约 5 MB），只有 GPU 渲染
# （AngleEgl）才会加载。2026-11-09 起渲染后端默认就是 AngleEgl（原因见 docs\交接文档.md §6.8：
# 软件光栅在 330Hz 屏上肉眼可见地顿），所以它们必须留在包里，本脚本不做任何剔除。
#
# 早先为了省这 5 MB，csproj 里有个 StripAngleLibraries target 会在 _ComputeFilesToBundle 之前
# 把这两条 ResolvedFileToPublish 摘掉；**那个 target 已经删了，别再按老代码加回去**——
# 加回去之后 AngleEgl 起不来，会一路落到 Program.cs 里那条 try/catch 上改用软件渲染，
# 每次启动都先失败一次、日志里天天躺一条假异常。


# ====== 补 MediaMTX 目录与说明 ======

$utf8Bom = [System.Text.UTF8Encoding]::new($true)

# 写进发布包的说明文件一律 LF。
# 这里必须显式规范化：用 Windows PowerShell 5.1 跑本脚本时，here-string 里的换行会被它换回 CRLF，
# 于是生成的 README.txt 跟项目其余部分的行尾对不上。
function Write-TextFile {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Text
    )
    $normalized = $Text.Replace("`r`n", "`n")
    [System.IO.File]::WriteAllText($Path, $normalized, $utf8Bom)
}

function Write-Package {
    param(
        [Parameter(Mandatory)][string]$Dir,
        [Parameter(Mandatory)][string]$Kind
    )

    $runtimeNote  = '这个是 full 版：自带 .NET 运行时，解压后直接双击就能用，电脑上不需要装 .NET。'
    $runtimeExtra = ''

    if ($Kind -eq 'light') {
        $runtimeNote  = '这个是 light 版：不带 .NET 运行时，体积小很多，但电脑上要先装 .NET 10 运行时（Runtime）。'
        $runtimeExtra = @'

装运行时
  到 https://dotnet.microsoft.com/download/dotnet/10.0 下载
  「.NET Runtime 10.x — Windows x64」，装完再双击 MediaMTX-Yuki-GUI.exe。
  注意是 .NET Runtime，不是 .NET Desktop Runtime —— 这个程序是 Avalonia 画的界面，
  不用 WPF/WinForms，所以不需要 Desktop Runtime。装 Desktop 版也能跑（它包含 Runtime），
  只是多下 50 MB 用不上的东西。
  没装的话，Windows 会直接提示缺少 .NET 运行时，程序不会启动。
'@
    }

    $readme = @"
MediaMTX-Yuki-GUI $versionText（$Kind 版）
============================================

$runtimeNote
$runtimeExtra

目录结构
  MediaMTX-Yuki-GUI.exe   双击这个（程序全在里面，就一个文件）
  runtime.dotnet\         只有 full 版才有：自带的 .NET 运行时，别删
  Languages\              界面语言包
  第一次启动还会生成 config\ 与 logs\；装好引擎后再多一个 MediaMTX\

第一次运行
  程序会在自己所在的目录下建两个文件夹：
    config   app.json（你的全部设置）、runtime.yml（每次启动引擎时重写，不用管）
    logs     mediamtx-yyyyMMdd.log（引擎日志，按天分文件，日志页看到的内容就在这里）
  程序目录放在哪里都行，但路径里带空格或中文时记得用引号。

引擎还没装
  包里不带引擎。第一次启动后到设置页点「立即检查」，状态行会提示
  未检测到引擎，此时右边的「下载并安装」就能直接把引擎装好，
  MediaMTX\ 目录也是在那时候自动建出来的。

关窗不等于退出
  点右上角的 × 只是收进托盘，引擎继续转流；图标在任务栏右下角的
  ^ 溢出区里（Windows 的默认行为，想常驻可以自己拖到任务栏上）。
  要真正退出：右键托盘图标 → 退出程序。

不会重复开
  已经在跑的时候再双击一次，不会开出第二个窗口，只会把原来的窗口叫出来。
"@

    # 统一成 Windows 换行（这里不写反引号，避免和脚本自身的转义打架）
    $crlf = [string]([char]13) + [string]([char]10)
    $lf   = [string]([char]10)
    $readme = $readme.Replace($lf, $crlf)

    Write-TextFile -Path (Join-Path $Dir 'README.txt') -Text $readme
}

Write-Package -Dir $fullDir  -Kind 'full'
Write-Package -Dir $lightDir -Kind 'light'


# ====== 校验：两个包的 exe 必须一模一样 ======
#
# 单文件发布之后所有代码都在 exe 里，比这一个文件就等于比了整个程序。
# 两个包唯一的区别是旁边的 runtime.dotnet\ 在不在，exe 必须逐字节相同——
# 这是让用户下载哪个包都拿到同一份程序的前提。

$fullExePath  = Join-Path $fullDir  'MediaMTX-Yuki-GUI.exe'
$lightExePath = Join-Path $lightDir 'MediaMTX-Yuki-GUI.exe'

$fullHash  = (Get-FileHash -LiteralPath $fullExePath  -Algorithm SHA256).Hash
$lightHash = (Get-FileHash -LiteralPath $lightExePath -Algorithm SHA256).Hash

if ($fullHash -ne $lightHash) {
    throw ('两个包的 exe 不一致（full ' + $fullHash + ' / light ' + $lightHash + '），不能发包')
}

$exeMb = [math]::Round((Get-Item -LiteralPath $fullExePath).Length / 1MB, 1)
Write-Host ('  两版一致：MediaMTX-Yuki-GUI.exe  ' + $fullHash + '（' + $exeMb + ' MB）') -ForegroundColor Green


# ====== 压 zip ======

Add-Type -AssemblyName System.IO.Compression.FileSystem

function New-PackageZip {
    param(
        [Parameter(Mandatory)][string]$Dir,
        [Parameter(Mandatory)][string]$ZipPath
    )

    if (Test-Path -LiteralPath $ZipPath) { Remove-Item -LiteralPath $ZipPath -Force }
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $Dir,
        $ZipPath,
        [System.IO.Compression.CompressionLevel]::Optimal,
        $true
    )
}

$fullZip  = Join-Path $distDir ($packageName + '-full.zip')
$lightZip = Join-Path $distDir ($packageName + '-light.zip')

Write-Host '压包' -ForegroundColor Cyan
New-PackageZip -Dir $fullDir  -ZipPath $fullZip
New-PackageZip -Dir $lightDir -ZipPath $lightZip

Remove-Item -LiteralPath $stageRoot -Recurse -Force


# ====== 结果 ======

Write-Host ''
Write-Host '打包完成' -ForegroundColor Green
foreach ($zip in @($fullZip, $lightZip)) {
    $sizeMb = [math]::Round((Get-Item -LiteralPath $zip).Length / 1MB, 1)
    Write-Host ('  {0,7} MB   {1}' -f $sizeMb, $zip)
}
