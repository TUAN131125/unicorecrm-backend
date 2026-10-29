-- Read-only; run only against the isolated UI test database identified in ui-only/run.json.
SET NOCOUNT ON;
SELECT LeadId, ScopeOwnerId, CustomerRef,
       COALESCE(JSON_VALUE(Profile,'$.Source'),JSON_VALUE(Profile,'$.source'),'') AS Source
FROM leads.Leads WHERE LeadId='lead_4c5ffe3b774644be8acc93aa94e2e602';
SELECT COUNT(*) AS UniqueContactSubjectCustomer FROM customers.Customers
WHERE RelationshipType='CONTACT' AND RelationshipId='contact_57ba85b4b55648829d0a71a4b384bfdb';
SELECT CustomerId,RelationshipType,RelationshipId FROM customers.Customers
WHERE CustomerId='customer_10a4bb9a68364f018e1ae76943981753';
SELECT COUNT(*) AS ActiveAffiliation FROM contacts.OrganizationRelationships
WHERE ContactId='contact_57ba85b4b55648829d0a71a4b384bfdb'
AND OrganizationId='organization_1f8c406b88e842d58b22a6148f1127bc' AND EffectiveTo IS NULL;
SELECT COUNT(*) AS PersistedActivity FROM tasks.Activities
WHERE ActivityId='activity_750681238f0b419ca878b50218470de3';
