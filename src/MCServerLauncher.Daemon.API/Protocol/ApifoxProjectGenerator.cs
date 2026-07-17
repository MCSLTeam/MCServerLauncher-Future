using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MCServerLauncher.Common.Contracts.Protocol;

namespace MCServerLauncher.Daemon.API.Protocol;

/// <summary>
/// Builds an internal Apifox project document from an OpenRPC catalog snapshot.
/// The daemon and protocol-documentation tooling pass the frozen catalog or built-in definitions.
/// </summary>
internal static class ApifoxProjectGenerator
{
    private const string ModuleId = "mcsl-daemon-protocol";

    internal static byte[] GenerateBuiltIn(string daemonVersion = "v2")
    {
        var document = BuiltInProtocolDefinitions.CreateDocument(daemonVersion);
        return Generate(document, BuiltInProtocolDefinitions.Rpcs);
    }

    internal static byte[] Generate(OpenRpcDocument document, ImmutableArray<RpcDescriptor> rpcs)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (rpcs.IsDefault)
            throw new ArgumentException("RPC descriptors cannot be default.", nameof(rpcs));

        var descriptorByMethod = new Dictionary<string, RpcDescriptor>(rpcs.Length, StringComparer.Ordinal);
        foreach (var descriptor in rpcs)
        {
            descriptorByMethod.Add(descriptor.Method.Value, descriptor);
        }
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
                ["description"] = "Generated from the frozen runtime RPC and event catalog (built-in plus admitted plugins when present). Connect to the selected /api/v2 WebSocket environment and pass the daemon token as the token query parameter."
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
                    ["description"] = "Generated from the frozen protocol catalog.",
                    ["moduleVariables"] = new JsonArray(),
                    ["openApiInfo"] = new JsonObject()
                }
            }
        };

    private static JsonArray CreateWebSocketCollection(
        OpenRpcDocument document,
        IReadOnlyDictionary<string, RpcDescriptor> descriptorByMethod)
    {
        var methodsByCategory = new SortedDictionary<string, List<OpenRpcMethod>>(StringComparer.Ordinal);
        foreach (var method in document.Methods)
        {
            var category = descriptorByMethod[method.Name].Documentation!.Category;
            if (!methodsByCategory.TryGetValue(category, out var methods))
            {
                methods = [];
                methodsByCategory.Add(category, methods);
            }

            methods.Add(method);
        }

        var folders = new JsonArray();
        var folderIndex = 0;
        foreach (var (category, methods) in methodsByCategory)
        {
            methods.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));

            var items = new JsonArray();
            for (var methodIndex = 0; methodIndex < methods.Count; methodIndex++)
            {
                items.Add(CreateRpcItem(methods[methodIndex], methodIndex + 1));
            }

            folders.Add(new JsonObject
            {
                ["id"] = StableId("folder", category),
                ["name"] = category,
                ["description"] = $"{category} RPC methods.",
                ["ordering"] = ++folderIndex,
                ["items"] = items
            });
        }

        return new JsonArray
        {
            new JsonObject
            {
                ["id"] = StableId("root", "websocket"),
                ["name"] = "root",
                ["description"] = "MCServerLauncher daemon JSON-RPC 2.0 over WebSocket.",
                ["items"] = folders
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
        var orderedParameters = new List<OpenRpcContentDescriptor>(method.Params);
        orderedParameters.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
        foreach (var parameter in orderedParameters)
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

    private static JsonArray CreateEventDocumentation(OpenRpcDocument document)
    {
        var events = new List<OpenRpcEvent>(document.Events);
        events.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));

        var items = new JsonArray();
        for (var eventIndex = 0; eventIndex < events.Count; eventIndex++)
        {
            var @event = events[eventIndex];
            items.Add(new JsonObject
            {
                ["id"] = StableId("event-doc", @event.Name),
                ["name"] = @event.Name,
                ["type"] = "markdown",
                ["content"] = CreateEventDescription(@event),
                ["ordering"] = eventIndex + 1,
                ["moduleId"] = ModuleId
            });
        }

        return new JsonArray
        {
            new JsonObject
            {
                ["id"] = StableId("docs", "events"),
                ["name"] = "Events",
                ["items"] = items
            }
        };
    }

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
        var schemaProperties = document.Components.GetProperty("schemas").EnumerateObject();
        if (!schemaProperties.MoveNext())
        {
            throw new InvalidOperationException("The protocol catalog must define exactly one error data schema.");
        }

        var errorDataSchema = schemaProperties.Current;
        if (schemaProperties.MoveNext())
        {
            throw new InvalidOperationException("The protocol catalog must define exactly one error data schema.");
        }

        var schemas = new List<(string Id, JsonObject Schema)>
        {
            (
                errorDataSchema.Name,
                JsonNode.Parse(errorDataSchema.Value.GetRawText())!.AsObject())
        };
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

        var schemaById = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var (id, schema) in schemas)
        {
            schemaById.TryAdd(id, schema);
        }

        var schemaIds = new List<string>(schemaById.Keys);
        schemaIds.Sort(StringComparer.Ordinal);

        var items = new JsonArray();
        for (var schemaIndex = 0; schemaIndex < schemaIds.Count; schemaIndex++)
        {
            var id = schemaIds[schemaIndex];
            items.Add(new JsonObject
            {
                ["id"] = $"#/definitions/{id}",
                ["name"] = id,
                ["displayName"] = id,
                ["description"] = "Generated from the frozen protocol catalog.",
                ["schema"] = new JsonObject { ["jsonSchema"] = schemaById[id] },
                ["ordering"] = schemaIndex + 1,
                ["moduleId"] = ModuleId
            });
        }

        return new JsonArray
        {
            new JsonObject
            {
                ["id"] = StableId("schema-folder", "protocol"),
                ["name"] = "Protocol schemas",
                ["items"] = items
            }
        };
    }

    private static JsonObject CreateRequestSchema(OpenRpcMethod method)
    {
        var properties = new JsonObject();
        var required = new JsonArray();
        var orderedParameters = new List<OpenRpcContentDescriptor>(method.Params);
        orderedParameters.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
        foreach (var parameter in orderedParameters)
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
