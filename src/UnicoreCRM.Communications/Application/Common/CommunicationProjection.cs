using System.Globalization;
using UnicoreCRM.Communications.Contracts;
using UnicoreCRM.Communications.Domain;

namespace UnicoreCRM.Communications.Application.Common;

internal static class CommunicationProjection
{
    internal static SenderProfileDocument SenderProfile(SenderProfile sender) =>
        new(
            sender.SenderProfileId,
            sender.WorkspaceId,
            sender.DisplayName,
            sender.EmailAddress,
            sender.Type.ToString(),
            sender.IsDefault,
            sender.IsEnabled,
            sender.Version,
            Format(sender.CreatedAt),
            Format(sender.UpdatedAt))
        {
            OwnerMembershipId = sender.OwnerMembershipId,
            EmailConnectionId = sender.EmailConnectionId,
            ReplyToAddress = sender.ReplyToAddress
        };

    internal static EmailConnectionDocument EmailConnection(EmailConnection connection) =>
        new(
            connection.EmailConnectionId,
            connection.WorkspaceId,
            connection.Provider.ToString(),
            connection.EmailAddress,
            connection.Status.ToString(),
            Format(connection.ConnectedAt),
            Format(connection.UpdatedAt),
            connection.Version)
        {
            OwnerMembershipId = connection.OwnerMembershipId,
            ProviderAccountId = connection.ProviderAccountId,
            RevokedAt = connection.RevokedAt is null ? null : Format(connection.RevokedAt.Value)
        };

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
