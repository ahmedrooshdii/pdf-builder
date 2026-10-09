using System.Text.Json;
using Scriban.Runtime;

namespace PdfBuilder.Infrastructure.Templates;

/// <summary>
/// Converts a JSON document into native Scriban objects, so templates see plain dictionaries and
/// arrays. Nothing is bound by reflection, which keeps CLR types out of reach of template authors.
/// </summary>
internal static class JsonToScriban
{
    private static readonly JsonDocumentOptions DocumentOptions = new() { MaxDepth = 64 };

    /// <exception cref="JsonException">The payload is not valid JSON, or its root is not an object.</exception>
    public static ScriptObject Parse(string json)
    {
        using var document = JsonDocument.Parse(json, DocumentOptions);

        return document.RootElement.ValueKind == JsonValueKind.Object
            ? ConvertObject(document.RootElement)
            : throw new JsonException("Root element must be a JSON object.");
    }

    private static object? Convert(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => ConvertObject(element),
        JsonValueKind.Array => ConvertArray(element),
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => ConvertNumber(element),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null
    };

    private static ScriptObject ConvertObject(JsonElement element)
    {
        var result = new ScriptObject();
        foreach (var property in element.EnumerateObject())
            result[property.Name] = Convert(property.Value);

        return result;
    }

    private static ScriptArray ConvertArray(JsonElement element)
    {
        var result = new ScriptArray();
        foreach (var item in element.EnumerateArray())
            result.Add(Convert(item));

        return result;
    }

    /// <summary>
    /// long, then decimal, then double. Keeps money exact and keeps large integers from
    /// rendering in scientific notation.
    /// </summary>
    private static object ConvertNumber(JsonElement element)
    {
        if (element.TryGetInt64(out var integer)) return integer;
        if (element.TryGetDecimal(out var exact)) return exact;

        return element.GetDouble();
    }
}
