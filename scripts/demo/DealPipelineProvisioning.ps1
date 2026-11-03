function Initialize-PipelineTestIdentity([string]$Password) {
    foreach($entry in @{
        UNICORE_DEV_SEED_ENABLED='true';UNICORE_DEV_SEED_EMAIL='pipeline.provisioning@example.com';UNICORE_DEV_SEED_PASSWORD=$Password
        IdentityAuth__DevelopmentBootstrap__Enabled='true';IdentityAuth__DevelopmentBootstrap__Email='pipeline.provisioning@example.com';IdentityAuth__DevelopmentBootstrap__Password=$Password
        IdentityAuth__DevelopmentBootstrap__DisplayName='Pipeline Provisioning Test'
        Workspace__DevelopmentBootstrap__Enabled='false';AccessControl__DevelopmentBootstrap__Enabled='false'
    }.GetEnumerator()){Set-PipelineEnvironment $entry.Key $entry.Value}
    # Only the IdentityAuth owner fixture is enabled; no Workspace/CRM seed is invoked.
    & (Join-Path $script:hostDirectory 'bin/Debug/net10.0/UnicoreCRM.ApiHost.exe') --seed-demo --contentRoot $script:hostDirectory *> (Join-Path $script:stateDirectory 'identity-fixture.log')
    Assert-Demo ($LASTEXITCODE -eq 0) 'FIXTURE_IDENTITY_FAILED' 'Inspect identity-fixture.log'
}
function Test-NewWorkspacePipeline([string]$Password) {
    $script:workspace=$null
    $login=Invoke-PipelineApi POST '/auth/sessions' @{email='pipeline.provisioning@example.com';password=$Password} "provisioning-login-$([Guid]::NewGuid().ToString('N'))"
    Assert-Demo ($login.status -eq 200) 'LOGIN_FAILED' 'Official development test identity required'
    $script:token=$login.body.accessToken
    $provisioned=Invoke-PipelineApi POST '/workspaces/initial-provisioning' @{name='Kiểm thử Pipeline Provisioning';logoText='PL';locale='vi';timeZone='Asia/Ho_Chi_Minh';baseCurrency='VND'} 'pipeline-new-workspace-v1'
    Assert-Demo ($provisioned.status -in 200,201) 'PROVISIONING_FAILED' "HTTP $($provisioned.status) $($provisioned.body.code)"
    $id=$provisioned.body.workspaceId
    # Check SQL BEFORE registry GET: the durable provisioning participant must have committed it.
    $count=(Invoke-DemoSql 'SELECT COUNT(*) Count FROM deals.Pipelines WHERE WorkspaceId=@ws AND IsDefault=1 AND IsActive=1' @{'@ws'=$id}).Rows[0].Count
    Assert-Demo ($count -eq 1) 'PL-01' 'Provisioning must commit exactly one active default before completion'
    $script:workspace=$id
    $registry=Invoke-PipelineApi GET '/crm-configuration/pipelines'
    Assert-Demo ($registry.status -eq 200 -and $registry.body.items.Count -eq 1 -and $registry.body.items[0].stages.Count -eq 7) 'PL-01' 'New Workspace must read its persisted default'
    $replayed=Invoke-PipelineApi POST '/workspaces/initial-provisioning' @{name='Kiểm thử Pipeline Provisioning';logoText='PL';locale='vi';timeZone='Asia/Ho_Chi_Minh';baseCurrency='VND'} 'pipeline-new-workspace-v1'
    Assert-Demo ($replayed.status -eq 200 -and $replayed.body.workspaceId -ceq $id) 'PROVISIONING_REPLAY' 'No duplicate workspace/default allowed'
    Verify-LegacyDeals
    @{id='PL-01';status='PASS';workspaceId=$id;workspaceKey=$provisioned.body.workspace.workspaceKey;defaultCount=[int]$count;pipeline=$registry.body.items[0];http=$script:httpEvidence}|ConvertTo-Json -Depth 15|Set-Content (Join-Path $script:stateDirectory 'provisioning-acceptance.json')
    Write-Host 'PASS: new Workspace provisioning committed its own default Pipeline before first registry read; replay converged'
}
