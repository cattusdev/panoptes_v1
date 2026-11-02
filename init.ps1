<#
.SYNOPSIS
    Turns a fresh copy of Panoptes into a new project.

.DESCRIPTION
    1. Renames the solution, projects, folders, namespaces, database names and app name.
    2. Gives the project its own user-secrets id and its own ports.
    3. Optionally stores the database (and SMTP) settings in user secrets, creates the
       database and the first admin.
    4. Deletes this script and commits the result (when the folder is a git repository).

    Works in Windows PowerShell 5.1 and PowerShell 7 (pwsh) on Windows, macOS and Linux.

.EXAMPLE
    ./init.ps1
    Asks for everything.

.EXAMPLE
    ./init.ps1 -Name Acme.Portal -DisplayName "Acme Portal"

.EXAMPLE
    ./init.ps1 -Name Acme.Portal -SkipSetup
    Rename only: no database, no admin.
#>
[CmdletBinding()]
param(
    # C# style project name, e.g. Acme.Portal or AcmePortal
    [string]$Name,
    # Name people see (page titles, emails, authenticator apps), e.g. "Acme Portal"
    [string]$DisplayName,
    # Skips the database questions
    [string]$ConnectionString,
    [string]$AdminEmail,
    [securestring]$AdminPassword,
    # Rename only
    [switch]$SkipSetup,
    [switch]$SkipSmtp,
    [switch]$NoCommit,
    [switch]$KeepScript
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3
$OutputEncoding = New-Object System.Text.UTF8Encoding $false # text piped to dotnet (passwords may not be ASCII)

# The source project's name. One word stands for everything, so the meaning comes from the context:
# the rules below pick the database and display-name spots, every other "Panoptes" is the C# name
# and every "panoptes" (storage keys, npm package, file names) is the kebab-case name.
$TemplateName = 'Panoptes'
$TemplateLower = 'panoptes'

# Case-sensitive regexes, applied before the plain replacements
$CompactRules = @(
    '(?<=Database=)Panoptes',                 # connection strings
    'Panoptes(?=_E2E)'                        # test databases
)
$DisplayRules = @(
    'Panoptes(?=</title>)',                                                    # index.html, error page
    '(?<=(DefaultAppName|SenderName|appName)"?\s*[:=]\s*["''])Panoptes(?=["''])', # default branding, sender name, tests
    '(?<=UrlEncoder\.Default\.Encode\(")Panoptes(?="\))',                     # authenticator issuer
    '(?<=Is\.EqualTo\(")Panoptes(?="\))',                                      # tests on the app name
    '(?m)(?<=\A# )Panoptes(?=\r?$)'                                            # README / CLAUDE.md title
)

# Folders that are never touched (generated, or not ours)
$SkipFolders = @('.git', '.vs', '.idea', 'node_modules', 'bin', 'obj', 'App_Data', 'Logs', 'TestResults')

$Root = $PSScriptRoot

function Write-Step([string]$text) { Write-Host ''; Write-Host "==> $text" -ForegroundColor Cyan }
function Write-Note([string]$text) { Write-Host "    $text" -ForegroundColor DarkGray }

function Read-Value([string]$prompt, [string]$default) {
    $suffix = ''
    if ($default) { $suffix = " [$default]" }
    $value = Read-Host "$prompt$suffix"
    if ([string]::IsNullOrWhiteSpace($value)) { return $default }
    return $value.Trim()
}

function ConvertTo-Plain([securestring]$secure) {
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
}

# "Acme.Portal" -> Acme, Portal ; "MyHRApp" -> My, HRApp
function Get-Words([string]$value) {
    return @(($value -creplace '([a-z0-9])([A-Z])', '$1 $2') -split '[._ ]+' | Where-Object { $_ })
}

function Test-ProjectName([string]$value) {
    return $value -match '^[A-Za-z][A-Za-z0-9]*([._][A-Za-z][A-Za-z0-9]*)*$' -and
        $value -ne $TemplateName -and $value.Length -le 60
}

function Test-DisplayName([string]$value) {
    # Ends up inside C#/TypeScript/JSON strings and HTML: letters, digits, spaces, dots and dashes only
    return $value -match '^[\p{L}\p{N}][\p{L}\p{N} .\-]*$' -and $value.Length -le 40
}

function Get-ProjectItems([string]$folder) {
    foreach ($item in Get-ChildItem -LiteralPath $folder -Force) {
        if ($item.PSIsContainer) {
            if ($SkipFolders -notcontains $item.Name) {
                $item
                Get-ProjectItems $item.FullName
            }
        }
        else { $item }
    }
}

function Invoke-Dotnet([string[]]$arguments) {
    & dotnet @arguments | Out-Host
    return $LASTEXITCODE -eq 0
}

# --- Checks ---------------------------------------------------------------------------------

if (-not (Test-Path -LiteralPath (Join-Path $Root "$TemplateName.sln"))) {
    throw "$TemplateName.sln not found next to init.ps1. Run the script from the root of a fresh copy of $TemplateName (it may already have been initialized)."
}

# Never rename the original: a copy that still pushes to the Panoptes repository is probably the original
if ((Test-Path -LiteralPath (Join-Path $Root '.git')) -and (Get-Command git -ErrorAction SilentlyContinue)) {
    $origin = & git -C $Root config --get remote.origin.url
    if ($origin -and $origin -match '[/:]panoptes(\.git)?/?$') {
        throw "This folder still pushes to the $TemplateName repository ($origin). Run the script in a new repository (GitHub: Use this template), or point origin elsewhere first: git remote set-url origin <new-url> (or git remote remove origin)."
    }
}

# --- Names ----------------------------------------------------------------------------------

while (-not $Name -or -not (Test-ProjectName $Name)) {
    if ($Name) { Write-Host "  '$Name' isn't a valid name: use letters and digits, optionally split by dots (Acme.Portal) or underscores." -ForegroundColor Yellow }
    $Name = Read-Value 'Project name, C# style (e.g. Acme.Portal)' ''
}

$words = Get-Words $Name
$Compact = $Name -replace '[._]', ''
$Kebab = ($words -join '-').ToLowerInvariant()
if (-not $DisplayName) { $DisplayName = Read-Value 'App name people see' ($words -join ' ') }
while (-not (Test-DisplayName $DisplayName)) {
    Write-Host "  '$DisplayName' can only contain letters, digits, spaces, dots and dashes (max 40)." -ForegroundColor Yellow
    $DisplayName = Read-Value 'App name people see' ($words -join ' ')
}

# --- Ids and ports --------------------------------------------------------------------------

$webProjectFile = Join-Path $Root "$TemplateName/$TemplateName.csproj"
$launchSettingsFile = Join-Path $Root "$TemplateName/Properties/launchSettings.json"
$oldSecretsId = [regex]::Match((Get-Content -LiteralPath $webProjectFile -Raw), '<UserSecretsId>[^<]*?([0-9a-fA-F]{8}-[0-9a-fA-F-]{27})</UserSecretsId>').Groups[1].Value
$launch = Get-Content -LiteralPath $launchSettingsFile -Raw
$oldHttps = [regex]::Match($launch, '"https://localhost:(\d+);http://localhost:(\d+)"')
$oldIis = [regex]::Match($launch, '"iisExpress":\s*\{\s*"applicationUrl":\s*"http://localhost:(\d+)",\s*"sslPort":\s*(\d+)')
if (-not $oldSecretsId -or -not $oldHttps.Success -or -not $oldIis.Success) {
    throw "Could not read the UserSecretsId or the ports from $TemplateName. Has the project file or launchSettings.json changed?"
}

$httpsPort = Get-Random -Minimum 7100 -Maximum 7999
$httpPort = Get-Random -Minimum 5100 -Maximum 5999
$iisPort = Get-Random -Minimum 50000 -Maximum 59999
$iisSslPort = Get-Random -Minimum 44300 -Maximum 44399 # IIS Express only trusts this range for HTTPS

# Placeholders first, real values last, so a new name that contains "Panoptes" isn't replaced twice
$tokens = @{
    '<<init:name>>'    = $Name
    '<<init:compact>>' = $Compact
    '<<init:display>>' = $DisplayName
    '<<init:kebab>>'   = $Kebab
}

# Case-sensitive (old, new) pairs; order matters: ids and ports first, then the names
$replacements = @(
    @($oldSecretsId, [guid]::NewGuid().ToString()),
    @("localhost:$($oldHttps.Groups[1].Value)", "localhost:$httpsPort"),
    @("localhost:$($oldHttps.Groups[2].Value)", "localhost:$httpPort"),
    @("localhost:$($oldIis.Groups[1].Value)", "localhost:$iisPort"),
    @("`"sslPort`": $($oldIis.Groups[2].Value)", "`"sslPort`": $iisSslPort"),
    @($TemplateName, '<<init:name>>'),
    @($TemplateLower, '<<init:kebab>>')
)

Write-Host ''
Write-Host "  Project:   $Name" -ForegroundColor White
Write-Host "  App name:  $DisplayName"
Write-Host "  Database:  $Compact (tests: ${Compact}_E2E)"
Write-Host "  URL:       https://localhost:$httpsPort"

# --- Rename ---------------------------------------------------------------------------------

Write-Step 'Renaming'

# Build output still carries the old names; it's regenerated on the next build
foreach ($generated in @("$TemplateName/bin", "$TemplateName/obj", "$TemplateName/wwwroot/spa", "$TemplateName.E2E/bin", "$TemplateName.E2E/obj")) {
    $path = Join-Path $Root $generated
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
}

$items = @(Get-ProjectItems $Root)
$changedFiles = 0
foreach ($file in $items | Where-Object { -not $_.PSIsContainer }) {
    if ($file.FullName -eq $PSCommandPath -or $file.Length -gt 5MB) { continue }

    $bytes = [IO.File]::ReadAllBytes($file.FullName)
    if ($bytes.Length -eq 0 -or [Array]::IndexOf($bytes, [byte]0, 0, [Math]::Min(8000, $bytes.Length)) -ge 0) { continue } # binary

    $bom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    $offset = 0
    if ($bom) { $offset = 3 }
    $text = [Text.Encoding]::UTF8.GetString($bytes, $offset, $bytes.Length - $offset)

    # Template-only notes (README, CLAUDE.md, AGENTS.md) between <!-- template --> and <!-- /template -->
    $updated = [regex]::Replace($text, '(?s)<!-- template -->.*?<!-- /template -->(\r?\n){0,2}', '')
    foreach ($rule in $CompactRules) { $updated = [regex]::Replace($updated, $rule, '<<init:compact>>') }
    foreach ($rule in $DisplayRules) { $updated = [regex]::Replace($updated, $rule, '<<init:display>>') }
    foreach ($pair in $replacements) { $updated = $updated.Replace($pair[0], $pair[1]) }
    foreach ($token in $tokens.Keys) { $updated = $updated.Replace($token, $tokens[$token]) }

    if ($updated -cne $text) {
        [IO.File]::WriteAllText($file.FullName, $updated, (New-Object System.Text.UTF8Encoding $bom))
        $changedFiles++
    }
}

# Deepest paths first, so a folder is renamed after everything inside it
$renamed = 0
foreach ($item in $items | Where-Object { $_.Name.Contains($TemplateName) } | Sort-Object { $_.FullName.Length } -Descending) {
    Rename-Item -LiteralPath $item.FullName -NewName $item.Name.Replace($TemplateName, $Name)
    $renamed++
}
Write-Note "$changedFiles files updated, $renamed files and folders renamed."

$webProject = Join-Path $Root "$Name/$Name.csproj"

# --- Setup ----------------------------------------------------------------------------------

$databaseReady = $false
$adminCreated = $false

if (-not $SkipSetup) {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'The .NET SDK (dotnet) is needed for the setup. Install it, or run the script with -SkipSetup.' }
    if (-not (Get-Command npm -ErrorAction SilentlyContinue)) { throw 'Node.js (npm) is needed to build the React app. Install it, or run the script with -SkipSetup.' }

    Write-Step 'Settings (stored in user secrets on this machine, never in the repository)'
    $secrets = [ordered]@{}

    if (-not $ConnectionString) {
        Write-Note 'PostgreSQL connection. The database is created if it does not exist.'
        $builder = New-Object System.Data.Common.DbConnectionStringBuilder
        $builder['Host'] = Read-Value '  Host' 'localhost'
        $builder['Port'] = Read-Value '  Port' '5432'
        $builder['Database'] = Read-Value '  Database' $Compact
        $builder['Username'] = Read-Value '  Username' 'postgres'
        $builder['Password'] = ConvertTo-Plain (Read-Host '  Password' -AsSecureString)
        # get_ConnectionString(): PowerShell treats .ConnectionString on this dictionary type as a key, not the property
        $ConnectionString = $builder.get_ConnectionString()
    }
    $secrets['ConnectionStrings:DefaultConnection'] = $ConnectionString

    if (-not $SkipSmtp) {
        Write-Note 'Emails can also be set up later in the app: Settings > Email.'
        $answer = Read-Value 'Set up an SMTP server for sending emails now? (y/N)' 'n'
        if ($answer -match '^(y|yes)$') {
            $secrets['SmtpSettings:Server'] = Read-Value '  SMTP server (Mailpit: localhost)' ''
            $secrets['SmtpSettings:Port'] = Read-Value '  Port (Mailpit: 1025)' '587'
            $secrets['SmtpSettings:Security'] = Read-Value '  Security: None, StartTls, SslOnConnect or Auto (Mailpit: None)' 'StartTls'
            $secrets['SmtpSettings:SenderEmail'] = Read-Value '  Sender email' ''
            $secrets['SmtpSettings:SenderName'] = Read-Value '  Sender name' $DisplayName
            $username = Read-Value '  Username (empty for none)' ''
            if ($username) {
                $secrets['SmtpSettings:Username'] = $username
                $secrets['SmtpSettings:Password'] = ConvertTo-Plain (Read-Host '  Password' -AsSecureString)
            }
        }
        else {
            Write-Note 'Skipped: in Development, emails are written to App_Data/emails instead of being sent.'
        }
    }

    # Piped as JSON so passwords never appear on a command line
    $secrets | ConvertTo-Json | & dotnet user-secrets set --project $webProject | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Saving the user secrets failed.' }
    Write-Note "Saved: $($secrets.Keys -join ', ')"
    $target = New-Object System.Data.Common.DbConnectionStringBuilder
    $target.set_ConnectionString($ConnectionString)
    Write-Note "Database: $($target['Database']) on $($target['Host'])"

    $previousEnvironment = $env:ASPNETCORE_ENVIRONMENT
    $env:ASPNETCORE_ENVIRONMENT = 'Development' # user secrets are only read in Development
    try {
        Write-Step 'Building and creating the database (the first build installs the React packages, this takes a minute)'
        $databaseReady = Invoke-Dotnet @('run', '--project', $webProject, '--no-launch-profile', '--', 'db:migrate')
        if (-not $databaseReady) {
            Write-Host '    The database step failed (see above). Fix the connection and run it again:' -ForegroundColor Yellow
            Write-Note "dotnet user-secrets set ConnectionStrings:DefaultConnection `"Host=...;Database=$Compact;Username=...;Password=...`" --project $Name"
            Write-Note "dotnet run --project $Name -- db:migrate"
        }

        if ($databaseReady) {
            Write-Step 'First admin'
            if (-not $AdminEmail) { $AdminEmail = Read-Value 'Admin email (empty to skip)' '' }
            while ($AdminEmail -and -not $adminCreated) {
                if (-not $AdminPassword) {
                    $AdminPassword = Read-Host '  Password' -AsSecureString
                    if ((ConvertTo-Plain $AdminPassword) -cne (ConvertTo-Plain (Read-Host '  Repeat password' -AsSecureString))) {
                        Write-Host "    The passwords don't match." -ForegroundColor Yellow
                        $AdminPassword = $null
                        continue
                    }
                }

                $env:ADMIN_PASSWORD = ConvertTo-Plain $AdminPassword
                try { $adminCreated = Invoke-Dotnet @('run', '--project', $webProject, '--no-launch-profile', '--no-build', '--', 'admin:create', $AdminEmail) }
                finally { Remove-Item Env:ADMIN_PASSWORD -ErrorAction SilentlyContinue }

                if (-not $adminCreated) {
                    $AdminPassword = $null
                    $retry = Read-Value 'Try another password? (Y/n)' 'y'
                    if ($retry -notmatch '^(y|yes)$') { break }
                }
            }
            if (-not $adminCreated) { Write-Note "Later: dotnet run --project $Name -- admin:create you@example.com" }
        }
    }
    finally {
        $env:ASPNETCORE_ENVIRONMENT = $previousEnvironment
    }
}

# --- Finish ---------------------------------------------------------------------------------

if (-not $KeepScript) { Remove-Item -LiteralPath $PSCommandPath -Force }

$committed = $false
if (-not $NoCommit -and (Test-Path -LiteralPath (Join-Path $Root '.git')) -and (Get-Command git -ErrorAction SilentlyContinue)) {
    Write-Step 'Committing'
    & git -C $Root add -A
    & git -C $Root commit -q -m "Start $DisplayName from $TemplateName"
    $committed = $LASTEXITCODE -eq 0
    if (-not $committed) { Write-Host '    The commit failed (see above); the changes are in the working tree.' -ForegroundColor Yellow }
}

Write-Host ''
Write-Host "Done: $DisplayName is ready." -ForegroundColor Green
Write-Host ''
Write-Host 'Next:'
if ($SkipSetup) {
    Write-Host "  cd $Name"
    Write-Host "  dotnet user-secrets set ConnectionStrings:DefaultConnection `"Host=localhost;Database=$Compact;Username=postgres;Password=...`""
    Write-Host '  dotnet run -- db:migrate'
    Write-Host '  dotnet run -- admin:create you@example.com'
    Write-Host '  dotnet run --launch-profile https'
}
else {
    Write-Host "  cd $Name; dotnet run --launch-profile https    -> https://localhost:$httpsPort"
}
if ($committed) { Write-Host '  git push' }
Write-Host "  Tests: dotnet test $Name.E2E  (uses a separate ${Compact}_E2E database)"
