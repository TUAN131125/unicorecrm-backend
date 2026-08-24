namespace UnicoreCRM.Operations.Tasks.Domain;

internal sealed record TaskReferenceData(
    string? RelationshipType,
    string? RelationshipId,
    string? RecordModuleKey,
    string? RecordId,
    string? RecordLabel,
    string? SourceType,
    string? SourceId,
    string? SourceEvidence);
