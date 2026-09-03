[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CredentialsDirectory,
    [Parameter(Mandatory)][string]$ExpectedUserSid
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new()

if (-not (Test-Path -LiteralPath $CredentialsDirectory -PathType Container)) {
    throw "Credentials directory was not found: $CredentialsDirectory"
}

$currentUserSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
if (-not [string]::Equals($currentUserSid, $ExpectedUserSid, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Current user SID '$currentUserSid' does not match expected '$ExpectedUserSid'. Account isolation check must run as the expected user."
}

# DPAPI CurrentUser 암호문은 다른 Windows 사용자가 복호화할 수 없다.
# 이 검사는 같은 사용자로 실행될 때만 통과해야 하며, 다른 사용자로 실행되면 실패해야 한다.
$secretFiles = @(Get-ChildItem -LiteralPath $CredentialsDirectory -File -Filter '*.bin')
foreach ($file in $secretFiles) {
    $ciphertext = [IO.File]::ReadAllBytes($file.FullName)
    try {
        $plaintext = [Security.Cryptography.ProtectedData]::Unprotect(
            $ciphertext,
            $null,
            [Security.Cryptography.DataProtectionScope]::CurrentUser)
        Write-Host "Decrypted $($file.Name) as current user (expected for same-user check)."
    }
    catch {
        throw "DPAPI decryption failed for $($file.Name) as current user: $($_.Exception.Message)"
    }
}

# 상태 파일에 비밀값이 평문으로 남지 않았는지 확인한다.
$stateFile = Join-Path $CredentialsDirectory 'state.json'
if (Test-Path -LiteralPath $stateFile -PathType Leaf) {
    $state = Get-Content -LiteralPath $stateFile -Raw
    if ($state -match 'sessionKey=|__Secure-session=|access_token') {
        throw "Account state file contains a plaintext secret pattern."
    }
}

Write-Host "Account isolation check passed for user SID '$currentUserSid'."
exit 0
