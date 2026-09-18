namespace UnicoreCRM.Communications.Domain;

internal enum SenderProfileType
{
    Company = 1,
    MemberCompany = 2,
    MemberPersonal = 3
}

internal enum EmailProviderKind
{
    Gmail = 1
}

internal enum EmailConnectionStatus
{
    Connected = 1,
    Revoked = 2
}

internal enum EmailMessageStatus
{
    Draft = 1,
    Sending = 2,
    Sent = 3,
    Failed = 4
}

internal enum EmailRecipientType
{
    To = 1,
    Cc = 2,
    Bcc = 3
}
