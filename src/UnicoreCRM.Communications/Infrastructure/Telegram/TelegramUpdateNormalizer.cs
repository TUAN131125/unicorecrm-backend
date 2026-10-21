using System.Globalization;
using System.Text;
using System.Text.Json;

namespace UnicoreCRM.Communications.Infrastructure.Telegram;

internal sealed record TelegramInbound(long UpdateId, string Classification, long ChatId = 0,
    long UserId = 0, long MessageId = 0, string? Text = null, DateTimeOffset At = default);

internal static class TelegramUpdateNormalizer
{
    internal static long Id(JsonElement value, string key)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(key, out var property)
            || property.ValueKind != JsonValueKind.Number || !property.TryGetInt64(out var id) || id <= 0 || id > 4_503_599_627_370_495)
            throw new TelegramFailure("RESPONSE_INVALID");
        return id;
    }

    internal static TelegramInbound Normalize(JsonElement update)
    {
        var id = Id(update, "update_id");
        if (!update.TryGetProperty("message", out var message)) return new(id, "UNSUPPORTED_UPDATE");
        if (update.EnumerateObject().Count() != 2 || message.ValueKind != JsonValueKind.Object) throw new TelegramFailure("RESPONSE_INVALID");
        if (!message.TryGetProperty("chat", out var chat) || chat.ValueKind != JsonValueKind.Object
            || !chat.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String) throw new TelegramFailure("RESPONSE_INVALID");
        if (type.GetString() != "private") return new(id, "UNSUPPORTED_CHAT");
        // Explicit allowlist prevents new Telegram media/service fields being interpreted as customer text.
        var permitted = new HashSet<string>(StringComparer.Ordinal) { "message_id", "date", "chat", "from", "text", "entities" };
        if (message.EnumerateObject().Any(p => !permitted.Contains(p.Name))) return new(id, "UNSUPPORTED_CONTENT");
        if (!message.TryGetProperty("text", out var text)) return new(id, "UNSUPPORTED_CONTENT");
        if (text.ValueKind != JsonValueKind.String) throw new TelegramFailure("RESPONSE_INVALID");
        var body = text.GetString()!;
        if (string.IsNullOrWhiteSpace(body) || body.Length > 10000 || !ValidUnicode(body)) return new(id, "UNSUPPORTED_TEXT");
        if (body.StartsWith("/start", StringComparison.Ordinal) && (body.Length == 6 || char.IsWhiteSpace(body[6]) || body[6] == '@'))
            return new(id, "CONTROL_START");
        if (!message.TryGetProperty("from", out var from) || from.ValueKind != JsonValueKind.Object
            || !from.TryGetProperty("is_bot", out var bot) || bot.ValueKind is not (JsonValueKind.False or JsonValueKind.True))
            throw new TelegramFailure("RESPONSE_INVALID");
        if (bot.GetBoolean()) return new(id, "UNSUPPORTED_SENDER");
        var chatId = Id(chat, "id");
        var userId = Id(from, "id");
        if (chatId != userId) throw new TelegramFailure("RESPONSE_INVALID");
        var messageId = Id(message, "message_id");
        var date = Id(message, "date");
        DateTimeOffset at;
        try { at = DateTimeOffset.FromUnixTimeSeconds(date); }
        catch (ArgumentOutOfRangeException) { throw new TelegramFailure("RESPONSE_INVALID"); }
        return new(id, "TEXT", chatId, userId, messageId, body, at);
    }

    private static bool ValidUnicode(string text)
    {
        for (var i = 0; i < text.Length; i++)
            if (char.IsSurrogate(text[i]) && (!char.IsHighSurrogate(text[i]) || ++i >= text.Length || !char.IsLowSurrogate(text[i]))) return false;
        return true;
    }
}
