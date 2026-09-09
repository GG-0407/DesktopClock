# 脚本位于 scripts/ 子目录，项目根 = 向上跳一级
$dir = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$lnkPath = Join-Path ([Environment]::GetFolderPath('Desktop')) '桌面时钟.lnk'
$ws = New-Object -ComObject WScript.Shell
$sc = $ws.CreateShortcut($lnkPath)
$sc.TargetPath = Join-Path $dir 'DesktopClock.exe'
$sc.WorkingDirectory = $dir
$sc.IconLocation = (Join-Path $dir 'DesktopClock.exe') + ",0"
$sc.Description = '简洁大气的桌面时钟'
$sc.Save()
Write-Host "Created: $lnkPath"
