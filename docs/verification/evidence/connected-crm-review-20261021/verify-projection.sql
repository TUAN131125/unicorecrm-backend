-- Read-only evidence. This is a retained isolated verification database, never production.
USE [UnicoreCRM_ActivityTest_87a69f3116574b61ab762886beffcab3];
SET NOCOUNT ON;
SELECT CustomerId, RelationshipType, RelationshipId
FROM customers.Customers
WHERE CustomerId IN ('customer_048778ba18b2488793fd9b29e3604988','customer_29117ab819ff45108d2e2df43c202365');
SELECT RelationshipType, RelationshipId, COUNT(*) AS SubjectCount
FROM customers.Customers
WHERE RelationshipId IN ('contact_afa83a94cf0e465fb8a5fbc61dcb3b2d','organization_640e0bcee659433dae8daca8434a712e')
GROUP BY RelationshipType, RelationshipId;
SELECT COUNT(*) AS StakeholderMembershipCount FROM contacts.CustomerRelationships
WHERE ContactId='contact_afa83a94cf0e465fb8a5fbc61dcb3b2d';
SELECT COUNT(*) AS EndedOrganizationAffiliationCount FROM contacts.OrganizationRelationships
WHERE ContactId='contact_afa83a94cf0e465fb8a5fbc61dcb3b2d' AND EffectiveTo IS NOT NULL;
SELECT COUNT(*) AS PersistedManualActivityCount FROM tasks.Activities
WHERE ActivityId='activity_31ae4419da214497a2f5f669cf44dde7';
