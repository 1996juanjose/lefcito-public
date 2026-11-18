param(
    [string]$PostgresBin = 'C:/Program Files/PostgreSQL/17/bin',
    [int]$PostgresPort = 55439,
    [int]$WebPort = 5180
)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    $local = Join-Path $PSScriptRoot '.local'
    $cluster = Join-Path $local 'postgres'
    $credential = Join-Path $local 'postgres.password'
    $pgCtl = Join-Path $PostgresBin 'pg_ctl.exe'
    if (!(Test-Path -LiteralPath $pgCtl)) {
        throw 'Indica la carpeta bin de PostgreSQL con -PostgresBin.'
    }
    New-Item -ItemType Directory -Path $local -Force | Out-Null
    if (!(Test-Path -LiteralPath $credential)) {
        [IO.File]::WriteAllText($credential, [guid]::NewGuid().ToString('N'))
    }

    if (!(Test-Path -LiteralPath (Join-Path $cluster 'PG_VERSION'))) {
        $init = Join-Path $PostgresBin 'initdb.exe'
        & $init -D $cluster -A scram-sha-256 -U lefcito "--pwfile=$credential" --encoding=UTF8
        if ($LASTEXITCODE -ne 0) { throw 'No se pudo inicializar el PostgreSQL local.' }
    }

    & $pgCtl -D $cluster status *> $null
    $startedHere = $LASTEXITCODE -ne 0
    if ($startedHere) {
        & $pgCtl -D $cluster -l (Join-Path $local 'postgres.log') -o "-h 127.0.0.1 -p $PostgresPort" -w start
        if ($LASTEXITCODE -ne 0) { throw 'No se pudo iniciar el PostgreSQL local.' }
    }

    $env:ConnectionStrings__Postgres = "Host=127.0.0.1;Port=$PostgresPort;Database=lefcito;Username=lefcito;Password=$([IO.File]::ReadAllText($credential))"
    try {
        dotnet run --no-launch-profile -- --init-db
        if ($LASTEXITCODE -ne 0) { throw 'No se pudo inicializar la base Lefcito.' }
        dotnet run --no-launch-profile --no-build -- --urls "http://127.0.0.1:$WebPort"
    } finally {
        Remove-Item Env:ConnectionStrings__Postgres -ErrorAction SilentlyContinue
        if ($startedHere) { & $pgCtl -D $cluster -m fast -w stop }
    }
} finally {
    Pop-Location
}
