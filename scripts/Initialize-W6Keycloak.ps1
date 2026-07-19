[CmdletBinding()]
param(
    [string] $KeycloakUrl = 'http://127.0.0.1:8082',
    [string] $AdminUsername = 'admin',
    [string] $AdminPassword = 'admin',
    [string] $Realm = 'campus-w6',
    [string] $WebBaseUrl = 'http://localhost:5018',
    [string] $BffBaseUrl = 'http://localhost:5019',
    [string] $TestPassword,
    [string] $WebClientSecret,
    [string] $BffClientSecret,
    [switch] $Force
)

$ErrorActionPreference = 'Stop'

function New-RandomHex([int] $ByteCount) {
    $bytes = [byte[]]::new($ByteCount)
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    return [Convert]::ToHexString($bytes).ToLowerInvariant()
}

function Set-JsonStringToken(
    [string] $Json,
    [string] $Token,
    [string] $Value
) {
    $encoded = [System.Text.Json.JsonSerializer]::Serialize(
        $Value,
        [string]
    )
    $encodedContent = $encoded.Substring(1, $encoded.Length - 2)
    return $Json.Replace($Token, $encodedContent)
}

if ([string]::IsNullOrWhiteSpace($TestPassword)) {
    $TestPassword = New-RandomHex 16
}
if ([string]::IsNullOrWhiteSpace($WebClientSecret)) {
    $WebClientSecret = New-RandomHex 32
}
if ([string]::IsNullOrWhiteSpace($BffClientSecret)) {
    $BffClientSecret = New-RandomHex 32
}

$repository = Split-Path -Parent $PSScriptRoot
$templatePath = Join-Path $repository 'deploy/keycloak/campus-w6-realm.template.json'
$realmJson = Get-Content -LiteralPath $templatePath -Raw
$replacements = [ordered]@{
    '__REALM__' = $Realm
    '__PASSWORD__' = $TestPassword
    '__WEB_SECRET__' = $WebClientSecret
    '__BFF_SECRET__' = $BffClientSecret
    '__WEB_BASE_URL__' = $WebBaseUrl.TrimEnd('/')
    '__BFF_BASE_URL__' = $BffBaseUrl.TrimEnd('/')
    '__BFF_SECOND_BASE_URL__' = $BffBaseUrl.TrimEnd('/')
    '__BFF_ACCESS_TOKEN_LIFESPAN__' = '300'
}
foreach ($replacement in $replacements.GetEnumerator()) {
    $realmJson = Set-JsonStringToken $realmJson $replacement.Key $replacement.Value
}

$keycloak = $KeycloakUrl.TrimEnd('/')
$token = Invoke-RestMethod `
    -Method Post `
    -Uri "$keycloak/realms/master/protocol/openid-connect/token" `
    -ContentType 'application/x-www-form-urlencoded' `
    -Body @{
        grant_type = 'password'
        client_id = 'admin-cli'
        username = $AdminUsername
        password = $AdminPassword
    }
$headers = @{ Authorization = "Bearer $($token.access_token)" }

$realmExists = $false
try {
    Invoke-RestMethod `
        -Method Get `
        -Uri "$keycloak/admin/realms/$Realm" `
        -Headers $headers | Out-Null
    $realmExists = $true
}
catch {
    if ($_.Exception.Response.StatusCode.value__ -ne 404) {
        throw
    }
}

if ($realmExists) {
    if (-not $Force) {
        throw "Realm '$Realm' already exists. Re-run with -Force to replace this local lab realm."
    }

    Invoke-RestMethod `
        -Method Delete `
        -Uri "$keycloak/admin/realms/$Realm" `
        -Headers $headers | Out-Null
}

Invoke-RestMethod `
    -Method Post `
    -Uri "$keycloak/admin/realms" `
    -Headers $headers `
    -ContentType 'application/json' `
    -Body $realmJson | Out-Null

Write-Host "Created Keycloak realm '$Realm' at $keycloak."
Write-Host "Lab users: alice, bob, admin-user, rate-user"
Write-Host "Lab password: $TestPassword"
Write-Host ''
Write-Host 'Set these only in the shells that launch the two applications:'
Write-Host "`$env:Security__WebClientSecret='$WebClientSecret'"
Write-Host "`$env:Bff__ClientSecret='$BffClientSecret'"
