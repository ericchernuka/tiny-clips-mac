[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [switch]$Push
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($Version -notmatch '^v\d+\.\d+\.\d+$') {
    throw "Version must match v<major>.<minor>.<patch> (example: v1.0.8)."
}

$macTag = $Version
$windowsTag = "$Version-windows"

git rev-parse --is-inside-work-tree *> $null
if ($LASTEXITCODE -ne 0) {
    throw "This script must run inside a git repository."
}

function Test-LocalTagExists {
    param([Parameter(Mandatory = $true)][string]$Tag)

    git rev-parse -q --verify "refs/tags/$Tag" *> $null
    return $LASTEXITCODE -eq 0
}

function Test-RemoteTagExists {
    param([Parameter(Mandatory = $true)][string]$Tag)

    $matches = git ls-remote --tags origin "refs/tags/$Tag" 2>$null
    return -not [string]::IsNullOrWhiteSpace($matches)
}

foreach ($tag in @($macTag, $windowsTag)) {
    if (Test-LocalTagExists -Tag $tag) {
        throw "Tag '$tag' already exists locally."
    }
    if (Test-RemoteTagExists -Tag $tag) {
        throw "Tag '$tag' already exists on origin."
    }
}

if ($PSCmdlet.ShouldProcess($macTag, "Create annotated macOS tag")) {
    git tag -a $macTag -m "Release $macTag"
}

if ($PSCmdlet.ShouldProcess($windowsTag, "Create annotated Windows tag")) {
    git tag -a $windowsTag -m "Release $windowsTag"
}

if ($Push -and $PSCmdlet.ShouldProcess("origin", "Push tags $macTag and $windowsTag")) {
    git push origin $macTag $windowsTag
}

Write-Host "Created tags:"
Write-Host "- $macTag"
Write-Host "- $windowsTag"
if (-not $Push) {
    Write-Host "Push with: git push origin $macTag $windowsTag"
}
