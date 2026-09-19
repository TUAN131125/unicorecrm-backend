namespace UnicoreCRM.Crm.Organizations.Contracts;

public enum OrganizationSummaryReadStatus { Succeeded, AccessDenied, WorkspaceMismatch, InvalidReference, NotFound }
public sealed record OrganizationSummaryProjection(string OrganizationId, string? DisplayName, string? Status, string? Industry, int? EmployeeCount, string? RelationshipLevel, long Version);
public sealed record OrganizationSummaryReadResult(OrganizationSummaryReadStatus Status, OrganizationSummaryProjection? Summary = null);
public interface IOrganizationSummaryReader
{
    Task<OrganizationSummaryReadResult> ReadAsync(string organizationId, string requestId, string correlationId, CancellationToken cancellationToken);
}
