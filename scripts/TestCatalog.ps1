$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$scratch = Join-Path $root '_verify' 'publisher-cases'
$publisher = Join-Path $root 'scripts' 'BuildCatalog.cs'

function New-Fixture([string] $name) {
    $location = Join-Path $scratch $name
    New-Item -ItemType Directory -Path $location -Force | Out-Null
    foreach ($folder in @('apps', 'assets', 'schemas', 'recipes')) {
        Copy-Item (Join-Path $root $folder) (Join-Path $location $folder) -Recurse
    }
    return $location
}

function Assert-Rejected([string] $name, [scriptblock] $mutate) {
    $fixture = New-Fixture $name
    & $mutate $fixture
    dotnet run --file $publisher -- --root $fixture --output (Join-Path 'published' 'v1') `
        --revision local-fixture --generated-at 2026-10-27T14:17:27Z *> $null
    if ($LASTEXITCODE -eq 0) {
        throw "The publisher accepted invalid case '$name'."
    }
    Write-Output "Rejected $name."
}

Push-Location $root
try {
    if (Test-Path $scratch) { Remove-Item $scratch -Recurse -Force }
    New-Item -ItemType Directory -Path $scratch | Out-Null

    $valid = New-Fixture 'valid-control'
    dotnet run --file $publisher -- --root $valid --output (Join-Path 'published' 'v1') `
        --revision local-fixture --generated-at 2026-10-27T14:17:27Z *> $null
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path (Join-Path $valid 'published' 'v1' 'catalog.json'))) {
        throw 'Publisher control fixture could not build; rejection tests would be meaningless.'
    }

    Assert-Rejected 'duplicate-property' {
        param($fixture)
        $file = Join-Path $fixture 'apps' 'tray-converter.json'
        $json = [IO.File]::ReadAllText($file)
        [IO.File]::WriteAllText($file, $json.Replace('"id": "tray-converter",',
            '"id": "tray-converter", "id": "tray-converter",'))
    }
    Assert-Rejected 'duplicate-id' {
        param($fixture)
        Copy-Item (Join-Path $fixture 'apps' 'tray-converter.json') (Join-Path $fixture 'apps' 'clone.json')
    }
    Assert-Rejected 'missing-description' {
        param($fixture)
        $file = Join-Path $fixture 'apps' 'tray-converter.json'
        $app = Get-Content $file -Raw | ConvertFrom-Json -AsHashtable
        $null = $app.Remove('description')
        $app | ConvertTo-Json -Depth 20 | Set-Content $file
    }
    Assert-Rejected 'unsupported-status' {
        param($fixture)
        $file = Join-Path $fixture 'apps' 'tray-converter.json'
        $json = [IO.File]::ReadAllText($file)
        [IO.File]::WriteAllText($file, $json.Replace('"status": "active"', '"status": "unsupported"'))
    }
    Assert-Rejected 'cross-app-artwork' {
        param($fixture)
        $file = Join-Path $fixture 'apps' 'tray-converter.json'
        $json = [IO.File]::ReadAllText($file)
        [IO.File]::WriteAllText($file, $json.Replace('assets/tray-converter/icon.png',
            'assets/tray-radar/icon.png'))
    }
    Assert-Rejected 'cross-repository-feed' {
        param($fixture)
        $file = Join-Path $fixture 'apps' 'tray-converter.json'
        $app = Get-Content $file -Raw | ConvertFrom-Json -AsHashtable
        $app.channels = @(@{
            id = 'release'
            kind = 'gitHub'
            identityVerified = $true
            identity = @{ name = 'Example.App'; publisher = 'CN=Example' }
            appInstallerUrl = 'https://github.com/TheJoeFin/tray-converter/releases/latest/download/App.appinstaller'
        })
        $app | ConvertTo-Json -Depth 20 | Set-Content $file
    }
    Assert-Rejected 'store-product-mismatch' {
        param($fixture)
        $file = Join-Path $fixture 'apps' 'tray-converter.json'
        $app = Get-Content $file -Raw | ConvertFrom-Json -AsHashtable
        $app.channels = @(@{
            id = 'store'
            kind = 'store'
            storeProductId = '9TEST'
            storeUrl = 'https://apps.microsoft.com/detail/9OTHER'
        })
        $app | ConvertTo-Json -Depth 20 | Set-Content $file
    }
    Assert-Rejected 'executable-image' {
        param($fixture)
        [IO.File]::WriteAllText((Join-Path $fixture 'assets' 'tray-converter' 'icon.png'),
            '<script>alert(1)</script>')
    }
    Assert-Rejected 'unapproved-remix' {
        param($fixture)
        $file = Join-Path $fixture 'apps' 'tray-converter.json'
        $app = Get-Content $file -Raw | ConvertFrom-Json -AsHashtable
        $app.remix = @{
            recipeId = 'tray-converter'
            recipeVersion = '1.0.0'
            sourceRevision = 'fe46aaaef4bd871090b72b52e0f3c73809fb20b4'
            projectPath = 'TrayConverter.csproj'
        }
        $app | ConvertTo-Json -Depth 20 | Set-Content $file
    }
}
finally {
    Pop-Location
    if (Test-Path $scratch) { Remove-Item $scratch -Recurse -Force }
}
