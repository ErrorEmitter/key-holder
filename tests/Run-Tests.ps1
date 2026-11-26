<#
.SYNOPSIS
  KeyHolder 自动测试：编一个不要求管理员的测试版，用 SendInput 模拟按键，检查按键状态。

.DESCRIPTION
  只用 F21–F24、右 Shift 和 Scroll Lock 这几个无害的键。测试结束后会把 Scroll Lock 的切换状态恢复原样。
  运行期间不要碰键盘。需要 .NET 11 SDK。

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tests\Run-Tests.ps1
#>
param(
    [string]$OutDir = (Join-Path $env:TEMP 'keyholder-test'),
    [string]$Screenshot
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
New-Item -ItemType Directory -Force $OutDir | Out-Null

# 1. 编译不提权的测试版
$manifest = Join-Path $OutDir 'asinvoker.manifest'
(Get-Content (Join-Path $repo 'app.manifest') -Raw).Replace('requireAdministrator', 'asInvoker') |
    Set-Content $manifest -Encoding UTF8
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
& dotnet build (Join-Path $repo 'KeyHolder.csproj') -c Debug "-p:ApplicationManifest=$manifest" -o (Join-Path $OutDir 'bin') | Out-Host
if ($LASTEXITCODE -ne 0) { throw '编译失败' }
$exe = Join-Path $OutDir 'bin\KeyHolder.exe'

# 2. 模拟按键用的辅助类
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;

public static class KH
{
    [StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public UIntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public UIntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit)] public struct U { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public U u; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }

    [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] static extern short GetKeyState(int vk);
    [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint type);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr ctx);

    public static void Key(int vk, bool up)
    {
        var i = new INPUT();
        i.type = 1;
        i.u.ki.wVk = (ushort)vk;
        i.u.ki.wScan = (ushort)MapVirtualKey((uint)vk, 0);
        i.u.ki.dwFlags = up ? 2u : 0u;
        SendInput(1, new[] { i }, Marshal.SizeOf(typeof(INPUT)));
        Thread.Sleep(40);
    }
    public static void Tap(int vk) { Key(vk, false); Key(vk, true); }
    public static bool Down(int vk) { return (GetAsyncKeyState(vk) & 0x8000) != 0; }
    public static bool ScrollToggled() { return (GetKeyState(0x91) & 1) != 0; }

    public static void Capture(IntPtr h, string path)
    {
        SetThreadDpiAwarenessContext(new IntPtr(-4));
        RECT r; GetWindowRect(h, out r);
        using (var bmp = new Bitmap(r.R - r.L, r.B - r.T))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                IntPtr dc = g.GetHdc();
                PrintWindow(h, dc, 2);
                g.ReleaseHdc(dc);
            }
            bmp.Save(path, ImageFormat.Png);
        }
    }
}
'@

$F21 = 0x84; $F22 = 0x85; $F23 = 0x86; $F24 = 0x87; $SCROLL = 0x91; $RSHIFT = 0xA1; $SHIFT = 0x10
$testKeys = $F21, $F22, $F23, $F24, $RSHIFT
$script:fail = 0
function Check([string]$name, [bool]$ok) {
    if ($ok) { Write-Host "PASS  $name" -ForegroundColor Green }
    else { Write-Host "FAIL  $name" -ForegroundColor Red; $script:fail++ }
}
function Scroll([int]$times) { for ($i = 0; $i -lt $times; $i++) { [KH]::Tap($SCROLL) }; Start-Sleep -Milliseconds 60 }
function Trigger { Scroll 2 }
function Settle { Start-Sleep -Milliseconds 120 }
function NoneHeld { -not ($testKeys | Where-Object { [KH]::Down($_) }) }

# 3. 测试
$scrollBefore = [KH]::ScrollToggled()
$p = Start-Process $exe -PassThru
Start-Sleep -Milliseconds 2500
$p.Refresh()
Check '程序启动并显示窗口' ($p.MainWindowHandle -ne 0)

try {
    Trigger; [KH]::Tap($F24); Settle
    Check '两下 Scroll Lock + F24 → F24 被按住' ([KH]::Down($F24))

    [KH]::Tap($F24); Settle
    Check '亲手按放 F24 不会打断按住' ([KH]::Down($F24))

    Trigger; [KH]::Tap($F23); Settle
    Check '再触发 F23 → F23 和 F24 都按住' ([KH]::Down($F23) -and [KH]::Down($F24))

    if ($Screenshot) { [KH]::Capture($p.MainWindowHandle, $Screenshot) }

    Trigger; [KH]::Tap($F24); Settle
    Check '对 F24 再触发一次 → F24 松开，F23 仍按住' ((-not [KH]::Down($F24)) -and [KH]::Down($F23))

    Trigger; [KH]::Tap($F24); Settle
    Scroll 4; Settle
    Check '连按 4 下 Scroll Lock → 全部松开' (NoneHeld)

    Scroll 4; Settle
    Check '没有按住的键时连按 4 下 → 不出错' (NoneHeld)

    Scroll 3; [KH]::Tap($F22); Settle
    Check '连按 3 下后按 F22 → 不触发' (-not [KH]::Down($F22))

    Trigger; [KH]::Key($SCROLL, $false); Settle
    $scrollHeld = [KH]::Down($SCROLL)
    [KH]::Key($SCROLL, $true); Settle
    Check 'Scroll Lock 不能被设为按住（第 3 下照常通过，松手即抬起）' ($scrollHeld -and -not [KH]::Down($SCROLL))
    [KH]::Tap($F21); Settle   # 打断计数

    Trigger; [KH]::Tap($F24); Settle
    Scroll 3; Start-Sleep -Milliseconds 1200; Scroll 1; Settle
    Check '第 4 下晚了 1.2 秒 → 不算连按 4 下' ([KH]::Down($F24))
    [KH]::Tap($F21); Settle

    Scroll 6; [KH]::Tap($F22); Settle
    Check '连按 6 下 + F22 → 前 4 下全部松开，后 2 下按住 F22' ((-not [KH]::Down($F24)) -and [KH]::Down($F22))
    Trigger; [KH]::Tap($F22); Settle

    Trigger; Start-Sleep -Milliseconds 1300; [KH]::Key($F22, $false); Settle
    $downWhilePressed = [KH]::Down($F22)
    [KH]::Key($F22, $true); Settle
    Check '两下 Scroll Lock 后超过 1 秒才按 F22 → 正常按放' ($downWhilePressed -and -not [KH]::Down($F22))

    [KH]::Tap($SCROLL); Start-Sleep -Milliseconds 1200; Scroll 1
    [KH]::Tap($F22); Settle
    Check '两下 Scroll Lock 间隔 1.2 秒 → 不触发' (-not [KH]::Down($F22))

    [KH]::Tap($SCROLL); [KH]::Tap($F22); Scroll 1
    [KH]::Tap($F22); Settle
    Check 'Scroll、F22、Scroll 之后按 F22 → 不触发' (-not [KH]::Down($F22))

    Trigger; [KH]::Tap($RSHIFT); Settle
    $rs = [KH]::Down($RSHIFT); $s = [KH]::Down($SHIFT)
    Trigger; [KH]::Tap($RSHIFT); Settle
    Check '右 Shift 可以按住' ($rs -and $s)
    Check '右 Shift 再触发一次即松开' (-not [KH]::Down($RSHIFT) -and -not [KH]::Down($SHIFT))

    Check '结束时所有测试键都已抬起' (NoneHeld)
}
finally {
    foreach ($vk in $testKeys) { if ([KH]::Down($vk)) { [KH]::Key($vk, $true) } }
    if (-not $p.HasExited) { $p.Kill() }
    Start-Sleep -Milliseconds 200
    if ([KH]::ScrollToggled() -ne $scrollBefore) { [KH]::Tap($SCROLL) }
    foreach ($vk in $testKeys) { if ([KH]::Down($vk)) { [KH]::Key($vk, $true) } }
}
Check 'Scroll Lock 指示灯恢复原状' ([KH]::ScrollToggled() -eq $scrollBefore)
Write-Host "失败数：$script:fail"
exit $script:fail
