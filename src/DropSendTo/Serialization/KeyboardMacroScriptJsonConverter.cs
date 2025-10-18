using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DropSendTo.Serialization;

/// <summary>
/// JsonConverter that obfuscates keyboard macro scripts when persisted to disk.
/// </summary>
public sealed class KeyboardMacroScriptJsonConverter : JsonConverter<string?>
{
    private const string Prefix = "!obf!";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DropSendTo.KeyboardMacro.v1");

    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        var value = reader.GetString();
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        if (!value.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return value;
        }

        var payload = value.AsSpan(Prefix.Length);
        try
        {
            var protectedBytes = Convert.FromBase64String(payload.ToString());
            var unprotected = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(unprotected);
        }
        catch
        {
            // Return empty when payload is invalid so we do not surface raw secrets.
            return string.Empty;
        }
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (string.IsNullOrEmpty(value))
        {
            writer.WriteStringValue(value);
            return;
        }

        var plainBytes = Encoding.UTF8.GetBytes(value);
        var protectedBytes = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
        writer.WriteStringValue($"{Prefix}{Convert.ToBase64String(protectedBytes)}");
    }
}
