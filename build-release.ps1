<#
    MediaMTX-Yuki-GUI 打包脚本

    产出两个 zip，解压即用（软件名 / 版本号 / 架构 / 引擎标记之间一律用下划线分隔）：
      MediaMTX-Yuki-GUI_<版本号>_x64.zip                 不带引擎，体积小；引擎由程序自己去 GitHub 下载
      MediaMTX-Yuki-GUI_<版本号>_x64_With_MediaMTX.zip   多带一份推荐适配版本的 MediaMTX 引擎，装完就能转流

    版本号从 csproj 的 <Version> 读，别在这里手写。

    两个包里的程序完全一样（同一个 exe，逐字节相同），差别只有旁边那份 MediaMTX\。
    程序本身是自包含 + 裁剪 + 单文件：不依赖系统装的 .NET，也没有 runtime.dotnet\ 这类外挂目录。

    为什么没有 arm64：MediaMTX 官方只发 windows_amd64（v1.21.3 的 8 个资产里，
    darwin 与 linux 都有 arm64，Windows 只有 amd64），没有引擎就做不成带引擎的包，
    所以暂时只出 x64。等官方发了 Windows arm64 再说。

    用法：
      .\build-release.ps1                     正式包，完整跑一遍（含 restore）
      .\build-release.ps1 -NoRestore          正式包，依赖包没变化时跳过 restore
      .\build-release.ps1 -NoRestore -Label test
                                               临时测试包，文件名带 -test
      .\build-release.ps1 -SkipEngine         出包但不带引擎（断网时的应急口子）

    -Label 是给「自己临时验一下」用的：包名变成
    MediaMTX-Yuki-GUI_<版本号>_<Label>_x64.zip，并且**只清自己那一份旧产物**，
    不会顺手把 dist\ 里的正式包删掉。不带 -Label 时行为与以前一样
    （整目录清空后重出两个正式包）。
#>

param(
    [switch]$NoRestore,
    [switch]$SkipEngine,
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
$packageName = 'MediaMTX-Yuki-GUI_' + $versionText

# -Label 只影响文件名与清理范围，不动 exe 内容：临时包和正式包是同一份程序，
# 区别仅在于「你知道它是临时打的」。这样临时验完之后，dist\ 里的正式包
# 还是上一次正式打包的那个，不会被顺手覆盖掉。
if ($Label) {
    if ($Label -notmatch '^[A-Za-z0-9._-]+$') {
        throw ('-Label 只能是字母、数字、点、下划线、连字符：' + $Label)
    }
    $packageName = $packageName + '_' + $Label
    Write-Host ('版本号 ' + $versionText + '（临时测试包 -' + $Label + '）') -ForegroundColor Cyan
}
else {
    Write-Host ('版本号 ' + $versionText) -ForegroundColor Cyan
}

# 目前只有 x64：引擎官方不发 Windows arm64，带引擎那个包就无从谈起。
$archName = 'x64'


# ====== 收掉旧进程与旧产物 ======

# 程序在跑的时候 exe 是锁着的，publish 会报 MSB3027
# -ErrorAction SilentlyContinue 要写在 Stop-Process 上：杀不掉的残留进程（句柄已失效、
# 权限也拿不到的那种）会让 $ErrorActionPreference='Stop' 直接把整脚本掀翻。
Get-Process -Name 'MediaMTX-Yuki-GUI' -ErrorAction SilentlyContinue |
    Stop-Process -Force -ErrorAction SilentlyContinue
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

# 必须带 RID：publish -r win-x64 需要 net10.0/win-x64 那一份还原结果。
#
# 裁剪相关的两个属性也必须出现在 restore 里，不能只写在 publish 上：
# 裁剪要靠 ILLink 与那一套 reference assemblies，restore 阶段不带这些属性就不会把它们拉进来，
# 于是 publish 会**静默地不裁剪**（实测过：exe 47.8 MB 而不是 22.6 MB，一句报错都没有）。
# 下面「校验」那段专门断言了体积，就是为了兜住这种静默失败。
if (-not $NoRestore) {
    Write-Host 'restore (win-x64, trimmed)' -ForegroundColor Cyan
    & $dotnetExe restore $csprojPath -r win-x64 -p:PublishTrimmed=true -p:TrimMode=partial -v minimal
    if ($LASTEXITCODE -ne 0) { throw 'restore 失败' }
}


# ====== publish ======
#
# 自包含 + 裁剪 + 单文件：托管 dll、被裁剩的运行时、native 库全部焊进一个 exe，
# 根目录只剩一个 exe，加上 Languages\。目标机不需要装 .NET，也没有 runtime.dotnet\。
#
# 为什么从「外挂运行时」改成这条路：那套布局必须按 hostfxr 的硬槽位摆
# （runtime.dotnet\host\fxr\<版本>\ 与 runtime.dotnet\shared\Microsoft.NETCore.App\<版本>\），
# 少一层就起不来、摊平也起不来，想「文件夹里干干净净」只能走自包含。
#
# 裁剪（PublishTrimmed + TrimMode=partial）省下约 25 MB：exe 从 47.8 MB 降到 22.6 MB，
# 比改造前那个框架依赖的 32.2 MB 还小。代价有两个，都必须在 csproj 里兜住：
#   1. System.Text.Json 的反射序列化被关掉 —— csproj 的
#      JsonSerializerIsReflectionEnabledByDefault=true 就是为它写的，删了配置就读不出来；
#   2. 运行时会按需砍掉「没被静态引用到」的代码，Avalonia 这种靠反射加载 XAML 的框架
#      属于高危区。所以出包之后必须逐页点一遍（验收清单见 docs\交接文档.md §6.9）。
#
# 单文件的代价：首次启动要把 native 库解压到 %TEMP%\.net\<应用名>\<哈希>\，
# 每次 exe 变化都会多留一份约 18 MB 的缓存，首启比二启慢一秒左右。
# -p:IncludeNativeLibrariesForSelfExtract=true 是必须的，否则 libSkiaSharp.dll
# 这类 native 库会散落在 exe 旁边，文件树又乱回去了；EnableCompressionInSingleFile
# 再把这堆东西压一遍，exe 47.8 → 42 MB 级别。
#
# 一次 publish 就够——两个包共用同一份 exe，差别只在「旁边的 MediaMTX\ 在不在」。

$publishBase = @(
    'publish', $csprojPath,
    '-c', 'Release',
    '-r', 'win-x64',
    '--no-restore',
    '-v', 'minimal'
)

$publishArgs = $publishBase + @(
    '--self-contained', 'true',
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:PublishTrimmed=true',
    '-p:TrimMode=partial',
    '-p:EnableCompressionInSingleFile=true'
)

$appDir        = Join-Path $stageRoot '_app'
$plainDir      = Join-Path $stageRoot ($packageName + '_' + $archName)
$withEngineDir = Join-Path $stageRoot ($packageName + '_' + $archName + '_With_MediaMTX')

Write-Host 'publish app（自包含 + 裁剪 + 单文件）' -ForegroundColor Cyan
& $dotnetExe @($publishArgs + @('-o', $appDir))
if ($LASTEXITCODE -ne 0) { throw 'publish 失败' }


# ====== 组装两个包 ======
#
# _app 里只有 MediaMTX-Yuki-GUI.exe 与 Languages\ 两项，直接复制两遍。
# MediaMTX\ 一开始都不放：带引擎那个包会在下面单独补进去。

Write-Host '组装两个包' -ForegroundColor Cyan
New-Item -ItemType Directory -Path $plainDir      -Force | Out-Null
New-Item -ItemType Directory -Path $withEngineDir -Force | Out-Null
Copy-Item -Path (Join-Path $appDir '*') -Destination $plainDir      -Recurse -Force
Copy-Item -Path (Join-Path $appDir '*') -Destination $withEngineDir -Recurse -Force

$appItems = @(Get-ChildItem -LiteralPath $appDir -Force | ForEach-Object { $_.Name })
Write-Host ('  publish 输出 ' + $appItems.Count + ' 项：' + ($appItems -join ' / ')) -ForegroundColor Green


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


# ====== 带引擎的那个包补一份推荐版本的引擎 ======
#
# 为什么要自带：本程序是对着某一个引擎版本把界面与行为都验过的，用户第一次装完
# 还要自己联网下载引擎，很容易下到比界面更新的版本，出问题也说不清是谁的问题。
# 推荐版本号只维护在 csproj 的 <RecommendedEngineVersion>，与程序里那个常量同源。
#
# 缓存放 obj\engine-cache\ 而不是 dist\：dist\ 每次打包都会被清掉，
# 缓存放那里等于每次都重新下一遍。
#
# 断网时用 -SkipEngine 出包：两个包都出，只是带引擎那个里面没有 MediaMTX\，
# 回到「引擎要用户自己装」的样子。

$bundledEngineTag = ''

if ($SkipEngine) {
    Write-Host '按 -SkipEngine 出包：两个包都不带引擎' -ForegroundColor Yellow
}
else {
    $engineNode = Select-Xml -LiteralPath $csprojPath -XPath '//RecommendedEngineVersion'
    if (-not $engineNode) {
        throw 'csproj 里没有 <RecommendedEngineVersion>，无法确定要内置哪个版本的引擎'
    }

    $engineVer = $engineNode.Node.InnerText.Trim()
    $bundledEngineTag = 'v' + $engineVer

    $engineAsset = 'mediamtx_' + $bundledEngineTag + '_windows_amd64.zip'
    $engineUrl   = 'https://github.com/bluenviron/mediamtx/releases/download/' + $bundledEngineTag + '/' + $engineAsset

    $engineCacheDir = Join-Path $projectDir 'obj\engine-cache'
    $engineZip      = Join-Path $engineCacheDir $engineAsset

    New-Item -ItemType Directory -Path $engineCacheDir -Force | Out-Null

    # 三兆是这份 zip 的大致量级；小得离谱说明上次下到一半就断了，删掉重下
    $engineZipOk = (Test-Path -LiteralPath $engineZip) -and ((Get-Item -LiteralPath $engineZip).Length -gt 3MB)

    if ($engineZipOk) {
        Write-Host ('  引擎 ' + $bundledEngineTag + '：用缓存 ' + $engineZip) -ForegroundColor Green
    }
    else {
        Write-Host ('  引擎 ' + $bundledEngineTag + '：下载 ' + $engineAsset) -ForegroundColor Cyan

        # PowerShell 5.1 的 Invoke-WebRequest 默认走 TLS 1.0，GitHub 早就不认了；
        # 进度条在 5.1 上还会把下载拖成十分钟，这里一律关掉
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
        $progressBackup = $ProgressPreference
        $ProgressPreference = 'SilentlyContinue'

        try {
            Invoke-WebRequest -Uri $engineUrl -OutFile $engineZip -UseBasicParsing
        }
        catch {
            throw ('引擎下载失败（' + $engineUrl + '）：' + $_.Exception.Message + '。断网时可以用 -SkipEngine 出不带引擎的包。')
        }
        finally {
            $ProgressPreference = $progressBackup
        }
    }

    $engineStage = Join-Path $projectDir 'obj\engine-extract'
    if (Test-Path -LiteralPath $engineStage) { Remove-Item -LiteralPath $engineStage -Recurse -Force }
    New-Item -ItemType Directory -Path $engineStage -Force | Out-Null

    Expand-Archive -LiteralPath $engineZip -DestinationPath $engineStage -Force

    $stagedExe = Join-Path $engineStage 'mediamtx.exe'
    if (-not (Test-Path -LiteralPath $stagedExe)) {
        throw ('引擎包里没有 mediamtx.exe，解压目录：' + $engineStage)
    }

    $engineDir = Join-Path $withEngineDir 'MediaMTX'
    New-Item -ItemType Directory -Path $engineDir -Force | Out-Null
    Copy-Item -LiteralPath $stagedExe -Destination (Join-Path $engineDir 'mediamtx.exe') -Force

    # version.json 只是给人看的记录，程序读的是 mediamtx.exe --version；
    # 结构要与 EngineUpdater.WriteVersionFile 写出来的那份保持一致
    $engineRecord = '{"Version":"' + $bundledEngineTag + '","InstalledAt":"' + (Get-Date).ToString('o') + '"}'
    [System.IO.File]::WriteAllText((Join-Path $engineDir 'version.json'), $engineRecord)

    Remove-Item -LiteralPath $engineStage -Recurse -Force

    $engineMb = [math]::Round((Get-Item -LiteralPath (Join-Path $engineDir 'mediamtx.exe')).Length / 1MB, 1)
    Write-Host ('  引擎 ' + $bundledEngineTag + ' 已放进带引擎那个包（mediamtx.exe，' + $engineMb + ' MB）') -ForegroundColor Green
}


# ====== 说明文件 ======

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
        [Parameter(Mandatory)][string]$Kind,
        [string]$EngineVersion = ''
    )

    # 两个包的程序本体一样，差别只有带不带引擎，所以说明里只分这两段
    $engineRow  = '  第一次启动还会生成 config\ 与 logs\；引擎装好后会多一个 MediaMTX\'
    $engineHelp = @'
引擎还没装
  这个包不带引擎。第一次启动后到设置页的「引擎更新」那一行点「检查」，
  状态行会提示未检测到引擎，此时右边的「安装」就能直接把引擎装好，
  MediaMTX\ 目录也是在那时候自动建出来的。
'@

    if ($EngineVersion) {
        $engineRow  = '  MediaMTX\                自带的 MediaMTX 引擎 ' + $EngineVersion + '（界面适配过的版本，别随便换）'
        $engineHelp = @'
引擎已经装好了
  包里自带 MediaMTX 引擎，解压后直接能用，不需要再下载。
  这是本程序界面适配过的版本；想换成别的版本，到设置页的「引擎更新」里操作。
'@
    }

    $readme = @"
MediaMTX-Yuki-GUI $versionText（$Kind）
============================================

解压后直接双击 MediaMTX-Yuki-GUI.exe 就能用，电脑上不需要装 .NET。

目录结构
  MediaMTX-Yuki-GUI.exe   双击这个（程序全在里面，就一个文件）
  Languages\              界面语言包
$engineRow

第一次运行
  程序会在自己所在的目录下建两个文件夹：
    config   app.json（你的全部设置）、runtime.yml（每次启动引擎时重写，不用管）
    logs     mediamtx-yyyyMMdd.log（引擎日志，按天分文件，日志页看到的内容就在这里）
  程序目录放在哪里都行，但路径里带空格或中文时记得用引号。

$engineHelp

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

Write-Package -Dir $withEngineDir -Kind ($archName + ' + 自带引擎') -EngineVersion $bundledEngineTag
Write-Package -Dir $plainDir      -Kind $archName


# ====== 校验：包的形状对不对 ======
#
# 单文件发布之后所有代码都在 exe 里，所以只需要确认两件事：
# 1. exe 真的在（裁剪/单文件任何一步挂掉都可能产出一个空壳目录）；
# 2. 带引擎那个包里确实有 mediamtx.exe —— 这是它与另一个包唯一的区别，
#    上一段被 -SkipEngine 跳过或者复制失败时，这里会当场拦住。

$plainExePath = Join-Path $plainDir 'MediaMTX-Yuki-GUI.exe'

if (-not (Test-Path -LiteralPath $plainExePath)) {
    throw ('publish 没有产出 exe：' + $plainExePath)
}

if ($bundledEngineTag) {
    $engineExePath = Join-Path $withEngineDir 'MediaMTX\mediamtx.exe'
    if (-not (Test-Path -LiteralPath $engineExePath)) {
        throw ('带引擎的包里没有 MediaMTX\mediamtx.exe：' + $engineExePath)
    }
}

$exeHash = (Get-FileHash -LiteralPath $plainExePath -Algorithm SHA256).Hash
$exeMb   = [math]::Round((Get-Item -LiteralPath $plainExePath).Length / 1MB, 1)
Write-Host ('  MediaMTX-Yuki-GUI.exe  ' + $exeHash + '（' + $exeMb + ' MB）') -ForegroundColor Green

# 体积红线：裁好的 exe 约 22.6 MB，没裁的是 47.8 MB。
# 超了就是上面那条「restore 没带裁剪属性 → publish 静默不裁」，别把胖包发出去。
$exeLimitMb = 32
if ($exeMb -gt $exeLimitMb) {
    throw ('exe ' + $exeMb + ' MB 超过 ' + $exeLimitMb + ' MB，说明这次没有裁剪（多半是 restore 没带 -p:PublishTrimmed=true）。删掉 obj\ 与 bin\ 后重跑一次完整打包。')
}


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

$plainZip      = Join-Path $distDir ((Split-Path -Leaf $plainDir) + '.zip')
$withEngineZip = Join-Path $distDir ((Split-Path -Leaf $withEngineDir) + '.zip')

Write-Host '压包' -ForegroundColor Cyan
New-PackageZip -Dir $plainDir      -ZipPath $plainZip
New-PackageZip -Dir $withEngineDir -ZipPath $withEngineZip

Remove-Item -LiteralPath $stageRoot -Recurse -Force


# ====== 结果 ======

Write-Host ''
Write-Host '打包完成' -ForegroundColor Green
foreach ($zip in @($plainZip, $withEngineZip)) {
    $sizeMb = [math]::Round((Get-Item -LiteralPath $zip).Length / 1MB, 1)
    Write-Host ('  {0,7} MB   {1}' -f $sizeMb, $zip)
}

