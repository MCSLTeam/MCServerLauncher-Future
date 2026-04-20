#if !NO_DAEMON_REFS
using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using MCServerLauncher.Common.ProtoType;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Event;
using MCServerLauncher.Common.ProtoType.Serialization;
using MCServerLauncher.Daemon.Serialization;
using MCServerLauncher.DaemonClient.Serialization;
using Xunit;

namespace MCServerLauncher.ProtocolTests;

/// <summary>
/// T20: Public contract safety tests — verify public protocol DTOs and resolver/serializer
/// boundaries remain shape/type-kind stable where required by the wire protocol.
/// </summary>
[Collection("RuntimeSwitchIsolation")]
public class PublicContractSafetyTests
{
    #region Contract DTO shape assertions

    [Fact]
    public void ActionRequest_IsRecordType()
    {
        var type = typeof(ActionRequest);
        Assert.True(type.IsClass, "ActionRequest must be a reference type");
        Assert.True(type.BaseType == typeof(ValueType) || type.IsClass, "ActionRequest should be a record (class)");

        // Records are class types with synthesized constructors/properties
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        Assert.Contains(properties, p => p.Name == nameof(ActionRequest.ActionType));
        Assert.Contains(properties, p => p.Name == nameof(ActionRequest.Parameter));
        Assert.Contains(properties, p => p.Name == nameof(ActionRequest.Id));
    }

    [Fact]
    public void ActionResponse_IsRecordType()
    {
        var type = typeof(ActionResponse);
        Assert.True(type.IsClass, "ActionResponse must be a reference type");

        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        Assert.Contains(properties, p => p.Name == nameof(ActionResponse.RequestStatus));
        Assert.Contains(properties, p => p.Name == nameof(ActionResponse.Retcode));
        Assert.Contains(properties, p => p.Name == nameof(ActionResponse.Data));
        Assert.Contains(properties, p => p.Name == nameof(ActionResponse.Message));
        Assert.Contains(properties, p => p.Name == nameof(ActionResponse.Id));
    }

    [Fact]
    public void EventPacket_IsRecordType()
    {
        var type = typeof(EventPacket);
        Assert.True(type.IsClass, "EventPacket must be a reference type");

        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        Assert.Contains(properties, p => p.Name == nameof(EventPacket.EventType));
        Assert.Contains(properties, p => p.Name == nameof(EventPacket.EventMeta));
        Assert.Contains(properties, p => p.Name == nameof(EventPacket.EventData));
        Assert.Contains(properties, p => p.Name == nameof(EventPacket.Timestamp));
    }

    #endregion

    #region STJ context type registration assertions

    [Fact]
    public void RpcEnvelopeContext_RegistersActionRequest()
    {
        var context = RpcEnvelopeContext.Default;
        Assert.NotNull(context);

        var typeInfo = context.GetTypeInfo(typeof(ActionRequest));
        Assert.NotNull(typeInfo);
    }

    [Fact]
    public void RpcEnvelopeContext_RegistersActionResponse()
    {
        var context = RpcEnvelopeContext.Default;
        Assert.NotNull(context);

        var typeInfo = context.GetTypeInfo(typeof(ActionResponse));
        Assert.NotNull(typeInfo);
    }

    [Fact]
    public void RpcEnvelopeContext_RegistersEventPacket()
    {
        var context = RpcEnvelopeContext.Default;
        Assert.NotNull(context);

        var typeInfo = context.GetTypeInfo(typeof(EventPacket));
        Assert.NotNull(typeInfo);
    }

    [Fact]
    public void EventDataContext_OwnsInstanceLogEventMeta()
    {
        // InstanceLogEventMeta is a Common-owned event meta type used on the RPC wire path.
        // Common must be the explicit source-generated metadata owner for this type.
        var context = EventDataContext.Default;
        Assert.NotNull(context);

        var typeInfo = context.GetTypeInfo(typeof(InstanceLogEventMeta));
        Assert.NotNull(typeInfo);
    }

    [Fact]
    public void StjResolver_CombinesAllCommonContexts()
    {
        var resolver = StjResolver.CreateDefaultResolver();
        Assert.NotNull(resolver);

        // Use StjResolver's own options to check GetTypeInfo
        var options = StjResolver.CreateDefaultOptions();

        // The combined resolver should be able to handle all envelope types
        var envelopeTypes = new[] { typeof(ActionRequest), typeof(ActionResponse), typeof(EventPacket) };
        foreach (var envelopeType in envelopeTypes)
        {
            var info = resolver.GetTypeInfo(envelopeType, options);
            Assert.NotNull(info);
        }
    }

    #endregion

    #region Serializer boundary resolver composition

    [Fact]
    public void DaemonRpcBoundary_HasValidResolver()
    {
        var resolver = DaemonRpcJsonBoundary.CreateStjResolver(DaemonStjReflectionFallbackPolicy.Disabled);
        Assert.NotNull(resolver);

        var options = DaemonRpcJsonBoundary.CreateStjOptions(DaemonStjReflectionFallbackPolicy.Disabled);

        // Verify resolver can provide type info for known types
        var info = resolver.GetTypeInfo(typeof(ActionRequest), options);
        Assert.NotNull(info);
    }

    [Fact]
    public void DaemonPersistenceBoundary_HasValidResolver()
    {
        var resolver = DaemonPersistenceJsonBoundary.CreateStjResolver(DaemonStjReflectionFallbackPolicy.Disabled);
        Assert.NotNull(resolver);

        var options = DaemonPersistenceJsonBoundary.CreateStjOptions(DaemonStjReflectionFallbackPolicy.Disabled);
        Assert.True(options.IncludeFields);

        var info = resolver.GetTypeInfo(typeof(Guid), options);
        Assert.NotNull(info);
    }

    [Fact]
    public void DaemonClientRpcBoundary_HasValidResolver()
    {
        var resolver = DaemonClientRpcJsonBoundary.CreateStjResolver(DaemonClientStjReflectionFallbackPolicy.Disabled);
        Assert.NotNull(resolver);

        var options = DaemonClientRpcJsonBoundary.CreateStjOptions(DaemonClientStjReflectionFallbackPolicy.Disabled);

        var info = resolver.GetTypeInfo(typeof(ActionResponse), options);
        Assert.NotNull(info);
    }

    #endregion

    #region Contract property type stability

    [Fact]
    public void ActionRequest_ParameterProperty_IsNullableJsonElement()
    {
        var prop = typeof(ActionRequest).GetProperty(nameof(ActionRequest.Parameter));
        Assert.NotNull(prop);
        Assert.Equal(typeof(JsonElement?), prop.PropertyType);
    }

    [Fact]
    public void ActionResponse_DataProperty_IsNullableJsonElement()
    {
        var prop = typeof(ActionResponse).GetProperty(nameof(ActionResponse.Data));
        Assert.NotNull(prop);
        Assert.Equal(typeof(JsonElement?), prop.PropertyType);
    }

    [Fact]
    public void EventPacket_EventMeta_IsNullableJsonPayloadBuffer()
    {
        var prop = typeof(EventPacket).GetProperty(nameof(EventPacket.EventMeta));
        Assert.NotNull(prop);
        Assert.Equal(typeof(JsonPayloadBuffer?), prop.PropertyType);
    }

    [Fact]
    public void EventPacket_EventData_IsNullableJsonPayloadBuffer()
    {
        var prop = typeof(EventPacket).GetProperty(nameof(EventPacket.EventData));
        Assert.NotNull(prop);
        Assert.Equal(typeof(JsonPayloadBuffer?), prop.PropertyType);
    }

    [Fact]
    public void ActionRequest_Id_IsGuid()
    {
        var prop = typeof(ActionRequest).GetProperty(nameof(ActionRequest.Id));
        Assert.NotNull(prop);
        Assert.Equal(typeof(Guid), prop.PropertyType);
    }

    [Fact]
    public void ActionResponse_Id_IsGuid()
    {
        var prop = typeof(ActionResponse).GetProperty(nameof(ActionResponse.Id));
        Assert.NotNull(prop);
        Assert.Equal(typeof(Guid), prop.PropertyType);
    }

    #endregion

    #region JsonSerializerOptions consistency checks

    [Fact]
    public void DaemonRpcBoundary_Options_UsesSnakeCaseNaming()
    {
        var options = DaemonRpcJsonBoundary.StjOptions;
        Assert.Equal(JsonNamingPolicy.SnakeCaseLower, options.PropertyNamingPolicy);
    }

    [Fact]
    public void DaemonClientRpcBoundary_Options_UsesSnakeCaseNaming()
    {
        var options = DaemonClientRpcJsonBoundary.StjOptions;
        Assert.Equal(JsonNamingPolicy.SnakeCaseLower, options.PropertyNamingPolicy);
    }

    [Fact]
    public void DaemonRpcBoundary_Options_HasRequiredConverters()
    {
        var options = DaemonRpcJsonBoundary.StjOptions;
        Assert.NotNull(options.Converters);
        Assert.Contains(options.Converters, c => c is GuidStjConverter);
        Assert.Contains(options.Converters, c => c is EncodingStjConverter);
        Assert.Contains(options.Converters, c => c is PlaceHolderStringStjConverter);
    }

    #endregion

    #region Common ownership: envelope types reside in Common assembly

    [Fact]
    public void ActionRequest_OwnedByCommonAssembly()
    {
        var type = typeof(ActionRequest);
        Assert.Equal("MCServerLauncher.Common", type.Assembly.GetName().Name,
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ActionResponse_OwnedByCommonAssembly()
    {
        var type = typeof(ActionResponse);
        Assert.Equal("MCServerLauncher.Common", type.Assembly.GetName().Name,
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void EventPacket_OwnedByCommonAssembly()
    {
        var type = typeof(EventPacket);
        Assert.Equal("MCServerLauncher.Common", type.Assembly.GetName().Name,
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void RpcEnvelopeContext_OwnedByCommonAssembly()
    {
        var type = typeof(RpcEnvelopeContext);
        Assert.Equal("MCServerLauncher.Common", type.Assembly.GetName().Name,
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void StjResolver_OwnedByCommonAssembly()
    {
        var type = typeof(StjResolver);
        Assert.Equal("MCServerLauncher.Common", type.Assembly.GetName().Name,
            StringComparer.OrdinalIgnoreCase);
    }

    #endregion

    #region Boundary composition: daemon/client consume Common contexts first

    [Fact]
    public void DaemonRpcBoundary_ResolvesWireEnvelopeTypes_WithoutFallback()
    {
        var options = DaemonRpcJsonBoundary.CreateStjOptions(DaemonStjReflectionFallbackPolicy.Disabled);

        // Wire envelope types must resolve without any reflection fallback
        var wireTypes = new[] { typeof(ActionRequest), typeof(ActionResponse), typeof(EventPacket) };
        foreach (var wireType in wireTypes)
        {
            var info = options.TypeInfoResolver?.GetTypeInfo(wireType, options);
            Assert.NotNull(info);
        }
    }

    [Fact]
    public void DaemonClientRpcBoundary_ResolvesWireEnvelopeTypes_WithoutFallback()
    {
        var options = DaemonClientRpcJsonBoundary.CreateStjOptions(DaemonClientStjReflectionFallbackPolicy.Disabled);

        var wireTypes = new[] { typeof(ActionRequest), typeof(ActionResponse), typeof(EventPacket) };
        foreach (var wireType in wireTypes)
        {
            var info = options.TypeInfoResolver?.GetTypeInfo(wireType, options);
            Assert.NotNull(info);
        }
    }

    [Fact]
    public void DaemonRpcBoundary_Options_HasSameCoreConvertersAsCommon()
    {
        // Daemon boundary must include the same core converters Common defines
        var daemonOptions = DaemonRpcJsonBoundary.StjOptions;

        Assert.Contains(daemonOptions.Converters, c => c is GuidStjConverter);
        Assert.Contains(daemonOptions.Converters, c => c is EncodingStjConverter);
        Assert.Contains(daemonOptions.Converters, c => c is PlaceHolderStringStjConverter);
    }

    [Fact]
    public void DaemonClientRpcBoundary_Options_HasSameCoreConvertersAsCommon()
    {
        var daemonClientOptions = DaemonClientRpcJsonBoundary.StjOptions;

        Assert.Contains(daemonClientOptions.Converters, c => c is GuidStjConverter);
        Assert.Contains(daemonClientOptions.Converters, c => c is EncodingStjConverter);
        Assert.Contains(daemonClientOptions.Converters, c => c is PlaceHolderStringStjConverter);
    }

    #endregion

}
#endif
