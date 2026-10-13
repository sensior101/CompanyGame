# EOS test runner for the Windows bundle (EosTestQA.Bundle copies this). Called by eos_test.bat / eos_test_server.bat.
# This PowerShell and everything it starts sit in a kill-on-close job: closing the window (or Ctrl+C) ends the game process too.
param([string]$Mode, [string]$Puid)
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class KillOnCloseJob
{
    [StructLayout(LayoutKind.Sequential)] struct Basic { public long a, b; public uint LimitFlags; public UIntPtr c, d; public uint e; public UIntPtr f; public uint g, h; }
    [StructLayout(LayoutKind.Sequential)] struct Io { public ulong a, b, c, d, e, f; }
    [StructLayout(LayoutKind.Sequential)] struct Extended { public Basic Basic; public Io Io; public UIntPtr a, b, c, d; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr attributes, string name);
    [DllImport("kernel32.dll")] static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref Extended info, int length);
    [DllImport("kernel32.dll")] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    static IntPtr job; // never closed by hand: the OS closes it when this process ends, which kills every process in the job
    public static void Enter()
    {
        job = CreateJobObject(IntPtr.Zero, null);
        var info = new Extended();
        info.Basic.LimitFlags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        if (job == IntPtr.Zero || !SetInformationJobObject(job, 9, ref info, Marshal.SizeOf(typeof(Extended))) || !AssignProcessToJobObject(job, GetCurrentProcess()))
            throw new Exception("job object error " + Marshal.GetLastWin32Error());
    }
}
'@
[KillOnCloseJob]::Enter()

Set-Location $PSScriptRoot
$log = Join-Path $PSScriptRoot 'eos_test_log.txt'
if (Test-Path $log) { Remove-Item $log }
# the game gets its own hidden console (CreateNoWindow), so its stdout stays out of this window; it still joins the job
function StartGame($arguments) {
    $info = New-Object Diagnostics.ProcessStartInfo (Join-Path $PSScriptRoot 'CompanyGame.exe'), $arguments
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.WorkingDirectory = $PSScriptRoot
    [Diagnostics.Process]::Start($info)
}
function LogText { if (Test-Path $log) { Get-Content $log -Raw -Encoding UTF8 } else { '' } }

if ($Mode -eq 'client') {
    Write-Host '시험 중입니다. 1~2분 걸립니다... (창을 닫으면 시험도 끝납니다)'
    $game = StartGame "-batchmode -nographics -eostest client $Puid -logFile unity_client_log.txt"
    $game.WaitForExit()
    exit 0
}

Write-Host 'EOS 시험 서버를 켭니다. 잠시 기다려 주세요...'
$game = StartGame '-batchmode -nographics -eostest server -eostest-minutes 60 -logFile unity_server_log.txt'
$serverPuid = $null
for ($i = 0; $i -lt 90 -and -not $game.HasExited; $i++) {
    Start-Sleep -Seconds 1
    $text = LogText
    if ($text -match 'FAILED') { break }
    if ($text -match 'SESSION=' -and $text -match ' PUID=(\w+)') { $serverPuid = $Matches[1]; break }
}
if (-not $serverPuid) {
    Write-Host ''
    Write-Host '서버를 켜지 못했습니다. eos_test_log.txt 와 unity_server_log.txt 를 보내 주세요.'
    Write-Host (LogText)
    exit 1
}
Write-Host ''
Write-Host '============================================================'
Write-Host ''
Write-Host "    서버 PUID:  $serverPuid"
Write-Host ''
Write-Host '============================================================'
Write-Host ' 위 PUID를 팀원에게 알려 주세요. (eos_test_log.txt 에도 있습니다)'
Write-Host ' 서버는 60분 동안 켜져 있습니다. 끝내려면 아무 키를 누르거나 이 창을 닫으세요.'
while (-not $game.HasExited) {
    if ([Console]::KeyAvailable) { [void][Console]::ReadKey($true); break }
    Start-Sleep -Milliseconds 200
}
if (-not $game.HasExited) { $game.Kill(); $game.WaitForExit() }
Write-Host '서버를 껐습니다. eos_test_log.txt 를 보내 주세요.'
