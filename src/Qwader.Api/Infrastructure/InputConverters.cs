using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Qwader.Api.Infrastructure;

/// <summary>
/// Every text that comes in loses the characters a person cannot see: control characters (NUL…), zero-width characters and
/// direction overrides (a name that reads backwards or looks empty). Line breaks and tabs stay (notes, addresses).
/// </summary>
public sealed class CleanStringConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.Null ? null : Clean(reader.GetString());

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue(value);

    internal static string? Clean(string? value)
    {
        if (string.IsNullOrEmpty(value) || !value.Any(IsHidden)) return value;
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
            if (!IsHidden(c)) sb.Append(c);
        return sb.ToString();
    }

    private static bool IsHidden(char c) => c switch
    {
        '\n' or '\r' or '\t' => false,
        < ' ' or '\u007F' => true,                       // control characters, NUL
        >= '​' and <= '‏' => true,             // zero-width space/joiners, LRM, RLM
        >= '‪' and <= '‮' => true,             // direction embeddings and overrides
        >= '⁦' and <= '⁩' => true,             // direction isolates
        '﻿' => true,                                // zero-width no-break space
        _ => false,
    };
}

/// <summary>
/// Numbers beyond any real price, quantity or amount (more than a billion) are refused as invalid input, instead of
/// overflowing in the database and answering «unexpected error».
/// </summary>
public sealed class BoundedDecimalConverter : JsonConverter<decimal>
{
    public const decimal Max = 1_000_000_000m;

    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.Number || !reader.TryGetDecimal(out var value) || Math.Abs(value) > Max)
            throw new JsonException("number out of range");
        return value;
    }

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
}
