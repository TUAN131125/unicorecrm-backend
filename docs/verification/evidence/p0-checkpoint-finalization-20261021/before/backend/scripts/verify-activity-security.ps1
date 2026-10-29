[CmdletBinding()]
param([string]$SqlServer = 'localhost', [int]$Port = 5389, [switch]$Browser, [switch]$ScaleReview, [switch]$ProjectionReview, [switch]$RaceReview, [switch]$UiReview, [switch]$CommitRaceReview, [switch]$CustomerTaskReview, [switch]$ActivityContractReview)
$ErrorActionPreference = 'Stop'
$runId = [Guid]::NewGuid().ToString('N')
$startedAt = [DateTimeOffset]::UtcNow.ToString('O')
$database = "UnicoreCRM_ActivityTest_$runId"
$repo = Split-Path $PSScriptRoot
$evidence = Join-Path $repo "artifacts/activity-security/$runId"
[void](New-Item -ItemType Directory -Path $evidence -Force)
$connection = "Server=$SqlServer;Database=$database;Integrated Security=True;TrustServerCertificate=True"
function Sql([string]$query, [string]$db = $database) {
    $c = [System.Data.SqlClient.SqlConnection]::new("Server=$SqlServer;Database=$db;Integrated Security=True;TrustServerCertificate=True")
    $c.Open()
    try {
        $cmd = $c.CreateCommand(); $cmd.CommandText = $query; $cmd.CommandTimeout = 120
        $table = [System.Data.DataTable]::new(); $table.Load($cmd.ExecuteReader()); return ,$table
    } finally { $c.Dispose() }
}
$results = [Collections.Generic.List[object]]::new()
$http = [Collections.Generic.List[object]]::new()
$token = $null; $workspace = $null; $counter = 0
function Api([string]$method, [string]$path, $body = $null, [string]$key = '', [Nullable[int]]$version = $null) {
    $script:counter++
    $correlation = "activity-$runId-$script:counter"
    $headers = @{ 'X-Request-Id' = $correlation; 'X-Correlation-Id' = $correlation }
    if ($script:token) { $headers.Authorization = "Bearer $script:token" }
    if ($script:workspace) { $headers['X-Workspace-Id'] = $script:workspace }
    if ($key) { $headers['Idempotency-Key'] = $key }
    if ($null -ne $version) { $headers['If-Match'] = '"' + $version + '"' }
    $args = @{ Method = $method; Uri = "http://127.0.0.1:$Port$path"; Headers = $headers; SkipHttpErrorCheck = $true }
    if ($null -ne $body) { $args.Body = $body | ConvertTo-Json -Depth 12 -Compress; $args.ContentType = 'application/json' }
    $response = Invoke-WebRequest @args
    $value = if ($response.Content) { $response.Content | ConvertFrom-Json } else { $null }
    # Authentication payloads and free-form content never enter evidence.
    $http.Add(@{ method = $method; path = $path; status = [int]$response.StatusCode; correlationId = $correlation;
        code = $value.code; itemCount = @($value.items).Count; pageInfo = $value.pageInfo })
    return @{ Status = [int]$response.StatusCode; Body = $value }
}
function Check([string]$name, $expected, $actual) {
    $pass = "$expected" -ceq "$actual"
    $results.Add(@{ test = $name; expected = $expected; actual = $actual; status = $(if ($pass) {'PASS'} else {'FAIL'}) })
    Write-Host "$(if ($pass) {'PASS'} else {'FAIL'}) $name expected=$expected actual=$actual"
}
function Scope([string]$resource, [string]$scope) {
    $null = Sql "IF EXISTS (SELECT 1 FROM access.RoleDataScopes WHERE PolicyId = 'activity_$resource') UPDATE access.RoleDataScopes SET Scope='$scope' WHERE PolicyId='activity_$resource'; ELSE INSERT INTO access.RoleDataScopes (PolicyId,WorkspaceId,RoleId,ResourceKey,Scope,AllowedOwnerIdsJson) VALUES ('activity_$resource','$workspace','$role','$resource','$scope','[]');"
}
function Draft([string]$id = 'activity_contact_own', [string]$module = 'contacts') {
    return @{ type='NOTE'; subject='Activity security test'; body='isolated test content'; recordRef=@{moduleKey=$module;recordId=$id} }
}
function Invoke-ConnectedBrowser {
        $browserSettings = @{
            UNICORECRM_TEST_API_BASE_URL="http://127.0.0.1:$Port";
            UNICORECRM_TEST_EMAIL=$settings.DevelopmentDemoBootstrap__Email;
            UNICORECRM_TEST_PASSWORD=$settings.DevelopmentDemoBootstrap__Password;
            UNICORECRM_TEST_WORKSPACE_ID=$workspace
            UNICORECRM_TEST_OWN_EMAIL="activity-own-$runId@example.test"
            UNICORECRM_TEST_OWN_CONTACT_ID='activity_sales_contact'
        }
        foreach ($setting in $browserSettings.GetEnumerator()) {
            $oldEnvironment[$setting.Key] = [Environment]::GetEnvironmentVariable($setting.Key)
            [Environment]::SetEnvironmentVariable($setting.Key, $setting.Value)
        }
        $script:frontend = Join-Path (Split-Path $repo) 'frontend/unicorecrm-web'
        Push-Location $frontend
        try {
            $browserConfig = if ($UiReview) { 'playwright.crm-ui.config.ts' } else { 'playwright.crm-real.config.ts' }
            & node node_modules/@playwright/test/cli.js test --config $browserConfig
            Check $(if ($UiReview) { 'UI-only CRM journey (BLOCKED is not PASS)' } else { 'API workflow with real browser projection/history' }) 0 $LASTEXITCODE
        } finally { Pop-Location }
}
$process = $null
$oldEnvironment = @{}
try {
    # Always allocate a fresh name; never drop or reuse an existing database.
    $null = Sql "IF DB_ID('$database') IS NOT NULL THROW 50000,'Test database already exists',1; CREATE DATABASE [$database];" 'master'
    if($ScaleReview){$null=Sql "ALTER DATABASE [$database] SET QUERY_STORE = ON (OPERATION_MODE=READ_WRITE,QUERY_CAPTURE_MODE=ALL);"}
    $settings = @{
        ASPNETCORE_ENVIRONMENT='Development'; ASPNETCORE_URLS="http://127.0.0.1:$Port";
        ConnectionStrings__UnicoreCRM=$connection;
        DevelopmentDemoBootstrap__Email="activity-$runId@example.test";
        DevelopmentDemoBootstrap__Password="Test!$([Guid]::NewGuid().ToString('N'))aA9";
        IdentityAuth__EmailVerification__Sender__Kind='DevelopmentLog'
        AI__DataProtection__KeyRingPath=(Join-Path $evidence 'keys')
        Frontend__AllowedOrigins__0='http://127.0.0.1:3039'
    }
    foreach ($setting in $settings.GetEnumerator()) {
        $oldEnvironment[$setting.Key] = [Environment]::GetEnvironmentVariable($setting.Key)
        [Environment]::SetEnvironmentVariable($setting.Key, $setting.Value)
    }
    $project = Join-Path $repo 'src/UnicoreCRM.ApiHost/UnicoreCRM.ApiHost.csproj'
    & dotnet run --no-launch-profile --project $project -- --migrate *> (Join-Path $evidence 'migration.log')
    if ($LASTEXITCODE) { throw 'Migration failed; inspect local migration.log' }
    & dotnet run --no-build --no-launch-profile --project $project -- --seed-demo *> (Join-Path $evidence 'bootstrap.log')
    if ($LASTEXITCODE) { throw 'Bootstrap failed; inspect local bootstrap.log' }
    $process = Start-Process dotnet -ArgumentList @('run','--no-build','--no-launch-profile','--project',$project) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $evidence 'host.log') -RedirectStandardError (Join-Path $evidence 'host-error.log')
    $ready = $false
    for ($i=0; $i -lt 90; $i++) {
        try { $null = Api GET '/auth/session'; $ready=$true; break } catch { Start-Sleep -Seconds 1 }
        if ($process.HasExited) { throw 'ApiHost exited' }
    }
    if (!$ready) { throw 'ApiHost readiness timeout' }
    $login = Api POST '/auth/sessions' @{email=$settings.DevelopmentDemoBootstrap__Email;password=$settings.DevelopmentDemoBootstrap__Password} "login-$runId"
    if ($login.Status -ne 200) { throw "Login failed: $($login.Status)" }
    $script:token = $login.Body.accessToken
    $session = Api GET '/auth/session'
    $member = $session.Body.principal.memberId
    $script:workspace = (Sql "SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key]='unicore-demo'").Rows[0].WorkspaceId
    $foreign = (Sql "SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key]='unicore-demo-isolated'").Rows[0].WorkspaceId
    $role = (Sql "SELECT TOP 1 RoleId FROM access.Roles WHERE WorkspaceId='$workspace'").Rows[0].RoleId
    if ($UiReview) {
        Check 'fresh Workspace has no Lead before UI scenario' 0 (Sql "SELECT COUNT(*) AS Count FROM leads.Leads WHERE WorkspaceId='$workspace'").Rows[0].Count
        Invoke-ConnectedBrowser
        $uiRecordFile=Get-ChildItem (Join-Path $frontend 'test-results/crm-ui-real') -Filter ui-records.json -Recurse | Select-Object -First 1
        if ($uiRecordFile) {
            $uiRecords=Get-Content $uiRecordFile.FullName -Raw | ConvertFrom-Json
            if ($uiRecords.leadId -notmatch '^[A-Za-z0-9_-]{1,128}$') { throw 'Unexpected UI Lead identifier' }
            Check 'UI Lead persisted in fresh SQL Workspace' 1 (Sql "SELECT COUNT(*) AS Count FROM leads.Leads WHERE WorkspaceId='$workspace' AND LeadId='$($uiRecords.leadId)'").Rows[0].Count
            Check 'UI Lead Source remains empty in SQL' 1 (Sql "SELECT COUNT(*) AS Count FROM leads.Leads WHERE WorkspaceId='$workspace' AND LeadId='$($uiRecords.leadId)' AND COALESCE(JSON_VALUE(Profile,'$.Source'),JSON_VALUE(Profile,'$.source'),'')='' ").Rows[0].Count
            Copy-Item -LiteralPath $uiRecordFile.FullName -Destination (Join-Path $evidence 'ui-records.json')
        }
        $journeyFile=Get-ChildItem (Join-Path $frontend 'test-results/crm-ui-real') -Filter ui-journey-records.json -Recurse | Select-Object -First 1
        if ($journeyFile) {
            $journey=Get-Content $journeyFile.FullName -Raw | ConvertFrom-Json
            foreach ($field in @('customerId','taskId','marker')) {
                if ($journey.$field -notmatch '^[A-Za-z0-9_-]{1,128}$') { throw "UI journey did not produce a valid $field" }
            }
            Check 'UI Task SQL unique title in trusted Workspace' 1 (Sql "SELECT COUNT(*) AS Count FROM tasks.Tasks WHERE WorkspaceId='$workspace' AND Title='$($journey.marker) Task'").Rows[0].Count
            Check 'UI Task SQL Customer recordRef' $journey.customerId (Sql "SELECT RecordId FROM tasks.Tasks WHERE WorkspaceId='$workspace' AND TaskId='$($journey.taskId)' AND RecordModuleKey='customers' AND RelationshipType IS NULL").Rows[0].RecordId
            Check 'UI Task SQL completed lifecycle' 1 (Sql "SELECT COUNT(*) AS Count FROM tasks.Tasks WHERE WorkspaceId='$workspace' AND TaskId='$($journey.taskId)' AND Status=1 AND CompletedAt IS NOT NULL").Rows[0].Count
            Check 'UI Task SQL assignee active Workspace member' 1 (Sql "SELECT COUNT(*) AS Count FROM tasks.Tasks t JOIN workspace.Memberships m ON m.WorkspaceId=t.WorkspaceId AND m.MemberId=t.AssigneeId AND m.Status='Active' WHERE t.WorkspaceId='$workspace' AND t.TaskId='$($journey.taskId)'").Rows[0].Count
            Copy-Item -LiteralPath $journeyFile.FullName -Destination (Join-Path $evidence 'ui-journey-records.json')
            Stop-Process -Id $process.Id
            $process.WaitForExit()
            $process = Start-Process dotnet -ArgumentList @('run','--no-build','--no-launch-profile','--project',$project) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $evidence 'restart.log') -RedirectStandardError (Join-Path $evidence 'restart-error.log')
            $ready=$false
            for ($i=0; $i -lt 90; $i++) {
                try { $null=Api GET '/auth/session'; $ready=$true; break } catch { Start-Sleep -Seconds 1 }
                if ($process.HasExited) { throw 'Restarted UI ApiHost exited' }
            }
            if (!$ready) { throw 'Restarted UI ApiHost readiness timeout' }
            $script:token=$null
            $restartLogin=Api POST '/auth/sessions' @{email=$settings.DevelopmentDemoBootstrap__Email;password=$settings.DevelopmentDemoBootstrap__Password} "ui-restart-$runId"
            Check 'UI dataset real reauthentication after ApiHost restart' 200 $restartLogin.Status
            $script:token=$restartLogin.Body.accessToken
            Check 'UI Task GET after ApiHost restart' 'COMPLETED' (Api GET "/tasks/$($journey.taskId)").Body.status
            Check 'UI Task list after ApiHost restart' $journey.taskId ((Api GET "/tasks?recordModuleKey=customers&recordId=$($journey.customerId)").Body.items.id -join ',')
        }
        if (@($results | Where-Object status -eq FAIL).Count) { exit 1 }
        exit 0
    }
    $null = Sql @"
INSERT INTO contacts.Contacts (ContactId,WorkspaceId,OwnerId,FullName,Status,Version,CreatedAt,UpdatedAt,Profile) VALUES
('activity_contact_own','$workspace','$member','Activity own','active',0,SYSUTCDATETIME(),SYSUTCDATETIME(),N'{}'),
('activity_contact_other','$workspace','activity_other_member','Activity other','active',0,SYSUTCDATETIME(),SYSUTCDATETIME(),N'{}'),
('activity_contact_foreign','$foreign','$member','Activity foreign','active',0,SYSUTCDATETIME(),SYSUTCDATETIME(),N'{}');
"@
    $ownDraft = Draft
    $own = Api POST '/activities' $ownDraft 'activity-own'
    Check 'authorized log' 201 $own.Status
    Check 'authorized replay' 201 (Api POST '/activities' $ownDraft 'activity-own').Status
    Check 'other record log at workspace scope' 201 (Api POST '/activities' (Draft 'activity_contact_other') 'activity-other').Status
    Check 'missing record refused' 404 (Api POST '/activities' (Draft 'activity_missing') 'activity-missing').Status
    Check 'foreign workspace refused' 404 (Api POST '/activities' (Draft 'activity_contact_foreign') 'activity-foreign').Status
    Check 'unsupported module refused' 422 (Api POST '/activities' (Draft 'activity_unknown' 'arbitrary') 'activity-module').Status
    $mismatch = Draft; $mismatch.relationshipRef=@{type='CONTACT';id='activity_contact_other'}
    Check 'conflicting relationship refused' 422 (Api POST '/activities' $mismatch 'activity-conflict').Status
    $system = Draft; $system.type='SYSTEM'
    Check 'HTTP cannot forge system audit' 422 (Api POST '/activities' $system 'activity-system').Status
    Scope contacts Own
    Check 'OWN log outside scope refused' 404 (Api POST '/activities' (Draft 'activity_contact_other') 'activity-own-denied').Status
    $list = Api GET '/activities?limit=1'
    Check 'OWN count includes authorized rows only' 1 $list.Body.pageInfo.totalCount
    Check 'OWN page includes own record' 'activity_contact_own' $list.Body.items[0].recordRef.recordId
    Check 'OWN no hidden cursor' False $list.Body.pageInfo.hasNextPage
    Check 'hidden search count zero' 0 (Api GET '/activities?recordId=activity_contact_other').Body.pageInfo.totalCount
    Scope contacts Team
    Check 'TEAM cannot log outside team' 404 (Api POST '/activities' $ownDraft 'activity-team').Status
    Check 'TEAM list fails closed' 0 (Api GET '/activities').Body.pageInfo.totalCount
    Scope contacts Workspace
    $null = Sql "UPDATE contacts.Contacts SET OwnerId='activity_other_member' WHERE ContactId='activity_contact_own'"
    Scope contacts Own
    Check 'owner change replay refused' 404 (Api POST '/activities' $ownDraft 'activity-own').Status
    Scope contacts Workspace
    $null = Sql "UPDATE contacts.Contacts SET ArchivedAt=SYSUTCDATETIME(),Status='archived' WHERE ContactId='activity_contact_own'"
    Check 'archived replay refused' 404 (Api POST '/activities' $ownDraft 'activity-own').Status
    $null = Sql "DELETE FROM access.RoleCapabilities WHERE RoleId='$role' AND Capability='contacts.read'"
    Check 'tasks.read without contacts.read hides activity' 0 (Api GET '/activities').Body.pageInfo.totalCount
    Check 'revoked capability replay refused' 404 (Api POST '/activities' $ownDraft 'activity-own').Status
    $count = (Sql "SELECT COUNT(*) AS Count FROM tasks.Activities").Rows[0].Count
    Check 'negative commands and retries create no duplicates' 2 $count
    $null = Sql "INSERT INTO access.RoleCapabilities (RoleId,Capability) VALUES ('$role','contacts.read'); UPDATE contacts.Contacts SET ArchivedAt=NULL,Status='active',OwnerId='$member' WHERE ContactId='activity_contact_own';"
    $null = Sql "INSERT INTO tasks.Activities (ActivityId,WorkspaceId,Type,Subject,ActorId,OccurredAt,Version,RecordModuleKey,RecordId) VALUES ('activity_legacy_system','$workspace',5,'legacy system test','$member',SYSUTCDATETIME(),0,'contacts','activity_contact_own');"
    Check 'legacy client SYSTEM row is not published as trusted audit' 0 (Api GET '/activities?type=SYSTEM').Body.pageInfo.totalCount
    Check 'legacy SYSTEM content is excluded from search metadata' 0 (Api GET '/activities?search=legacy').Body.pageInfo.totalCount
    Scope tasks Own
    Scope contacts Own
    Check 'Sales OWN can log own Contact with Tasks OWN' 201 (Api POST '/activities' $ownDraft 'activity-own').Status
    Check 'Sales OWN reads own Contact with Tasks OWN' 1 (Api GET '/activities').Body.pageInfo.totalCount
    Scope tasks Workspace
    Scope contacts Workspace
    $null = Sql "INSERT INTO access.RoleFieldSecurity (PolicyId,WorkspaceId,RoleId,ResourceKey,FieldKey,Access) VALUES ('activity_hidden_email','$workspace','$role','contacts','workEmail','Hidden');"
    Check 'owner field security hides free text and count' 0 (Api GET '/activities?search=isolated').Body.pageInfo.totalCount
    Check 'owner field security gates cached replay' 404 (Api POST '/activities' $ownDraft 'activity-own').Status
    $null = Sql "DELETE FROM access.RoleFieldSecurity WHERE PolicyId='activity_hidden_email'"
    $source = Draft; $source.sourceRef=@{type='CONTACT_ACTIVITY';id='activity_contact_other'}
    Check 'forged source target refused' 422 (Api POST '/activities' $source 'activity-source-id').Status
    $source.sourceRef=@{type='SYSTEM';id='activity_contact_own';evidence='forged'}
    Check 'forged audit source refused' 422 (Api POST '/activities' $source 'activity-source-audit').Status
    Check 'unanchored activity refused' 422 (Api POST '/activities' @{type='NOTE';subject='unanchored'} 'activity-unanchored').Status
    $relationshipOnly=@{type='NOTE';subject='relationship only';relationshipRef=@{type='CONTACT';id='activity_contact_own'}}
    Check 'relationship-only Contact supported' 201 (Api POST '/activities' $relationshipOnly 'activity-relationship').Status
    Check 'relationship-only retry is idempotent' 201 (Api POST '/activities' $relationshipOnly 'activity-relationship').Status
    $source = Draft; $source.sourceRef=@{type='CONTACT_ACTIVITY';id='activity_contact_own'}
    Check 'existing Contact UI source remains supported' 201 (Api POST '/activities' $source 'activity-source-valid').Status
    Scope tasks Own
    Scope contacts Own
    $pageOne=Api GET '/activities?limit=1'
    $pageTwo=Api GET "/activities?limit=1&cursor=$($pageOne.Body.pageInfo.nextCursor)"
    $pageThree=Api GET "/activities?limit=1&cursor=$($pageTwo.Body.pageInfo.nextCursor)"
    Check 'OWN multi-page total excludes hidden records' 3 $pageOne.Body.pageInfo.totalCount
    Check 'OWN next page retains authorized total' 3 $pageTwo.Body.pageInfo.totalCount
    Check 'OWN continuation returns three distinct authorized rows' 3 (@($pageOne.Body.items.id,$pageTwo.Body.items.id,$pageThree.Body.items.id) | Select-Object -Unique).Count
    Check 'OWN last page has no hidden continuation' False $pageThree.Body.pageInfo.hasNextPage
    Scope tasks Workspace
    Scope contacts Workspace
    $lead = Api POST '/leads' @{displayName="Activity lead $runId";email="lead-$runId@example.test";phone='0900000123';source='WEB';ownerId=$member;estimatedValue=@{amount='0';currency='USD'}} 'activity-lead-create'
    Check 'real Lead fixture created through HTTP' 201 $lead.Status
    $leadId = $lead.Body.aggregateId
    if (!$leadId) { throw 'Lead fixture did not return an aggregate ID' }
    Check 'Lead Activity positive' 201 (Api POST '/activities' (Draft $leadId 'leads') 'activity-lead').Status
    Check 'Lead missing target refused' 404 (Api POST '/activities' (Draft 'activity_lead_missing' 'leads') 'activity-lead-missing').Status
    $null = Sql "SELECT * INTO #activity_foreign_lead FROM leads.Leads WHERE LeadId='$leadId'; UPDATE #activity_foreign_lead SET LeadId='activity_lead_foreign',WorkspaceId='$foreign'; INSERT INTO leads.Leads SELECT * FROM #activity_foreign_lead; DROP TABLE #activity_foreign_lead;"
    Check 'Lead foreign Workspace refused' 404 (Api POST '/activities' (Draft 'activity_lead_foreign' 'leads') 'activity-lead-foreign').Status
    $null = Sql "UPDATE leads.Leads SET ArchivedAt=SYSUTCDATETIME() WHERE LeadId='$leadId'"
    Check 'Lead archived replay refused' 404 (Api POST '/activities' (Draft $leadId 'leads') 'activity-lead').Status
    $null = Sql @"
INSERT INTO organizations.Organizations (OrganizationId,WorkspaceId,OwnerId,DisplayName,Status,Version,CreatedAt,UpdatedAt,Profile)
VALUES ('activity_org','$workspace','$member','Activity organization','active',0,SYSUTCDATETIME(),SYSUTCDATETIME(),N'{}');
INSERT INTO customers.Customers (WorkspaceId,CustomerId,CustomerCode,OwnerId,Type,RelationshipType,RelationshipId,Status,Version,CreatedAt,UpdatedAt,Profile)
VALUES ('$workspace','activity_customer','CU-ACTIVITY','$member','B2C','CONTACT','activity_contact_own','NEW',0,SYSUTCDATETIME(),SYSUTCDATETIME(),N'{}');
"@
    if ($CustomerTaskReview) {
        $taskDraft=@{title="Customer task $runId";description='Expected outcome';priority='HIGH';assigneeId=$member;dueAt='2026-12-01T00:00:00Z';recordRef=@{moduleKey='customers';recordId='activity_customer'};sourceRef=@{type='CUSTOMER_360';id='activity_customer'}}
        $createdTask=Api POST '/tasks' $taskDraft 'customer-task-positive'
        Check 'Customer Task create positive' 201 $createdTask.Status
        $taskId=$createdTask.Body.aggregateId
        if ($taskId -notmatch '^[A-Za-z0-9_-]{1,128}$') { throw 'Invalid Task identifier' }
        Check 'Customer Task exact replay' 201 (Api POST '/tasks' $taskDraft 'customer-task-positive').Status
        Check 'Customer Task replay SQL duplicate count' 1 (Sql "SELECT COUNT(*) AS Count FROM tasks.Tasks WHERE WorkspaceId='$workspace' AND Title='Customer task $runId'").Rows[0].Count
        Check 'Customer Task get exact reference' 'activity_customer' (Api GET "/tasks/$taskId").Body.recordRef.recordId
        Check 'Customer Task scoped list' $taskId ((Api GET '/tasks?recordModuleKey=customers&recordId=activity_customer').Body.items.id -join ',')
        $otherDraft=$taskDraft.Clone();$otherDraft.assigneeId='nonexistent-member'
        Check 'Customer Task rejects invalid assignee' 422 (Api POST '/tasks' $otherDraft 'customer-task-invalid-member').Status
        $otherDraft=$taskDraft.Clone();$otherDraft.relationshipRef=@{type='CONTACT';id='activity_contact_own'}
        Check 'Customer Task rejects subject relationshipRef' 422 (Api POST '/tasks' $otherDraft 'customer-task-subject').Status
        $otherDraft=$taskDraft.Clone();$otherDraft.recordRef=@{moduleKey='customers';recordId='missing-customer'}
        Check 'Customer Task rejects missing Customer' 404 (Api POST '/tasks' $otherDraft 'customer-task-missing').Status
        $null=Sql "UPDATE customers.Customers SET WorkspaceId='$foreign' WHERE CustomerId='activity_customer'"
        Check 'Customer Task rejects foreign Workspace' 404 (Api POST '/tasks' $taskDraft 'customer-task-foreign').Status
        $null=Sql "UPDATE customers.Customers SET WorkspaceId='$workspace',Status='ARCHIVED' WHERE CustomerId='activity_customer'"
        Check 'Customer Task rejects archived Customer' 404 (Api POST '/tasks' $taskDraft 'customer-task-archived').Status
        Check 'Customer Task archived receipt replay denied' 404 (Api POST '/tasks' $taskDraft 'customer-task-positive').Status
        $null=Sql "UPDATE customers.Customers SET Status='NEW',OwnerId='activity_other_member' WHERE CustomerId='activity_customer'"
        Scope customers Own
        Check 'Customer Task outside OWN denied' 404 (Api POST '/tasks' $taskDraft 'customer-task-other-owner').Status
        Check 'Customer Task replay after Customer owner loss denied' 404 (Api POST '/tasks' $taskDraft 'customer-task-positive').Status
        $null=Sql "UPDATE customers.Customers SET OwnerId='$member' WHERE CustomerId='activity_customer'"
        Check 'Customer Task OWN positive' 201 (Api POST '/tasks' $taskDraft 'customer-task-positive').Status
        Scope customers Workspace
        Scope tasks Own
        Check 'Customer Task OWN list positive' $taskId ((Api GET '/tasks?recordModuleKey=customers&recordId=activity_customer').Body.items.id -join ',')
        $null=Sql "UPDATE tasks.Tasks SET AssigneeId='activity_other_member' WHERE TaskId='$taskId'"
        Check 'Customer Task get outside Task OWN denied' 404 (Api GET "/tasks/$taskId").Status
        Check 'Customer Task list outside Task OWN empty' 0 (Api GET '/tasks?recordModuleKey=customers&recordId=activity_customer').Body.pageInfo.totalCount
        $null=Sql "UPDATE tasks.Tasks SET AssigneeId='$member' WHERE TaskId='$taskId'"
        Scope tasks Workspace
        $null=Sql "INSERT INTO access.RoleFieldSecurity (PolicyId,WorkspaceId,RoleId,ResourceKey,FieldKey,Access) VALUES ('task_hidden_description','$workspace','$role','tasks','description','Hidden')"
        Check 'Customer Task hidden description omitted' False ((Api GET "/tasks/$taskId").Body.PSObject.Properties.Name -contains 'description')
        Check 'Customer Task hidden description cannot be written' 403 (Api POST '/tasks' $taskDraft 'customer-task-hidden-write').Status
        $null=Sql "DELETE FROM access.RoleFieldSecurity WHERE PolicyId='task_hidden_description'"
        $null=Sql "DELETE FROM access.RoleCapabilities WHERE RoleId='$role' AND Capability='tasks.create'"
        Check 'Customer Task missing capability refused' 403 (Api POST '/tasks' $taskDraft 'customer-task-no-create').Status
        $null=Sql "INSERT INTO access.RoleCapabilities (RoleId,Capability) VALUES ('$role','tasks.create')"
        $taskRead=Api GET "/tasks/$taskId"
        $completeTask=Api POST "/tasks/$taskId/complete" @{outcome='Completed by HTTP regression'} 'customer-task-complete' $taskRead.Body.resourceVersion
        Check 'Customer Task complete' 200 $completeTask.Status
        Check 'Customer Task completed get' 'COMPLETED' (Api GET "/tasks/$taskId").Body.status
    }
    $organization = Draft 'activity_org' 'organizations'; $organization.relationshipRef=@{type='ORGANIZATION_ACCOUNT';id='activity_org'}; $organization.sourceRef=@{type='ORGANIZATION_DETAIL';id='activity_org'}
    if ($ProjectionReview) {
        Check 'canonical Contact Customer is present without stakeholder membership' 'activity_customer' ((Api GET '/contacts/activity_contact_own/relationship-summary').Body.customerIds -join ',')
        Scope customers Team
        Check 'canonical Customer outside admitted scope is hidden' 0 @((Api GET '/contacts/activity_contact_own/relationship-summary').Body.customerIds).Count
        Scope customers Workspace
        $null=Sql "DELETE FROM access.RoleCapabilities WHERE RoleId='$role' AND Capability='customers.view'"
        Check 'canonical Customer without capability is hidden' 0 @((Api GET '/contacts/activity_contact_own/relationship-summary').Body.customerIds).Count
        $null=Sql "INSERT INTO access.RoleCapabilities (RoleId,Capability) VALUES ('$role','customers.view')"
        Check 'foreign Contact relationship summary is refused' 404 (Api GET '/contacts/activity_contact_foreign/relationship-summary').Status
    }
    Check 'existing Organization UI shape supported' 201 (Api POST '/activities' $organization 'activity-org').Status
    Check 'Organization missing target refused' 404 (Api POST '/activities' (Draft 'activity_org_missing' 'organizations') 'activity-org-missing').Status
    $customer = Draft 'activity_customer' 'customers'; $customer.relationshipRef=@{type='CONTACT';id='activity_contact_own'}; $customer.sourceRef=@{type='CUSTOMER_360';id='activity_customer'}
    Check 'existing Customer UI shape supported' 201 (Api POST '/activities' $customer 'activity-customer').Status
    $customer.relationshipRef.id='activity_contact_other'
    Check 'Customer forged subject relationship refused' 404 (Api POST '/activities' $customer 'activity-customer-forged').Status
    $customer.relationshipRef.id='activity_contact_own'
    $null = Sql "UPDATE contacts.Contacts SET OwnerId='activity_other_member' WHERE ContactId='activity_contact_own'"
    Scope contacts Own
    Check 'allowed Customer cannot expose denied Contact' 0 (Api GET '/activities?recordModuleKey=customers').Body.pageInfo.totalCount
    Check 'multi-reference replay requires every target' 404 (Api POST '/activities' $customer 'activity-customer').Status
    Scope contacts Workspace
    $null = Sql "UPDATE contacts.Contacts SET OwnerId='$member' WHERE ContactId='activity_contact_own'"
    $null = Sql "UPDATE customers.Customers SET Status='ARCHIVED' WHERE CustomerId='activity_customer'"
    Check 'Customer archived replay refused' 404 (Api POST '/activities' $customer 'activity-customer').Status
    $savedWorkspace = $script:workspace; $script:workspace=$foreign
    Check 'Workspace switch cannot replay protected receipt' 403 (Api POST '/activities' $ownDraft 'activity-own').Status
    $script:workspace=$savedWorkspace
    $sqlEvidence = Sql "SELECT RecordModuleKey,COUNT(*) AS Count FROM tasks.Activities WHERE WorkspaceId='$workspace' GROUP BY RecordModuleKey;"
    @($sqlEvidence.Rows | ForEach-Object { @{module=$_.RecordModuleKey;count=$_.Count} }) | ConvertTo-Json | Set-Content (Join-Path $evidence 'sql-counts.json')
    # Test-only account fixtures share the fresh run's password hash. No hash or password is
    # read into evidence, and each account must authenticate through the actual Identity pipeline.
    $adminAccount=$session.Body.principal.accountId
    foreach ($kind in @('own','team','denied')) {
        $email="activity-$kind-$runId@example.test"
        $null = Sql @"
SELECT * INTO #activity_account FROM iam.Accounts WHERE AccountId='$adminAccount';
UPDATE #activity_account SET AccountId='acc_activity_$kind',MemberId='mem_activity_$kind',Email='$email',NormalizedEmail=UPPER('$email'),DisplayName='Activity $kind test';
INSERT INTO iam.Accounts SELECT * FROM #activity_account;
INSERT INTO iam.Credentials (AccountId,PasswordHash,UpdatedAt) SELECT 'acc_activity_$kind',PasswordHash,SYSUTCDATETIME() FROM iam.Credentials WHERE AccountId='$adminAccount';
INSERT INTO workspace.Memberships (MembershipId,WorkspaceId,AccountId,MemberId,Status,CreatedAt)
VALUES ('wsm_activity_$kind','$workspace','acc_activity_$kind','mem_activity_$kind','Active',SYSUTCDATETIME());
DROP TABLE #activity_account;
"@
        if ($kind -ne 'denied') {
            $dataScope=if ($kind -eq 'own') {'Own'} else {'Team'}
            $null = Sql @"
INSERT INTO access.Roles (RoleId,WorkspaceId,Name,NormalizedName,IsActive,Version,CreatedAt,UpdatedAt)
VALUES ('role_activity_$kind','$workspace','Activity $kind',UPPER('Activity $kind'),1,0,SYSUTCDATETIME(),SYSUTCDATETIME());
INSERT INTO access.MembershipRoleAssignments (AssignmentId,WorkspaceId,MembershipId,RoleId,AssignedAt)
VALUES ('assignment_activity_$kind','$workspace','wsm_activity_$kind','role_activity_$kind',SYSUTCDATETIME());
INSERT INTO access.RoleCapabilities (RoleId,Capability) VALUES
('role_activity_$kind','tasks.read'),('role_activity_$kind','tasks.update'),('role_activity_$kind','contacts.read'),
('role_activity_$kind','workspace.context.resolve'),('role_activity_$kind','access.read');
INSERT INTO access.RoleDataScopes (PolicyId,WorkspaceId,RoleId,ResourceKey,Scope,AllowedOwnerIdsJson) VALUES
('scope_activity_contacts_$kind','$workspace','role_activity_$kind','contacts','$dataScope','[]'),
('scope_activity_tasks_$kind','$workspace','role_activity_$kind','tasks','$dataScope','[]');
"@
        }
    }
    $null = Sql "INSERT INTO contacts.Contacts (ContactId,WorkspaceId,OwnerId,FullName,Status,Version,CreatedAt,UpdatedAt,Profile) VALUES ('activity_sales_contact','$workspace','mem_activity_own','Sales own test Contact','active',0,SYSUTCDATETIME(),SYSUTCDATETIME(),N'{}');"
    $adminToken=$script:token
    foreach ($kind in @('own','team','denied')) {
        $script:token=$null
        $salesLogin=Api POST '/auth/sessions' @{email="activity-$kind-$runId@example.test";password=$settings.DevelopmentDemoBootstrap__Password} "sales-$kind-$runId"
        Check "separate $kind account signs in through real authentication" 200 $salesLogin.Status
        $script:token=$salesLogin.Body.accessToken
        if ($kind -eq 'own') {
            $salesOwnToken=$salesLogin.Body.accessToken
            Check 'separate Sales OWN logs own Contact' 201 (Api POST '/activities' (Draft 'activity_sales_contact') 'sales-own-activity').Status
            Check 'separate Sales OWN sees only own Activity' 1 (Api GET '/activities').Body.pageInfo.totalCount
            Check 'separate Sales OWN cannot log other Contact' 404 (Api POST '/activities' $ownDraft 'sales-own-forbidden').Status
        } elseif ($kind -eq 'team') {
            Check 'separate TEAM account cannot read outside admitted scope' 0 (Api GET '/activities').Body.pageInfo.totalCount
            Check 'separate TEAM account cannot write outside admitted scope' 404 (Api POST '/activities' (Draft 'activity_sales_contact') 'sales-team-forbidden').Status
        } else {
            Check 'separate user without capabilities cannot list Activity' 403 (Api GET '/activities').Status
            Check 'separate user without capabilities cannot log Activity' 403 (Api POST '/activities' $ownDraft 'sales-denied').Status
        }
    }
    $script:token=$adminToken
    if ($CustomerTaskReview) {
        $null=Sql @"
INSERT INTO access.RoleCapabilities (RoleId,Capability) VALUES ('role_activity_own','tasks.create'),('role_activity_own','tasks.complete'),('role_activity_own','customers.view');
INSERT INTO access.RoleDataScopes (PolicyId,WorkspaceId,RoleId,ResourceKey,Scope,AllowedOwnerIdsJson) VALUES ('task_customer_sales_scope','$workspace','role_activity_own','customers','Own','[]');
INSERT INTO customers.Customers (WorkspaceId,CustomerId,CustomerCode,OwnerId,Type,RelationshipType,RelationshipId,Status,Version,CreatedAt,UpdatedAt,Profile)
VALUES ('$workspace','customer_sales_task','CU-SALES-TASK','mem_activity_own','B2C','CONTACT','activity_sales_contact','NEW',0,SYSUTCDATETIME(),SYSUTCDATETIME(),N'{}');
"@
        $script:token=$salesOwnToken
        $salesTask=@{title="Sales own task $runId";priority='NORMAL';assigneeId='mem_activity_own';dueAt='2026-12-01T00:00:00Z';recordRef=@{moduleKey='customers';recordId='customer_sales_task'};sourceRef=@{type='CUSTOMER_360';id='customer_sales_task'}}
        $salesCreated=Api POST '/tasks' $salesTask 'sales-customer-task'
        Check 'separate SALES OWN creates own Customer Task' 201 $salesCreated.Status
        $salesTaskId=$salesCreated.Body.aggregateId
        Check 'separate SALES OWN reads own Customer Task' 'customer_sales_task' (Api GET "/tasks/$salesTaskId").Body.recordRef.recordId
        Check 'separate SALES OWN lists only its own Customer Task' $salesTaskId ((Api GET '/tasks?recordModuleKey=customers&recordId=customer_sales_task').Body.items.id -join ',')
        $null=Sql "UPDATE customers.Customers SET OwnerId='$member' WHERE CustomerId='customer_sales_task'"
        Check 'separate SALES OWN cannot create for another owner Customer' 404 (Api POST '/tasks' $salesTask 'sales-customer-forbidden').Status
        Check 'separate SALES OWN replay after Customer revocation denied' 404 (Api POST '/tasks' $salesTask 'sales-customer-task').Status
        $script:token=$null
        $deniedLogin=Api POST '/auth/sessions' @{email="activity-denied-$runId@example.test";password=$settings.DevelopmentDemoBootstrap__Password} 'denied-task-login'
        $script:token=$deniedLogin.Body.accessToken
        Check 'separate unprivileged user cannot create Customer Task' 403 (Api POST '/tasks' $salesTask 'denied-customer-task').Status
        Check 'separate unprivileged user cannot get Task' 403 (Api GET "/tasks/$salesTaskId").Status
        $script:token=$adminToken
    }
    if ($RaceReview) {
        Scope contacts Own
        foreach ($raceKey in @("race-$runId", 'activity-own')) {
        $lockConnection = [System.Data.SqlClient.SqlConnection]::new($connection)
        $lockConnection.Open()
        $lockTransaction = $lockConnection.BeginTransaction()
        $client = [System.Net.Http.HttpClient]::new()
        try {
            $lockCommand = $lockConnection.CreateCommand(); $lockCommand.Transaction=$lockTransaction
            $lockCommand.CommandText='SELECT COUNT(*) FROM tasks.IdempotencyRecords WITH (TABLOCKX,HOLDLOCK)'
            $null=$lockCommand.ExecuteScalar()
            $request=[System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post,"http://127.0.0.1:$Port/activities")
            $request.Headers.Add('Authorization',"Bearer $script:token")
            $request.Headers.Add('X-Workspace-Id',$workspace)
            $request.Headers.Add('Idempotency-Key',$raceKey)
            $request.Headers.Add('X-Request-Id',"race-$runId")
            $request.Headers.Add('X-Correlation-Id',"race-$runId")
            $request.Content=[System.Net.Http.StringContent]::new(((Draft)|ConvertTo-Json -Depth 5),[Text.Encoding]::UTF8,'application/json')
            $pending=$client.SendAsync($request)
            $blocked=$false
            for ($attempt=0;$attempt -lt 50;$attempt++) {
                $waiting=(Sql "SELECT COUNT(*) AS Count FROM sys.dm_exec_requests r CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) t WHERE r.database_id=DB_ID() AND r.wait_type LIKE 'LCK%' AND t.text LIKE '%IdempotencyRecords%'").Rows[0].Count
                if ($waiting -gt 0) { $blocked=$true; break }
                Start-Sleep -Milliseconds 100
            }
            if (!$blocked) {
                $earlyStatus=if($pending.IsCompleted){[int]$pending.GetAwaiter().GetResult().StatusCode}else{'pending'}
                throw "Race barrier not reached; result is inconclusive; HTTP=$earlyStatus"
            }
            $null=Sql "UPDATE contacts.Contacts SET OwnerId='activity_other_member' WHERE ContactId='activity_contact_own' AND WorkspaceId='$workspace'"
            $lockTransaction.Commit()
            $raceResponse=$pending.GetAwaiter().GetResult()
            Check "owner revoked while command waits cannot write or replay ($raceKey)" 404 ([int]$raceResponse.StatusCode)
            @{barrier='SQL idempotency table lock';idempotencyKey=$raceKey;ownerChangedBeforeRelease=$true;status=[int]$raceResponse.StatusCode} | ConvertTo-Json | Set-Content (Join-Path $evidence "race-review-$raceKey.json")
            $null=Sql "UPDATE contacts.Contacts SET OwnerId='$member' WHERE ContactId='activity_contact_own' AND WorkspaceId='$workspace'"
        } finally { $lockTransaction.Dispose(); $lockConnection.Dispose(); $client.Dispose() }
        }
        Scope contacts Workspace
    }
    if ($CommitRaceReview) {
        Scope contacts Own
        $lockConnection=[System.Data.SqlClient.SqlConnection]::new($connection);$lockConnection.Open()
        $lockTransaction=$lockConnection.BeginTransaction()
        $mutationConnection=[System.Data.SqlClient.SqlConnection]::new($connection);$mutationConnection.Open()
        $client=[System.Net.Http.HttpClient]::new()
        try {
            $lockCommand=$lockConnection.CreateCommand();$lockCommand.Transaction=$lockTransaction
            $lockCommand.CommandText='SELECT COUNT(*) FROM tasks.Activities WITH (TABLOCKX,HOLDLOCK)'
            $null=$lockCommand.ExecuteScalar()
            $request=[System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post,"http://127.0.0.1:$Port/activities")
            foreach($header in @{Authorization="Bearer $script:token";'X-Workspace-Id'=$workspace;'Idempotency-Key'="commit-race-$runId";'X-Request-Id'="commit-race-$runId";'X-Correlation-Id'="commit-race-$runId"}.GetEnumerator()){$request.Headers.Add($header.Key,$header.Value)}
            $request.Content=[System.Net.Http.StringContent]::new(((Draft)|ConvertTo-Json -Depth 5),[Text.Encoding]::UTF8,'application/json')
            $pending=$client.SendAsync($request)
            $blocked=$false
            for($attempt=0;$attempt -lt 80;$attempt++) {
                $waiting=(Sql "SELECT COUNT(*) AS Count FROM sys.dm_exec_requests r CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) t WHERE r.database_id=DB_ID() AND r.wait_type LIKE 'LCK%' AND t.text LIKE '%Activities%'").Rows[0].Count
                if($waiting -gt 0){$blocked=$true;break}
                Start-Sleep -Milliseconds 100
            }
            if(!$blocked){throw 'Commit race barrier not reached; inconclusive'}
            $mutation=$mutationConnection.CreateCommand();$mutation.CommandTimeout=30
            $mutation.CommandText="UPDATE contacts.Contacts SET OwnerId='activity_other_member',ArchivedAt=SYSUTCDATETIME(),Status='archived' WHERE WorkspaceId='$workspace' AND ContactId='activity_contact_own'"
            $mutationPending=$mutation.ExecuteNonQueryAsync()
            # Let the competing transaction reach the target row; do not await it while
            # holding the Activity barrier, because a correct guard must make it wait.
            Start-Sleep -Milliseconds 300
            $mutatedBeforeRelease=$mutationPending.IsCompletedSuccessfully
            $lockTransaction.Commit()
            $response=$pending.GetAwaiter().GetResult()
            $null=$mutationPending.GetAwaiter().GetResult()
            Check 'owner/archive mutation cannot commit before blocked Activity' $false $mutatedBeforeRelease
            Check 'Activity commits before the now-unblocked owner/archive change' 201 ([int]$response.StatusCode)
            @{barrier='Activity INSERT blocked by test table lock';mutationCompletedBeforeActivityRelease=$mutatedBeforeRelease;activityHttp=[int]$response.StatusCode} | ConvertTo-Json | Set-Content (Join-Path $evidence 'commit-race.json')
        } finally {
            $lockTransaction.Dispose();$lockConnection.Dispose();$mutationConnection.Dispose();$client.Dispose()
            $null=Sql "UPDATE contacts.Contacts SET OwnerId='$member',ArchivedAt=NULL,Status='active' WHERE WorkspaceId='$workspace' AND ContactId='activity_contact_own'"
            Scope contacts Workspace
        }
    }
    if ($ScaleReview) {
        $metrics=[Collections.Generic.List[object]]::new()
        $apiChild=Get-CimInstance Win32_Process -Filter "ParentProcessId=$($process.Id)" | Where-Object Name -Like '*ApiHost*' | Select-Object -First 1
        $memoryProcess=if($apiChild){Get-Process -Id $apiChild.ProcessId}else{$null}
        function Measure-ActivityRead([string]$shape,[int]$size,[string]$scope,[string]$kind,[string]$path,[int]$expectedCount,[int]$expectedItems) {
            $commandsBefore=@(Select-String -LiteralPath (Join-Path $evidence 'host.log') -Pattern 'Executed DbCommand').Count
            $securedBefore=@(Select-String -LiteralPath (Join-Path $evidence 'host.log') -Pattern '^\s*-- Activity authorized reference query').Count
            if($memoryProcess){$memoryProcess.Refresh();$privateBefore=$memoryProcess.PrivateMemorySize64;$workingBefore=$memoryProcess.WorkingSet64}
            $watch=[Diagnostics.Stopwatch]::StartNew()
            $response=Api GET $path
            $watch.Stop()
            if($memoryProcess){$memoryProcess.Refresh();$privateAfter=$memoryProcess.PrivateMemorySize64;$workingAfter=$memoryProcess.WorkingSet64}
            $commandsAfter=@(Select-String -LiteralPath (Join-Path $evidence 'host.log') -Pattern 'Executed DbCommand').Count
            $securedAfter=@(Select-String -LiteralPath (Join-Path $evidence 'host.log') -Pattern '^\s*-- Activity authorized reference query').Count
            $metrics.Add(@{shape=$shape;size=$size;scope=$scope;kind=$kind;path=$path;sqlCommands=$commandsAfter-$commandsBefore;securedSqlCommands=$securedAfter-$securedBefore;elapsedMs=$watch.Elapsed.TotalMilliseconds;status=$response.Status;totalCount=$response.Body.pageInfo.totalCount;itemCount=@($response.Body.items).Count;privateBytesBefore=$privateBefore;privateBytesAfter=$privateAfter;workingSetBefore=$workingBefore;workingSetAfter=$workingAfter})
            Check "$shape/$size/$scope/$kind HTTP" 200 $response.Status
            Check "$shape/$size/$scope/$kind exactly COUNT and page SQL" 2 ($securedAfter-$securedBefore)
            Check "$shape/$size/$scope/$kind totalCount" $expectedCount $response.Body.pageInfo.totalCount
            Check "$shape/$size/$scope/$kind items" $expectedItems @($response.Body.items).Count
            if($shape -eq 'distinct' -and $scope -eq 'Own') {
                $deniedItems=@($response.Body.items | Where-Object { $_.recordRef.recordId -match '^perf_contact_(\d+)$' -and [int]$Matches[1] % 2 -eq 0 })
                Check "$shape/$size/$scope/$kind no denied target" 0 $deniedItems.Count
            }
            return $response
        }
        $benchWorkspaceBaseCount=(Api GET '/activities?limit=1').Body.pageInfo.totalCount
        $script:token=$salesOwnToken
        $benchOwnBaseCount=(Api GET '/activities?limit=1').Body.pageInfo.totalCount
        $script:token=$adminToken
        $null=Sql "INSERT INTO contacts.Contacts (ContactId,WorkspaceId,OwnerId,FullName,Status,Version,CreatedAt,UpdatedAt,Profile) VALUES ('perf_same','$workspace','mem_activity_own','Performance same target','active',0,SYSUTCDATETIME(),SYSUTCDATETIME(),N'{}');"
        foreach($shape in @('same','distinct')) {
            foreach($size in @(100,1000,5000)) {
                if($shape -eq 'same') {
                    $null=Sql @"
;WITH n AS (SELECT TOP ($size) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i FROM sys.all_objects a CROSS JOIN sys.all_objects b)
INSERT INTO tasks.Activities (ActivityId,WorkspaceId,Type,Subject,ActorId,OccurredAt,RecordModuleKey,RecordId,Version)
SELECT CONCAT('perf_same_',i),'$workspace',3,'bench_same','$member',DATEADD(SECOND,CONVERT(int,i),'2026-01-01'),'contacts','perf_same',0 FROM n
WHERE NOT EXISTS (SELECT 1 FROM tasks.Activities a WHERE a.ActivityId=CONCAT('perf_same_',n.i));
"@
                } else {
                    $null=Sql @"
;WITH n AS (SELECT TOP ($size) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i FROM sys.all_objects a CROSS JOIN sys.all_objects b)
INSERT INTO contacts.Contacts (ContactId,WorkspaceId,OwnerId,FullName,Status,Version,CreatedAt,UpdatedAt,Profile)
SELECT CONCAT('perf_contact_',i),'$workspace',CASE WHEN i%2=1 THEN 'mem_activity_own' ELSE 'activity_other_member' END,'Performance target','active',0,SYSUTCDATETIME(),SYSUTCDATETIME(),N'{}' FROM n
WHERE NOT EXISTS (SELECT 1 FROM contacts.Contacts c WHERE c.ContactId=CONCAT('perf_contact_',n.i));
INSERT INTO tasks.Activities (ActivityId,WorkspaceId,Type,Subject,ActorId,OccurredAt,RecordModuleKey,RecordId,Version)
SELECT CONCAT('perf_distinct_',c.ContactId),'$workspace',3,CASE WHEN c.OwnerId='mem_activity_own' THEN 'bench_distinct' ELSE 'bench_distinct bench_hidden' END,'$member',SYSUTCDATETIME(),'contacts',c.ContactId,0 FROM contacts.Contacts c
WHERE c.WorkspaceId='$workspace' AND c.ContactId LIKE 'perf_contact_%' AND NOT EXISTS (SELECT 1 FROM tasks.Activities a WHERE a.ActivityId=CONCAT('perf_distinct_',c.ContactId));
"@
                }
                foreach($scopeName in @('Workspace','Own')) {
                    $script:token=if($scopeName -eq 'Own'){$salesOwnToken}else{$adminToken}
                    $count=if($shape -eq 'same' -or $scopeName -eq 'Workspace'){$size}else{$size/2}
                    $baseCount=if($scopeName -eq 'Own'){$benchOwnBaseCount}else{$benchWorkspaceBaseCount}
                    $globalCount=$baseCount+$count+$(if($shape -eq 'distinct'){5000}else{0})
                    $null=Measure-ActivityRead $shape $size $scopeName 'unfiltered-limit1' '/activities?limit=1' $globalCount 1
                    $globalPage=Measure-ActivityRead $shape $size $scopeName 'unfiltered-limit50' '/activities?limit=50' $globalCount 50
                    $null=Measure-ActivityRead $shape $size $scopeName 'unfiltered-next50' "/activities?limit=50&cursor=$($globalPage.Body.pageInfo.nextCursor)" $globalCount ([Math]::Min(50,$globalCount-50))
                    $base="/activities?search=bench_$shape"
                    $null=Measure-ActivityRead $shape $size $scopeName 'global-limit1' "$base&limit=1" $count 1
                    $page=Measure-ActivityRead $shape $size $scopeName 'global-limit50' "$base&limit=50" $count ([Math]::Min(50,$count))
                    if($page.Body.pageInfo.hasNextPage){$null=Measure-ActivityRead $shape $size $scopeName 'global-next50' "$base&limit=50&cursor=$($page.Body.pageInfo.nextCursor)" $count ([Math]::Min(50,$count-50))}
                    else{Check "$shape/$size/$scopeName exhausted cursor" $false $page.Body.pageInfo.hasNextPage}
                    $target=if($shape -eq 'same'){'perf_same'}else{'perf_contact_1'}
                    $scopedCount=if($shape -eq 'same'){$size}else{1}
                    $scopedBase="$base&recordModuleKey=contacts&recordId=$target"
                    $null=Measure-ActivityRead $shape $size $scopeName 'record-limit1' "$scopedBase&limit=1" $scopedCount 1
                    $scopedPage=Measure-ActivityRead $shape $size $scopeName 'record-limit50' "$scopedBase&limit=50" $scopedCount ([Math]::Min(50,$scopedCount))
                    if($scopedPage.Body.pageInfo.hasNextPage){$null=Measure-ActivityRead $shape $size $scopeName 'record-next50' "$scopedBase&limit=50&cursor=$($scopedPage.Body.pageInfo.nextCursor)" $scopedCount ([Math]::Min(50,$scopedCount-50))}
                    if($shape -eq 'distinct') {
                        $hiddenCount=if($scopeName -eq 'Own'){0}else{$size/2}
                        $hidden=Measure-ActivityRead $shape $size $scopeName 'hidden-search' '/activities?search=bench_hidden&limit=1' $hiddenCount ([Math]::Min(1,$hiddenCount))
                        Check "$shape/$size/$scopeName hidden continuation" ($hiddenCount -gt 1) $hidden.Body.pageInfo.hasNextPage
                    }
                }
            }
        }
        Scope contacts Workspace
        $script:token=$adminToken
        $metrics | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $evidence 'performance-matrix.json')
        $null=Sql 'EXEC sys.sp_query_store_flush_db;'
        $plans=Sql "SELECT TOP (4) q.query_id,p.plan_id,t.query_sql_text,p.query_plan FROM sys.query_store_query_text t JOIN sys.query_store_query q ON q.query_text_id=t.query_text_id JOIN sys.query_store_plan p ON p.query_id=q.query_id WHERE t.query_sql_text LIKE '%FROM tasks.Activities a%' AND t.query_sql_text LIKE '%@activityWorkspace%' ORDER BY p.plan_id DESC"
        $planIndex=0
        foreach($plan in $plans.Rows){$planIndex++;$plan.query_plan | Set-Content (Join-Path $evidence "activity-plan-$planIndex.sqlplan");$plan.query_sql_text | Set-Content (Join-Path $evidence "activity-query-$planIndex.sql")}
        $indexes=Sql "SELECT i.name AS IndexName,c.name AS ColumnName,ic.key_ordinal FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE i.object_id=OBJECT_ID('tasks.Activities') AND ic.key_ordinal>0 ORDER BY i.name,ic.key_ordinal"
        @($indexes.Rows|ForEach-Object{@{index=$_.IndexName;column=$_.ColumnName;ordinal=$_.key_ordinal}})|ConvertTo-Json|Set-Content (Join-Path $evidence 'activity-indexes.json')
    }
    if ($Browser -and !$UiReview) { Invoke-ConnectedBrowser }
    $beforeRestart = Api GET '/activities?recordModuleKey=contacts&recordId=activity_contact_own'
    Stop-Process -Id $process.Id
    $process.WaitForExit()
    $process = Start-Process dotnet -ArgumentList @('run','--no-build','--no-launch-profile','--project',$project) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $evidence 'restart.log') -RedirectStandardError (Join-Path $evidence 'restart-error.log')
    $ready=$false
    for ($i=0; $i -lt 90; $i++) {
        try { $null = Api GET '/auth/session'; $ready=$true; break } catch { Start-Sleep -Seconds 1 }
        if ($process.HasExited) { throw 'Restarted ApiHost exited' }
    }
    if (!$ready) { throw 'Restarted ApiHost readiness timeout' }
    $afterRestart = Api GET '/activities?recordModuleKey=contacts&recordId=activity_contact_own'
    Check 'SQL Activity survives ApiHost restart' ($beforeRestart.Body.items.id -join ',') ($afterRestart.Body.items.id -join ',')
    $script:token=$null
    $login = Api POST '/auth/sessions' @{email=$settings.DevelopmentDemoBootstrap__Email;password=$settings.DevelopmentDemoBootstrap__Password} "relogin-$runId"
    Check 'real reauthentication after restart' 200 $login.Status
    $script:token=$login.Body.accessToken
    Check 'reauthenticated session reads persisted Activity' $beforeRestart.Body.pageInfo.totalCount (Api GET '/activities?recordModuleKey=contacts&recordId=activity_contact_own').Body.pageInfo.totalCount
    if ($Browser -and !$UiReview) {
        $recordFile=Get-ChildItem (Join-Path $frontend 'test-results/crm-real') -Filter records.json -Recurse | Select-Object -First 1
        if ($recordFile) {
            $records=Get-Content $recordFile.FullName -Raw | ConvertFrom-Json
            foreach ($field in @('leadId','contactId','organizationId','customerId','taskMeetingId','activityId')) {
                if ($records.$field -notmatch '^[A-Za-z0-9_-]{1,128}$') { throw 'Unexpected test record identifier' }
            }
            foreach ($target in @(@('leads','leadId'),@('contacts','contactId'),@('organizations','organizationId'),@('customers','customerId'),@('tasks','taskMeetingId'))) {
                Check "SQL-backed $($target[0]) survives ApiHost restart" 200 (Api GET "/$($target[0])/$($records.($target[1]))").Status
            }
            $customerCount=(Sql "SELECT COUNT(*) AS Count FROM customers.Customers WHERE WorkspaceId='$workspace' AND RelationshipType='CONTACT' AND RelationshipId='$($records.contactId)'").Rows[0].Count
            Check 'SQL conversion retry creates exactly one Customer for subject' 1 $customerCount
            Check 'SQL Lead retains canonical Customer reference' $records.customerId (Sql "SELECT CustomerRef FROM leads.Leads WHERE WorkspaceId='$workspace' AND LeadId='$($records.leadId)'").Rows[0].CustomerRef
            Check 'SQL Contact retains active Organization relationship' 1 (Sql "SELECT COUNT(*) AS Count FROM contacts.OrganizationRelationships WHERE WorkspaceId='$workspace' AND ContactId='$($records.contactId)' AND OrganizationId='$($records.organizationId)' AND EffectiveTo IS NULL").Rows[0].Count
            Check 'SQL Meeting retains Customer reference' $records.customerId (Sql "SELECT RecordId FROM tasks.Tasks WHERE WorkspaceId='$workspace' AND TaskId='$($records.taskMeetingId)'").Rows[0].RecordId
            if ($records.organizationCustomerId) {
                if ($records.organizationCustomerId -notmatch '^[A-Za-z0-9_-]{1,128}$') { throw 'Unexpected Organization Customer identifier' }
                Check 'SQL canonical Organization Customer subject is unique' 1 (Sql "SELECT COUNT(*) AS Count FROM customers.Customers WHERE WorkspaceId='$workspace' AND RelationshipType='ORGANIZATION_ACCOUNT' AND RelationshipId='$($records.organizationId)' AND CustomerId='$($records.organizationCustomerId)'").Rows[0].Count
            }
            Copy-Item -LiteralPath $recordFile.FullName -Destination (Join-Path $evidence 'browser-records.json')
            if ($ProjectionReview) {
                $null=Sql "UPDATE contacts.OrganizationRelationships SET EffectiveTo=SYSUTCDATETIME() WHERE WorkspaceId='$workspace' AND ContactId='$($records.contactId)' AND OrganizationId='$($records.organizationId)' AND EffectiveTo IS NULL"
                Check 'historical Activity remains after Organization affiliation ends' 1 (Api GET "/activities?recordModuleKey=contacts&recordId=$($records.contactId)").Body.pageInfo.totalCount
                $endedDraft=Draft $records.contactId; $endedDraft.relationshipRef=@{type='ORGANIZATION_ACCOUNT';id=$records.organizationId}
                Check 'new Activity cannot claim ended Organization affiliation' 404 (Api POST '/activities' $endedDraft 'ended-affiliation').Status
            }
        }
    }
    if ($ActivityContractReview) {
        Scope tasks Workspace; Scope contacts Workspace; Scope customers Workspace
        $null=Sql "UPDATE contacts.Contacts SET OwnerId='$member',ArchivedAt=NULL,Status='active' WHERE ContactId='activity_contact_own'; UPDATE customers.Customers SET Status='NEW' WHERE CustomerId='activity_customer'"
        foreach ($manualType in @('CALL','EMAIL','MEETING','NOTE','MESSAGE')) {
            $manual=Draft; $manual.type=$manualType
            Check "F01 manual $manualType HTTP write" 201 (Api POST '/activities' $manual "f01-$manualType").Status
        }
        foreach ($invalidType in @('SYSTEM','UNKNOWN','note')) {
            $invalid=Draft; $invalid.type=$invalidType
            Check "F01 invalid $invalidType HTTP write" 422 (Api POST '/activities' $invalid "f01-invalid-$invalidType").Status
        }
        Check 'F01 unanchored manual request' 422 (Api POST '/activities' @{type='NOTE';subject='Unanchored contract'} 'f01-unanchored').Status
        Check 'F01 missing record target' 404 (Api POST '/activities' (Draft 'f01_nonexistent') 'f01-missing').Status
        Check 'F01 foreign Workspace record' 404 (Api POST '/activities' (Draft 'activity_contact_foreign') 'f01-foreign').Status
        $conflict=Draft; $conflict.relationshipRef=@{type='CONTACT';id='activity_contact_other'}
        Check 'F01 contradictory references' 422 (Api POST '/activities' $conflict 'f01-conflict').Status
        $forged=Draft; $forged.sourceRef=@{type='SYSTEM';id='activity_contact_own';evidence='untrusted-client-claim'}
        Check 'F01 forged source' 422 (Api POST '/activities' $forged 'f01-forged').Status
        $valid=Draft 'activity_customer' 'customers'; $valid.relationshipRef=@{type='CONTACT';id='activity_contact_own'}; $valid.sourceRef=@{type='CUSTOMER_360';id='activity_customer'}
        Check 'F01 canonical Customer Contact multi-reference' 201 (Api POST '/activities' $valid 'f01-canonical').Status
        Check 'F01 relationship-only Contact Note' 201 (Api POST '/activities' @{type='NOTE';subject='Relationship only contract';relationshipRef=@{type='CONTACT';id='activity_contact_own'}} 'f01-relationship').Status
    }
} finally {
    if ($process -and !$process.HasExited) { Stop-Process -Id $process.Id }
    foreach ($setting in $oldEnvironment.GetEnumerator()) { [Environment]::SetEnvironmentVariable($setting.Key, $setting.Value) }
    $results | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $evidence 'results.json')
    $http | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $evidence 'http.json')
    @{runId=$runId;startedAtUtc=$startedAt;finishedAtUtc=[DateTimeOffset]::UtcNow.ToString('O');
      backendCommit=(& git -C $repo rev-parse HEAD);database=$database;sqlServer=$SqlServer} |
      ConvertTo-Json | Set-Content (Join-Path $evidence 'run.json')
    Write-Host "Evidence: $evidence; isolated database retained: $database"
}
if (@($results | Where-Object status -eq FAIL).Count) { exit 1 }
