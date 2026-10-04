# Focused cases hosted by verify-contacts-read-core.ps1. Not a standalone database runner.
function Invoke-ContactWriteAccessCases {
    foreach ($capability in @('contacts.read', 'contacts.create', 'contacts.update', 'contacts.delete')) {
        Add-Result "new Owner receives $capability exactly once" '1' ([string](Get-Scalar -Database $DatabaseName `
            -Query "SELECT COUNT(*) FROM access.RoleCapabilities WHERE RoleId='$roleId' AND Capability='$capability'"))
    }

    function Get-ContactAccess([string] $ContactId) {
        $result = Invoke-Contact -Method 'POST' -Path '/access/records/evaluate' -Body (@{
            resourceKey = 'contacts'; recordId = $ContactId
            requestedFields = @('fullName', 'workEmail', 'notes')
        } | ConvertTo-Json -Compress)
        Add-Result "effective access HTTP $ContactId" '200' ([string]$result.Status)
        return $result.Body
    }

    $allowed = Get-ContactAccess $contactA
    Add-Result 'Contact descriptor permits authorized read' 'True' ([string]$allowed.canRead)
    Add-Result 'Contact descriptor permits authorized update' 'True' ([string]$allowed.canUpdate)
    Add-Result 'Contact descriptor permits authorized archive' 'True' ([string]$allowed.canDelete)

    # Contact Detail requests canonical owner-declared fields, not compatibility aliases.
    $profileFields = @('fullName','workEmail','personalEmail','mobilePhone','workPhone','otherPhone','organizationRelationships','ownerId','consent')
    function Get-ContactProfileAccess([string[]] $Fields = $profileFields) {
        $result = Invoke-Contact -Method POST -Path '/access/records/evaluate' -Body (@{
            resourceKey = 'contacts'; recordId = $contactA; requestedFields = $Fields
        } | ConvertTo-Json -Compress)
        Add-Result 'Contact field-profile evaluation HTTP' '200' ([string]$result.Status)
        return $result.Body
    }
    $profileEvidence = @{ noPolicy = (Get-ContactProfileAccess) }
    Add-Result 'canonical Contact profile without policies has no restricted fields' '0' `
        ([string]@($profileEvidence.noPolicy.fieldAccess.PSObject.Properties | Where-Object Value -ne 'READ_WRITE').Count)
    $profileEvidence.unknown = Get-ContactProfileAccess ($profileFields + 'unknownContactField')
    Add-Result 'unknown Contact profile field stays fail-closed' 'HIDDEN' $profileEvidence.unknown.fieldAccess.unknownContactField

    foreach ($case in @(@('contacts.update', 'canUpdate'), @('contacts.delete', 'canDelete'))) {
        Invoke-SqlNonQuery -Database $DatabaseName -Query "DELETE FROM access.RoleCapabilities WHERE RoleId='$roleId' AND Capability='$($case[0])'"
        $denied = Get-ContactAccess $contactA
        Add-Result "missing $($case[0]) denies $($case[1]) even for Owner" 'False' ([string]$denied.($case[1]))
        Add-Result "missing $($case[0]) preserves readable record" 'True' ([string]$denied.canRead)
        $profileEvidence[$case[1]] = Get-ContactProfileAccess
        Invoke-SqlNonQuery -Database $DatabaseName -Query "INSERT INTO access.RoleCapabilities VALUES ('$roleId','$($case[0])')"
    }

    $foreign = Get-ContactAccess $contactC
    foreach ($flag in @('canRead', 'canUpdate', 'canDelete')) {
        Add-Result "foreign Contact $flag denied" 'False' ([string]$foreign.$flag)
    }
    Set-ContactScope -RoleId $roleId -Scope 'Own'
    $own = Get-ContactAccess $contactA
    $other = Get-ContactAccess $contactB
    Add-Result 'OWN scope permits owned Contact update' 'True' ([string]$own.canUpdate)
    foreach ($flag in @('canRead', 'canUpdate', 'canDelete')) {
        Add-Result "OWN scope other-owner $flag denied" 'False' ([string]$other.$flag)
    }
    Set-ContactScope -RoleId $roleId -Scope 'Workspace'
    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
INSERT INTO access.RoleFieldSecurity (PolicyId,WorkspaceId,RoleId,ResourceKey,FieldKey,Access) VALUES
('field_c0_hidden','$($script:WorkspaceId)','$roleId','contacts','workEmail','Hidden'),
('field_c0_readonly','$($script:WorkspaceId)','$roleId','contacts','notes','ReadOnly');
"@
    $fields = Get-ContactAccess $contactA
    Add-Result 'field policy does not erase admitted record update' 'True' ([string]$fields.canUpdate)
    Add-Result 'hidden Contact field remains hidden' 'HIDDEN' $fields.fieldAccess.workEmail
    Add-Result 'read-only Contact field remains read-only' 'READ_ONLY' $fields.fieldAccess.notes
    $profileEvidence.realPolicy = Get-ContactProfileAccess
    Add-Result 'canonical Contact profile retains real field restriction' 'HIDDEN' $profileEvidence.realPolicy.fieldAccess.workEmail
    if ($env:CONTACT_FIELD_PROFILE_EVIDENCE_PATH) {
        $profileEvidence | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $env:CONTACT_FIELD_PROFILE_EVIDENCE_PATH -Encoding utf8
    }
    Invoke-SqlNonQuery -Database $DatabaseName -Query "DELETE FROM access.RoleFieldSecurity WHERE PolicyId IN ('field_c0_hidden','field_c0_readonly')"

    $created = Invoke-Api -Method POST -Path '/contacts' -Token $script:Token -WorkspaceId $script:WorkspaceId `
        -IdempotencyKey 'c0-write-create-allowed' -Body '{"fullName":"C0 isolated writer"}'
    Add-Result 'Contact Create authorized server path' '201' ([string]$created.Status)
    $id = $created.Body.aggregateId
    $updated = Invoke-Api -Method PATCH -Path "/contacts/$id" -Token $script:Token -WorkspaceId $script:WorkspaceId `
        -IdempotencyKey 'c0-write-update-allowed' -IfMatch '"0"' -Body '{"fullName":"C0 updated"}'
    Add-Result 'Contact Update authorized server path' '200' ([string]$updated.Status)
    foreach ($case in @(@('contacts.create','POST','/contacts','{"fullName":"Must not exist"}'),
                       @('contacts.update','PATCH',"/contacts/$id",'{"fullName":"Must not update"}'),
                       @('contacts.delete','POST',"/contacts/$id/archive",'{}'))) {
        Invoke-SqlNonQuery -Database $DatabaseName -Query "DELETE FROM access.RoleCapabilities WHERE RoleId='$roleId' AND Capability='$($case[0])'"
        $denied = Invoke-Api -Method $case[1] -Path $case[2] -Body $case[3] -Token $script:Token `
            -WorkspaceId $script:WorkspaceId -IdempotencyKey "c0-denied-$($case[0])" -IfMatch '"1"'
        Add-Result "server requires $($case[0])" '403' ([string]$denied.Status)
        Add-Result "server capability denial $($case[0])" 'ACCESS_DENIED' $denied.Body.code
        Invoke-SqlNonQuery -Database $DatabaseName -Query "INSERT INTO access.RoleCapabilities VALUES ('$roleId','$($case[0])')"
    }
    $archived = Invoke-Api -Method POST -Path "/contacts/$id/archive" -Token $script:Token -WorkspaceId $script:WorkspaceId `
        -IdempotencyKey 'c0-write-archive-allowed' -IfMatch '"1"' -Body '{}'
    Add-Result 'Contact Archive authorized server path' '200' ([string]$archived.Status)
    Add-Result 'denied mutations did not advance version; authorized archive did' 'archived|2' ([string](Get-Scalar `
        -Database $DatabaseName -Query "SELECT CONCAT(Status,'|',Version) FROM contacts.Contacts WHERE ContactId='$id'"))
}

function Invoke-WorkspaceOwnerRepairCases {
    # Frozen V2 server-owned snapshot, InitialWorkspaceAccessPolicy at commit 595eeb1.
    $historical = @('contacts.read','deals.assign','deals.bulk','deals.close','deals.create','deals.delete',
        'deals.read','deals.update','leads.create','leads.qualify','leads.read','leads.update','products.create',
        'products.delete','products.edit','products.read','support.assign','support.create','support.read',
        'support.update','tasks.assign','tasks.complete','tasks.create','tasks.read','tasks.update','workspace.context.resolve')
    $current = @(Invoke-Sql -Database $DatabaseName -Query "SELECT Capability FROM access.RoleCapabilities WHERE RoleId='$roleId' ORDER BY Capability" | ForEach-Object Capability)

    function New-RepairFixture([string] $Name, [string[]] $Capabilities, [string] $Template = 'system:workspace-owner') {
        $ws = "ws_c0_$Name"; $role = "role_c0_$Name"
        $templateSql = if ($Template) { "'$Template'" } else { 'NULL' }
        Invoke-SqlNonQuery -Database $DatabaseName -Query @"
INSERT INTO workspace.Workspaces (WorkspaceId,[Key],Name,LogoText,CreatedAt) VALUES ('$ws','$ws','$ws','C0',SYSUTCDATETIME());
INSERT INTO workspace.Memberships (MembershipId,WorkspaceId,AccountId,MemberId,Status,CreatedAt)
VALUES ('wsm_c0_$Name','$ws','acc_c0_$Name','mem_c0_$Name','Active',SYSUTCDATETIME());
INSERT INTO access.Roles (RoleId,WorkspaceId,Name,NormalizedName,Description,SourceTemplateId,IsActive,Version,CreatedAt,UpdatedAt)
VALUES ('$role','$ws','Workspace Owner','WORKSPACE OWNER','Initial Workspace provisioning role for the account that created this Workspace.',$templateSql,1,0,SYSUTCDATETIME(),SYSUTCDATETIME());
INSERT INTO access.MembershipRoleAssignments (AssignmentId,WorkspaceId,MembershipId,RoleId,AssignedAt)
VALUES ('assignment_c0_$Name','$ws','wsm_c0_$Name','$role',SYSUTCDATETIME());
INSERT INTO access.WorkspaceDirectoryRevisions (WorkspaceId,Revision) VALUES ('$ws',7);
"@
        foreach ($capability in $Capabilities) {
            Invoke-SqlNonQuery -Database $DatabaseName -Query "INSERT INTO access.RoleCapabilities VALUES ('$role','$capability')"
        }
    }
    function Get-RepairState([string] $Name) {
        # Include role scalars, policies, assignments, capabilities and revision in no-op evidence.
        return Get-Scalar -Database $DatabaseName -Query @"
SELECT CONCAT(
 (SELECT * FROM access.Roles WHERE WorkspaceId='ws_c0_$Name' ORDER BY RoleId FOR JSON PATH),
 (SELECT c.* FROM access.RoleCapabilities c JOIN access.Roles r ON r.RoleId=c.RoleId WHERE r.WorkspaceId='ws_c0_$Name' ORDER BY c.RoleId,c.Capability FOR JSON PATH),
 (SELECT * FROM access.MembershipRoleAssignments WHERE WorkspaceId='ws_c0_$Name' ORDER BY AssignmentId FOR JSON PATH),
 (SELECT * FROM access.WorkspaceDirectoryRevisions WHERE WorkspaceId='ws_c0_$Name' FOR JSON PATH),
 (SELECT * FROM access.RoleDataScopes WHERE WorkspaceId='ws_c0_$Name' ORDER BY PolicyId FOR JSON PATH),
 (SELECT * FROM access.RoleFieldSecurity WHERE WorkspaceId='ws_c0_$Name' ORDER BY PolicyId FOR JSON PATH))
"@
    }
    function Run-Repair([string] $Name, [bool] $ShouldFail = $false) {
        # Environment set by parent verifier targets only its isolated database.
        $configured = [System.Data.SqlClient.SqlConnectionStringBuilder]::new($env:ConnectionStrings__UnicoreCRM)
        if ($configured.InitialCatalog -ne $DatabaseName -or $DatabaseName -eq 'UnicoreCRM_Development') {
            throw 'Repair test connection must target the isolated verifier database.'
        }
        $output = & $hostExe --repair-workspace-owner-authority "--workspace-id=ws_c0_$Name" --contentRoot $contentRoot 2>&1
        Add-Result "repair $Name exit classification" ([string]$ShouldFail) ([string]($LASTEXITCODE -ne 0))
        return ($output -join "`n")
    }

    New-RepairFixture 'historical' $historical
    New-RepairFixture 'historicalv1' @($historical | Where-Object { $_ -ne 'contacts.read' })
    New-RepairFixture 'legacy' $historical ''
    [void](Run-Repair 'legacy')
    Add-Result 'legacy null-provenance snapshot is still repaired and system-tagged' 'system:workspace-owner' `
        ([string](Get-Scalar -Database $DatabaseName -Query "SELECT SourceTemplateId FROM access.Roles WHERE RoleId='role_c0_legacy'"))
    $repaired = @(Invoke-Sql -Database $DatabaseName -Query "SELECT Capability FROM access.RoleCapabilities WHERE RoleId='role_c0_legacy' ORDER BY Capability" | ForEach-Object Capability)
    Add-Result 'eligible null-template legacy Owner becomes exact current policy' ($current -join ',') ($repaired -join ',')
    Add-Result 'legacy repair removes no unrelated capabilities' '0' ([string]@($historical | Where-Object { $_ -notin $repaired }).Count)
    Add-Result 'legacy repair advances directory once' '8' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT Revision FROM access.WorkspaceDirectoryRevisions WHERE WorkspaceId='ws_c0_legacy'"))
    $before = Get-RepairState 'legacy'
    [void](Run-Repair 'legacy')
    Add-Result 'second legacy repair is exact no-op including revision and capability rows' 'True' ([string]($before -ceq (Get-RepairState 'legacy')))

    New-RepairFixture 'current' $current
    New-RepairFixture 'custom' $historical 'custom:owner'
    New-RepairFixture 'unknown' ($historical + 'access.read')
    New-RepairFixture 'inactive' $historical
    New-RepairFixture 'unassigned' $historical ''
    New-RepairFixture 'suspended' $historical
    New-RepairFixture 'wrongworkspace' $historical
    New-RepairFixture 'edited' $historical
    New-RepairFixture 'restricted' $historical ''
    New-RepairFixture 'fieldpolicy' $historical ''
    New-RepairFixture 'missingmember' $historical
    New-RepairFixture 'mixedassignments' $historical
    # Exact historically recognized predecessor projection; persisted system-owned creation
    # provenance is unproven, so it is not admitted for automatic repair.
    $affected = @('access.configure','access.read','contacts.read','customers.view','deals.assign','deals.bulk',
        'deals.close','deals.create','deals.delete','deals.read','deals.update','invoices.create','invoices.create_credit_note',
        'invoices.edit','invoices.issue','invoices.read','invoices.send','invoices.update_draft','invoices.void',
        'leads.assign','leads.bulk','leads.create','leads.delete','leads.export','leads.qualify','leads.read','leads.update',
        'orders.complete','orders.confirm','orders.create','orders.credit_approval.decide','orders.credit_approval.request',
        'orders.delete','orders.read','orders.update','organizations.read','payments.allocate','payments.intent.cancel',
        'payments.intent.create','payments.plan.activate','payments.plan.read','payments.plan.supersede',
        'payments.plan.update_draft','payments.read','payments.reconcile','payments.record_manual','payments.refund',
        'payments.reverse_allocation','products.create','products.delete','products.edit','products.read','quotes.approve',
        'quotes.create','quotes.delete','quotes.read','quotes.update','receivables.read','returns.read','returns.resolve',
        'returns.update','shipping.create','shipping.read','studio.configure','studio.read','support.assign',
        'support.create','support.read','support.update','tasks.assign','tasks.complete','tasks.create','tasks.read',
        'tasks.update','workspace.context.resolve')
    New-RepairFixture 'affected' $affected
    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
UPDATE access.Roles SET IsActive=0 WHERE RoleId='role_c0_inactive';
UPDATE access.Roles SET Version=1 WHERE RoleId='role_c0_edited';
DELETE FROM access.MembershipRoleAssignments WHERE RoleId='role_c0_unassigned';
UPDATE workspace.Memberships SET Status='Suspended' WHERE MembershipId='wsm_c0_suspended';
UPDATE access.MembershipRoleAssignments SET MembershipId='wsm_c0_custom' WHERE RoleId='role_c0_wrongworkspace';
UPDATE access.MembershipRoleAssignments SET MembershipId='wsm_c0_missing' WHERE RoleId='role_c0_missingmember';
INSERT INTO access.MembershipRoleAssignments (AssignmentId,WorkspaceId,MembershipId,RoleId,AssignedAt)
VALUES ('assignment_c0_invalid','ws_c0_mixedassignments','wsm_c0_custom','role_c0_mixedassignments',SYSUTCDATETIME());
INSERT INTO access.RoleDataScopes (PolicyId,WorkspaceId,RoleId,ResourceKey,Scope,AllowedOwnerIdsJson)
VALUES ('scope_c0_restricted','ws_c0_restricted','role_c0_restricted','contacts','Own','[]');
INSERT INTO access.RoleFieldSecurity (PolicyId,WorkspaceId,RoleId,ResourceKey,FieldKey,Access)
VALUES ('field_c0_repair','ws_c0_fieldpolicy','role_c0_fieldpolicy','contacts','notes','ReadOnly');
"@
    foreach ($name in @('current','historical','historicalv1','custom','unknown','inactive','unassigned','suspended','wrongworkspace','edited','restricted','fieldpolicy','missingmember','mixedassignments','affected')) {
        $before = Get-RepairState $name
        $output = Run-Repair $name
        Add-Result "$name Owner state preserved exactly" 'True' ([string]($before -ceq (Get-RepairState $name)))
        if ($name -in @('historical','historicalv1','unknown','affected')) {
            Add-Result "$name Owner authority explicitly classified" 'True' ([string]($output -match 'UNKNOWN_OWNER_AUTHORITY_DRIFT'))
        }
    }
    New-RepairFixture 'ambiguous' $historical
    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
INSERT INTO access.Roles (RoleId,WorkspaceId,Name,NormalizedName,Description,SourceTemplateId,IsActive,Version,CreatedAt,UpdatedAt)
VALUES ('role_c0_second','ws_c0_ambiguous','Second Owner','SECOND OWNER',NULL,'system:workspace-owner',1,0,SYSUTCDATETIME(),SYSUTCDATETIME());
"@
    $before = Get-RepairState 'ambiguous'
    [void](Run-Repair 'ambiguous' $true)
    Add-Result 'ambiguous claims fail closed without mutations' 'True' ([string]($before -ceq (Get-RepairState 'ambiguous')))
    Add-Result 'unrelated real fixture Owner capabilities unchanged' ($current -join ',') `
        ((@(Invoke-Sql -Database $DatabaseName -Query "SELECT Capability FROM access.RoleCapabilities WHERE RoleId='$roleId' ORDER BY Capability" | ForEach-Object Capability)) -join ',')
}


# Real HTTP -> configured JSON -> command -> domain -> SQL regression proof.
function Invoke-ContactPatchSemanticsCases {
    $script:ContactPatchCounter = 0
    $initial = [ordered]@{
        fullName = 'Patch Authority'; ownerId = $callerMemberId; salutation = 'Ms'; jobTitle = 'Director'
        department = 'Sales'; roleAtCompany = 'Sponsor'; workEmail = 'patch.work@example.test'
        personalEmail = 'patch.personal@example.test'; mobilePhone = '090123'; workPhone = '091234'
        otherPhone = '092345'; zaloId = 'patch-zalo'; facebook = 'https://example.test/patch'
        preferredContactChannel = 'email'; address = 'Preserved address'; source = 'Verifier'
        decisionRole = 'buyer'; relationshipLevel = 'good'; painPoint = 'Preserved pain'
        needSummary = 'Preserved need'; notes = 'Initial note'; tags = @(' Core ', 'core', '', 'VIP')
        displayName = 'Patch display'
    }
    $created = Invoke-Api -Method POST -Path '/contacts' -Token $script:Token -WorkspaceId $script:WorkspaceId `
        -IdempotencyKey 'patch-create-fixture' -Body ($initial | ConvertTo-Json -Compress)
    Add-Result 'PATCH fixture create remains valid' '201' ([string]$created.Status)
    $script:PatchContactId = $created.Body.aggregateId
    if (-not $script:PatchContactId) { throw 'Contact PATCH fixture creation failed.' }
    $id = $script:PatchContactId
    # These facts are not part of UpdateContact and must never be overwritten by a patch.
    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
UPDATE contacts.Contacts SET Profile = JSON_MODIFY(JSON_MODIFY(JSON_MODIFY(JSON_MODIFY(Profile,
 '$.addressDetails',JSON_QUERY('{"line1":"Private address","line2":null,"ward":null,"district":null,"province":null,"country":null,"postalCode":null,"formatted":null}')),
 '$.consent',JSON_QUERY('{"current":{"email":"granted"},"ledger":[],"updatedAt":"2026-10-04T00:00:00+00:00","lawfulBasis":null}')),
 '$.doNotContact',CAST(1 AS bit)), '$.organizationRelationships',JSON_QUERY('[]')) WHERE ContactId='$id';
"@
    function Read-PatchRow {
        return (Invoke-Sql -Database $DatabaseName -Query "SELECT FullName,OwnerId,Version,Profile FROM contacts.Contacts WHERE ContactId='$script:PatchContactId'")[0]
    }
    function Read-PatchState {
        return [string](Get-Scalar -Database $DatabaseName -Query "SELECT CONCAT(Version,'|',FullName,'|',COALESCE(OwnerId,'<null>'),'|',Profile) FROM contacts.Contacts WHERE ContactId='$script:PatchContactId'")
    }
    function Send-ContactPatch {
        param([string]$Body, [string]$Key, [Nullable[long]]$Version = $null)
        $script:ContactPatchCounter++
        if (-not $Key) { $Key = "patch-case-$($script:ContactPatchCounter)" }
        if ($null -eq $Version) { $Version = [long](Read-PatchRow).Version }
        return Invoke-Api -Method PATCH -Path "/contacts/$script:PatchContactId" -Token $script:Token `
            -WorkspaceId $script:WorkspaceId -IdempotencyKey $Key -IfMatch ('"{0}"' -f $Version) -Body $Body
    }
    $before = Read-PatchRow
    $beforeProfile = $before.Profile | ConvertFrom-Json
    $notes = Send-ContactPatch '{"notes":"updated"}'
    Add-Result 'notes_only HTTP' '200' ([string]$notes.Status)
    $after = Read-PatchRow
    Add-Result 'fullName_omitted preserves authoritative name' $before.FullName $after.FullName
    Add-Result 'owner_omitted preserves authoritative owner' $before.OwnerId $after.OwnerId
    Add-Result 'nonempty PATCH increments version exactly once' ([string]([long]$before.Version + 1)) ([string]$after.Version)
    $afterProfile = $after.Profile | ConvertFrom-Json
    Add-Result 'notes-only applies supplied value' 'updated' $afterProfile.notes
    foreach ($property in $beforeProfile.PSObject.Properties) {
        if ($property.Name -eq 'notes') { continue }
        Add-Result "notes-only preserves omitted $($property.Name)" `
            ($property.Value | ConvertTo-Json -Compress -Depth 20) `
            ($afterProfile.($property.Name) | ConvertTo-Json -Compress -Depth 20)
    }
    Add-Result 'existing tags trim/distinct/empty normalization preserved' 'Core,VIP' (@($afterProfile.tags) -join ',')
    Add-Result 'one CONTACT_UPDATED outbox event per successful patch' '1' ([string](Get-Scalar -Database $DatabaseName `
        -Query "SELECT COUNT(*) FROM contacts.OutboxMessages WHERE AggregateId='$id' AND EventType='CONTACT_UPDATED'"))
    $profileBeforeName = $after.Profile
    $name = Send-ContactPatch '{"fullName":"Updated Name"}'
    Add-Result 'fullName_value HTTP' '200' ([string]$name.Status)
    Add-Result 'fullName_value changes only name' 'Updated Name' (Read-PatchRow).FullName
    Add-Result 'fullName_value preserves entire profile' $profileBeforeName (Read-PatchRow).Profile
    foreach ($case in @(@('fullName_null','{"fullName":null}'), @('fullName_blank','{"fullName":"   "}'),
                         @('empty_patch','{}'), @('unknown_field','{"unknownProperty":"x"}'),
                         @('mixed_unknown','{"notes":"x","unknownProperty":"x"}'))) {
        $state = Read-PatchState
        $invalid = Send-ContactPatch $case[1]
        $status = if ($case[0] -match 'unknown') { '400' } else { '422' }
        Add-Result "$($case[0]) HTTP rejection" $status ([string]$invalid.Status)
        Add-Result "$($case[0]) error code" 'VALIDATION_FAILED' $invalid.Body.code
        Add-Result "$($case[0]) preserves entire persisted state" $state (Read-PatchState)
    }
    $clear = Send-ContactPatch '{"notes":null}'
    Add-Result 'optional_null HTTP' '200' ([string]$clear.Status)
    Add-Result 'optional_null clears notes' 'True' ([string]($null -eq ((Read-PatchRow).Profile | ConvertFrom-Json).notes))
    # Omitted/explicit-null owner must not look up the now-inactive existing member.
    Invoke-SqlNonQuery -Database $DatabaseName -Query "UPDATE workspace.Memberships SET Status='Suspended' WHERE WorkspaceId='$($script:WorkspaceId)' AND MemberId='mem-contacts-read-other'"
    Invoke-SqlNonQuery -Database $DatabaseName -Query "UPDATE contacts.Contacts SET OwnerId='mem-contacts-read-other' WHERE ContactId='$id'"
    $omittedOwner = Send-ContactPatch '{"notes":"owner omitted"}'
    Add-Result 'owner omitted skips inactive-member validation HTTP' '200' ([string]$omittedOwner.Status)
    Add-Result 'owner omitted preserves inactive owner reference' 'mem-contacts-read-other' (Read-PatchRow).OwnerId
    $clearOwner = Send-ContactPatch '{"ownerId":null}'
    Add-Result 'owner_null HTTP' '200' ([string]$clearOwner.Status)
    Add-Result 'owner_null authoritative unassign' 'True' ([string]((Read-PatchRow).OwnerId -is [DBNull]))
    $badOwner = Send-ContactPatch '{"ownerId":"mem-contacts-read-other"}'
    Add-Result 'present inactive owner still rejected' '422' ([string]$badOwner.Status)
    Invoke-SqlNonQuery -Database $DatabaseName -Query "UPDATE workspace.Memberships SET Status='Active' WHERE WorkspaceId='$($script:WorkspaceId)' AND MemberId='mem-contacts-read-other'"
    $validOwner = Send-ContactPatch ('{"ownerId":"' + $callerMemberId + '"}')
    Add-Result 'present active owner HTTP' '200' ([string]$validOwner.Status)
    Add-Result 'present active owner assigned' $callerMemberId (Read-PatchRow).OwnerId
    # FullName read-only must not block a notes-only write; explicitly touched fields must deny.
    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
INSERT INTO access.RoleFieldSecurity (PolicyId,WorkspaceId,RoleId,ResourceKey,FieldKey,Access) VALUES
('field_patch_owner','$($script:WorkspaceId)','$roleId','contacts','ownerId','ReadOnly'),
('field_patch_name','$($script:WorkspaceId)','$roleId','contacts','fullName','ReadOnly');
"@
    Add-Result 'notes write ignores omitted read-only owner/name' '200' ([string](Send-ContactPatch '{"notes":"allowed"}').Status)
    foreach ($body in @('{"ownerId":null}', '{"fullName":"Denied"}')) {
        $state = Read-PatchState
        $denied = Send-ContactPatch $body
        Add-Result 'explicit supplied read-only field denied HTTP' '403' ([string]$denied.Status)
        Add-Result 'explicit supplied read-only field denial code' 'ACCESS_DENIED' $denied.Body.code
        Add-Result 'field denial cannot mutate state' $state (Read-PatchState)
    }
    Invoke-SqlNonQuery -Database $DatabaseName -Query "UPDATE access.RoleFieldSecurity SET Access='Hidden' WHERE PolicyId='field_patch_owner'"
    Add-Result 'notes write ignores omitted hidden owner' '200' ([string](Send-ContactPatch '{"notes":"hidden owner omitted"}').Status)
    Add-Result 'explicit hidden owner denied' '403' ([string](Send-ContactPatch '{"ownerId":null}').Status)
    Invoke-SqlNonQuery -Database $DatabaseName -Query "DELETE FROM access.RoleFieldSecurity WHERE PolicyId IN ('field_patch_owner','field_patch_name')"
    $version = [long](Read-PatchRow).Version
    $first = Send-ContactPatch '{"notes":"idem"}' 'patch-idem-value' $version
    $firstState = Read-PatchState
    $replay = Send-ContactPatch '{"notes":"idem"}' 'patch-idem-value' $version
    Add-Result 'same canonical patch safely replays HTTP' '200' ([string]$replay.Status)
    Add-Result 'same canonical patch replay outcome' 'REPLAYED' $replay.Body.outcome
    Add-Result 'replay cannot increment version or mutate state' $firstState (Read-PatchState)
    # Exact committed replay survives later field-policy changes; response still uses current projection.
    function Read-PatchCommitEvidence {
        return [string](Get-Scalar -Database $DatabaseName -Query "SELECT CONCAT(Version,'|',(SELECT COUNT(*) FROM contacts.AuditRecords WHERE AggregateId='$script:PatchContactId'),'|',(SELECT COUNT(*) FROM contacts.OutboxMessages WHERE AggregateId='$script:PatchContactId')) FROM contacts.Contacts WHERE ContactId='$script:PatchContactId'")
    }
    $committedEvidence = Read-PatchCommitEvidence
    foreach ($policy in @('ReadOnly','Hidden')) {
        Invoke-SqlNonQuery -Database $DatabaseName -Query @"
DELETE FROM access.RoleFieldSecurity WHERE PolicyId='field_patch_replay';
INSERT INTO access.RoleFieldSecurity (PolicyId,WorkspaceId,RoleId,ResourceKey,FieldKey,Access)
VALUES ('field_patch_replay','$($script:WorkspaceId)','$roleId','contacts','notes','$policy');
"@
        $policyReplay = Send-ContactPatch '{"notes":"idem"}' 'patch-idem-value' $version
        Add-Result "exact replay after $policy policy HTTP" '200' ([string]$policyReplay.Status)
        Add-Result "exact replay after $policy policy outcome" 'REPLAYED' $policyReplay.Body.outcome
        Add-Result "exact replay after $policy has no version/audit/outbox mutation" $committedEvidence (Read-PatchCommitEvidence)
        Add-Result "exact replay after $policy preserves persisted state" $firstState (Read-PatchState)
        if ($policy -eq 'Hidden') {
            Add-Result 'hidden replay response uses current field projection' 'False' ([string]($policyReplay.Body.result.contact.PSObject.Properties.Name -contains 'notes'))
        } else {
            Add-Result 'read-only replay response retains readable field' 'idem' $policyReplay.Body.result.contact.notes
        }
        $newRestricted = Send-ContactPatch '{"notes":"new restricted value"}'
        Add-Result "new $policy notes PATCH denied HTTP" '403' ([string]$newRestricted.Status)
        Add-Result "new $policy notes PATCH denial code" 'ACCESS_DENIED' $newRestricted.Body.code
        Add-Result "new $policy denial has no version/audit/outbox mutation" $committedEvidence (Read-PatchCommitEvidence)
        $policyMismatch = Send-ContactPatch '{"notes":null}' 'patch-idem-value' $version
        Add-Result "different fingerprint after $policy retains reuse conflict" '409' ([string]$policyMismatch.Status)
        Add-Result "different fingerprint after $policy reuse code" 'IDEMPOTENCY_KEY_REUSED' $policyMismatch.Body.code
    }
    Invoke-SqlNonQuery -Database $DatabaseName -Query "DELETE FROM access.RoleFieldSecurity WHERE PolicyId='field_patch_replay'"
    $reuse = Send-ContactPatch '{"notes":null}' 'patch-idem-value' $version
    Add-Result 'same key value vs null conflict HTTP' '409' ([string]$reuse.Status)
    Add-Result 'same key value vs null conflict code' 'IDEMPOTENCY_KEY_REUSED' $reuse.Body.code
    $version = [long](Read-PatchRow).Version
    [void](Send-ContactPatch '{"notes":null}' 'patch-idem-presence' $version)
    $differentFields = Send-ContactPatch '{"notes":null,"ownerId":null}' 'patch-idem-presence' $version
    Add-Result 'omission vs explicit null differs in canonical intent' '409' ([string]$differentFields.Status)
    Add-Result 'presence mismatch idempotency code' 'IDEMPOTENCY_KEY_REUSED' $differentFields.Body.code
    $state = Read-PatchState
    $stale = Send-ContactPatch '{"notes":"stale"}' 'patch-stale-version' 0
    Add-Result 'stale If-Match partial patch HTTP' '409' ([string]$stale.Status)
    Add-Result 'stale If-Match error code' 'RESOURCE_VERSION_CONFLICT' $stale.Body.code
    Add-Result 'stale If-Match cannot mutate state' $state (Read-PatchState)
    Add-Result 'tags explicit null HTTP' '200' ([string](Send-ContactPatch '{"tags":null}').Status)
    Add-Result 'tags explicit null clears' 'True' ([string]($null -eq ((Read-PatchRow).Profile | ConvertFrom-Json).tags))
    Add-Result 'displayName remains writable' '200' ([string](Send-ContactPatch '{"displayName":"New display"}').Status)
    Add-Result 'displayName supplied value persisted' 'New display' ((Read-PatchRow).Profile | ConvertFrom-Json).displayName
    # Every nullable public profile member supports explicit clearing, including displayName.
    foreach ($field in @('salutation','jobTitle','department','roleAtCompany','workEmail','personalEmail',
                         'mobilePhone','workPhone','otherPhone','zaloId','facebook','preferredContactChannel',
                         'address','source','decisionRole','relationshipLevel','painPoint','needSummary','notes','tags','displayName')) {
        $beforeClear = Read-PatchRow
        $cleared = Send-ContactPatch ('{"' + $field + '":null}')
        Add-Result "$field explicit null HTTP" '200' ([string]$cleared.Status)
        $afterClear = Read-PatchRow
        Add-Result "$field explicit null persisted" 'True' ([string]($null -eq ($afterClear.Profile | ConvertFrom-Json).$field))
        Add-Result "$field clear preserves fullName" $beforeClear.FullName $afterClear.FullName
        Add-Result "$field clear preserves owner" $beforeClear.OwnerId $afterClear.OwnerId
    }
    $invalidTagBody = @{ tags = @((1..101 | ForEach-Object { "tag$_" })) } | ConvertTo-Json -Compress
    foreach ($invalidBody in @('{"ownerId":"   "}', '{"workEmail":"invalid"}', '{"relationshipLevel":"invalid"}', $invalidTagBody,
                              ('{"tags":["' + ('x' * 101) + '"]}'))) {
        $state = Read-PatchState
        $invalidValue = Send-ContactPatch $invalidBody
        Add-Result 'existing PATCH value constraints reject invalid input' '422' ([string]$invalidValue.Status)
        Add-Result 'legacy supplied-value validation code preserved' 'VALIDATION_FAILED' $invalidValue.Body.code
        Add-Result 'invalid supplied profile creates no mutation' $state (Read-PatchState)
    }
    # Configured case-insensitive ASP.NET naming must touch the canonical field key.
    Add-Result 'configured case-insensitive naming HTTP' '200' ([string](Send-ContactPatch '{"NOTES":"case accepted"}').Status)
    Add-Result 'configured naming records actual touched value' 'case accepted' ((Read-PatchRow).Profile | ConvertFrom-Json).notes
    $events = [long](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM contacts.OutboxMessages WHERE AggregateId='$id' AND EventType='CONTACT_UPDATED'")
    Add-Result 'successful version increments match exact update-event count' ([string](Read-PatchRow).Version) ([string]$events)
    Add-Result 'one update audit per committed version' ([string]$events) ([string](Get-Scalar -Database $DatabaseName `
        -Query "SELECT COUNT(*) FROM contacts.AuditRecords WHERE AggregateId='$id' AND Operation='updateContact'"))
}
