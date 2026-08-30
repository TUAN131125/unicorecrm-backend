<#
.SYNOPSIS
    Reproducible owner-local CommercialEvidence Original Core verification.

.DESCRIPTION
    Applies the real CommercialEvidence migration to an isolated SQL Server database and executes
    the internal application boundary through production DI. No HTTP route or foreign owner is used.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9_]{1,128}$')]
    [string] $DatabaseName,

    [string] $SqlServer = '(localdb)\MSSQLLocalDB',

    [switch] $KeepDatabase
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $repositoryRoot 'scripts/CommercialEvidenceOriginalCoreVerifier/UnicoreCRM.CommercialEvidence.Verifier.csproj'
$connectionString = "Server=$SqlServer;Database=$DatabaseName;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True"

try {
    Push-Location $repositoryRoot
    try {
        & dotnet run --project $project -- $connectionString
        if ($LASTEXITCODE -ne 0) { throw 'CommercialEvidence Original Core executable verifier failed.' }

        $env:ConnectionStrings__UnicoreCRM = $connectionString
        & dotnet ef migrations has-pending-model-changes `
            --project 'src/UnicoreCRM.CommercialEvidence' `
            --context CommercialEvidenceDbContext `
            --no-build
        if ($LASTEXITCODE -ne 0) { throw 'CommercialEvidence has pending EF model changes.' }

        $source = (Get-ChildItem -Path 'src/UnicoreCRM.CommercialEvidence' -Recurse -File -Filter '*.cs' |
            Where-Object { $_.FullName -notmatch '\\Migrations\\' } |
            Get-Content -Raw) -join "`n"
        $forbidden = @(
            'MapGet(', 'MapPost(', 'MapPut(', 'MapPatch(', 'MapDelete(',
            'OrdersDbContext', 'CustomersDbContext', 'ContactsDbContext', 'OrganizationsDbContext',
            'OutboxMessage', 'InboxMessage', 'DomainEvent', 'IntegrationEvent',
            'AppendExternalPurchase', 'AppendHistoricalPurchase', 'AppendReversal',
            'IEntityReader', 'IGenericRecordReader', 'ICrossOwnerReader', 'IUniversalLookup',
            'UnicoreCRM.Workflows', 'Orders.Infrastructure', 'Customers.Infrastructure',
            'Contacts.Infrastructure', 'Organizations.Infrastructure'
        )
        foreach ($token in $forbidden) {
            if ($source.IndexOf($token, [StringComparison]::Ordinal) -ge 0) {
                throw "Forbidden CommercialEvidence runtime surface found: $token"
            }
        }
        Write-Host 'PASS | static architecture forbidden-surface scan'
        Write-Host 'COMMERCIALEVIDENCE ORIGINAL OWNER-LOCAL CORE: PASS'
    }
    finally {
        Pop-Location
    }
}
finally {
    Remove-Item Env:ConnectionStrings__UnicoreCRM -ErrorAction SilentlyContinue
    if (-not $KeepDatabase) {
        $master = "Server=$SqlServer;Database=master;Trusted_Connection=True;TrustServerCertificate=True"
        $connection = New-Object System.Data.SqlClient.SqlConnection $master
        try {
            $connection.Open()
            $command = $connection.CreateCommand()
            $command.CommandText = "IF DB_ID('$DatabaseName') IS NOT NULL BEGIN ALTER DATABASE [$DatabaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$DatabaseName]; END"
            [void]$command.ExecuteNonQuery()
        }
        catch { Write-Warning "Could not drop isolated verifier database $DatabaseName : $_" }
        finally { $connection.Dispose() }
    }
}
