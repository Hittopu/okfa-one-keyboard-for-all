$ErrorActionPreference = "Stop"

$capability = Get-WindowsCapability -Online |
    Where-Object { $_.Name -like "OpenSSH.Server*" } |
    Select-Object -First 1

if (-not $capability) {
    throw "OpenSSH.Server capability not found."
}

if ($capability.State -ne "Installed") {
    Add-WindowsCapability -Online -Name "OpenSSH.Server~~~~0.0.1.0" | Out-Null
}

Start-Service sshd
Set-Service -Name sshd -StartupType Automatic

if (-not (Get-NetFirewallRule -Name sshd -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule `
        -Name sshd `
        -DisplayName "OpenSSH Server" `
        -Enabled True `
        -Direction Inbound `
        -Protocol TCP `
        -Action Allow `
        -LocalPort 22 | Out-Null
}

Write-Output "OpenSSH Server ready."
Write-Output ("Hostname: {0}" -f $env:COMPUTERNAME)
Write-Output ("User: {0}" -f $env:USERNAME)
Write-Output "Port: 22"
Get-NetIPAddress -AddressFamily IPv4 |
    Where-Object {
        $_.IPAddress -notlike "169.254*" -and
        $_.IPAddress -ne "127.0.0.1"
    } |
    Select-Object InterfaceAlias, IPAddress |
    Format-Table -Auto
