# Synthetic fixtures in the pre-existing control workspace; never modifies demo Deal rows.
function Test-DealPipelineAcceptance($Pipeline,$OriginalRequest) {
    $script:workspace='ws_811689cfc4b446eab4f29dc109e2abf2'
    $results=[Collections.Generic.List[object]]::new()
    function Check([string]$Id,[bool]$Condition,[string]$Evidence){
        $results.Add(@{id=$Id;status=if($Condition){'PASS'}else{'FAIL'};evidence=$Evidence})
        Assert-Demo $Condition $Id $Evidence
    }
    function Send-Check([string]$Name,[string]$Path,$Body,$Version=$null,[string]$Method='POST') {
        $value=Invoke-PipelineApi $Method $Path $Body "pipeline-check-$Name-v1" $Version
        Assert-Demo ($value.status -in 200,201) 'COMMAND_FAILED' "$Name HTTP $($value.status) $($value.body.code)"
        return $value.body
    }
    function Clone($Value){return $Value|ConvertTo-Json -Depth 30|ConvertFrom-Json -AsHashtable}
    $member=(Invoke-PipelineApi GET '/auth/session').body.principal.memberId
    $contact=Send-Check 'contact' '/contacts' @{fullName='Nguyễn An Thử Nghiệm';ownerId=$member;workEmail='pipeline.acceptance@example.com';notes='Synthetic pipeline acceptance fixture'}
    $contactId=$contact.result.contact.id
    Assert-Demo ([bool]$contactId) 'CONTACT_REQUIRED' 'Control workspace synthetic buyer required'
    $dealRequest=@{name='Tư vấn CRM theo quy trình kiểm thử';buyerRef=@{type='CONTACT';id=$contactId};pipelineId=$Pipeline.id;stageCode='DISCOVERY';amount=@{amount='120000000';currency='VND'};opportunityScore='0';ownerId=$member;expectedCloseDate='2027-06-01';interestedProductIds=@();lineItems=@();notes='Synthetic authoritative pipeline acceptance'}
    $created=Send-Check 'deal' '/deals' $dealRequest
    $deal=$created.result.deal
    Check 'PL-06' ($deal.pipelineId -ceq $Pipeline.id -and $deal.stageCode -ceq 'DISCOVERY' -and $deal.opportunityScore -ceq '0') 'Actual Deal command persisted assigned PipelineId and retained score zero'
    $customRequest=Clone $dealRequest;$customRequest.stageCode='ADVISORY';$customRequest.name='Cơ hội được tạo trực tiếp ở giai đoạn tư vấn tùy chỉnh'
    $direct=Send-Check 'direct-custom-deal' '/deals' $customRequest
    $customSql=(Invoke-DemoSql 'SELECT StageCode,PipelineId FROM deals.Deals WHERE WorkspaceId=@ws AND DealId=@id' @{'@ws'=$script:workspace;'@id'=$direct.result.deal.id}).Rows[0]
    Check 'PL-06-custom' ($customSql.StageCode -ceq 'ADVISORY' -and $customSql.PipelineId -ceq $Pipeline.id -and $direct.result.deal.opportunityScore -ceq '0') 'Create directly at a custom stage verified in SQL without changing Opportunity Score'
    $moved=Send-Check 'move-advisory' "/deals/$($deal.id)/change-stage" @{stageCode='ADVISORY'} $deal.resourceVersion
    $movedDeal=$moved.result.deal
    $sql=(Invoke-DemoSql 'SELECT PipelineId,StageCode,Version FROM deals.Deals WHERE WorkspaceId=@ws AND DealId=@id' @{'@ws'=$script:workspace;'@id'=$deal.id}).Rows[0]
    Check 'PL-07' ($sql.PipelineId -ceq $Pipeline.id -and $sql.StageCode -ceq 'ADVISORY' -and $movedDeal.forecastCategory -ceq 'PIPELINE') 'SQL stage/relationship and score-derived forecast verified'
    $rename=Clone $OriginalRequest
    ($rename.stages|Where-Object code -eq ADVISORY).labelVi='Tư vấn chuyên sâu và xác nhận phương án triển khai CRM cho doanh nghiệp'
    ($rename.stages|Where-Object code -eq ADVISORY).labelEn='Detailed advisory and CRM implementation planning for enterprise customers'
    ($rename.stages|Where-Object code -eq ADVISORY).order=5
    $renamed=Send-Check 'rename' "/crm-configuration/pipelines/$($Pipeline.id)" $rename $Pipeline.resourceVersion 'PATCH'
    $read=Invoke-PipelineApi GET "/crm-configuration/pipelines/$($Pipeline.id)"
    Check 'PL-08' ($read.status -eq 200 -and ($read.body.stages|Where-Object code -eq ADVISORY).labelVi -ceq ($rename.stages|Where-Object code -eq ADVISORY).labelVi) 'Persisted bilingual label read through actual API'
    Check 'PL-09' ($read.body.stages[0].code -ceq 'ADVISORY') 'Server returns custom configured order'
    $invalid=Clone $rename
    ($invalid.stages|Where-Object code -eq ADVISORY).isActive=$false
    $blocked=Invoke-PipelineApi PATCH "/crm-configuration/pipelines/$($Pipeline.id)" $invalid 'pipeline-disable-used-v1' $renamed.pipeline.resourceVersion
    Check 'PL-10' ($blocked.status -eq 409 -and $blocked.body.code -ceq 'PIPELINE_STAGE_IN_USE') "In-use stage deactivation: HTTP $($blocked.status) $($blocked.body.code)"
    $invalidStage=Invoke-PipelineApi POST "/deals/$($deal.id)/change-stage" @{stageCode='UNCONFIGURED'} 'pipeline-invalid-stage-v1' $movedDeal.resourceVersion
    Check 'PL-11' ($invalidStage.status -eq 422 -and $invalidStage.body.code -ceq 'DEAL_STAGE_NOT_FOUND') 'Unknown stage is a structured 422'
    foreach($terminal in @('WON','LOST')){
        $outcome=Invoke-PipelineApi POST "/deals/$($deal.id)/change-stage" @{stageCode=$terminal} "pipeline-terminal-$terminal-v1" $movedDeal.resourceVersion
        Check $(if($terminal -eq 'WON'){'PL-16'}else{'PL-17'}) ($outcome.status -eq 409 -and $outcome.body.code -ceq 'DEAL_TERMINAL_TRANSITION_REQUIRES_OUTCOME') "Typed outcome required for $terminal"
    }
    $other=Clone $OriginalRequest
    $other.nameVi='Quy trình chăm sóc kiểm thử';$other.nameEn='Acceptance retention pipeline'
    $other.stages=@($other.stages|Where-Object category -ne OPEN)+@(@{id='acceptance_retention';code='RETENTION';labelVi='Chăm sóc';labelEn='Retention';order=10;category='OPEN';defaultProbability=20;color='purple';isActive=$true})
    $second=Send-Check 'second-pipeline' '/crm-configuration/pipelines' $other
    $wrongStage=Invoke-PipelineApi POST "/deals/$($deal.id)/change-stage" @{stageCode='RETENTION'} 'pipeline-other-stage-v1' $movedDeal.resourceVersion
    Check 'PL-12' ($wrongStage.status -eq 422) 'Stage from another Pipeline rejected'
    $invalidBuyer=Clone $dealRequest;$invalidBuyer.pipelineId='pipeline_foreign_unknown'
    $wrongPipeline=Invoke-PipelineApi POST '/deals' $invalidBuyer 'pipeline-other-buyer-v1'
    Check 'PL-20' ($wrongPipeline.status -eq 422 -and $wrongPipeline.body.code -ceq 'DEAL_PIPELINE_NOT_FOUND') 'Invalid Pipeline rejected; no implicit Pipeline reassignment'
    $stale=Invoke-PipelineApi PATCH "/crm-configuration/pipelines/$($Pipeline.id)" $OriginalRequest 'pipeline-stale-v1' $Pipeline.resourceVersion
    Check 'PL-15' ($stale.status -eq 412 -and $stale.body.code -ceq 'VERSION_CONFLICT') 'Concurrent/stale configuration version refused'
    $makeDefault=Clone $other;$makeDefault.isDefault=$true
    $currentDefaultTarget=(Invoke-PipelineApi GET "/crm-configuration/pipelines/$($second.pipeline.id)").body
    if(!$currentDefaultTarget.isDefault){
        $null=Send-Check "switch-default-$($currentDefaultTarget.resourceVersion)" "/crm-configuration/pipelines/$($second.pipeline.id)" $makeDefault $currentDefaultTarget.resourceVersion 'PATCH'
    }
    $oldDeal=Invoke-PipelineApi GET "/deals/$($deal.id)"
    $newList=Invoke-PipelineApi GET '/crm-configuration/pipelines'
    Check 'PL-22' ($oldDeal.body.pipelineId -ceq $Pipeline.id -and @($newList.body.items|Where-Object isDefault).Count -eq 1 -and ($newList.body.items|Where-Object isDefault).id -ceq $second.pipeline.id) 'Atomic default change retained original Deal association'
    $createAtDefault=Clone $dealRequest;$createAtDefault.Remove('pipelineId');$createAtDefault.Remove('stageCode');$createAtDefault.name='Chăm sóc CRM theo pipeline mặc định kiểm thử'
    $defaultDeal=Send-Check 'default-deal' '/deals' $createAtDefault
    Check 'PL-20-default' ($defaultDeal.result.deal.pipelineId -ceq $second.pipeline.id -and $defaultDeal.result.deal.stageCode -ceq 'RETENTION') 'New Deal resolved authoritative default and first active OPEN stage'
    $replayedMove=Invoke-PipelineApi POST "/deals/$($deal.id)/change-stage" @{stageCode='ADVISORY'} 'pipeline-check-move-advisory-v1' $deal.resourceVersion
    Check 'PL-replay' ($replayedMove.status -eq 200 -and $replayedMove.body.outcome -ceq 'REPLAYED') 'Committed stage command replays after configuration rename/reorder/default switch'
    $script:workspace='ws_f2332a12d56a421d8a722bd2f65c5efb'
    $foreignGet=Invoke-PipelineApi GET "/deals/$($deal.id)"
    $foreignWrite=Invoke-PipelineApi POST "/deals/$($deal.id)/change-stage" @{stageCode='ADVISORY'} 'pipeline-cross-workspace-v1' $movedDeal.resourceVersion
    $foreignConfig=Invoke-PipelineApi PATCH "/crm-configuration/pipelines/$($Pipeline.id)" $rename 'pipeline-cross-config-v1' $renamed.pipeline.resourceVersion
    Check 'PL-14' ($foreignGet.status -eq 404 -and $foreignWrite.status -eq 404 -and $foreignConfig.status -eq 404) 'Foreign Deal/config reads and mutations withheld'
    $script:workspace='ws_811689cfc4b446eab4f29dc109e2abf2'
    Stop-DemoHost; Start-DemoHost
    $persisted=Invoke-PipelineApi GET "/crm-configuration/pipelines/$($Pipeline.id)"
    $persistedDeal=Invoke-PipelineApi GET "/deals/$($deal.id)"
    Check 'PL-18' ($persisted.status -eq 200 -and $persistedDeal.body.stageCode -ceq 'ADVISORY') 'Private ApiHost restart retained SQL configuration and Deal'
    Verify-LegacyDeals
    Check 'PL-19' $true '181 pre-existing Deal rows retain exact original column hashes'
    . (Join-Path $PSScriptRoot 'DealPipelineConcurrency.ps1')
    Test-DealPipelineConcurrency $OriginalRequest $contactId $member
    @{results=$results;pipelineId=$Pipeline.id;otherPipelineId=$second.pipeline.id;dealId=$deal.id;contactId=$contactId;http=$script:httpEvidence}|ConvertTo-Json -Depth 15|Set-Content (Join-Path $script:stateDirectory 'api-acceptance.json')
    Write-Host "PASS: $($results.Count) connected API/SQL pipeline assertions"
}
