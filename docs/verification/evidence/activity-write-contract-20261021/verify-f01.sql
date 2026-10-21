-- Read-only; isolated DB UnicoreCRM_ActivityTest_3dd6e8153b3d40e98e7851aba2de7237 only.
SET NOCOUNT ON;
SELECT r.IdempotencyKey,a.Type,a.RecordModuleKey,a.RecordId,a.RelationshipType,a.RelationshipId,a.SourceType
FROM tasks.IdempotencyRecords r JOIN tasks.Activities a
ON a.ActivityId=JSON_VALUE(r.ResponseJson,'$.aggregateId') AND a.WorkspaceId=r.WorkspaceId
WHERE r.Operation='logActivity' AND r.IdempotencyKey LIKE 'f01-%'
ORDER BY r.IdempotencyKey;
SELECT COUNT(*) AS RejectedRequestReceipts FROM tasks.IdempotencyRecords
WHERE Operation='logActivity' AND (IdempotencyKey LIKE 'f01-invalid-%'
OR IdempotencyKey IN ('f01-unanchored','f01-missing','f01-foreign','f01-conflict','f01-forged'));
