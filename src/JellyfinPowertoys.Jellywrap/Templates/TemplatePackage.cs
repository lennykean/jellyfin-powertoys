using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal sealed class TemplatePackage(Func<string, byte[]?> readFile)
{
    private const string TemplateFile = "template.json";

    private static readonly string[] _fileFolders = ["fonts/", "img/"];

    private static readonly JsonDocumentOptions _jsonOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        AllowDuplicateProperties = false,
    };
    public JsonObject? Json { get; private set; }
    public string? Id { get; private set; }

    public List<string> Errors { get; } = [];
    public byte[]? ReadFile(string path)
    {
        return readFile(path);
    }

    public void Load()
    {
        try
        {
            var bytes = readFile(TemplateFile) ?? throw new InvalidDataException("The package has no template.json.");
            var preamble = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble) ? Encoding.UTF8.Preamble.Length : 0;
            var json = JsonNode.Parse(bytes.AsSpan(preamble), documentOptions: _jsonOptions) as JsonObject
                ?? throw new InvalidDataException("template.json is not an object.");
            Id = PropertyReader.IsString(json["id"], out var id) ? id : throw new InvalidDataException("template.json has no \"id\".");
            Qualify(json);
            Json = json;
        }
        catch (Exception exception)
        {
            Errors.Add(exception.Message);
            Id = null;
        }
    }

    private void Qualify(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject parent:
                foreach (var (key, value) in parent.ToList())
                {
                    if (value is JsonValue reference && Qualified(reference) is { } qualified)
                    {
                        parent[key] = qualified;
                    }
                    else
                    {
                        Qualify(value);
                    }
                }

                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    if (array[i] is JsonValue reference && Qualified(reference) is { } qualified)
                    {
                        array[i] = qualified;
                    }
                    else
                    {
                        Qualify(array[i]);
                    }
                }

                break;
        }
    }

    private string? Qualified(JsonValue value)
    {
        var isOwnFile = value.GetValueKind() == JsonValueKind.String
            && value.GetValue<string>() is var text
            && _fileFolders.Any(folder => text.StartsWith(folder, StringComparison.Ordinal));
        return isOwnFile ? $"{Template.PackagePrefix}{Id}/{value.GetValue<string>()}" : null;
    }
}
