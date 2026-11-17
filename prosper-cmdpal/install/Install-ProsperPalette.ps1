<#
.SYNOPSIS
  Installs the Prosper extension for PowerToys Command Palette from the build's .msix and .cer.

.DESCRIPTION
  Put this script, ProsperPalette-<x64|ARM64>.msix and ProsperPalette-<x64|ARM64>.cer in one folder and run:
      powershell -ExecutionPolicy Bypass -File .\Install-ProsperPalette.ps1
  The package is signed with a self-signed certificate made for that build. Windows only installs packages it
  trusts, so the certificate's public half goes into the machine's "Trusted People" store first; that one step needs
  administrator rights and the script asks for them (UAC). The extension itself installs for you only.

  Uninstall:  Get-AppxPackage ProsperPalette | Remove-AppxPackage
#>
[CmdletBinding()]
param(
    [string]$Package,
    [string]$Certificate
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

$arch = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'ARM64' } else { 'x64' }
if (-not $Package) {
    $Package = Join-Path $here "ProsperPalette-$arch.msix"
    if (-not (Test-Path $Package)) {
        $found = Get-ChildItem $here -Filter 'ProsperPalette*.msix' | Select-Object -First 1
        if ($found) { $Package = $found.FullName }
    }
}
if (-not $Package -or -not (Test-Path $Package)) { throw "No ProsperPalette .msix found next to this script." }
# Each architecture's package is signed with its own certificate: use the one that matches the package.
if (-not $Certificate) {
    $pkgArch = if ([IO.Path]::GetFileNameWithoutExtension($Package) -match '-(x64|ARM64)$') { $Matches[1] } else { $arch }
    $Certificate = Join-Path $here "ProsperPalette-$pkgArch.cer"
    if (-not (Test-Path $Certificate)) { $Certificate = Join-Path $here 'ProsperPalette.cer' }
}
if (-not (Test-Path $Certificate)) { throw "The package's certificate ($([IO.Path]::GetFileName($Certificate))) was not found next to this script." }

# 1. Trust the build's certificate (machine store: administrator, asked once through UAC).
$cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2 $Certificate
$trusted = Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object Thumbprint -eq $cert.Thumbprint
if (-not $trusted) {
    Write-Host "Trusting the Prosper package certificate ($($cert.Subject), $($cert.Thumbprint))..."
    $isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if ($isAdmin) {
        Import-Certificate -FilePath $Certificate -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
    } else {
        $cmd = "Import-Certificate -FilePath '$Certificate' -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null"
        $p = Start-Process powershell -Verb RunAs -Wait -PassThru -ArgumentList '-NoProfile', '-Command', $cmd
        if ($p.ExitCode -ne 0) { throw "The certificate wasn't trusted (exit code $($p.ExitCode))." }
    }
}

# 2. Install (or update) the extension for this user.
$existing = Get-AppxPackage -Name ProsperPalette
if ($existing) {
    Write-Host "Replacing the installed Prosper extension ($($existing.Version))..."
    $existing | Remove-AppxPackage
}
Write-Host "Installing $([IO.Path]::GetFileName($Package))..."
Add-AppxPackage -Path $Package

Write-Host ""
Write-Host "Done. Open Command Palette (Win+Alt+Space), type 'Reload' and choose 'Reload Command Palette extensions'," -ForegroundColor Green
Write-Host "then type 'Prosper settings' to paste your OpenAI API key." -ForegroundColor Green
