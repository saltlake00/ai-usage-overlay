[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ArtifactDirectory,
    [Parameter(Mandatory)][string]$SyntheticMarkerFile
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new()

if (-not (Test-Path -LiteralPath $ArtifactDirectory -PathType Container)) {
    throw "Artifact directory was not found: $ArtifactDirectory"
}
if (-not (Test-Path -LiteralPath $SyntheticMarkerFile -PathType Leaf)) {
    throw "Synthetic marker file was not found: $SyntheticMarkerFile"
}

# The marker file contains only a synthetic test secret. Never pass a real secret on the command line.
$marker = Get-Content -LiteralPath $SyntheticMarkerFile -Raw
if ([string]::IsNullOrWhiteSpace($marker)) {
    throw "Synthetic marker file is empty: $SyntheticMarkerFile"
}

$violations = @()

# Allow-list: only executables, installer, licenses, notices, and checksums may be staged.
$allowedNames = @(
    'AIUsageOverlay-Setup-*.exe',
    'AIUsageOverlay-Portable-*.exe',
    'LICENSE',
    'THIRD-PARTY-NOTICES.md',
    'Win-CodexBar-MIT.txt',
    'SHA256SUMS.txt'
)
$actualFiles = @(Get-ChildItem -LiteralPath $ArtifactDirectory -File)
foreach ($file in $actualFiles) {
    $matched = $false
    foreach ($pattern in $allowedNames) {
        if ($file.Name -like $pattern) {
            $matched = $true
            break
        }
    }
    if (-not $matched) {
        $violations += "Unexpected artifact file: $($file.Name)"
    }
}

# Scan staged artifacts for the synthetic marker.
foreach ($file in $actualFiles) {
    $content = Get-Content -LiteralPath $file.FullName -Raw -ErrorAction SilentlyContinue
    if ($null -ne $content -and $content.Contains($marker)) {
        $violations += "Synthetic marker leaked into artifact: $($file.Name)"
    }
}

# Scan source/resource files for sensitive secret patterns.
$sourceRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$sensitivePatterns = @(
    'sessionKey=',
    '__Secure-session=',
    'CLAUDE_AI_SESSION_KEY',
    'OLLAMA_SESSION_COOKIE',
    'access_token'
)
$sourceFiles = @(Get-ChildItem -LiteralPath $sourceRoot -Recurse -File |
    Where-Object {
        $_.Extension -in @('.cs', '.json', '.ps1', '.md', '.xaml') -and
        $_.FullName -notmatch '\\(bin|obj|out|\.git)\\' -and
        $_.Name -ne 'DpapiAccountSecretStoreTests.cs' -and
        $_.Name -ne 'AccountConnectionServiceTests.cs' -and
        $_.Name -ne 'AccountsViewModelTests.cs' -and
        $_.Name -ne 'OllamaUsageClientTests.cs' -and
        $_.Name -ne 'ClaudeCredentialSourceTests.cs' -and
        $_.Name -ne 'OllamaCredentialSourceTests.cs'
    })
foreach ($file in $sourceFiles) {
    $content = Get-Content -LiteralPath $file.FullName -Raw -ErrorAction SilentlyContinue
    if ($null -eq $content) { continue }
    foreach ($pattern in $sensitivePatterns) {
        if ($content.Contains($pattern)) {
            $violations += "Sensitive pattern '$pattern' in source: $($file.FullName)"
            break
        }
    }
}

if ($violations.Count -gt 0) {
    $violations | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Host "Privacy check passed for $($actualFiles.Count) artifact files."
exit 0
