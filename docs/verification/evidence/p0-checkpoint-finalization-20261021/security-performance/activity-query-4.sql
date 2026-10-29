(@activityWorkspace nvarchar(128),@owned0 bit,@owner0 nvarchar(128),@queue0 bit,@restrict0 bit,@module0 nvarchar(100),@owned1 bit,@owner1 nvarchar(128),@queue1 bit,@restrict1 bit,@module1 nvarchar(100),@owned2 bit,@owner2 nvarchar(128),@queue2 bit,@restrict2 bit,@module2 nvarchar(100),@owned3 bit,@owner3 nvarchar(128),@queue3 bit,@restrict3 bit,@module3 nvarchar(100),@specification_Search_contains nvarchar(300),@specification_Search_contains0 nvarchar(4000))SELECT COUNT(*)
FROM (
    SELECT a.* FROM tasks.Activities a
    WHERE a.WorkspaceId=@activityWorkspace AND a.Type<>5
      AND a.SourceEvidence IS NULL
      AND ((a.SourceType IS NULL AND a.SourceId IS NULL) OR
           (a.SourceId=COALESCE(a.RecordId,a.RelationshipId) AND
            ((COALESCE(a.RecordModuleKey, CASE a.RelationshipType WHEN N'CONTACT' THEN N'contacts' WHEN N'ORGANIZATION_ACCOUNT' THEN N'organizations' END)=N'leads' AND a.SourceType IN (N'LEAD_ACTIVITY',N'LEAD_DETAIL')) OR
             (COALESCE(a.RecordModuleKey, CASE a.RelationshipType WHEN N'CONTACT' THEN N'contacts' WHEN N'ORGANIZATION_ACCOUNT' THEN N'organizations' END)=N'contacts' AND a.SourceType IN (N'CONTACT_ACTIVITY',N'CONTACT_DETAIL')) OR
             (COALESCE(a.RecordModuleKey, CASE a.RelationshipType WHEN N'CONTACT' THEN N'contacts' WHEN N'ORGANIZATION_ACCOUNT' THEN N'organizations' END)=N'organizations' AND a.SourceType IN (N'ORGANIZATION_ACTIVITY',N'ORGANIZATION_DETAIL')) OR
             (COALESCE(a.RecordModuleKey, CASE a.RelationshipType WHEN N'CONTACT' THEN N'contacts' WHEN N'ORGANIZATION_ACCOUNT' THEN N'organizations' END)=N'customers' AND a.SourceType IN (N'CUSTOMER_ACTIVITY',N'CUSTOMER_DETAIL',N'CUSTOMER_360')))))
      AND EXISTS (SELECT 1 FROM (SELECT @module0 AS ModuleKey, f.RecordId, f.RelationshipType, f.RelationshipId
    FROM (SELECT WorkspaceId, LeadId AS RecordId, ScopeOwnerId AS OwnerId, CASE WHEN ArchivedAt IS NULL THEN 1 ELSE 0 END AS CanLog, RelationshipType, RelationshipId FROM leads.Leads) f
    WHERE f.WorkspaceId=@activityWorkspace
      AND (@owned0=0 OR f.OwnerId=@owner0 OR (@queue0=1 AND f.OwnerId IS NULL))
      AND (@restrict0=0 OR @queue0=1 OR f.OwnerId IS NOT NULL)
      
    UNION ALL
    SELECT @module1 AS ModuleKey, f.RecordId, f.RelationshipType, f.RelationshipId
    FROM (SELECT WorkspaceId, CustomerId AS RecordId, OwnerId, CASE WHEN Status IN (N'NEW',N'ACTIVE',N'INACTIVE') THEN 1 ELSE 0 END AS CanLog, RelationshipType, RelationshipId FROM customers.Customers) f
    WHERE f.WorkspaceId=@activityWorkspace
      AND (@owned1=0 OR f.OwnerId=@owner1 OR (@queue1=1 AND f.OwnerId IS NULL))
      AND (@restrict1=0 OR @queue1=1 OR f.OwnerId IS NOT NULL)
      
    UNION ALL
    SELECT @module2 AS ModuleKey, f.RecordId, f.RelationshipType, f.RelationshipId
    FROM (SELECT WorkspaceId, OrganizationId AS RecordId, OwnerId, CASE WHEN Status<>N'archived' THEN 1 ELSE 0 END AS CanLog, N'ORGANIZATION_ACCOUNT' AS RelationshipType, OrganizationId AS RelationshipId FROM organizations.Organizations) f
    WHERE f.WorkspaceId=@activityWorkspace
      AND (@owned2=0 OR f.OwnerId=@owner2 OR (@queue2=1 AND f.OwnerId IS NULL))
      AND (@restrict2=0 OR @queue2=1 OR f.OwnerId IS NOT NULL)
      
    UNION ALL
    SELECT @module3 AS ModuleKey, f.RecordId, f.RelationshipType, f.RelationshipId
    FROM (SELECT c.WorkspaceId, c.ContactId AS RecordId, c.OwnerId, CASE WHEN c.ArchivedAt IS NULL AND c.Status<>N'archived' THEN 1 ELSE 0 END AS CanLog,
        N'CONTACT' AS RelationshipType, c.ContactId AS RelationshipId
    FROM contacts.Contacts c
    UNION ALL
    SELECT c.WorkspaceId, c.ContactId AS RecordId, c.OwnerId, CASE WHEN c.ArchivedAt IS NULL AND c.Status<>N'archived' AND r.EffectiveTo IS NULL THEN 1 ELSE 0 END AS CanLog,
        N'ORGANIZATION_ACCOUNT' AS RelationshipType, r.OrganizationId AS RelationshipId
    FROM contacts.Contacts c INNER JOIN contacts.OrganizationRelationships r
        ON r.WorkspaceId=c.WorkspaceId AND r.ContactId=c.ContactId) f
    WHERE f.WorkspaceId=@activityWorkspace
      AND (@owned3=0 OR f.OwnerId=@owner3 OR (@queue3=1 AND f.OwnerId IS NULL))
      AND (@restrict3=0 OR @queue3=1 OR f.OwnerId IS NOT NULL)
      ) p
                  WHERE p.ModuleKey=COALESCE(a.RecordModuleKey, CASE a.RelationshipType WHEN N'CONTACT' THEN N'contacts' WHEN N'ORGANIZATION_ACCOUNT' THEN N'organizations' END) AND p.RecordId=COALESCE(a.RecordId,a.RelationshipId)
                    AND (a.RelationshipType IS NULL OR
                         (p.RelationshipType=a.RelationshipType AND p.RelationshipId=a.RelationshipId)))
      AND (a.RelationshipType IS NULL OR
           EXISTS (SELECT 1 FROM (SELECT @module0 AS ModuleKey, f.RecordId, f.RelationshipType, f.RelationshipId
    FROM (SELECT WorkspaceId, LeadId AS RecordId, ScopeOwnerId AS OwnerId, CASE WHEN ArchivedAt IS NULL THEN 1 ELSE 0 END AS CanLog, RelationshipType, RelationshipId FROM leads.Leads) f
    WHERE f.WorkspaceId=@activityWorkspace
      AND (@owned0=0 OR f.OwnerId=@owner0 OR (@queue0=1 AND f.OwnerId IS NULL))
      AND (@restrict0=0 OR @queue0=1 OR f.OwnerId IS NOT NULL)
      
    UNION ALL
    SELECT @module1 AS ModuleKey, f.RecordId, f.RelationshipType, f.RelationshipId
    FROM (SELECT WorkspaceId, CustomerId AS RecordId, OwnerId, CASE WHEN Status IN (N'NEW',N'ACTIVE',N'INACTIVE') THEN 1 ELSE 0 END AS CanLog, RelationshipType, RelationshipId FROM customers.Customers) f
    WHERE f.WorkspaceId=@activityWorkspace
      AND (@owned1=0 OR f.OwnerId=@owner1 OR (@queue1=1 AND f.OwnerId IS NULL))
      AND (@restrict1=0 OR @queue1=1 OR f.OwnerId IS NOT NULL)
      
    UNION ALL
    SELECT @module2 AS ModuleKey, f.RecordId, f.RelationshipType, f.RelationshipId
    FROM (SELECT WorkspaceId, OrganizationId AS RecordId, OwnerId, CASE WHEN Status<>N'archived' THEN 1 ELSE 0 END AS CanLog, N'ORGANIZATION_ACCOUNT' AS RelationshipType, OrganizationId AS RelationshipId FROM organizations.Organizations) f
    WHERE f.WorkspaceId=@activityWorkspace
      AND (@owned2=0 OR f.OwnerId=@owner2 OR (@queue2=1 AND f.OwnerId IS NULL))
      AND (@restrict2=0 OR @queue2=1 OR f.OwnerId IS NOT NULL)
      
    UNION ALL
    SELECT @module3 AS ModuleKey, f.RecordId, f.RelationshipType, f.RelationshipId
    FROM (SELECT c.WorkspaceId, c.ContactId AS RecordId, c.OwnerId, CASE WHEN c.ArchivedAt IS NULL AND c.Status<>N'archived' THEN 1 ELSE 0 END AS CanLog,
        N'CONTACT' AS RelationshipType, c.ContactId AS RelationshipId
    FROM contacts.Contacts c
    UNION ALL
    SELECT c.WorkspaceId, c.ContactId AS RecordId, c.OwnerId, CASE WHEN c.ArchivedAt IS NULL AND c.Status<>N'archived' AND r.EffectiveTo IS NULL THEN 1 ELSE 0 END AS CanLog,
        N'ORGANIZATION_ACCOUNT' AS RelationshipType, r.OrganizationId AS RelationshipId
    FROM contacts.Contacts c INNER JOIN contacts.OrganizationRelationships r
        ON r.WorkspaceId=c.WorkspaceId AND r.ContactId=c.ContactId) f
    WHERE f.WorkspaceId=@activityWorkspace
      AND (@owned3=0 OR f.OwnerId=@owner3 OR (@queue3=1 AND f.OwnerId IS NULL))
      AND (@restrict3=0 OR @queue3=1 OR f.OwnerId IS NOT NULL)
      ) r
                   WHERE r.RecordId=a.RelationshipId AND
                    ((a.RelationshipType=N'CONTACT' AND r.ModuleKey=N'contacts') OR
                     (a.RelationshipType=N'ORGANIZATION_ACCOUNT' AND r.ModuleKey=N'organizations'))))
) AS [u]
WHERE [u].[Subject] LIKE @specification_Search_contains ESCAPE N'\' OR ([u].[Body] IS NOT NULL AND [u].[Body] LIKE @specification_Search_contains0 ESCAPE N'\')
