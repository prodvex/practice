param(
    [Parameter(Mandatory)][ValidateSet('EnableHypervisor','InstallLocalDB')][string]$Action
)

$ErrorActionPreference = 'Stop'
$powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'

if ($Action -eq 'EnableHypervisor') {
    $operation = {
        $ErrorActionPreference = 'Stop'
        try {
            $computer = Get-CimInstance Win32_ComputerSystem
            $processors = @(Get-CimInstance Win32_Processor)
            if (!$computer.HypervisorPresent -and $processors.Count -gt 0 -and
                @($processors | Where-Object { $_.VirtualizationFirmwareEnabled }).Count -eq 0) {
                exit 11
            }
            $feature = Get-WindowsOptionalFeature -Online -FeatureName HypervisorPlatform
            if ($feature.State -eq 'EnablePending') { exit 10 }
            $restart = $false
            if ($feature.State -ne 'Enabled') {
                $change = Enable-WindowsOptionalFeature -Online -FeatureName HypervisorPlatform -All -NoRestart
                $restart = $change.RestartNeeded
            }
            $bcd = Join-Path $env:SystemRoot 'System32\bcdedit.exe'
            $configuration = & $bcd /enum '{current}'
            if ($LASTEXITCODE -eq 0 -and ($configuration -match '^hypervisorlaunchtype\s+Off\s*$')) {
                & $bcd /set '{current}' hypervisorlaunchtype Auto | Out-Null
                if ($LASTEXITCODE -ne 0) { exit 1 }
                $restart = $true
            }
            if ($restart) { exit 10 }
            exit 0
        } catch { exit 1 }
    }
    $payload = $operation.ToString()
} else {
    $package = (Resolve-Path -LiteralPath $env:GAMINGCLUB_LOCALDB_MSI).Path
    $escapedPackage = $package.Replace("'", "''")
    $redistPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'runtime/installers/vc_redist.x64.exe'
    $escapedRedist = $redistPath.Replace("'", "''")
    $operation = {
        $ErrorActionPreference = 'Stop'
        try {
            if ((Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash -ne
                '224D483992EF60368DAC70CEA174DCFAF43A3CA06ADA331C67DC6119A26490F6') { exit 13 }
            $signature = Get-AuthenticodeSignature -LiteralPath $package
            if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch
                '^CN=Microsoft Corporation,') { exit 13 }
            $logs = Split-Path -Parent $package
            $key = 'HKLM:\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64'
            $version = Get-ItemProperty -LiteralPath $key -ErrorAction SilentlyContinue
            if ($version.Installed -ne 1 -or [version]($version.Version -replace '^v','') -lt [version]'14.34') {
                if ((Get-FileHash -LiteralPath $redist -Algorithm SHA256).Hash -ne
                    '9C02C9ED120F671B900E6B9471D890E3B2414117A7E53F4AE2E855466FA2B7C9') { exit 13 }
                $signature = Get-AuthenticodeSignature -LiteralPath $redist
                if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch
                    '^CN=Microsoft Corporation,') { exit 13 }
                $redistLog = Join-Path $logs 'vc-redist.log'
                $result = Start-Process -FilePath $redist -ArgumentList "/install /quiet /norestart /log ""$redistLog""" -Wait -PassThru -WindowStyle Hidden
                if ($result.ExitCode -ne 0) { exit $result.ExitCode }
            }
            $log = Join-Path $logs 'localdb-install.log'
            $msiexec = Join-Path $env:SystemRoot 'System32\msiexec.exe'
            $arguments = '/i "{0}" /quiet /norestart /l*v "{1}" IACCEPTSQLLOCALDBLICENSETERMS=YES' -f $package,$log
            $installation = Start-Process -FilePath $msiexec -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
            exit $installation.ExitCode
        } catch { exit 1 }
    }
    $payload = '$package = ' + "'" + $escapedPackage + "'" + [Environment]::NewLine +
        '$redist = ' + "'" + $escapedRedist + "'" + [Environment]::NewLine + $operation.ToString()
}

$encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($payload))
try {
    $arguments = '-NoProfile','-NonInteractive','-EncodedCommand',$encoded
    $child = Start-Process -FilePath $powershell -Verb RunAs -WindowStyle Hidden -Wait -PassThru -ArgumentList $arguments
    exit $child.ExitCode
} catch {
    [Console]::Error.WriteLine('Windows did not approve or start the elevated setup.')
    exit 1223
}
