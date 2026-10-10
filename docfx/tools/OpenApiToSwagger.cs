#:package Microsoft.OpenApi@3.10.0

// Converts the OpenAPI 3.x document served by WebDevLoop into the Swagger 2.0 file that DocFX can render.
// Usage: dotnet run docfx/tools/OpenApiToSwagger.cs <input.json> <output.swagger.json>
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: OpenApiToSwagger <input.json> <output.swagger.json>");
    return 1;
}

using MemoryStream input = new(File.ReadAllBytes(args[0]));
(OpenApiDocument? document, OpenApiDiagnostic? diagnostic) = OpenApiDocument.Load(input, "json");
if (document is null || diagnostic?.Errors.Count > 0)
{
    foreach (OpenApiError error in diagnostic?.Errors ?? [])
    {
        Console.Error.WriteLine(error);
    }
    return 1;
}

// The document records the address it was fetched from; show the default https launch profile instead.
document.Servers = [new OpenApiServer { Url = "https://localhost:7233" }];

using StringWriter swagger = new();
document.SerializeAsV2(new OpenApiJsonWriter(swagger));

// DocFX only shows the type of a path or query parameter when it sits in a "schema" object.
JsonNode root = JsonNode.Parse(swagger.ToString())!;
foreach (JsonNode? operation in root["paths"]!.AsObject().SelectMany(path => path.Value!.AsObject()).Select(entry => entry.Value))
{
    foreach (JsonObject parameter in operation?["parameters"]?.AsArray().OfType<JsonObject>() ?? [])
    {
        if (parameter["in"]?.GetValue<string>() != "body" && parameter["type"] is JsonNode type && parameter["schema"] is null)
        {
            JsonObject schema = new() { ["type"] = type.DeepClone() };
            if (parameter["format"] is JsonNode format)
            {
                schema["format"] = format.DeepClone();
            }

            parameter["schema"] = schema;
        }
    }
}

File.WriteAllText(args[1], root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
return 0;
