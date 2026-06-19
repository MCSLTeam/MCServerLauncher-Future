# Migrate To Apifox Skill Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create a personal agent skill for migrating API documentation from project source code, OpenAPI, Postman, or Swagger into Apifox project JSON.

**Architecture:** The skill lives under `/Users/lxhtt/.agents/skills/migrate-to-apifox`. `SKILL.md` gives the workflow and trigger rules, `APIFOX_FORMAT.md` records the observed Apifox project shape from `/Users/lxhtt/Downloads/schema-test.apifox.json`, and `scripts/validate-apifox.js` performs deterministic structure checks.

**Tech Stack:** Markdown skill instructions, Apifox project JSON, Node.js validation script using only built-in modules.

---

### Task 1: Skill instructions and format reference

**Files:**
- Create: `/Users/lxhtt/.agents/skills/migrate-to-apifox/SKILL.md`
- Create: `/Users/lxhtt/.agents/skills/migrate-to-apifox/APIFOX_FORMAT.md`
- Create: `/Users/lxhtt/.agents/skills/migrate-to-apifox/scripts/validate-apifox.js`

- [x] **Step 1: Inspect Apifox sample**

Run:

```bash
python3 -m json.tool /Users/lxhtt/Downloads/schema-test.apifox.json >/tmp/schema-test.pretty.json
```

Expected: command exits 0 and reveals root collections such as `apiCollection`, `webSocketCollection`, and `schemaCollection`.

- [x] **Step 2: Write `SKILL.md`**

Include triggers for Apifox migration, source-code extraction, OpenAPI, Swagger, and Postman conversion. Keep the file under 100 lines and link to `APIFOX_FORMAT.md`.

- [x] **Step 3: Write `APIFOX_FORMAT.md`**

Document the project root shape, HTTP API item shape, WebSocket API item shape, schema item shape, and migration rules that prevent WebSocket APIs from being represented as HTTP `GET`.

- [x] **Step 4: Write validation script**

Create `scripts/validate-apifox.js` with checks for:

- root `$schema.app === "apifox"`
- at least one of `apiCollection`, `webSocketCollection`, or `schemaCollection`
- HTTP APIs have lowercase `method`; `api.type` may be omitted in Apifox samples and defaults to HTTP
- webhook items under `apiCollection` are accepted with `api.type === "webhook"`
- WebSocket APIs have no `method`; `api.type` may be omitted in Apifox samples or set to `websocket`
- schema items carry `schema.jsonSchema`

- [x] **Step 5: Validate the skill artifacts**

Run:

```bash
node /Users/lxhtt/.agents/skills/migrate-to-apifox/scripts/validate-apifox.js /Users/lxhtt/Downloads/schema-test.apifox.json
node /Users/lxhtt/.agents/skills/migrate-to-apifox/scripts/validate-apifox.js MCServerLauncher.Daemon/.Resources/Docs/apifox.json
python3 - <<'PY'
from pathlib import Path
p = Path('/Users/lxhtt/.agents/skills/migrate-to-apifox/SKILL.md')
print(len(p.read_text().splitlines()))
PY
git diff --check
```

Expected: validation commands exit 0, `SKILL.md` stays below 100 lines, and `git diff --check` exits 0.

## Changelog

- Created this plan for the `migrate-to-apifox` personal skill.
- Added `migrate-to-apifox` with a concise `SKILL.md`, an Apifox format reference, and a Node.js validation script.
- Validated the script against `/Users/lxhtt/Downloads/schema-test.apifox.json` and `MCServerLauncher.Daemon/.Resources/Docs/apifox.json`.
- Expanded the skill from a narrow HTTP/WebSocket/schema migration helper into a full Apifox project migration skill covering raw socket services, Socket.IO, MCP, custom endpoint protocols, reusable components, auth, tests, and environments.
- Added strict native validation after Apifox imported a generated daemon project with zero recognized APIs. The fix aligns WebSocket items with Apifox's native export shape by omitting `api.type`, while still forbidding `api.method`, and checks HTTP API metadata required by native project imports.
- Updated the daemon Apifox export to match `/Users/lxhtt/Downloads/pingws.json`: WebSocket APIs are nested under `根目录 -> WebSocket actions`, payloads are stored in `api.requestBody.message`, and `api.parameters` carries query/path/cookie/header buckets.
- Updated the daemon Apifox export and skill rules so WebSocket action `api.path` no longer embeds `{{wsUrl}}?token={{token}}`; the environment owns the WebSocket base URL and `token` is documented in `api.parameters.query`.
- Updated dynamic value and native-variable rules: WebSocket request IDs use Apifox dynamic values such as `{{$string.uuid}}`, environment/global variables use native `name` fields, and shared daemon token auth lives in root `commonParameters.parameters.query`.
- Updated daemon Apifox guidance so the project homepage explains how to obtain MainToken, `globalVariables` no longer duplicates `baseUrl` or `wsUrl`, environment variables no longer carry `token`, `permissions`, or `expires`, and users are directed to edit environment management -> global parameters -> Query -> `token`.
