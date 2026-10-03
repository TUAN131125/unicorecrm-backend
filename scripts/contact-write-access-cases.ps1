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

    foreach ($case in @(@('contacts.update', 'canUpdate'), @('contacts.delete', 'canDelete'))) {
        Invoke-SqlNonQuery -Database $DatabaseName -Query "DELETE FROM access.RoleCapabilities WHERE RoleId='$roleId' AND Capability='$($case[0])'"
        $denied = Get-ContactAccess $contactA
        Add-Result "missing $($case[0]) denies $($case[1]) even for Owner" 'False' ([string]$denied.($case[1]))
        Add-Result "missing $($case[0]) preserves readable record" 'True' ([string]$denied.canRead)
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
