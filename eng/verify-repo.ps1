$ErrorActionPreference = 'Stop'

# Scan every tracked text/source/config/document extension used by this repository.
# Binary media/archives are intentionally excluded; their provenance is locked by
# SHA-256 manifests and they must not contain deployable credentials.
$textExtensions = @(
    '.cs', '.csproj', '.props', '.targets', '.sln', '.xaml',
    '.json', '.yml', '.yaml', '.xml', '.config', '.toml',
    '.ps1', '.sh', '.cmd', '.bat',
    '.ts', '.tsx', '.js', '.jsx', '.py', '.sql',
    '.md', '.txt', '.html', '.css'
)

$patterns = @(
    'AKIA[0-9A-Z]{16}',
    'ASIA[0-9A-Z]{16}',
    '-----BEGIN (RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----',
    '(?i)"Password"\s*:\s*"(?!REPLACE-WITH|<redacted>|\$\{|development-user-secrets:)[^\"]{4,}"',
    '(?i)"ApiKey"\s*:\s*"(?!REPLACE-WITH|<redacted>|\$\{|development-user-secrets:)[^\"]{8,}"',
    '(?i)(Server|Data Source)=[^;\r\n]+;[^\r\n]*(Password|Pwd)=[^;<\r\n]{4,}',
    '(?i)Authorization\s*:\s*Bearer\s+(?!REPLACE-WITH|<redacted>|\$\{)[A-Za-z0-9._~-]{16,}'
)

$violations = New-Object System.Collections.Generic.List[string]
foreach ($file in (git ls-files)) {
    $extension = [System.IO.Path]::GetExtension($file).ToLowerInvariant()
    if ($textExtensions -notcontains $extension) { continue }

    try {
        $text = Get-Content -Raw -LiteralPath $file -ErrorAction Stop
    }
    catch {
        $violations.Add("$file could not be read by the text secret scanner: $($_.Exception.Message)")
        continue
    }

    foreach ($pattern in $patterns) {
        if ($text -match $pattern) {
            $violations.Add("$file matched forbidden credential/private-key pattern: $pattern")
        }
    }
}

if ($violations.Count -gt 0) {
    $violations | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Host 'PASS: no forbidden credential/private-key patterns detected in tracked text/source/config/reference files.'
