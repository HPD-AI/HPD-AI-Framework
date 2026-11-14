[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $ProbePath,

    [string] $GitPath,

    [string] $Dotnet = 'dotnet'
)

$ErrorActionPreference = 'Stop'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
try {
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Run this test from an elevated Windows PowerShell session.'
    }
}
finally {
    $identity.Dispose()
}

$probe = (Resolve-Path -LiteralPath $ProbePath).ProviderPath
if (!(Test-Path -LiteralPath $probe -PathType Leaf)) {
    throw 'ProbePath must identify the compiled WindowsSocketProbe executable.'
}

$id = [guid]::NewGuid().ToString('N')
$name = 'hpdwfp-' + $id.Substring(0, 12)
$password = 'Hpd!8-' + [guid]::NewGuid().ToString('N')
$securePassword = ConvertTo-SecureString -String $password -AsPlainText -Force
$project = Join-Path $PSScriptRoot 'HPD-Environment.HostProcess.Tests.csproj'
$directory = Join-Path $env:ProgramData "HPD-Wfp-Test-$id"
$account = $null
$processInfo = [Diagnostics.ProcessStartInfo]::new()
$processInfo.FileName = (Get-Command $Dotnet -CommandType Application).Source
$processInfo.UseShellExecute = $false
$processInfo.Arguments = "test `"$project`" -f net10.0 --filter FullyQualifiedName~Native_probe_is_denied_after_installing_the_account_fence"

try {
    $account = New-LocalUser -Name $name -Password $securePassword -Description 'Disposable HPD WFP integration test'
    Add-LocalGroupMember -SID ([Security.Principal.SecurityIdentifier]::new('S-1-5-32-545')) -Member $account
    New-Item -ItemType Directory -Path $directory | Out-Null
    $stagedProbe = Join-Path $directory 'WindowsSocketProbe.exe'
    Copy-Item -LiteralPath $probe -Destination $stagedProbe
    $processInfo.EnvironmentVariables['HPD_WINDOWS_SOCKET_PROBE'] = $stagedProbe
    if ($GitPath) {
        $processInfo.EnvironmentVariables['HPD_WINDOWS_FENCE_TEST_GIT'] = (Resolve-Path -LiteralPath $GitPath).ProviderPath
    }
    $processInfo.EnvironmentVariables['HPD_WINDOWS_FENCE_TEST_ACCOUNT'] = $name
    $processInfo.EnvironmentVariables['HPD_WINDOWS_FENCE_TEST_PASSWORD'] = $password
    $processInfo.EnvironmentVariables['DOTNET_ROLL_FORWARD'] = 'Major'
    $process = [Diagnostics.Process]::Start($processInfo)
    try {
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) {
            throw "Windows network fence integration test exited with code $($process.ExitCode)."
        }
    }
    finally {
        $process.Dispose()
    }
}
finally {
    try {
        if ($account) {
            Remove-LocalUser -SID $account.SID
        }
    }
    finally {
        if (Test-Path -LiteralPath $directory) {
            Remove-Item -LiteralPath (Join-Path $directory 'WindowsSocketProbe.exe') -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath $directory
        }
        $securePassword.Dispose()
    }
}
