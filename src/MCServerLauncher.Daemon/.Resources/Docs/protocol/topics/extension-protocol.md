# Extension Protocol v1

MCSL Extension Protocol is the compatibility boundary between daemon-side extension
business logic and client-side renderers. Web and WPF clients consume the same protocol
values and must not receive C# runtime type names, object references, daemon tokens, raw
WebSocket handles, or raw RPC method strings.

Runtime packages load UI Schema and resources from local package cache. The WebSocket
runtime channel carries state patches, events, command requests/responses, and structured
errors. Client-to-daemon extension requests are sent through the built-in JSON-RPC method
`mcsl.extension.dispatch`; its `params.envelope` value is one Extension Protocol envelope.
The daemon validates the envelope, maps `request.command` to the plugin RPC method
`plugin.<plugin>.rpc.<command>`, applies the target RPC permission and source-generated
DTO metadata, then returns an Extension Protocol envelope in `result.envelope`.

## Envelope Rules

Every envelope contains `protocol: "mcsl.extension.v1"` and a `type` discriminator.
The first supported envelope set is:

- `state.patch`: partial UI state update for one extension.
- `state.snapshot`: full UI state snapshot for resync.
- `event`: typed extension event with versioned data and optional metadata.
- `request`: command request from client extension UI to daemon extension logic.
- `response`: command result or structured error.
- `error`: structured protocol error. It may carry `id` when it correlates to a request.

`state.patch` carries a partial monotonic update and clients apply it only when the
revision is greater than their current snapshot revision. `state.snapshot` carries a
full authoritative replacement and clients apply it when the revision is not older than
the current snapshot. Stale state envelopes are ignored and must not mutate local UI
state.

## UI Command Binding

Client UI Schema event props such as `OnClick` and `OnChanged` support two safe forms:
legacy handler names for local script/runtime callbacks, or command binding objects for
daemon-backed extension logic. Command bindings are normalized into Extension Protocol
`request` envelopes and dispatched through `mcsl.extension.dispatch`; renderers do not
construct raw plugin RPC method names.

```json
{
  "type": "Button",
  "props": {
    "Text": "Refresh",
    "OnClick": {
      "Command": "refresh",
      "Params": {
        "mode": "{state.mode}",
        "selected": "{event.value}"
      }
    }
  }
}
```

Command names must be canonical identifiers with optional dot-separated segments.
`Params` must be a JSON object; it may use `{state.path}`, allowed formatters, and
`{event.value}`. Unsafe keys `__proto__`, `prototype`, and `constructor` are rejected at
every depth before a request envelope is created.

## Command Discovery

Runtime `.mpx` manifests and daemon `mcsl-plugin.json` manifests declare daemon-backed UI
commands in `commands`. Each command has a canonical `id`, optional `title` and
`description`, and `target: "daemon"`. A package that declares commands must also include
a daemon plugin payload and the formal `command.daemon` extension point declaration. A C#
daemon plugin that declares commands must also declare feature `rpc.register` and
extension point `command.daemon`. Web and WPF validators reject UI command bindings that
are not listed in `manifest.commands`, and daemon dispatch rejects requests for commands
missing from the loaded plugin manifest, so renderer-visible controls cannot silently
invent undeclared daemon calls.

```json
{
  "extensionPoints": [
    { "kind": "command", "id": "command.daemon", "target": "daemon" }
  ],
  "commands": [
    {
      "id": "refresh",
      "title": "Refresh",
      "description": "Refreshes panel state.",
      "target": "daemon"
    }
  ]
}
```

## Daemon Push Channel

Daemon C# plugins publish client-visible Extension Protocol updates through the existing
plugin event namespace using `plugin.<plugin>.event.extension`. The event data is an
`ExtensionProtocolDispatchResult` object whose `envelope` field contains a complete
`mcsl.extension.v1` envelope. Runtime package validators only allow this event permission
when the `<plugin>` segment exactly matches the package id, and require the package to
include a daemon plugin payload plus the formal `event.daemon` extension point. Clients
ignore envelopes whose `plugin` field does not match the source event.

Built-in daemon events remain separate host events. Product surfaces may wrap allowed
built-in daemon events such as `mcsl.event.notification` into Extension Protocol `event`
envelopes for installed entries that explicitly declared the matching `daemon.*` event
permission, but state synchronization from daemon business logic should use the
plugin-owned extension event channel.

## Daemon Bundle Deployment

Runtime `.mpx` packages that include daemon business logic carry a complete daemon plugin
bundle zip, not a standalone DLL. The bundle must contain `mcsl-plugin.json`, the entry
assembly, and private dependencies. Clients first cache and review the package, then upload
the daemon bundle to the daemon upload staging area. The daemon-only RPC
`mcsl.extension.daemon.bundle.install` validates the staged zip hash, rejects zip-slip
entries, verifies the manifest plugin id, validates the plugin bundle, installs it under
`plugins/<plugin-id>/`, enables the plugin entry in `config.json`, and returns
`restart_required: true`. `mcsl.extension.daemon.bundle.status` reports the persisted
bundle state for one plugin id (`not_installed`, `installed_enabled`,
`installed_disabled`, `installed_unconfigured`, `configured_missing`, or
`disabled_missing`) from the plugin directory plus `config.json`, without claiming runtime
hot-load state. `mcsl.extension.daemon.bundle.remove` disables and removes an
installed bundle and also returns `restart_required: true`; active plugin code is unloaded
only by daemon restart.

## Installed Client Runtime

Web and WPF clients render installed Client Extensions from validated cache entries, not
from ad hoc manifest payloads. The installed entry stores the normalized UI schema,
manifest command declarations, host/event permissions, file digests, and client resource
metadata. WPF cache entries persist extracted payload paths and reject restored entries
when cached files are missing or hash-mismatched. Web cache entries persist payload bytes
through a dedicated payload store, use IndexedDB in browser runtimes, and reject restored
entries when stored payload bytes fail digest verification. Web runtime surfaces resolve
cached payloads to safe `blob:` URLs only after digest verification.

Client renderers validate command events against the installed entry's declared daemon
commands before calling `mcsl.extension.dispatch`. A restored or previewed UI cannot
invoke an undeclared daemon command even when the renderer emits a command-shaped event.
Installed-entry runtime helpers also gate state envelopes by plugin id and apply
`state.patch` / `state.snapshot` through the same monotonic revision rules on Web and
WPF. Extension `event` envelopes are dispatched only when the installed entry declares
the event permission, and foreign-plugin events are ignored before callbacks run.

Client-only themes use `mcsl.theme.v1` runtime JSON. Builders compile authoring theme
JSON into normalized runtime tokens; validators reject unsupported color/spacing tokens;
Web maps tokens to CSS variables and WPF maps tokens to a `ResourceDictionary`.

## Type Mapping

| C# value | Protocol value |
| --- | --- |
| `DateTime` / `DateTimeOffset` | ISO 8601 string |
| `Guid` | string |
| enum | string |
| `byte[]` | base64 string or binary frame when explicitly negotiated |
| `Exception` | structured `error` envelope |
| nullable | `null` |
| custom object | JSON object |

## Example Patch

```json
{
  "protocol": "mcsl.extension.v1",
  "type": "state.patch",
  "plugin": "community.example.status-panel",
  "revision": 42,
  "patch": {
    "cpu": 37.5,
    "memory": 8192
  }
}
```

## Safety

State object keys `__proto__`, `prototype`, and `constructor` are forbidden at every
object depth. Clients and daemon bridges reject those payloads before mutation.
