#if !NO_DAEMON_REFS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using MCServerLauncher.Common;
using MCServerLauncher.Common.ProtoType;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Event;
using MCServerLauncher.Common.ProtoType.Serialization;
using MCServerLauncher.Daemon;
using MCServerLauncher.Daemon.Serialization;
using MCServerLauncher.DaemonClient;
using MCServerLauncher.DaemonClient.Serialization;
using Xunit;

namespace MCServerLauncher.ProtocolTests;

/// <summary>
/// T21: Public surface leakage tests — reflect over public members and serializer context
/// registrations to ensure no future internal perf-helper types/namespaces leak into
/// the public contract or serializer contexts of MCServerLauncher.Common or MCServerLauncher.Daemon.
/// </summary>
[Collection("RuntimeSwitchIsolation")]
public class PublicSurfaceLeakageTests
{
    // Known internal namespaces that should NOT appear in public serializer registrations
    // These are utility/concurrency namespaces, NOT contract type namespaces
    private static readonly string[] ForbiddenInternalNamespacePrefixes =
    {
        "MCServerLauncher.Common.Helpers",
        "MCServerLauncher.Common.Utils",
        "MCServerLauncher.Common.Concurrent",
        "MCServerLauncher.Common.Internal",
        "MCServerLauncher.Daemon.Utils",
        "MCServerLauncher.Daemon.Storage"
        // NOTE: MCServerLauncher.Daemon.Remote IS a contract namespace - contains ActionError, Permission
        // which are legitimate RPC contract types registered in serializer contexts
    };

    // Known perf-helper type name patterns that should never appear in public serializer contexts
    private static readonly string[] ForbiddenTypeNamePatterns =
    {
        "PooledObject",
        "ObjectPool",
        "Pool",
        "Cache",
        "Span",
        "Memory",
        "ReadOnlySpan",
        "ReadOnlyMemory",
        "RefCounter",
        "Recycler"
    };

    #region Contract types that ARE allowed in public serializer contexts

    private static readonly Type[] AllowedEnvelopeTypes =
    {
        typeof(ActionRequest),
        typeof(ActionResponse),
        typeof(EventPacket)
    };

    private static readonly Type[] AllowedContextTypes =
    {
        typeof(RpcEnvelopeContext),
        typeof(ActionParametersContext),
        typeof(ActionResultsContext),
        typeof(EventDataContext),
        typeof(PersistenceContext),
        typeof(StjResolver)
    };

    #endregion

    #region Public surface: contract DTO properties use allowed types

    [Fact]
    public void ActionRequest_PublicProperties_HaveAllowedTypes()
    {
        var type = typeof(ActionRequest);
        var publicProps = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in publicProps)
        {
            Assert.True(
                IsAllowedContractPropertyType(prop.PropertyType),
                $"ActionRequest.{prop.Name} has disallowed property type: {prop.PropertyType.FullName}");
        }
    }

    [Fact]
    public void ActionResponse_PublicProperties_HaveAllowedTypes()
    {
        var type = typeof(ActionResponse);
        var publicProps = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in publicProps)
        {
            Assert.True(
                IsAllowedContractPropertyType(prop.PropertyType),
                $"ActionResponse.{prop.Name} has disallowed property type: {prop.PropertyType.FullName}");
        }
    }

    [Fact]
    public void EventPacket_PublicProperties_HaveAllowedTypes()
    {
        var type = typeof(EventPacket);
        var publicProps = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in publicProps)
        {
            Assert.True(
                IsAllowedContractPropertyType(prop.PropertyType),
                $"EventPacket.{prop.Name} has disallowed property type: {prop.PropertyType.FullName}");
        }
    }

    #endregion

    #region Assembly public surface: Common internal namespace stays internal-only

    [Fact]
    public void CommonAssembly_DoesNotExposeInternalPerformanceTypes()
    {
        var exportedTypes = typeof(StjResolver).Assembly.GetExportedTypes();

        var leaked = exportedTypes
            .Where(type => type.Namespace?.StartsWith("MCServerLauncher.Common.Internal", StringComparison.Ordinal) == true)
            .Select(type => type.FullName)
            .ToArray();

        Assert.True(leaked.Length == 0,
            "Common assembly exports internal-performance namespace types: " + string.Join(", ", leaked));
    }

    #endregion

    #region STJ context: ensure no internal perf-helper types leak into registrations

    [Fact]
    public void RpcEnvelopeContext_DoesNotRegisterInternalTypes()
    {
        var registeredTypes = GetJsonSerializableTypes(typeof(RpcEnvelopeContext));
        CheckForLeakedInternalTypes(registeredTypes, "RpcEnvelopeContext");
    }

    [Fact]
    public void ActionParametersContext_DoesNotRegisterInternalTypes()
    {
        var registeredTypes = GetJsonSerializableTypes(typeof(ActionParametersContext));
        CheckForLeakedInternalTypes(registeredTypes, "ActionParametersContext");
    }

    [Fact]
    public void ActionResultsContext_DoesNotRegisterInternalTypes()
    {
        var registeredTypes = GetJsonSerializableTypes(typeof(ActionResultsContext));
        CheckForLeakedInternalTypes(registeredTypes, "ActionResultsContext");
    }

    [Fact]
    public void EventDataContext_DoesNotRegisterInternalTypes()
    {
        var registeredTypes = GetJsonSerializableTypes(typeof(EventDataContext));
        CheckForLeakedInternalTypes(registeredTypes, "EventDataContext");
    }

    [Fact]
    public void PersistenceContext_DoesNotRegisterInternalTypes()
    {
        var registeredTypes = GetJsonSerializableTypes(typeof(PersistenceContext));
        CheckForLeakedInternalTypes(registeredTypes, "PersistenceContext");
    }

    [Fact]
    public void DaemonRpcSerializerContext_DoesNotRegisterInternalTypes()
    {
        var registeredTypes = GetJsonSerializableTypes(typeof(DaemonRpcSerializerContext));
        CheckForLeakedInternalTypes(registeredTypes, "DaemonRpcSerializerContext");
    }

    [Fact]
    public void DaemonPersistenceSerializerContext_DoesNotRegisterInternalTypes()
    {
        var registeredTypes = GetJsonSerializableTypes(typeof(DaemonPersistenceSerializerContext));
        CheckForLeakedInternalTypes(registeredTypes, "DaemonPersistenceSerializerContext");
    }

    [Fact]
    public void DaemonClientRpcSerializerContext_DoesNotRegisterInternalTypes()
    {
        var registeredTypes = GetJsonSerializableTypes(typeof(DaemonClientRpcSerializerContext));
        CheckForLeakedInternalTypes(registeredTypes, "DaemonClientRpcSerializerContext");
    }

    #endregion

    #region Resolver composition: no forbidden types in combined resolvers

    [Fact]
    public void StjResolver_CombinedResolver_DoesNotContainInternalTypes()
    {
        // StjResolver.CreateDefaultResolver combines multiple contexts - verify each
        var contextTypes = new[]
        {
            typeof(RpcEnvelopeContext),
            typeof(ActionParametersContext),
            typeof(ActionResultsContext),
            typeof(EventDataContext),
            typeof(PersistenceContext)
        };

        foreach (var contextType in contextTypes)
        {
            var registeredTypes = GetJsonSerializableTypes(contextType);
            CheckForLeakedInternalTypes(registeredTypes, $"StjResolver context {contextType.Name}");
        }
    }

    [Fact]
    public void DaemonRpcBoundary_Resolver_DoesNotContainInternalTypes()
    {
        // DaemonRpcJsonBoundary adds DaemonRpcSerializerContext on top of StjResolver
        var commonTypes = GetJsonSerializableTypes(typeof(RpcEnvelopeContext));
        var daemonTypes = GetJsonSerializableTypes(typeof(DaemonRpcSerializerContext));

        CheckForLeakedInternalTypes(commonTypes, "DaemonRpcJsonBoundary (Common)");
        CheckForLeakedInternalTypes(daemonTypes, "DaemonRpcJsonBoundary (DaemonRpc)");
    }

    [Fact]
    public void DaemonClientRpcBoundary_Resolver_DoesNotContainInternalTypes()
    {
        // DaemonClientRpcJsonBoundary adds DaemonClientRpcSerializerContext on top of StjResolver
        var commonTypes = GetJsonSerializableTypes(typeof(RpcEnvelopeContext));
        var clientTypes = GetJsonSerializableTypes(typeof(DaemonClientRpcSerializerContext));

        CheckForLeakedInternalTypes(commonTypes, "DaemonClientRpcJsonBoundary (Common)");
        CheckForLeakedInternalTypes(clientTypes, "DaemonClientRpcJsonBoundary (DaemonClientRpc)");
    }

    #endregion

    #region Daemon-local wire-contract ownership leakage guard

    [Fact]
    public void DaemonRpcContext_DoesNotDuplicateCommonEnvelopeTypes()
    {
        // RpcEnvelopeContext in Common owns ActionRequest, ActionResponse, EventPacket.
        // DaemonRpcSerializerContext must not re-register these types as daemon-local contracts.
        var commonEnvelopeTypes = GetJsonSerializableTypes(typeof(RpcEnvelopeContext));
        var daemonRpcTypes = GetJsonSerializableTypes(typeof(DaemonRpcSerializerContext));

        var duplicated = daemonRpcTypes
            .Where(dt => commonEnvelopeTypes.Any(ct => ct == dt))
            .Select(dt => dt.FullName)
            .ToList();

        Assert.True(duplicated.Count == 0,
            $"DaemonRpcSerializerContext duplicates Common-owned envelope types: {string.Join(", ", duplicated)}. " +
            "Envelope types must remain Common-owned only.");
    }

    [Fact]
    public void DaemonClientRpcContext_DoesNotDuplicateCommonEnvelopeTypes()
    {
        // DaemonClientRpcSerializerContext registers envelope types for client-side usage,
        // but these are already source-generated in Common's RpcEnvelopeContext.
        // The client context SHOULD NOT claim ownership of new wire-envelope types beyond
        // what Common provides.
        var commonEnvelopeTypes = GetJsonSerializableTypes(typeof(RpcEnvelopeContext));
        var clientRpcTypes = GetJsonSerializableTypes(typeof(DaemonClientRpcSerializerContext));

        // The client context may reference Common types (that's fine), but it must not
        // introduce NEW envelope types that aren't already in Common
        var wireEnvelopeTypes = new HashSet<Type>
        {
            typeof(ActionRequest),
            typeof(ActionResponse),
            typeof(EventPacket)
        };

        foreach (var clientType in clientRpcTypes)
        {
            if (wireEnvelopeTypes.Contains(clientType))
            {
                // This type is a known envelope type - verify Common already owns it
                Assert.Contains(commonEnvelopeTypes, ct => ct == clientType);
            }
        }
    }

    [Fact]
    public void DaemonRpcContext_OnlyRegistersDaemonLocalTypes()
    {
        // DaemonRpcSerializerContext should only contain types that are daemon-local
        // (ActionError, Permission, etc.) or Common types the daemon needs for local RPC.
        // It must NOT contain types from unrelated namespaces like WPF, UI, etc.
        var daemonRpcTypes = GetJsonSerializableTypes(typeof(DaemonRpcSerializerContext));

        foreach (var type in daemonRpcTypes)
        {
            Assert.True(
                type.Namespace?.StartsWith("MCServerLauncher.Daemon") == true ||
                type.Namespace?.StartsWith("MCServerLauncher.Common") == true ||
                type.Namespace?.StartsWith("System") == true,
                $"DaemonRpcSerializerContext registers type from unexpected namespace: {type.FullName}");
        }
    }

    [Fact]
    public void DaemonClientRpcContext_OnlyRegistersKnownTypes()
    {
        var clientRpcTypes = GetJsonSerializableTypes(typeof(DaemonClientRpcSerializerContext));

        foreach (var type in clientRpcTypes)
        {
            Assert.True(
                type.Namespace?.StartsWith("MCServerLauncher.DaemonClient") == true ||
                type.Namespace?.StartsWith("MCServerLauncher.Common") == true ||
                type.Namespace?.StartsWith("System") == true,
                $"DaemonClientRpcSerializerContext registers type from unexpected namespace: {type.FullName}");
        }
    }

    #endregion


    #region Helper methods

    private static List<Type> GetJsonSerializableTypes(Type contextType)
    {
        var types = new List<Type>();

        // Get custom attribute data to access constructor arguments
        var attributesData = contextType.GetCustomAttributesData();
        foreach (var attrData in attributesData)
        {
            // JsonSerializableAttribute has constructor that takes Type as first argument
            if (attrData.AttributeType == typeof(JsonSerializableAttribute) &&
                attrData.ConstructorArguments.Count > 0)
            {
                var typeValue = attrData.ConstructorArguments[0].Value;
                if (typeValue is Type type)
                {
                    types.Add(type);
                }
            }
        }

        return types;
    }

    private static bool IsAllowedContractNamespace(string namespaceName)
    {
        if (string.IsNullOrEmpty(namespaceName))
            return false;

        // Allow ProtoType namespaces
        if (namespaceName.StartsWith("MCServerLauncher.Common.ProtoType"))
            return true;

        // Allow System namespaces for primitives
        if (namespaceName.StartsWith("System"))
            return true;


        return false;
    }

    private static bool IsAllowedContractPropertyType(Type propertyType)
    {
        // Handle nullable types
        var underlyingType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        // Allow primitive and standard contract types
        if (underlyingType == typeof(Guid) ||
            underlyingType == typeof(string) ||
            underlyingType == typeof(int) ||
            underlyingType == typeof(long) ||
            underlyingType == typeof(bool) ||
            underlyingType == typeof(JsonElement) ||
            underlyingType == typeof(JsonElement?) ||
            underlyingType == typeof(JsonPayloadBuffer) ||
            underlyingType == typeof(JsonPayloadBuffer?))
            return true;

        // Allow enums (like ActionType, EventType, etc.)
        if (underlyingType.IsEnum)
            return true;

        // Allow arrays of primitives
        if (underlyingType.IsArray && IsAllowedContractPropertyType(underlyingType.GetElementType()!))
            return true;

        // Allow generic IEnumerable of allowed types
        if (underlyingType.IsGenericType)
        {
            var genericDef = underlyingType.GetGenericTypeDefinition();
            if (genericDef == typeof(IEnumerable<>) ||
                genericDef == typeof(ICollection<>) ||
                genericDef == typeof(IList<>) ||
                genericDef == typeof(List<>) ||
                genericDef == typeof(Dictionary<,>))
            {
                var typeArgs = underlyingType.GetGenericArguments();
                return typeArgs.All(IsAllowedContractPropertyType);
            }
        }

        return false;
    }

    private static void CheckForLeakedInternalTypes(List<Type> types, string contextName)
    {
        var leakedTypes = new List<string>();

        foreach (var type in types)
        {
            if (type == null)
                continue;

            // Check namespace against forbidden prefixes
            foreach (var forbiddenPrefix in ForbiddenInternalNamespacePrefixes)
            {
                if (type.Namespace?.StartsWith(forbiddenPrefix) == true)
                {
                    leakedTypes.Add($"{type.FullName} (namespace prefix: {forbiddenPrefix})");
                }
            }

            // Check type name against forbidden patterns
            foreach (var pattern in ForbiddenTypeNamePatterns)
            {
                if (type.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    leakedTypes.Add($"{type.FullName} (name contains: {pattern})");
                }
            }
        }

        if (leakedTypes.Count > 0)
        {
            var message = $"[{contextName}] Found leaked internal types:\n  - " +
                        string.Join("\n  - ", leakedTypes);
            Assert.Fail(message);
        }
    }

    #endregion
}
#endif
