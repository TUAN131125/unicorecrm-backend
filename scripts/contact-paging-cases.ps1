# Focused real-host cases. Dot-source only inside verify-contacts-read-core.ps1.
function Invoke-ContactPagingCases {
    $prefix = 'contact_paging_fixture_'
    $filter = 'search=PagingFixture'
    $originalZone = Get-Scalar -Database $DatabaseName -Query "SELECT TimeZone FROM workspace.BootstrapProjections WHERE WorkspaceId='$($script:WorkspaceId)'"
    try {
        Invoke-SqlNonQuery -Database $DatabaseName -Query "UPDATE workspace.BootstrapProjections SET TimeZone='Asia/Ho_Chi_Minh' WHERE WorkspaceId='$($script:WorkspaceId)'"
        Invoke-SqlNonQuery -Database $DatabaseName -Query @"
DECLARE @i int = 1;
WHILE @i <= 110
BEGIN
 INSERT INTO contacts.Contacts
 (ContactId, WorkspaceId, OwnerId, FullName, Status, Version, CreatedAt, UpdatedAt, Profile)
 VALUES (CONCAT('$prefix', FORMAT(@i,'000')), '$($script:WorkspaceId)',
 CASE WHEN @i % 2 = 0 THEN 'mem-contacts-read-other' ELSE '$callerMemberId' END,
 CONCAT('PagingFixture ', FORMAT(@i,'000')), CASE WHEN @i % 2 = 0 THEN 'needs_follow_up' ELSE 'active' END,
 0, '2026-01-01T00:00:00+00:00', '2026-01-01T00:00:00+00:00',
 N'{"source":"paging-source","relationshipLevel":"warm","decisionRole":"decision_maker","doNotContact":true}');
 SET @i += 1;
END;
"@
        Set-ContactScope -RoleId $roleId -Scope 'Workspace'
        $zoneId = Get-Scalar -Database $DatabaseName -Query "SELECT TimeZone FROM workspace.BootstrapProjections WHERE WorkspaceId='$($script:WorkspaceId)'"
        $zone = [TimeZoneInfo]::FindSystemTimeZoneById($zoneId)
        $localToday = [TimeZoneInfo]::ConvertTime([DateTimeOffset]::UtcNow, $zone).Date
        $todayNoon = [TimeZoneInfo]::ConvertTimeToUtc([DateTime]::SpecifyKind($localToday.AddHours(12), [DateTimeKind]::Unspecified), $zone).ToString('o')
        # Persist the same instant with a non-UTC offset to detect offset passthrough or relabeling.
        $todayNoonOffset = [DateTimeOffset]::Parse($todayNoon, [Globalization.CultureInfo]::InvariantCulture).ToOffset([TimeSpan]::FromHours(7)).ToString('o', [Globalization.CultureInfo]::InvariantCulture)
        $yesterdayNoon = [TimeZoneInfo]::ConvertTimeToUtc([DateTime]::SpecifyKind($localToday.AddDays(-1).AddHours(12), [DateTimeKind]::Unspecified), $zone).ToString('o')
        $todayDate = $localToday.ToString('yyyy-MM-dd')
        $dayStart = [TimeZoneInfo]::ConvertTimeToUtc([DateTime]::SpecifyKind($localToday, [DateTimeKind]::Unspecified), $zone)
        $boundaryToday = $dayStart.AddMinutes(1).ToString('o')
        $boundaryOverdue = $dayStart.AddMinutes(-1).ToString('o')
        Invoke-SqlNonQuery -Database $DatabaseName -Query @"
INSERT INTO tasks.Tasks (TaskId, WorkspaceId, Title, Status, Priority, AssigneeId, DueAt, RecordModuleKey, RecordId, CreatedAt, UpdatedAt, Version)
VALUES
('task_paging_001', '$($script:WorkspaceId)', 'Paging follow up yesterday', 0, 0, '$callerMemberId', '$yesterdayNoon', 'contacts', '${prefix}001', SYSUTCDATETIME(), SYSUTCDATETIME(), 0),
('task_paging_002', '$($script:WorkspaceId)', 'Paging follow up today', 0, 0, '$callerMemberId', '$todayNoonOffset', 'contacts', '${prefix}002', SYSUTCDATETIME(), SYSUTCDATETIME(), 0),
('task_paging_003', '$($script:WorkspaceId)', 'Paging after local midnight', 0, 0, '$callerMemberId', '$boundaryToday', 'contacts', '${prefix}003', SYSUTCDATETIME(), SYSUTCDATETIME(), 0),
('task_paging_004', '$($script:WorkspaceId)', 'Paging before local midnight', 0, 0, '$callerMemberId', '$boundaryOverdue', 'contacts', '${prefix}004', SYSUTCDATETIME(), SYSUTCDATETIME(), 0);
"@
        Add-Result 'follow-up SQL fixture retains a non-UTC offset' '420' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT DATEPART(TZOFFSET, DueAt) FROM tasks.Tasks WHERE WorkspaceId='$($script:WorkspaceId)' AND TaskId='task_paging_002'"))
        $timestampPage = Invoke-Contact -Method 'GET' -Path '/contacts?search=PagingFixture%20002'
        Add-Result 'follow-up timestamp proof reads the real HTTP Contact' "200|1|${prefix}002" "$($timestampPage.Status)|$(@($timestampPage.Body.items).Count)|$($timestampPage.Body.items[0].id)"
        # Inspect the wire string: ConvertFrom-Json can turn ISO timestamps into DateTime values.
        $followUpTimestamp = [regex]::Match($timestampPage.Raw, '"nextFollowUpAt"\s*:\s*"([^"]*)"').Groups[1].Value
        Add-Result 'follow-up HTTP timestamp uses the canonical UTC Z suffix' 'True' ($followUpTimestamp -cmatch 'Z$').ToString()
        Add-Result 'follow-up HTTP timestamp normalizes the non-UTC instant exactly' $todayNoon $followUpTimestamp
        Add-Result 'follow-up HTTP timestamp does not pass through the stored offset' 'True' ($followUpTimestamp -cne $todayNoonOffset).ToString()
        $relabelledLocalTime = $todayNoonOffset -replace '\+07:00$', 'Z'
        Add-Result 'follow-up HTTP timestamp does not relabel local wall time as UTC' 'True' ($followUpTimestamp -cne $relabelledLocalTime).ToString()
        $default = Invoke-Contact -Method 'GET' -Path "/contacts?$filter"
        Add-Result 'paging default limit executes against real SQL' '200|25|110|True' "$($default.Status)|$(@($default.Body.items).Count)|$($default.Body.pageInfo.totalCount)|$($default.Body.pageInfo.hasNextPage)"
        $max = Invoke-Contact -Method 'GET' -Path "/contacts?${filter}&limit=100"
        Add-Result 'paging maximum limit is bounded' '200|100|110|True' "$($max.Status)|$(@($max.Body.items).Count)|$($max.Body.pageInfo.totalCount)|$($max.Body.pageInfo.hasNextPage)"
        foreach ($sort in @('recentlyUpdated', 'nameAsc', 'nextFollowUp')) {
            $ids = [System.Collections.Generic.List[string]]::new()
            $cursor = $null
            $pages = 0
            do {
                $path = "/contacts?${filter}&limit=17&sort=$sort"
                if ($cursor) { $path += '&cursor=' + [Uri]::EscapeDataString($cursor) }
                $page = Invoke-Contact -Method 'GET' -Path $path
                Add-Result "paging $sort page $pages status" '200' $page.Status
                Add-Result "paging $sort page $pages count remains global" '110' ([string]$page.Body.pageInfo.totalCount)
                Add-Result "paging $sort page $pages stays bounded" 'True' (@($page.Body.items).Count -le 17).ToString()
                foreach ($item in $page.Body.items) { $ids.Add($item.id) }
                $cursor = $page.Body.pageInfo.nextCursor
                Add-Result "paging $sort page $pages continuation consistency" 'True' ([bool]$page.Body.pageInfo.hasNextPage -eq (-not [string]::IsNullOrWhiteSpace($cursor))).ToString()
                $pages++
            } while ($cursor -and $pages -lt 10)
            $expected = @(1..110 | ForEach-Object { '{0}{1:000}' -f $prefix, $_ })
            if ($sort -eq 'recentlyUpdated') { [Array]::Reverse($expected) }
            if ($sort -eq 'nextFollowUp') { $expected = @("${prefix}001", "${prefix}004", "${prefix}003", "${prefix}002") + @(5..110 | ForEach-Object { '{0}{1:000}' -f $prefix, $_ }) }
            Add-Result "paging $sort exact tie order with no gaps or duplicates" ($expected -join ',') ($ids -join ',')
            Add-Result "paging $sort traversal terminates" 'True' ([string]::IsNullOrWhiteSpace($cursor)).ToString()
        }
        foreach ($case in @(
            @{ Query = 'ownerScope=my'; Count = 55 },
            @{ Query = "ownerId=$callerMemberId"; Count = 55 },
            @{ Query = 'status=needs_follow_up'; Count = 55 },
            @{ Query = 'source=paging-source&relationshipLevel=warm&decisionRole=decision_maker&doNotContact=true&link=unlinked'; Count = 110 },
            @{ Query = 'link=linked'; Count = 0 },
            @{ Query = 'followUp=today'; Count = 2 },
            @{ Query = 'followUp=overdue'; Count = 2 },
            @{ Query = "nextFollowUpDate=$todayDate"; Count = 2 },
            @{ Query = 'search=PagingFixture%20007'; Count = 1 }
        )) {
            $query = $case.Query
            if ($query -notlike 'search=*') { $query = "${filter}&$query" }
            $response = Invoke-Contact -Method 'GET' -Path "/contacts?$query"
            Add-Result "SQL filtered page $query" "200|$($case.Count)" "$($response.Status)|$($response.Body.pageInfo.totalCount)"
            $summary = Invoke-Contact -Method 'GET' -Path "/contacts/summary?$query"
            Add-Result "SQL summary agrees with filtered page $query" "200|$($case.Count)" "$($summary.Status)|$($summary.Body.totalCount)"
        }
        $summary = Invoke-Contact -Method 'GET' -Path "/contacts/summary?$filter"
        Add-Result 'summary exact status buckets' '110|55|55' "$($summary.Body.totalCount)|$($summary.Body.statusCounts.active)|$($summary.Body.statusCounts.needs_follow_up)"
        foreach ($query in @('limit=0','limit=101','limit=no','limit=1&limit=2','unsupported=true','status=unknown','ownerScope=team','link=unknown','sort=unknown','doNotContact=unknown','cursor=garbage','nextFollowUpDate=bad','followUp=bad',"nextFollowUpDate=$todayDate&followUp=today")) {
            Add-Result "invalid paging query rejected $query" '422' (Invoke-Contact -Method 'GET' -Path "/contacts?$query").Status
        }
        $cursor = [Uri]::EscapeDataString($default.Body.pageInfo.nextCursor)
        foreach ($changed in @('search=Contact','search=PagingFixture&status=active','search=PagingFixture&ownerScope=my','search=PagingFixture&sort=nameAsc','search=PagingFixture&source=paging-source')) {
            Add-Result "cursor cannot change filter $changed" '422' (Invoke-Contact -Method 'GET' -Path "/contacts?${changed}&cursor=$cursor").Status
        }
        Add-Result 'cross-workspace cursor request is denied' '403' (Invoke-Api -Method 'GET' -Path "/contacts?${filter}&cursor=$cursor" -Token $script:Token -WorkspaceId $foreignWorkspaceId).Status
        # A second real authenticated principal distinguishes cursor binding from an auth denial.
        $secondEmail = 'contacts.paging.second@example.test'
        $registered = Invoke-Api -Method 'POST' -Path '/auth/accounts' -IdempotencyKey 'idem-paging-register-second' -Body (@{ email = $secondEmail; password = $demoPassword; displayName = 'Paging second principal' } | ConvertTo-Json -Compress)
        if ($registered.Status -ne 201) { throw "Second principal registration failed: $($registered.Status)" }
        $secondAccount = $registered.Body.accountId
        Invoke-SqlNonQuery -Database $DatabaseName -Query "UPDATE iam.Accounts SET Status='Active', EmailVerifiedAt=SYSUTCDATETIME() WHERE AccountId='$secondAccount'"
        $secondSignIn = Invoke-Api -Method 'POST' -Path '/auth/sessions' -IdempotencyKey 'idem-paging-signin-second' -Body (@{ email = $secondEmail; password = $demoPassword } | ConvertTo-Json -Compress)
        if ($secondSignIn.Status -ne 200) { throw "Second principal sign-in failed: $($secondSignIn.Status)" }
        $secondToken = $secondSignIn.Body.accessToken
        $secondMember = Get-Scalar -Database $DatabaseName -Query "SELECT MemberId FROM iam.Accounts WHERE AccountId='$secondAccount'"
        $callerAccount = Get-Scalar -Database $DatabaseName -Query "SELECT AccountId FROM iam.Accounts WHERE MemberId='$callerMemberId'"
        Invoke-SqlNonQuery -Database $DatabaseName -Query @"
INSERT INTO workspace.Memberships (MembershipId, WorkspaceId, AccountId, MemberId, Status, CreatedAt)
VALUES ('wsm_paging_second', '$($script:WorkspaceId)', '$secondAccount', '$secondMember', 'Active', SYSUTCDATETIME()),
('wsm_paging_foreign', '$foreignWorkspaceId', '$callerAccount', '$callerMemberId', 'Active', SYSUTCDATETIME());
INSERT INTO access.MembershipRoleAssignments (AssignmentId, WorkspaceId, MembershipId, RoleId, AssignedAt)
VALUES ('assignment_paging_second', '$($script:WorkspaceId)', 'wsm_paging_second', '$roleId', SYSUTCDATETIME());
INSERT INTO workspace.BootstrapProjections (WorkspaceId, ContextVersion, ConfigurationVersion, Locale, TimeZone, BaseCurrency, CapabilitiesJson, EnabledModuleKeysJson, AvailableProductSpacesJson)
SELECT '$foreignWorkspaceId', ContextVersion, ConfigurationVersion, Locale, TimeZone, BaseCurrency, CapabilitiesJson, EnabledModuleKeysJson, AvailableProductSpacesJson
FROM workspace.BootstrapProjections WHERE WorkspaceId='$($script:WorkspaceId)';
INSERT INTO access.Roles (RoleId,WorkspaceId,Name,NormalizedName,Description,SourceTemplateId,IsActive,Version,CreatedAt,UpdatedAt)
VALUES ('role_paging_foreign','$foreignWorkspaceId','Paging Reader','PAGING READER','Paging fixture',NULL,1,0,SYSUTCDATETIME(),SYSUTCDATETIME());
INSERT INTO access.RoleCapabilities (RoleId,Capability) VALUES ('role_paging_foreign','contacts.read');
INSERT INTO access.MembershipRoleAssignments (AssignmentId, WorkspaceId, MembershipId, RoleId, AssignedAt)
VALUES ('assignment_paging_foreign','$foreignWorkspaceId','wsm_paging_foreign','role_paging_foreign',SYSUTCDATETIME());
INSERT INTO access.RoleDataScopes (PolicyId, WorkspaceId, RoleId, ResourceKey, Scope, AllowedOwnerIdsJson)
VALUES ('scope_paging_foreign','$foreignWorkspaceId','role_paging_foreign','contacts','Workspace','[]');
"@
        Add-Result 'second principal has independent list access' '200' (Invoke-Api -Method 'GET' -Path "/contacts?$filter" -Token $secondToken -WorkspaceId $script:WorkspaceId).Status
        Add-Result 'cursor binds authenticated principal' '422' (Invoke-Api -Method 'GET' -Path "/contacts?${filter}&cursor=$cursor" -Token $secondToken -WorkspaceId $script:WorkspaceId).Status
        Add-Result 'caller has independent foreign workspace list access' '200' (Invoke-Api -Method 'GET' -Path "/contacts?$filter" -Token $script:Token -WorkspaceId $foreignWorkspaceId).Status
        Add-Result 'cursor binds authorized workspace' '422' (Invoke-Api -Method 'GET' -Path "/contacts?${filter}&cursor=$cursor" -Token $script:Token -WorkspaceId $foreignWorkspaceId).Status
        Add-Result 'summary requires authentication' '401' (Invoke-Api -Method 'GET' -Path '/contacts/summary' -WorkspaceId $script:WorkspaceId).Status
        Set-ContactScope -RoleId $roleId -Scope 'Own'
        Add-Result 'cursor cannot change security scope' '422' (Invoke-Contact -Method 'GET' -Path "/contacts?${filter}&cursor=$cursor").Status
        Add-Result 'my principal summary respects OWN' '55' ([string](Invoke-Contact -Method 'GET' -Path "/contacts/summary?${filter}&ownerScope=my").Body.totalCount)
        Add-Result 'owner filter cannot widen OWN' '0' ([string](Invoke-Contact -Method 'GET' -Path "/contacts?${filter}&ownerId=mem-contacts-read-other").Body.pageInfo.totalCount)
        Set-ContactScope -RoleId $roleId -Scope 'Workspace'
        Invoke-SqlNonQuery -Database $DatabaseName -Query @"
INSERT INTO access.RoleFieldSecurity (PolicyId, WorkspaceId, RoleId, ResourceKey, FieldKey, Access) VALUES
('field_contacts_read_paging_email', '$($script:WorkspaceId)', '$roleId', 'contacts', 'workEmail', 'Hidden'),
('field_contacts_read_paging_source', '$($script:WorkspaceId)', '$roleId', 'contacts', 'source', 'Hidden');
"@
        Add-Result 'hidden search value cannot match' '0' ([string](Invoke-Contact -Method 'GET' -Path ('/contacts?search=' + [Uri]::EscapeDataString($secretA))).Body.pageInfo.totalCount)
        Add-Result 'hidden filter field fails closed' '403' (Invoke-Contact -Method 'GET' -Path '/contacts?source=paging-source').Status
        Add-Result 'hidden summary filter fails closed' '403' (Invoke-Contact -Method 'GET' -Path '/contacts/summary?source=paging-source').Status
        Add-Result 'cursor cannot change searchable field security' '422' (Invoke-Contact -Method 'GET' -Path "/contacts?${filter}&cursor=$cursor").Status
        $projected = Invoke-Contact -Method 'GET' -Path '/contacts?search=Contact%20Alpha'
        Add-Result 'paged FLS projection omits hidden value' 'True' ($projected.Status -eq 200 -and $projected.Raw -notmatch [regex]::Escape($secretA) -and $projected.Raw -notmatch '"workEmail"').ToString()
        Clear-ContactFields
        $archived = Invoke-Contact -Method 'GET' -Path '/contacts?status=archived'
        Add-Result 'explicit archived filter includes preserved archived Contact' 'True' ($archived.Status -eq 200 -and $archived.Body.items.id -contains $archiveId).ToString()
        $archivedSummary = Invoke-Contact -Method 'GET' -Path '/contacts/summary?status=archived'
        Add-Result 'archived summary matches SQL count' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM contacts.Contacts WHERE WorkspaceId='$($script:WorkspaceId)' AND Status='archived'")) ([string]$archivedSummary.Body.totalCount)
    }
    finally {
        Clear-ContactFields
        Set-ContactScope -RoleId $roleId -Scope 'Workspace'
        Invoke-SqlNonQuery -Database $DatabaseName -Query "DELETE FROM tasks.Tasks WHERE TaskId IN ('task_paging_001','task_paging_002','task_paging_003','task_paging_004') AND WorkspaceId='$($script:WorkspaceId)'; DELETE FROM contacts.Contacts WHERE ContactId LIKE '${prefix}%' AND WorkspaceId='$($script:WorkspaceId)'; UPDATE workspace.BootstrapProjections SET TimeZone='$originalZone' WHERE WorkspaceId='$($script:WorkspaceId)'"
        Invoke-SqlNonQuery -Database $DatabaseName -Query @"
DELETE FROM access.MembershipRoleAssignments WHERE AssignmentId IN ('assignment_paging_second','assignment_paging_foreign');
DELETE FROM access.RoleDataScopes WHERE PolicyId='scope_paging_foreign';
DELETE FROM access.RoleCapabilities WHERE RoleId='role_paging_foreign';
DELETE FROM access.Roles WHERE RoleId='role_paging_foreign';
DELETE FROM workspace.Memberships WHERE MembershipId IN ('wsm_paging_second','wsm_paging_foreign');
DELETE FROM workspace.BootstrapProjections WHERE WorkspaceId='$foreignWorkspaceId';
"@
    }
}
