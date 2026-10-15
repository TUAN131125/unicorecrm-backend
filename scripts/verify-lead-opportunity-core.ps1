param(
    [Parameter(Mandatory)][string] $ApiBaseUrl,
    [Parameter(Mandatory)][string] $DatabaseName,
    [Parameter(Mandatory)][string] $RunId,
    [Parameter(Mandatory)][string] $WorkspaceId,
    [Parameter(Mandatory)][string] $LeadId,
    [Parameter(Mandatory)][string] $DealId,
    [Parameter(Mandatory)][string] $ContactId
)
$ErrorActionPreference = 'Stop'
if ($RunId -notmatch '^[a-f0-9]{32}$' -or $DatabaseName -cne "UniCoreCRM_ConnectedAcceptance_$RunId") { throw 'Task-owned disposable database identity required.' }
foreach ($id in @($WorkspaceId, $LeadId, $DealId, $ContactId)) { if ($id -notmatch '^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$') { throw 'Invalid fixture reference.' } }
if ($ApiBaseUrl -notmatch '^http://127\.0\.0\.1:[0-9]+$') { throw 'Local task-owned ApiHost required.' }
if (-not $env:UNICORE_ACCEPTANCE_TOKEN) { throw 'Authorized fixture bearer must be supplied through the environment.' }
$marker = & sqlcmd -S localhost -E -C -d $DatabaseName -b -h -1 -W -Q "SET NOCOUNT ON; SELECT CONVERT(nvarchar(128),value) FROM sys.extended_properties WHERE class=0 AND name=N'UniCoreCRM_ConnectedAcceptance_RunId'"
if ($LASTEXITCODE -ne 0 -or ($marker -join '').Trim() -cne $RunId) { throw 'Disposable database marker mismatch.' }
$headers = @{ Authorization = "Bearer $env:UNICORE_ACCEPTANCE_TOKEN"; 'X-Workspace-Id' = $WorkspaceId; 'X-Request-Id' = [Guid]::NewGuid().ToString('N'); 'X-Correlation-Id' = [Guid]::NewGuid().ToString('N') }
$lead = Invoke-RestMethod -Uri "$ApiBaseUrl/leads/$LeadId" -Headers $headers
$deal = Invoke-RestMethod -Uri "$ApiBaseUrl/deals/$DealId" -Headers $headers
if ($lead.id -cne $LeadId -or $lead.leadWorkState -cne 'CLOSED' -or $lead.qualificationOutcome -cne 'OPPORTUNITY' -or $lead.dealRef -cne $DealId) { throw 'Canonical Lead outcome mismatch.' }
if ($lead.relationshipRef.type -cne 'CONTACT' -or $lead.relationshipRef.id -cne $ContactId -or $deal.buyerRef.type -cne 'CONTACT' -or $deal.buyerRef.id -cne $ContactId -or $deal.sourceLeadId -cne $LeadId) { throw 'Canonical relationship/reference mismatch.' }
$query = "SET NOCOUNT ON; SELECT d.DealId,d.QualificationSourceLeadId,d.Profile,a.LeadId,a.ContactId,a.DealId anchorDealId,a.Stage FROM deals.Deals d JOIN workflow.LeadQualificationAnchors a ON a.DealId=d.DealId WHERE d.WorkspaceId=N'$WorkspaceId' AND d.DealId=N'$DealId' AND a.LeadId=N'$LeadId' FOR JSON PATH"
$json = & sqlcmd -S localhost -E -C -d $DatabaseName -b -y 0 -w 65535 -Q $query
if ($LASTEXITCODE -ne 0) { throw 'Fixture SQL assertions failed.' }
$rows = ($json -join '') | ConvertFrom-Json
if (@($rows).Count -ne 1 -or $rows[0].ContactId -cne $ContactId -or $rows[0].Stage -cne 'Completed') { throw 'Workflow recovery anchor mismatch.' }
$profile = $rows[0].Profile | ConvertFrom-Json
if ($profile.amount.amount -cne $deal.amount.amount -or $profile.amount.currency -cne $deal.amount.currency -or (ConvertTo-Json @($profile.interestedProductIds) -Compress) -cne (ConvertTo-Json @($deal.interestedProductIds) -Compress)) { throw 'Exact monetary/product persistence mismatch.' }
@{ status = 'PASS'; execution = 'REAL_AUTHORIZED_HTTP_AND_SQL'; leadId = $LeadId; dealId = $DealId; contactId = $ContactId; amount = $deal.amount; productIds = @($deal.interestedProductIds); anchorStage = $rows[0].Stage } | ConvertTo-Json -Depth 8
