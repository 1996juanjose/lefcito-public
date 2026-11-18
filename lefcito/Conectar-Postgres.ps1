param(
    [string]$DatabaseHost = '127.0.0.1',
    [int]$DatabasePort = 5432,
    [string]$Username = 'postgres',
    [string]$Database = 'lefcito',
    [string]$RootCertificate,
    [ValidateRange(1024, 65535)][int]$WebPort = 5182,
    [switch]$TunelSsh,
    [switch]$SinTls,
    [switch]$IniciarWeb,
    [switch]$Inicializar
)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
$previous = $env:ConnectionStrings__Postgres
try {
    if ($TunelSsh -and $SinTls) { throw 'Elige -TunelSsh o -SinTls.' }
    if ($TunelSsh -and $DatabaseHost -ne '127.0.0.1') {
        throw 'Para -TunelSsh usa -DatabaseHost 127.0.0.1 y el puerto local del túnel.'
    }
    $secure = Read-Host 'Contraseña de PostgreSQL' -AsSecureString
    if ($secure.Length -eq 0) { throw 'Escribe la contraseña en PowerShell.' }

    $connection = New-Object System.Data.Common.DbConnectionStringBuilder
    $connection['Host'] = $DatabaseHost
    $connection['Port'] = $DatabasePort
    $connection['Database'] = $Database
    $connection['Username'] = $Username
    $connection['SSL Mode'] = if ($TunelSsh -or $SinTls) { 'Disable' } else { 'VerifyFull' }
    $connection['Timeout'] = 8
    if ($RootCertificate) { $connection['Root Certificate'] = (Resolve-Path -LiteralPath $RootCertificate).Path }
    $connection['Password'] = [System.Net.NetworkCredential]::new('', $secure).Password
    $env:ConnectionStrings__Postgres = $connection.ConnectionString
    $connection.Clear()
    $secure.Dispose()

    $output = Join-Path $PSScriptRoot '.local/remote-app'
    dotnet build --output $output
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo compilar Lefcito.' }
    $backend = Join-Path $output 'Lefcito.dll'
    dotnet $backend --check-db
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo verificar PostgreSQL.' }

    if ($Inicializar) {
        dotnet $backend --init-db
        if ($LASTEXITCODE -ne 0) { throw 'No se pudo inicializar Lefcito en PostgreSQL.' }
    }
    if ($IniciarWeb) {
        dotnet $backend --urls "http://127.0.0.1:$WebPort"
        if ($LASTEXITCODE -ne 0) { throw 'No se pudo iniciar la web.' }
    }
} finally {
    $env:ConnectionStrings__Postgres = $previous
    Pop-Location
}
