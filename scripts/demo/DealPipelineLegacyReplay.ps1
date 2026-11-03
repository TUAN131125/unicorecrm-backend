function Test-DealPipelineLegacyReplay {
    $script:workspace='ws_f2332a12d56a421d8a722bd2f65c5efb'
    $manifestPath=Join-Path $repo 'artifacts/realistic-demo/realistic-v1/manifest.json'
    Assert-Demo (Test-Path $manifestPath) 'LEGACY_FIXTURE_REQUIRED' 'Original immutable demo intents required; never reseed'
    $original=Get-Content -Raw $manifestPath|ConvertFrom-Json -AsHashtable
    $results=[Collections.Generic.List[object]]::new()
    foreach($code in @('DISCOVERY','PROPOSAL','NEGOTIATION')){
        $entry=@($original.commands.GetEnumerator()|Where-Object {$_.Value.path -eq '/deals' -and $_.Value.body.stageCode -eq $code})[0].Value
        Assert-Demo ([bool]$entry) 'LEGACY_FIXTURE_REQUIRED' $code
        $replayed=Invoke-PipelineApi POST '/deals' $entry.body $entry.key
        Assert-Demo ($replayed.status -eq 201 -and $replayed.body.outcome -eq 'REPLAYED' -and [bool]$replayed.body.result.deal.pipelineId) 'LEGACY_REPLAY_FAILED' "Stage $code HTTP $($replayed.status) $($replayed.body.code)"
        $changed=$entry.body|ConvertTo-Json -Depth 30|ConvertFrom-Json -AsHashtable
        $changed.forecastCategory=if($entry.body.forecastCategory -eq 'COMMIT'){'PIPELINE'}else{'COMMIT'}
        $reused=Invoke-PipelineApi POST '/deals' $changed $entry.key
        Assert-Demo ($reused.status -eq 409 -and $reused.body.code -eq 'IDEMPOTENCY_KEY_REUSED') 'LEGACY_INTENT_CHANGED' 'Changed forecast override must not replay'
        $results.Add(@{stage=$code;httpStatus=$replayed.status;outcome=$replayed.body.outcome;pipelineId=$replayed.body.result.deal.pipelineId;changedIntentCode=$reused.body.code})
    }
    Verify-LegacyDeals
    @{status='PASS';results=$results;http=$script:httpEvidence}|ConvertTo-Json -Depth 12|Set-Content (Join-Path $script:stateDirectory 'legacy-replay-acceptance.json')
    Write-Host 'PASS: real pre-migration creates replay with PipelineId; changed forecast intent rejected; original Deals unchanged'
}
