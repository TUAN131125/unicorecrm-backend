using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Platform.AccessControl.Application.EvaluateEffectiveRecordAccess;

internal sealed record Query(
    EvaluateEffectiveRecordAccessRequest Request,
    string RequestId,
    string CorrelationId);
