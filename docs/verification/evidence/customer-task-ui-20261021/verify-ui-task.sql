-- Read-only. Use only isolated DB UnicoreCRM_ActivityTest_f812bf23acdc4e628a6171db82bc67f6.
SET NOCOUNT ON;
SELECT TaskId, WorkspaceId, RecordModuleKey, RecordId, RelationshipType, SourceType, SourceId,
       Status, Priority, AssigneeId, DueAt, CompletedAt, Version
FROM tasks.Tasks WHERE TaskId='task_800b700b25b34aeea5f9382885cec4bf';
SELECT COUNT(*) AS UniqueCustomerTask FROM tasks.Tasks
WHERE Title='ui-crm-1792579864592 Task' AND RecordModuleKey='customers'
AND RecordId='customer_5ef537a105ad429db0acb2eeac0acd08';
SELECT COUNT(*) AS ActiveAssignee FROM tasks.Tasks t JOIN workspace.Memberships m
ON m.WorkspaceId=t.WorkspaceId AND m.MemberId=t.AssigneeId AND m.Status='Active'
WHERE t.TaskId='task_800b700b25b34aeea5f9382885cec4bf';
SELECT CustomerRef, COALESCE(JSON_VALUE(Profile,'$.Source'),JSON_VALUE(Profile,'$.source'),'') AS Source
FROM leads.Leads WHERE LeadId='lead_d8cfb6f891db4cf39dcad874895169a2';
SELECT COUNT(*) AS UniqueContactCustomer FROM customers.Customers
WHERE RelationshipType='CONTACT' AND RelationshipId='contact_7203fa73a7c8450cab6961a65c2b1b9d';
SELECT COUNT(*) AS UiActivity FROM tasks.Activities
WHERE ActivityId='activity_2aa0d8d50f9141ec82004faa8d92fc36';
