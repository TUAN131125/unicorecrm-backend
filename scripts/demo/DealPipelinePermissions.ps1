function Initialize-PipelineReadOnlyIdentity([string]$Password) {
    $provisioningPath=Join-Path $script:stateDirectory 'provisioning-acceptance.json'
    Assert-Demo (Test-Path $provisioningPath) 'PROVISIONING_FIXTURE_REQUIRED' 'Use only the new workspace created by this task'
    $fixture=Get-Content -Raw $provisioningPath|ConvertFrom-Json -AsHashtable
    Assert-Demo ($fixture.workspaceId -notin @('ws_f2332a12d56a421d8a722bd2f65c5efb','ws_811689cfc4b446eab4f29dc109e2abf2')) 'UNSAFE_FIXTURE' 'Never alter existing demo/control permissions'
    foreach($entry in @{
        UNICORE_DEV_SEED_ENABLED='false'
        IdentityAuth__DevelopmentBootstrap__Enabled='true';IdentityAuth__DevelopmentBootstrap__Email='pipeline.readonly@example.com';IdentityAuth__DevelopmentBootstrap__Password=$Password;IdentityAuth__DevelopmentBootstrap__DisplayName='Pipeline Read Only Test'
        Workspace__DevelopmentBootstrap__Enabled='true';Workspace__DevelopmentBootstrap__IdentityEmail='pipeline.readonly@example.com';Workspace__DevelopmentBootstrap__EnsureStudioConfiguration='false'
        AccessControl__DevelopmentBootstrap__Enabled='true';AccessControl__DevelopmentBootstrap__IdentityEmail='pipeline.readonly@example.com';AccessControl__DevelopmentBootstrap__WorkspaceKey=$fixture.workspaceKey;AccessControl__DevelopmentBootstrap__RoleName='Pipeline Synthetic Read Only'
        AccessControl__DevelopmentBootstrap__Capabilities__0='workspace.context.resolve';AccessControl__DevelopmentBootstrap__Capabilities__1='deals.read'
        UNICORECRM_TEST_READ_ONLY_EMAIL='pipeline.readonly@example.com';UNICORECRM_TEST_READ_ONLY_PASSWORD=$Password
    }.GetEnumerator()){Set-PipelineEnvironment $entry.Key $entry.Value}
    foreach($kind in @('MemberWorkspace','NonMemberWorkspace')){
        $values=@{Key=if($kind -eq 'MemberWorkspace'){$fixture.workspaceKey}else{'unicore-demo-anh-duong-control'};Name='Synthetic pipeline permission fixture';LogoText='PL';Locale='vi';TimeZone='Asia/Ho_Chi_Minh';BaseCurrency='VND';AvailableProductSpaces__0='crm'}
        foreach($entry in $values.GetEnumerator()){Set-PipelineEnvironment "Workspace__DevelopmentBootstrap__${kind}__$($entry.Key)" $entry.Value}
    }
    & (Join-Path $script:hostDirectory 'bin/Debug/net10.0/UnicoreCRM.ApiHost.exe') --seed-demo --contentRoot $script:hostDirectory *> (Join-Path $script:stateDirectory 'readonly-fixture.log')
    Assert-Demo ($LASTEXITCODE -eq 0) 'FIXTURE_IDENTITY_FAILED' 'Inspect readonly-fixture.log'
}
function Test-DealPipelinePermissions {
    Assert-Demo ([bool]$env:UNICORECRM_TEST_READ_ONLY_EMAIL -and [bool]$env:UNICORECRM_TEST_READ_ONLY_PASSWORD) 'TEST_CREDENTIAL_REQUIRED' 'Provide the existing authorized read-only account through process environment'
    $script:workspace=$null
    $login=Invoke-PipelineApi POST '/auth/sessions' @{email=$env:UNICORECRM_TEST_READ_ONLY_EMAIL;password=$env:UNICORECRM_TEST_READ_ONLY_PASSWORD} "pipeline-readonly-$([Guid]::NewGuid().ToString('N'))"
    Assert-Demo ($login.status -eq 200) 'LOGIN_FAILED' 'Existing authorized test account required'
    $script:token=$login.body.accessToken
    $workspaces=Invoke-PipelineApi GET '/workspaces'
    $evidence=[Collections.Generic.List[object]]::new()
    foreach($item in $workspaces.body.items){
        $script:workspace=$item.workspaceId
        $context=Invoke-PipelineApi GET '/access/context'
        if($context.status -ne 200 -or $context.body.capabilities -contains 'studio.configure'){continue}
        if($context.body.capabilities -notcontains 'deals.read' -and $context.body.capabilities -notcontains 'studio.read'){continue}
        $read=Invoke-PipelineApi GET '/crm-configuration/pipelines'
        $denied=Invoke-PipelineApi POST '/crm-configuration/pipelines' @{nameVi='Permission test';nameEn='Permission test';isActive=$true;isDefault=$false;stages=@()} 'pipeline-readonly-forbidden-v1'
        $target=if($read.status -eq 200){$read.body.items[0].id}else{'pipeline_unavailable'}
        $update=Invoke-PipelineApi PATCH "/crm-configuration/pipelines/$target" @{} 'pipeline-readonly-update-forbidden-v1' 1
        Assert-Demo ($read.status -eq 200 -and $denied.status -eq 403 -and $update.status -eq 403) 'PL-13' 'CRM reads must succeed while configuration writes are refused'
        $evidence.Add(@{workspaceId=$item.workspaceId;read=$read.status;create=$denied.status;update=$update.status;hasDealsRead=($context.body.capabilities -contains 'deals.read');hasStudioRead=($context.body.capabilities -contains 'studio.read');hasStudioConfigure=$false})
    }
    Assert-Demo ($evidence.Count -gt 0) 'BLOCKED_TEST_FIXTURE' 'Existing account has no eligible read-only workspace; no permissions were changed'
    Verify-LegacyDeals
    @{id='PL-13';status='PASS';workspaces=$evidence;http=$script:httpEvidence}|ConvertTo-Json -Depth 12|Set-Content (Join-Path $script:stateDirectory 'permission-acceptance.json')
    Write-Host 'PASS: authorized read-only identity can read registry, cannot create/update; no existing user permissions changed'
}
