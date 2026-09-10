$ErrorActionPreference = 'Stop'

$allowedExtensions = @('.json', '.cs', '.yml', '.yaml', '.props', '.targets', '.csproj', '.xaml')
$patterns = @(
    'AKIA[0-9A-Z]{16}',
    '-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----',
    '(?i)"Password"\s*:\s*"(?!REPLACE-WITH|<redacted>|\$\{)[^\"]{4,}"',
    '(?i)"ApiKey"\s*:\s*"(?!REPLACE-WITH|<redacted>|\$\{)[^\"]{8,}"'
)

$violations = New-Object System.Collections.Generic.List[string]
foreach ($file in (git ls-files)) {
    $extension = [System.IO.Path]::GetExtension($file)
    if ($allowedExtensions -notcontains $extension) { continue }
    $text = Get-Content -Raw -LiteralPath $file
    foreach ($pattern in $patterns) {
        if ($text -match $pattern) {
            $violations.Add("$file matched secret pattern: $pattern")
        }
    }
}

if ($violations.Count -gt 0) {
    $violations | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Host 'PASS: no forbidden credential/private-key patterns detected in tracked source/config files.'
