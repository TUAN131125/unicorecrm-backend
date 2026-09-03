<#
.SYNOPSIS
    Reproducible owner-local verification of the Contacts Lead qualification participant.

.DESCRIPTION
    Applies the real Contacts and AccessControl migrations to an isolated SQL Server database,
    provisions real initial Workspace access through the production AccessControl contract, and
    executes the internal participant boundary through production DI. The boundary has no HTTP route
    and none is created; the run additionally proves no public Contact mutation route exists.
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
$project = Join-Path $repositoryRoot 'scripts/ContactQualificationParticipantVerifier/UnicoreCRM.Contacts.QualificationVerifier.csproj'
$connectionString = "Server=$SqlServer;Database=$DatabaseName;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True"

try {
    Push-Location $repositoryRoot
    try {
        & dotnet run --project $project -- $connectionString
        if ($LASTEXITCODE -ne 0) { throw 'Contact qualification participant executable verifier failed.' }

        $env:ConnectionStrings__UnicoreCRM = $connectionString
        & dotnet ef migrations has-pending-model-changes `
            --project 'src/UnicoreCRM.Crm' `
            --context ContactsDbContext `
            --no-build
        if ($LASTEXITCODE -ne 0) { throw 'Contacts has pending EF model changes.' }
        Write-Host 'PASS | no pending Contacts EF model change'

        # The public Contacts surface must remain exactly the two admitted reads.
        $endpoints = Get-Content -Raw -Path (Join-Path $repositoryRoot 'src/UnicoreCRM.Crm/Contacts/Contracts/ContactsEndpoints.cs')
        foreach ($verb in @('MapPost(', 'MapPut(', 'MapPatch(', 'MapDelete(')) {
            if ($endpoints.IndexOf($verb, [StringComparison]::Ordinal) -ge 0) {
                throw "A public Contact mutation route was introduced: $verb"
            }
        }
        Write-Host 'PASS | no public Contact mutation route'

        Write-Host 'CONTACT QUALIFICATION PARTICIPANT: PASS'
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
