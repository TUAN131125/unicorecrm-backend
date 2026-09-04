<#
.SYNOPSIS
    Reproducible verification of the internal NURTURE Lead Qualification workflow.

.DESCRIPTION
    Applies the real Leads, Contacts, Tasks, AccessControl and Workflows migrations to an isolated
    SQL Server database, provisions real Workspace access through the production AccessControl
    contract, and drives the Workflows coordinator through production DI. The workflow has no HTTP
    route; public exposure remains blocked by the G-1 consent-transfer gate, and the run proves no
    qualification route exists.
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
$project = Join-Path $repositoryRoot 'scripts/LeadNurtureQualificationVerifier/UnicoreCRM.Workflows.NurtureVerifier.csproj'
$connectionString = "Server=$SqlServer;Database=$DatabaseName;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True"

try {
    Push-Location $repositoryRoot
    try {
        & dotnet run --project $project -- $connectionString
        if ($LASTEXITCODE -ne 0) { throw 'NURTURE Lead qualification workflow verifier failed.' }

        $env:ConnectionStrings__UnicoreCRM = $connectionString
        foreach ($context in @(
            @{ Project = 'src/UnicoreCRM.Workflows'; Context = 'WorkflowsDbContext' },
            @{ Project = 'src/UnicoreCRM.Crm';       Context = 'LeadsDbContext' },
            @{ Project = 'src/UnicoreCRM.Crm';       Context = 'ContactsDbContext' })) {
            & dotnet ef migrations has-pending-model-changes `
                --project $context.Project --context $context.Context --no-build
            if ($LASTEXITCODE -ne 0) { throw "$($context.Context) has pending EF model changes." }
            Write-Host "PASS | no pending EF model change: $($context.Context)"
        }

        # G-1 is closed, so NURTURE exposure is admitted. The other two qualification operations and
        # the retired generic one must still have no route.
        $endpoints = (Get-ChildItem -Path (Join-Path $repositoryRoot 'src') -Recurse -File -Filter '*Endpoints.cs' |
            Get-Content -Raw) -join "`n"
        foreach ($token in @('lead-qualification/{leadId}/opportunity', 'lead-qualification/{leadId}/direct-sale', '/leads/{leadId}/qualify')) {
            if ($endpoints.IndexOf($token, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                throw "An unadmitted qualification route was introduced: $token"
            }
        }
        Write-Host 'PASS | only the admitted NURTURE qualification route is exposed'

        Write-Host 'NURTURE LEAD QUALIFICATION WORKFLOW: PASS'
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
