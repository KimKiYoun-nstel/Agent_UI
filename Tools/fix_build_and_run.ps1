<#
.SYNOPSIS
  빌드 시 exe 잠김(lock) 오류를 자동으로 해결하고 빌드/실행까지 수행하는 스크립트
.DESCRIPTION
  - 솔루션을 빌드한다. 빌드 중 "being used by another process"(파일 잠김) 오류가 감지되면
    잠금 중인 프로세스를 탐지하여 종료한 뒤 재빌드한다. 재빌드 성공 시 exe를 실행한다.
  - Sysinternals `handle.exe`가 PATH에 있으면 우선 사용하여 잠금 프로세스 PID를 정확히 찾는다.
  - handle.exe가 없으면 PowerShell의 Get-Process(MainModule.FileName) 방식으로 시도한다.
  - 이 스크립트는 GUI 앱 실행을 위해 exe를 Start-Process로 실행합니다.
.NOTES
  - Windows PowerShell 5.1에서 동작을 목표로 작성되었습니다.
  - 관리자 권한이 필요한 경우 프로세스 정보/종료가 실패할 수 있습니다.
#>
param(
    [string]$Solution = 'd:\CodeDev\Agent_UI\Agent.UI.Wpf.sln',
    [string]$Configuration = 'Debug',
    [string]$TargetExe = 'd:\CodeDev\Agent_UI\bin\Debug\net8.0-windows\Agent.UI.Wpf.exe',
    [int]$RetryDelayMs = 1000
)

### 콘솔 출력 인코딩을 UTF-8로 설정하여 한글 깨짐을 방지합니다 (PowerShell 5.1)
try {
    [System.Console]::OutputEncoding = [System.Text.Encoding]::UTF8
    $OutputEncoding = [System.Text.Encoding]::UTF8
    # 코드 페이지도 UTF-8로 설정(권장). 실패해도 무시.
    try { chcp 65001 > $null } catch { }
} catch { }

function WriteLog($msg) { Write-Host "[fix-build] $msg" }

function Run-Build {
    WriteLog "dotnet build '$Solution' -c $Configuration"
    $output = & dotnet build $Solution -c $Configuration 2>&1
    $rc = $LASTEXITCODE
    return @{ ExitCode = $rc; Output = $output -join "`n" }
}

function Find-LockingPidsByHandle($filePath) {
    $handleCmd = Get-Command handle.exe -ErrorAction SilentlyContinue
    if ($null -ne $handleCmd) {
        WriteLog "handle.exe found: using it to detect locking PIDs"
        try {
            # handle 출력은 영어/한국어 환경 모두 다를 수 있으니 pid:\s*(\d+) 로 파싱
            $raw = & handle.exe -accepteula -nobanner $filePath 2>&1
            $pids = @()
            foreach ($line in $raw) {
                if ($line -match 'pid: ?(\d+)') { $pids += [int]$matches[1] }
                elseif ($line -match 'pid\s+(\d+)') { $pids += [int]$matches[1] }
            }
            return $pids | Select-Object -Unique
        } catch {
            WriteLog "handle.exe 실행 실패: $_"
            return @()
        }
    }
    return @()
}

function Find-LockingPidsFallback($filePath) {
    WriteLog "handle.exe 없음: Get-Process(MainModule) 방식으로 잠금 프로세스 추정 시도"
    $found = @()
    foreach ($p in Get-Process -ErrorAction SilentlyContinue) {
        try {
            $m = $p.MainModule
            if ($null -ne $m -and $m.FileName -eq $filePath) { $found += $p.Id }
        } catch {
            # 접근 거부 등 에러 무시
        }
    }
    return $found | Select-Object -Unique
}

function Find-LockingPids($filePath) {
    $pids = Find-LockingPidsByHandle $filePath
    if ($pids.Count -gt 0) { return $pids }
    return Find-LockingPidsFallback $filePath
}

function Kill-Pids($pids) {
    foreach ($id in $pids) {
        try {
            WriteLog "Stopping process Id=$id ..."
            Stop-Process -Id $id -Force -ErrorAction Stop
            WriteLog "Stopped PID $id"
        } catch {
            WriteLog "PID $id 종료 실패: $_"
        }
    }
}

# 1) 초기 빌드 시도
$res = Run-Build
WriteLog "빌드 종료 코드: $($res.ExitCode)"
if ($res.ExitCode -eq 0) {
    WriteLog "빌드 성공"
    if (Test-Path $TargetExe) {
        WriteLog "실행: $TargetExe"
        Start-Process -FilePath $TargetExe
        exit 0
    } else {
        WriteLog "경고: 빌드 성공했지만 대상 exe가 존재하지 않습니다: $TargetExe"
        exit 0
    }
}

# 2) 빌드 실패면 출력에 잠김 관련 메시지가 있는지 확인
$txt = $res.Output
if ($txt -match 'being used by another process' -or $txt -match '잠겨 있습니다' -or $txt -match 'cannot access the file') {
    WriteLog "파일 잠김 오류 감지: 잠금 해제 시도 시작"
    $pids = Find-LockingPids $TargetExe
    if ($pids.Count -eq 0) {
        WriteLog "잠금 프로세스 감지 실패(방법 시도 완료). handle.exe 설치 여부 확인 권장."
        exit 1
    }

    WriteLog "감지된 PID: $($pids -join ',')"
    Kill-Pids $pids
    Start-Sleep -Milliseconds $RetryDelayMs

    WriteLog "재빌드 시도"
    $res2 = Run-Build
    if ($res2.ExitCode -eq 0) {
        WriteLog "재빌드 성공"
        if (Test-Path $TargetExe) {
            WriteLog "실행: $TargetExe"
            Start-Process -FilePath $TargetExe
            exit 0
        } else {
            WriteLog "재빌드 성공했으나 exe가 없습니다: $TargetExe"
            exit 0
        }
    } else {
        WriteLog "재빌드 실패: 출력 시작"
        Write-Host $res2.Output
        exit $res2.ExitCode
    }
} else {
    WriteLog "빌드 실패 원인이 파일 잠김이 아님. 빌드 출력:
$res.Output"
    exit $res.ExitCode
}
