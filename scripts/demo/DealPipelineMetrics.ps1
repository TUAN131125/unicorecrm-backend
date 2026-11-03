function Test-DealPipelineMetrics {
    $samples=[Collections.Generic.List[object]]::new()
    foreach($workspaceId in @('ws_f2332a12d56a421d8a722bd2f65c5efb','ws_811689cfc4b446eab4f29dc109e2abf2')){
        $script:workspace=$workspaceId
        for($index=0;$index -lt 3;$index++){
            $logPath=Join-Path $script:stateDirectory 'host.log'
            $startText=Get-Content -Raw $logPath
            $timer=[Diagnostics.Stopwatch]::StartNew()
            $response=Invoke-PipelineApi GET '/crm-configuration/pipelines'
            $timer.Stop()
            Assert-Demo ($response.status -eq 200) 'METRIC_READ_FAILED' 'Authoritative registry read required'
            Start-Sleep -Milliseconds 100
            $text=Get-Content -Raw $logPath
            $delta=$text.Substring($startText.Length)
            $selects=[regex]::Matches($delta,'FROM \[deals\]\.\[Pipelines\]').Count
            Assert-Demo ($selects -eq 1) 'REGISTRY_QUERY_COUNT' 'One registry SELECT per request; no per-stage or per-card queries'
            $samples.Add(@{workspaceId=$workspaceId;pipelines=$response.body.items.Count;stages=@($response.body.items|ForEach-Object {$_.stages}).Count;milliseconds=[math]::Round($timer.Elapsed.TotalMilliseconds,2);registrySelects=$selects;totalSqlCommands=[regex]::Matches($delta,'Executed DbCommand').Count})
        }
    }
    $script:tables=@{Lead='leads.Leads';Contact='contacts.Contacts';Organization='organizations.Organizations';Customer='customers.Customers';Deal='deals.Deals';Task='tasks.Tasks';Activity='tasks.Activities';Product='products.Products';Support='support.SupportCases'}
    $counts=Get-DemoCounts
    $prior=Get-Content -Raw (Join-Path (Split-Path $repo) 'frontend/unicorecrm-web/docs/verification/evidence/deal-workspace/after-data-preservation.json')|ConvertFrom-Json -AsHashtable
    $compared=@(foreach($old in $prior.after|Where-Object {$_.module -ne 'Deal'}){
        $current=$counts|Where-Object {$_.module -eq $old.module -and $_.workspaceId -ceq $old.workspaceId}
        Assert-Demo ($current.count -eq $old.count -and $current.hash -ceq $old.hash) 'ORIGINAL_CRM_CHANGED' "$($old.module) $($old.workspaceId)"
        @{module=$old.module;workspaceId=$old.workspaceId;count=$current.count;hashPreserved=$true}
    })
    Verify-LegacyDeals
    $stageCounts=@((Invoke-DemoSql 'SELECT WorkspaceId,PipelineId,StageCode,StageCategory,COUNT(*) Count FROM deals.Deals GROUP BY WorkspaceId,PipelineId,StageCode,StageCategory ORDER BY WorkspaceId,PipelineId,StageCode').Rows|ForEach-Object {@{workspaceId=[string]$_.WorkspaceId;pipelineId=[string]$_.PipelineId;stageCode=[string]$_.StageCode;stageCategory=[string]$_.StageCategory;count=[int]$_.Count}})
    $pipelineCounts=@((Invoke-DemoSql 'SELECT WorkspaceId,COUNT(*) Pipelines,SUM(CASE WHEN IsDefault=1 AND IsActive=1 THEN 1 ELSE 0 END) ActiveDefaults FROM deals.Pipelines GROUP BY WorkspaceId ORDER BY WorkspaceId').Rows|ForEach-Object {@{workspaceId=[string]$_.WorkspaceId;pipelines=[int]$_.Pipelines;activeDefaults=[int]$_.ActiveDefaults}})
    Assert-Demo (@($pipelineCounts|Where-Object activeDefaults -ne 1).Count -eq 0) 'DEFAULT_INVARIANT' 'Every initialized workspace must retain exactly one active default'
    @{status='PASS';samples=$samples;originalCrm=$compared;currentCounts=$counts;pipelineCounts=$pipelineCounts;stageCounts=$stageCounts;legacyDealVerification='migration-preservation.json';loadTest='NOT_RUN'}|ConvertTo-Json -Depth 12|Set-Content (Join-Path $script:stateDirectory 'metrics-and-preservation.json')
    Write-Host 'PASS: measured one registry SELECT per read, original CRM counts/hashes and all original Deal states preserved'
}
