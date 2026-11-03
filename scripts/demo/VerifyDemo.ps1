function Invoke-DemoVerification {
    $checks=[Collections.Generic.List[object]]::new()
    function Check([string]$Name,[bool]$Condition) {
        $checks.Add(@{test=$Name;status=$(if($Condition){'PASS'}else{'FAIL'})})
        Assert-Demo $Condition 'VERIFICATION_FAILED' $Name
    }
    $bootstrap=Invoke-DemoApi GET "/workspaces/$workspace/bootstrap"
    Check 'bootstrap vi/VND/Ho Chi Minh' ($bootstrap.status -eq 200 -and $bootstrap.body.configuration.locale -eq 'vi' -and $bootstrap.body.configuration.baseCurrency -eq 'VND' -and $bootstrap.body.configuration.timeZone -eq 'Asia/Ho_Chi_Minh')
    $expected=@{Lead=150;Contact=100;Organization=35;Customer=70;Deal=180;Task=160;Activity=300;Product=30;Support=25}
    $paths=@{Lead='/leads';Contact='/contacts';Organization='/organizations';Customer='/customers';Deal='/deals';Task='/tasks';Activity='/activities';Product='/products';Support='/support/cases'}
    $counts=Get-DemoCounts
    $summary=@(foreach($module in $expected.Keys|Sort-Object) {
        $row=@($counts|Where-Object {$_.module -eq $module -and $_.workspaceId -eq $workspace})
        $actual=if($row.Count){$row[0].count}else{0}
        Check "SQL $module target" $(if($module -eq 'Activity'){$actual -ge $expected[$module]}else{$actual -eq $expected[$module]})
        $first=Invoke-DemoApi GET "$($paths[$module])?limit=20"
        Check "API $module list" ($first.status -eq 200 -and $(if($module -eq 'Product'){$first.body.Count -eq 30}else{$first.body.items.Count -eq 20}))
        if($module -eq 'Product') {
            $checks.Add(@{test='Product list pagination';status='NOT_SUPPORTED';reason='Canonical listProducts returns a plain array; selection search has its own cursor API'})
        } elseif($first.body.pageInfo.hasNextPage) {
            $next=Invoke-DemoApi GET "$($paths[$module])?limit=20&cursor=$([Uri]::EscapeDataString($first.body.pageInfo.nextCursor))"
            $ids=@($first.body.items.id)
            Check "API $module pagination" ($next.status -eq 200 -and @($next.body.items|Where-Object {$_.id -in $ids}).Count -eq 0 -and $next.body.items.Count -gt 0)
        } else {Check "API $module pagination advertised" $false}
        @{module=$module;target=$expected[$module];sqlCount=$actual;status='PASS'}
    })
    foreach($before in $manifest.before) {
        $after=@($counts|Where-Object {$_.module -eq $before.module -and $_.workspaceId -eq $before.workspaceId})
        Check "existing workspace unchanged $($before.workspaceId) $($before.module)" ($after.Count -eq 1 -and $after[0].count -eq $before.count -and $after[0].hash -eq $before.hash)
    }
    $stageRows=Invoke-DemoSql 'SELECT StageCode,COUNT(*) RecordCount FROM deals.Deals WHERE WorkspaceId=@ws GROUP BY StageCode' @{'@ws'=$workspace}
    $stages=@{};foreach($row in $stageRows.Rows){$stages[$row.StageCode]=[int]$row.RecordCount}
    foreach($entry in @{DISCOVERY=50;QUALIFIED=35;SOLUTION=30;PROPOSAL=25;NEGOTIATION=25;LOST=15}.GetEnumerator()){Check "SQL stage $($entry.Key)" ($stages[$entry.Key] -eq $entry.Value)}
    $customerStates=Invoke-DemoSql 'SELECT Status,COUNT(*) RecordCount FROM customers.Customers WHERE WorkspaceId=@ws GROUP BY Status' @{'@ws'=$workspace}
    foreach($entry in @{NEW=40;ACTIVE=20;INACTIVE=10}.GetEnumerator()){Check "SQL Customer status $($entry.Key)" (@($customerStates.Rows|Where-Object Status -eq $entry.Key)[0].RecordCount -eq $entry.Value)}
    foreach($status in @('new','in_progress','waiting_customer','resolved','closed')) {
        $cases=Invoke-DemoApi GET "/support/cases?status=$status&limit=50"
        Check "API Support status $status" ($cases.status -eq 200 -and $cases.body.items.Count -eq 5)
    }
    $orphans=Invoke-DemoSql @'
SELECT 'affiliations' Relation,COUNT(*) Orphans FROM contacts.OrganizationRelationships r
LEFT JOIN contacts.Contacts c ON c.ContactId=r.ContactId AND c.WorkspaceId=r.WorkspaceId
LEFT JOIN organizations.Organizations o ON o.OrganizationId=r.OrganizationId AND o.WorkspaceId=r.WorkspaceId
WHERE r.WorkspaceId=@ws AND (c.ContactId IS NULL OR o.OrganizationId IS NULL)
UNION ALL SELECT 'deal-buyers',COUNT(*) FROM deals.Deals d
LEFT JOIN contacts.Contacts c ON JSON_VALUE(d.Profile,'$.buyerRef.id')=c.ContactId AND c.WorkspaceId=d.WorkspaceId
LEFT JOIN organizations.Organizations o ON JSON_VALUE(d.Profile,'$.buyerRef.id')=o.OrganizationId AND o.WorkspaceId=d.WorkspaceId
WHERE d.WorkspaceId=@ws AND ((JSON_VALUE(d.Profile,'$.buyerRef.type')='CONTACT' AND c.ContactId IS NULL) OR (JSON_VALUE(d.Profile,'$.buyerRef.type')='ORGANIZATION_ACCOUNT' AND o.OrganizationId IS NULL))
UNION ALL SELECT 'deal-tasks',COUNT(*) FROM tasks.Tasks t
LEFT JOIN deals.Deals d ON d.DealId=t.RecordId AND d.WorkspaceId=t.WorkspaceId
WHERE t.WorkspaceId=@ws AND t.RecordModuleKey='deals' AND d.DealId IS NULL
UNION ALL SELECT 'customer-subjects',COUNT(*) FROM customers.Customers cu
LEFT JOIN contacts.Contacts c ON c.ContactId=cu.RelationshipId AND c.WorkspaceId=cu.WorkspaceId
LEFT JOIN organizations.Organizations o ON o.OrganizationId=cu.RelationshipId AND o.WorkspaceId=cu.WorkspaceId
WHERE cu.WorkspaceId=@ws AND ((cu.RelationshipType='CONTACT' AND c.ContactId IS NULL) OR (cu.RelationshipType='ORGANIZATION_ACCOUNT' AND o.OrganizationId IS NULL))
UNION ALL SELECT 'manual-activities',COUNT(*) FROM tasks.Activities a
LEFT JOIN contacts.Contacts c ON c.ContactId=a.RecordId AND c.WorkspaceId=a.WorkspaceId
WHERE a.WorkspaceId=@ws AND a.Type<>5 AND (a.RecordModuleKey<>'contacts' OR c.ContactId IS NULL)
UNION ALL SELECT 'deal-products',COUNT(*) FROM deals.Deals d CROSS APPLY OPENJSON(d.Profile,'$.interestedProductIds') product
LEFT JOIN products.Products p ON p.ProductId=product.value AND p.WorkspaceId=d.WorkspaceId
WHERE d.WorkspaceId=@ws AND p.ProductId IS NULL
UNION ALL SELECT 'support-buyers',COUNT(*) FROM support.SupportCases s
LEFT JOIN organizations.Organizations o ON s.RelationshipId=o.OrganizationId AND o.WorkspaceId=s.WorkspaceId
WHERE s.WorkspaceId=@ws AND o.OrganizationId IS NULL
'@ @{'@ws'=$workspace}
    foreach($row in $orphans.Rows){Check "SQL no orphan $($row.Relation)" ($row.Orphans -eq 0)}
    # ActivityType is persisted as the CLR enum integer; System=5 in Tasks Domain.
    $manual=(Invoke-DemoSql "SELECT COUNT(*) RecordCount FROM tasks.Activities WHERE WorkspaceId=@ws AND Type<>5" @{'@ws'=$workspace}).Rows[0].RecordCount
    Check 'SQL exactly 300 manual activities' ($manual -eq 300)
    $journey=@{leadId=$manifest.commands['lead-1'].response.aggregateId;contactId=$manifest.commands['contact-1'].response.aggregateId;
      organizationId=$manifest.commands['organization-1'].response.aggregateId;customerId=$manifest.commands['conversion-1'].response.result.customerId;
      dealId=$manifest.commands['deal-1'].response.aggregateId;taskId=$manifest.commands['task-1'].response.aggregateId;activityId=$manifest.commands['activity-1'].response.aggregateId;
      qualificationLeadId=$manifest.commands['lead-0'].response.aggregateId;qualificationDealId=$manifest.commands['qualification-0'].response.result.dealId}
    foreach($module in @('leads','contacts','organizations','customers','deals','tasks')) {
        $key=@{leads='leadId';contacts='contactId';organizations='organizationId';customers='customerId';deals='dealId';tasks='taskId'}[$module]
        $detail=Invoke-DemoApi GET "/$module/$($journey[$key])"
        Check "API journey detail $module" ($detail.status -eq 200)
    }
    $lead=(Invoke-DemoApi GET "/leads/$($journey.leadId)").body
    $customer=(Invoke-DemoApi GET "/customers/$($journey.customerId)").body
    $deal=(Invoke-DemoApi GET "/deals/$($journey.dealId)").body
    $affiliation=(Invoke-DemoApi GET "/contacts/$($journey.contactId)/relationship-summary").body
    Check 'journey Lead conversion persisted' ($lead.customerRef -eq $journey.customerId -and $lead.leadWorkState -eq 'CLOSED' -and $lead.qualificationOutcome -eq 'CUSTOMER')
    Check 'journey Customer and Deal share Organization subject' ($customer.relationshipRef.type -eq 'ORGANIZATION_ACCOUNT' -and $customer.relationshipRef.id -eq $journey.organizationId -and $deal.buyerRef.type -eq 'ORGANIZATION_ACCOUNT' -and $deal.buyerRef.id -eq $journey.organizationId)
    Check 'journey Contact affiliated with Organization' ($journey.organizationId -in $affiliation.organizationIds)
    $timeline=Invoke-DemoApi GET "/activities?recordModuleKey=contacts&recordId=$($journey.contactId)&limit=20"
    Check 'API buyer timeline' ($timeline.status -eq 200 -and $journey.activityId -in $timeline.body.items.id)
    $tasks=Invoke-DemoApi GET "/tasks?recordModuleKey=deals&recordId=$($journey.dealId)&limit=20"
    Check 'API Deal Task follow-up' ($tasks.status -eq 200 -and $journey.taskId -in $tasks.body.items.id)
    $filter=Invoke-DemoApi GET '/deals?stageCode=PROPOSAL&limit=50'
    Check 'API stage filter' ($filter.status -eq 200 -and $filter.body.items.Count -eq 25 -and @($filter.body.items|Where-Object stageCode -ne 'PROPOSAL').Count -eq 0)
    $search=Invoke-DemoApi GET "/deals?search=$([Uri]::EscapeDataString('Minh Phúc'))&limit=50"
    Check 'API search' ($search.status -eq 200 -and $search.body.items.Count -gt 0 -and @($search.body.items|Where-Object {$_.name -notlike '*Minh Phúc*'}).Count -eq 0)
    # Replay actual saved intent, independently of the manifest's skip-completed behavior.
    $intent=$manifest.commands['deal-1']
    $replay=Invoke-DemoApi POST $intent.path $intent.body $intent.key $intent.version
    Check 'backend durable idempotency replay same Deal' ($replay.status -eq 201 -and $replay.body.aggregateId -eq $journey.dealId -and $replay.body.outcome -eq 'REPLAYED')
    $demoWorkspace=$workspace;$script:workspace=$manifest.controlId
    foreach($path in @('/leads','/contacts','/organizations','/customers','/deals','/tasks','/products','/support/cases','/activities')) {
        $empty=Invoke-DemoApi GET "$path`?limit=20"
        $emptyCount=if($path -eq '/products'){$empty.body.Count}else{$empty.body.items.Count}
        Check "control workspace empty $path" ($empty.status -eq 200 -and $emptyCount -eq 0)
    }
    $hidden=Invoke-DemoApi GET "/deals/$($journey.dealId)"
    Check 'cross workspace Deal detail denied' ($hidden.status -in 403,404)
    $changed=Invoke-DemoApi POST "/deals/$($journey.dealId)/next-action" @{nextActionAt=$AsOf.AddDays(2).UtcDateTime.ToString('yyyy-MM-ddTHH:mm:ssZ');nextActionSummary='Isolation probe'} 'isolation-probe-action' 0
    Check 'cross workspace Deal mutation denied' ($changed.status -in 403,404)
    $ref=Invoke-DemoApi POST '/tasks' @{title='Isolation probe';assigneeId=$member;dueAt=$AsOf.AddDays(2).UtcDateTime.ToString('yyyy-MM-ddTHH:mm:ssZ');recordRef=@{moduleKey='deals';recordId=$journey.dealId}} 'isolation-probe-task'
    Check 'cross workspace Task reference denied' ($ref.status -in 403,404)
    $forecast=Invoke-DemoApi GET '/deals/forecast-summary'
    Check 'control forecast contains no demo Deals' ($forecast.status -eq 200 -and @($forecast.body.buckets|Where-Object {$_.openDealCount -gt 0}).Count -eq 0)
    $search=Invoke-DemoApi GET "/deals?search=$([Uri]::EscapeDataString('Minh Phúc'))"
    Check 'control search contains no demo Deals' ($search.status -eq 200 -and $search.body.items.Count -eq 0)
    $script:workspace=$demoWorkspace
    Stop-DemoHost;Start-DemoHost;Connect-DemoIdentity
    $detail=Invoke-DemoApi GET "/deals/$($journey.dealId)"
    Check 'restart and new authentication preserve Deal' ($detail.status -eq 200 -and $detail.body.id -eq $journey.dealId)
    $afterRestart=Get-DemoCounts
    foreach($before in $counts){$after=@($afterRestart|Where-Object {$_.module -eq $before.module -and $_.workspaceId -eq $before.workspaceId});Check "restart SQL count/hash $($before.module) $($before.workspaceId)" ($after.Count -eq 1 -and $after[0].hash -eq $before.hash -and $after[0].count -eq $before.count)}
    @{checks=$checks;dataset=$summary;stages=$stages;journey=$journey;counts=$afterRestart;workspaceId=$workspace;controlId=$manifest.controlId;verifiedAt=[DateTimeOffset]::UtcNow.ToString('O');aiProvider='NOT_RUN';ui='NOT_RUN'}|ConvertTo-Json -Depth 15|Set-Content -LiteralPath (Join-Path $stateDirectory 'verification.json')
}
