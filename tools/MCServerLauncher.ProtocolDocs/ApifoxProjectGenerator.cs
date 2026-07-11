using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Protocol;

namespace MCServerLauncher.ProtocolDocs;

internal static class ApifoxProjectGenerator
{
    private const string ModuleId = "mcsl-daemon-protocol";

    public static byte[] Generate()
    {
        var document = ProtocolDocumentBuilder.Create(
            new OpenRpcInfo("MCServerLauncher daemon", "v2"),
            BuiltInProtocolDefinitions.Rpcs,
            BuiltInProtocolDefinitions.Events);
        var descriptorByMethod = BuiltInProtocolDefinitions.Rpcs.ToDictionary(
            descriptor => descriptor.Method.Value,
            StringComparer.Ordinal);
        var root = CreateProject(document, descriptorByMethod);
        EnsureUniqueEntityIds(root);
        var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true })
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        return Encoding.UTF8.GetBytes(json + "\n");
    }

    private static JsonObject CreateProject(
        OpenRpcDocument document,
        IReadOnlyDictionary<string, RpcDescriptor> descriptorByMethod) =>
        new()
        {
            ["apifoxProject"] = "1.0.0",
            ["$schema"] = new JsonObject
            {
                ["app"] = "apifox",
                ["type"] = "project",
                ["version"] = "1.2.0"
            },
            ["info"] = new JsonObject
            {
                ["name"] = "MCServerLauncher daemon V2 protocol",
                ["description"] = "Generated from the frozen built-in RPC and event catalog. Connect to the selected /api/v2 WebSocket environment and pass the daemon token as the token query parameter."
            },
            ["projectSetting"] = new JsonObject
            {
                ["id"] = "mcsl-daemon-v2-protocol",
                ["language"] = "en-US",
                ["servers"] = new JsonArray()
            },
            ["apiCollection"] = new JsonArray(),
            ["webSocketCollection"] = CreateWebSocketCollection(document, descriptorByMethod),
            ["socketCollection"] = new JsonArray(),
            ["customEndpointCollection"] = new JsonArray(),
            ["docCollection"] = CreateEventDocumentation(document),
            ["schemaCollection"] = CreateSchemaCollection(document),
            ["environments"] = CreateEnvironments(),
            ["commonParameters"] = new JsonObject
            {
                ["parameters"] = new JsonObject
                {
                    ["query"] = new JsonArray(),
                    ["header"] = new JsonArray(),
                    ["cookie"] = new JsonArray(),
                    ["body"] = new JsonArray()
                }
            },
            ["moduleSettings"] = new JsonArray
            {
                new JsonObject
                {
                    ["id"] = ModuleId,
                    ["name"] = "Daemon V2 protocol",
                    ["description"] = "Generated from BuiltInProtocolDefinitions.",
                    ["moduleVariables"] = new JsonArray(),
                    ["openApiInfo"] = new JsonObject()
                }
            }
        };

    private static JsonArray CreateWebSocketCollection(
        OpenRpcDocument document,
        IReadOnlyDictionary<string, RpcDescriptor> descriptorByMethod)
    {
        var folders = document.Methods
            .GroupBy(method => descriptorByMethod[method.Name].Documentation!.Category, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select((group, folderIndex) => new JsonObject
            {
                ["id"] = StableId("folder", group.Key),
                ["name"] = group.Key,
                ["description"] = $"{group.Key} RPC methods.",
                ["ordering"] = folderIndex + 1,
                ["items"] = new JsonArray(group
                    .OrderBy(method => method.Name, StringComparer.Ordinal)
                    .Select((method, methodIndex) => (JsonNode?)CreateRpcItem(method, methodIndex + 1))
                    .ToArray())
            });

        return new JsonArray
        {
            new JsonObject
            {
                ["id"] = StableId("root", "websocket"),
                ["name"] = "root",
                ["description"] = "MCServerLauncher daemon JSON-RPC 2.0 over WebSocket.",
                ["items"] = new JsonArray(folders.Select(folder => (JsonNode?)folder).ToArray())
            }
        };
    }

    private static JsonObject CreateRpcItem(OpenRpcMethod method, int ordering) =>
        new()
        {
            ["id"] = StableId("rpc-item", method.Name),
            ["name"] = method.Name,
            ["api"] = new JsonObject
            {
                ["id"] = StableId("rpc", method.Name),
                ["path"] = "{{wsUrl}}?token={{token}}",
                ["parameters"] = ConnectionParameters(method.Name),
                ["requestBody"] = new JsonObject
                {
                    ["parameters"] = new JsonArray(),
                    ["message"] = CreateRequestMessage(method)
                },
                ["description"] = CreateRpcDescription(method),
                ["tags"] = new JsonArray("json-rpc", "built-in"),
                ["status"] = "released",
                ["ordering"] = ordering,
                ["advancedSettings"] = new JsonObject(),
                ["moduleId"] = ModuleId
            },
            ["moduleId"] = ModuleId
        };

    private static JsonObject ConnectionParameters(string method) =>
        new()
        {
            ["query"] = new JsonArray
            {
                new JsonObject
                {
                    ["id"] = StableId("parameter", $"{method}:token"),
                    ["name"] = "token",
                    ["type"] = "string",
                    ["required"] = true,
                    ["defaultEnable"] = true,
                    ["defaultValue"] = "{{token}}",
                    ["description"] = "Daemon main token or JWT sub-token."
                }
            },
            ["path"] = new JsonArray(),
            ["cookie"] = new JsonArray(),
            ["header"] = new JsonArray()
        };

    private static string CreateRequestMessage(OpenRpcMethod method)
    {
        var parameters = new JsonObject();
        foreach (var parameter in method.Params.OrderBy(parameter => parameter.Name, StringComparer.Ordinal))
        {
            parameters[parameter.Name] = CreateExample(parameter.Schema);
        }

        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method.Name,
            ["params"] = parameters,
            ["id"] = "{{$string.uuid}}"
        }.ToJsonString(new JsonSerializerOptions { WriteIndented = true })
            .Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static JsonNode? CreateExample(JsonElement schema)
    {
        if (schema.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return new JsonObject();
        }

        if (schema.TryGetProperty("examples", out var examples) && examples.ValueKind == JsonValueKind.Array && examples.GetArrayLength() > 0)
        {
            return JsonNode.Parse(examples[0].GetRawText());
        }

        if (schema.TryGetProperty("const", out var constant))
        {
            return JsonNode.Parse(constant.GetRawText());
        }

        if (schema.TryGetProperty("enum", out var values) && values.ValueKind == JsonValueKind.Array && values.GetArrayLength() > 0)
        {
            return JsonNode.Parse(values[0].GetRawText());
        }

        if (schema.TryGetProperty("anyOf", out var alternatives) && alternatives.ValueKind == JsonValueKind.Array)
        {
            foreach (var alternative in alternatives.EnumerateArray())
            {
                if (!alternative.TryGetProperty("type", out var type) ||
                    type.ValueKind != JsonValueKind.String ||
                    type.GetString() != "null")
                {
                    return CreateExample(alternative);
                }
            }
        }

        return schema.TryGetProperty("type", out var schemaType) && schemaType.ValueKind == JsonValueKind.String
            ? schemaType.GetString() switch
            {
                "array" => new JsonArray(),
                "boolean" => JsonValue.Create(false),
                "integer" or "number" => JsonValue.Create(0),
                "object" => new JsonObject(),
                "null" => null,
                _ => JsonValue.Create("")
            }
            : new JsonObject();
    }

    private static string CreateRpcDescription(OpenRpcMethod method) =>
        $"{method.Description}\n\nPermission: `{method.Permission}`.\n\n" +
        $"Request schema: `mcsl.schema.{method.Name}.request`.\n" +
        $"Result schema: `{GetSchemaId(method.Result.Schema)}`.";

    private static JsonArray CreateEventDocumentation(OpenRpcDocument document) =>
        new()
        {
            new JsonObject
            {
                ["id"] = StableId("docs", "events"),
                ["name"] = "Events",
                ["items"] = new JsonArray(document.Events
                    .OrderBy(@event => @event.Name, StringComparer.Ordinal)
                    .Select((@event, eventIndex) => (JsonNode?)new JsonObject
                    {
                        ["id"] = StableId("event-doc", @event.Name),
                        ["name"] = @event.Name,
                        ["type"] = "markdown",
                        ["content"] = CreateEventDescription(@event),
                        ["ordering"] = eventIndex + 1,
                        ["moduleId"] = ModuleId
                    })
                    .ToArray())
            }
        };

    private static string CreateEventDescription(OpenRpcEvent @event)
    {
        var meta = @event.Meta.Presence == OpenRpcEventFieldPresence.Omitted
            ? "Meta: omitted."
            : $"Meta ({@event.Meta.Presence.ToString().ToLowerInvariant()}): `{GetSchemaId(@event.Meta.Schema!.Value)}`.";

        return $"# {@event.Name}\n\n{@event.Description}\n\nPermission: `{@event.Permission}`.\n\n" +
               $"Data ({@event.Data.Presence.ToString().ToLowerInvariant()}): `{GetSchemaId(@event.Data.Schema!.Value)}`.\n\n{meta}";
    }

    private static JsonArray CreateSchemaCollection(OpenRpcDocument document)
    {
        var schemas = new List<(string Id, JsonObject Schema)>();
        foreach (var method in document.Methods)
        {
            schemas.Add(($"mcsl.schema.{method.Name}.request", CreateRequestSchema(method)));
            schemas.Add((GetSchemaId(method.Result.Schema), JsonNode.Parse(method.Result.Schema.GetRawText())!.AsObject()));
        }

        foreach (var @event in document.Events)
        {
            schemas.Add((GetSchemaId(@event.Data.Schema!.Value), JsonNode.Parse(@event.Data.Schema.Value.GetRawText())!.AsObject()));
            if (@event.Meta.Presence != OpenRpcEventFieldPresence.Omitted)
            {
                schemas.Add((GetSchemaId(@event.Meta.Schema!.Value), JsonNode.Parse(@event.Meta.Schema.Value.GetRawText())!.AsObject()));
            }
        }

        var items = schemas
            .GroupBy(schema => schema.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(schema => schema.Id, StringComparer.Ordinal)
            .Select((schema, schemaIndex) => (JsonNode?)new JsonObject
            {
                ["id"] = $"#/definitions/{schema.Id}",
                ["name"] = schema.Id,
                ["displayName"] = schema.Id,
                ["description"] = "Generated from the frozen protocol catalog.",
                ["schema"] = new JsonObject { ["jsonSchema"] = schema.Schema },
                ["ordering"] = schemaIndex + 1,
                ["moduleId"] = ModuleId
            })
            .ToArray();

        return new JsonArray
        {
            new JsonObject
            {
                ["id"] = StableId("schema-folder", "protocol"),
                ["name"] = "Protocol schemas",
                ["items"] = new JsonArray(items)
            }
        };
    }

    private static JsonObject CreateRequestSchema(OpenRpcMethod method)
    {
        var properties = new JsonObject();
        var required = new JsonArray();
        foreach (var parameter in method.Params.OrderBy(parameter => parameter.Name, StringComparer.Ordinal))
        {
            properties[parameter.Name] = JsonNode.Parse(parameter.Schema.GetRawText());
            if (parameter.Required)
            {
                required.Add(parameter.Name);
            }
        }

        return new JsonObject
        {
            ["$id"] = $"mcsl.schema.{method.Name}.request",
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required,
            ["additionalProperties"] = false
        };
    }

    private static JsonArray CreateEnvironments() =>
        new()
        {
            new JsonObject
            {
                ["id"] = StableId("environment", "local-daemon"),
                ["name"] = "local-daemon",
                ["type"] = "normal",
                ["visibility"] = "SHARED",
                ["ordering"] = 1,
                ["variables"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["id"] = StableId("variable", "wsUrl"),
                        ["name"] = "wsUrl",
                        ["value"] = "ws://127.0.0.1:11452/api/v2",
                        ["type"] = "string"
                    },
                    new JsonObject
                    {
                        ["id"] = StableId("variable", "token"),
                        ["name"] = "token",
                        ["value"] = "",
                        ["defaultValue"] = "",
                        ["type"] = "string",
                        ["description"] = "Set a daemon token locally; never commit a real token."
                    }
                },
                ["parameters"] = new JsonObject(),
                ["websocketBaseUrls"] = new JsonObject { [ModuleId] = "ws://127.0.0.1:11452/api/v2" }
            }
        };

    private static string GetSchemaId(JsonElement schema) =>
        schema.TryGetProperty("$id", out var id) && !string.IsNullOrWhiteSpace(id.GetString())
            ? id.GetString()!
            : throw new InvalidOperationException("Protocol schemas must have a stable $id.");

    private static string StableId(string prefix, string value) => $"mcsl-{prefix}-{Ordinal(value):x8}";

    private static void EnsureUniqueEntityIds(JsonNode root)
    {
        var pathsById = new Dictionary<string, string>(StringComparer.Ordinal);
        Visit(root, "$", pathsById);
    }

    private static void Visit(JsonNode? node, string path, IDictionary<string, string> pathsById)
    {
        if (node is JsonObject obj)
        {
            if (obj.TryGetPropertyValue("id", out var idNode) && idNode is JsonValue idValue)
            {
                if (!idValue.TryGetValue<string>(out var id) || string.IsNullOrWhiteSpace(id))
                {
                    throw new InvalidOperationException($"Apifox entity at '{path}' has an invalid id.");
                }

                if (!pathsById.TryAdd(id, path))
                {
                    throw new InvalidOperationException(
                        $"Apifox entity id '{id}' is duplicated at '{pathsById[id]}' and '{path}'.");
                }
            }

            foreach (var property in obj)
            {
                Visit(property.Value, $"{path}.{property.Key}", pathsById);
            }

            return;
        }

        if (node is JsonArray array)
        {
            for (var index = 0; index < array.Count; index++)
            {
                Visit(array[index], $"{path}[{index}]", pathsById);
            }
        }
    }

    private static uint Ordinal(string value)
    {
        const uint offset = 2166136261;
        const uint prime = 16777619;
        var hash = offset;
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            hash = (hash ^ b) * prime;
        }

        return hash;
    }
}
