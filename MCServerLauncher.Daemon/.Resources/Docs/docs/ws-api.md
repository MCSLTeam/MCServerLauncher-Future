# WebSocket API

The daemon exposes HTTP helper endpoints and a WebSocket action/event protocol on the same daemon port.

The Apifox project export is available at:

```text
http://127.0.0.1:<port>/apifox.json
```

Import the Apifox export if you want native WebSocket entries. Select the `local-daemon` environment, then edit the token in Apifox environment management:

```text
Environment management -> Global parameters -> Query -> token
```

Use your daemon MainToken or a JWT sub-token as the `token` value. Do not put `token` in the WebSocket URL.

```text
WebSocket base URL: ws://127.0.0.1:<port>/api/v1
Project common query parameter: token={{token}}
```

For a default local daemon, Apifox stores the base URL in the environment and injects `token` through the project common query parameters:

```text
ws://127.0.0.1:11452/api/v1
```

## HTTP helpers

The standard HTTP endpoints are:

- `GET /`
- `GET /info`
- `POST /subtoken`
- `GET /apifox.json`

Daemon actions are not HTTP endpoints. There is no `GET /openapi.json` and no `POST /api/v1/actions/*` bridge.

## Action request

Send action requests as WebSocket text messages:

```json
{
  "action": "ping",
  "params": {},
  "id": "{{$string.uuid}}"
}
```

`action` is a snake_case action name. `params` follows the examples in `apifox.json` and the protocol markdown topics. `id` is a client-generated UUID used to match the response; the Apifox export uses `{{$string.uuid}}` so debug requests generate a fresh UUID.

## Action response

```json
{
  "status": "ok",
  "retcode": 0,
  "data": {
    "time": 1770000000000
  },
  "message": "OK",
  "id": "33333333-3333-3333-3333-333333333333"
}
```

Successful responses use `status: "ok"` and `retcode: 0`. Failed responses use `status: "error"` and a non-zero `retcode`.

## Events

Subscribed events are sent by the daemon as WebSocket text messages:

```json
{
  "event": "instance_log",
  "meta": {
    "instance_id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
  },
  "data": {
    "log": "server output"
  },
  "time": 1770000000000
}
```

The current event names are `instance_log` and `daemon_report`. Their meta/data shapes are documented in the protocol markdown topics and Apifox schemas.

## File transfer note

The JSON action `file_upload_chunk` is still documented for compatibility. The current daemon also accepts binary upload frames:

```text
[16 bytes file_id][8 bytes offset][20 bytes SHA1(data)][data bytes]
```

Download ranges use `file_download_request`, `file_download_range`, and `file_download_close`.
