function Test-DealPipelineConcurrency($Template,$BuyerId,$MemberId) {
    $script:workspace='ws_811689cfc4b446eab4f29dc109e2abf2'
    function Copy-Value($Value){$Value|ConvertTo-Json -Depth 30|ConvertFrom-Json -AsHashtable}
    function Concurrent($Requests) {
        $accessToken=$script:token;$workspaceId=$script:workspace
        return @($Requests | ForEach-Object -Parallel {
            $item=$_;$correlation='pipeline-concurrent-'+[Guid]::NewGuid().ToString('N')
            $headers=@{Authorization="Bearer $using:accessToken";'X-Workspace-Id'=$using:workspaceId;'X-Request-Id'=$correlation;'X-Correlation-Id'=$correlation;'Idempotency-Key'=$item.key}
            if($null -ne $item.version){$headers['If-Match']='"'+$item.version+'"'}
            $response=Invoke-WebRequest -Uri "http://127.0.0.1:5098$($item.path)" -Method $item.method -Headers $headers -ContentType 'application/json; charset=utf-8' -Body ($item.body|ConvertTo-Json -Depth 30 -Compress) -SkipHttpErrorCheck -TimeoutSec 60
            $json=if($response.Content -is [byte[]]){[Text.Encoding]::UTF8.GetString($response.Content)}else{[string]$response.Content}
            @{key=$item.key;status=[int]$response.StatusCode;body=($json|ConvertFrom-Json -AsHashtable)}
        } -ThrottleLimit 2)
    }
    $request=Copy-Value $Template;$request.nameVi='Quy trình kiểm thử đồng thời';$request.nameEn='Concurrency acceptance';$request.isDefault=$false
    $request.stages=@($request.stages|Where-Object category -ne OPEN)+@(
        @{id='concurrent_alpha';code='ALPHA';labelVi='Tiếp nhận';labelEn='Intake';order=10;category='OPEN';defaultProbability=20;color='blue';isActive=$true},
        @{id='concurrent_beta';code='BETA';labelVi='Khảo sát';labelEn='Review';order=20;category='OPEN';defaultProbability=50;color='teal';isActive=$true},
        @{id='concurrent_gamma';code='GAMMA';labelVi='Hoàn thiện';labelEn='Refine';order=30;category='OPEN';defaultProbability=90;color='purple';isActive=$true},
        @{id='concurrent_race';code='RACE_OPEN';labelVi='Giai đoạn kiểm thử khóa';labelEn='Lock test stage';order=40;category='OPEN';defaultProbability=10;color='slate';isActive=$true})
    $created=Invoke-PipelineApi POST '/crm-configuration/pipelines' $request 'pipeline-concurrent-registry-v1'
    Assert-Demo ($created.status -eq 201) 'CONCURRENCY_SETUP' 'Private synthetic pipeline required'
    $pipeline=$created.body.pipeline
    $first=Copy-Value $request;$first.nameEn='Concurrent configuration A'
    $second=Copy-Value $request;$second.nameEn='Concurrent configuration B'
    $config=Concurrent @(
        @{method='PATCH';path="/crm-configuration/pipelines/$($pipeline.id)";body=$first;version=$pipeline.resourceVersion;key='pipeline-concurrent-a-v1'},
        @{method='PATCH';path="/crm-configuration/pipelines/$($pipeline.id)";body=$second;version=$pipeline.resourceVersion;key='pipeline-concurrent-b-v1'})
    Assert-Demo (@($config|Where-Object status -eq 200).Count -eq 1 -and @($config|Where-Object {$_.status -eq 412 -and $_.body.code -eq 'VERSION_CONFLICT'}).Count -eq 1) 'CONFIG_CONCURRENCY' 'One commit, one version conflict required'
    $dealRequest=@{name='Cơ hội kiểm thử cập nhật đồng thời';buyerRef=@{type='CONTACT';id=$BuyerId};pipelineId=$pipeline.id;stageCode='ALPHA';amount=@{amount='1000000';currency='VND'};opportunityScore='10';ownerId=$MemberId;expectedCloseDate='2027-06-01';interestedProductIds=@();lineItems=@()}
    $createdDeal=Invoke-PipelineApi POST '/deals' $dealRequest 'pipeline-concurrent-deal-v1'
    Assert-Demo ($createdDeal.status -eq 201) 'CONCURRENCY_SETUP' 'Synthetic concurrent Deal required'
    $deal=$createdDeal.body.result.deal
    $moves=Concurrent @(
        @{method='POST';path="/deals/$($deal.id)/change-stage";body=@{stageCode='BETA'};version=$deal.resourceVersion;key='pipeline-concurrent-stage-a-v1'},
        @{method='POST';path="/deals/$($deal.id)/change-stage";body=@{stageCode='GAMMA'};version=$deal.resourceVersion;key='pipeline-concurrent-stage-b-v1'})
    Assert-Demo (@($moves|Where-Object status -eq 200).Count -eq 1 -and @($moves|Where-Object {$_.status -eq 412 -and $_.body.code -eq 'VERSION_CONFLICT'}).Count -eq 1) 'DEAL_CONCURRENCY' 'Competing Deal commands must not produce HTTP 500'
    $current=(Invoke-PipelineApi GET "/crm-configuration/pipelines/$($pipeline.id)").body
    $disable=Copy-Value $request;$disable.nameEn=$current.nameEn
    ($disable.stages|Where-Object code -eq RACE_OPEN).isActive=$false
    $racePath=Join-Path $script:stateDirectory 'configuration-stage-race.json'
    if(!(Test-Path $racePath)){
        $racingDeal=Copy-Value $dealRequest;$racingDeal.stageCode='RACE_OPEN';$racingDeal.name='Cơ hội kiểm thử vô hiệu hóa giai đoạn đồng thời'
        $race=Concurrent @(
            @{method='PATCH';path="/crm-configuration/pipelines/$($pipeline.id)";body=$disable;version=$current.resourceVersion;key='pipeline-race-disable-v1'},
            @{method='POST';path='/deals';body=$racingDeal;version=$null;key='pipeline-race-create-v1'})
        $disabled=$race|Where-Object key -eq 'pipeline-race-disable-v1';$newDeal=$race|Where-Object key -eq 'pipeline-race-create-v1'
        Assert-Demo (($disabled.status -eq 200 -and $newDeal.status -eq 422) -or ($disabled.status -eq 409 -and $newDeal.status -eq 201)) 'STAGE_DISABLE_RACE' 'Disable and selection must serialize safely'
        $race|ConvertTo-Json -Depth 20|Set-Content $racePath
    }else{$race=Get-Content -Raw $racePath|ConvertFrom-Json -AsHashtable}
    $integrity=(Invoke-DemoSql "SELECT COUNT(*) Count FROM deals.Deals d JOIN deals.PipelineStages s ON s.WorkspaceId=d.WorkspaceId AND s.PipelineId=d.PipelineId AND s.Code=d.StageCode WHERE d.PipelineId=@id AND s.IsActive=0" @{'@id'=$pipeline.id}).Rows[0].Count
    Assert-Demo ($integrity -eq 0) 'STAGE_DISABLE_RACE' 'No Deal may be newly placed in a disabled stage'
    $changed=Copy-Value $dealRequest;$changed.forecastCategory='PIPELINE'
    $reused=Invoke-PipelineApi POST '/deals' $changed 'pipeline-concurrent-deal-v1'
    Assert-Demo ($reused.status -eq 409 -and $reused.body.code -eq 'IDEMPOTENCY_KEY_REUSED') 'FORECAST_IDEMPOTENCY' 'Explicit forecast override is a distinct intent from omission'
    @{status='PASS';configuration=$config;deal=$moves;disableRace=$race;inactiveReferences=[int]$integrity;forecastIntent=$reused.body.code;pipelineId=$pipeline.id;dealId=$deal.id}|ConvertTo-Json -Depth 20|Set-Content (Join-Path $script:stateDirectory 'concurrency-acceptance.json')
    Write-Host 'PASS: real concurrent configuration writes, Deal writes, stage-disable race and distinct forecast intent'
}
