# gramps-mcp-dotnet

An MCP (Model Context Protocol) server that gives AI agents structured access to a
[Gramps Web](https://www.grampsweb.org/) genealogy database. Built with .NET 10 and shipped as a
Docker container.

It is a pure client of the existing [Gramps Web API](https://gramps-project.github.io/gramps-web-api/) —
no Gramps internals, no direct database access.

See [docs/PLAN.md](docs/PLAN.md) for the phased build plan (all phases complete) and the
appendix of measured Gramps Web API facts the design is built on.

## Design goals

The hard part of wrapping a 127-endpoint REST API in MCP isn't coverage — it's being **fast,
safe, and cheap in context**. Three measured facts from the Gramps Web API drive the design:

- **An omitted `page` returns the whole collection, unpaginated.** A naive list call floods the
  model's context. (An *explicit* `page=0` is 422-rejected, not treated as "unpaginated" — this
  server always sends an explicit `page`, forced to `>= 1`, and caps `pagesize`, so it never hits
  either quirk.)
- **GQL (Gramps Query Language) costs 10–15s per call regardless of result size**, because it
  scans the whole database upstream. The indexed `/api/search/` endpoint answers in ~2s. So
  search is the default path and GQL is an explicitly-labelled advanced escape hatch.
- **`profile=self,families,events` resolves opaque handles into readable text** (dates, place
  names, event summaries). That's the default response shape — the model gets usable answers
  without chasing follow-up calls. The default is trimmed per object type where Gramps Web
  rejects part of it (events only accept `self`), so it never 422s a plain call.

## Safety posture

- **Read-only by default.** Write tools (`gramps_create`, `gramps_update`, `gramps_delete`,
  `gramps_merge`, `gramps_history`, `gramps_undo`) are not registered at all unless
  `GRAMPS_ALLOW_WRITES=true`.
- **Permission-aware.** The Gramps JWT carries a `permissions` claim; the server reads its own
  real ceiling at startup and registers each write tool only if the service account actually holds
  the permission it needs (e.g. `gramps_delete` needs `DeleteObject`) — an unregistered tool can't
  be discovered or invoked at all, not merely refused at call time.
- **Updates are read-modify-write.** Gramps Web's `PUT` replaces the whole object, so
  `gramps_update` fetches the current object, applies your patch as a JSON Merge Patch (RFC 7396),
  and writes the merged result back.
- **Writes are serialized per server instance.** Overlapping write calls queue inside the server
  (reads stay concurrent), and `gramps_update` holds the queue across its read and write so two
  parallel updates can't lose one patch. This protects a SQLite-backed Gramps Web, where
  concurrent writes can fail with `database is locked` and leave it wedged until restart. It does
  **not** cover two server instances (e.g. two MCP clients each spawning a stdio process) writing
  to the same Gramps Web at once. If you need that, run a single shared HTTP instance, or fix it
  on the Gramps Web side with a single gunicorn worker or a PostgreSQL backend.
- **Undoable.** Every write is a transaction Gramps Web tracks; `gramps_history` lists them and
  `gramps_undo` reverses one.
- **Credentials from environment only** — never logged, never echoed into tool output.
- **Fails closed on a broken permission check.** If checking the service account's JWT
  permissions at startup errors (a transient Gramps outage, an unparseable token), the server
  doesn't crash — it silently falls back to read-only for that run rather than guessing which
  write tools are safe to register. A `Warning`-level log line
  (`GrampsToolTypeResolver`/`Log.WritePermissionCheckFailed`) is the only signal; if you enabled
  `GRAMPS_ALLOW_WRITES` and don't see write tools registered, check the logs at `Warning` level
  before assuming they're misconfigured.

## Configuration

| Variable | Default | Purpose |
| --- | --- | --- |
| `GRAMPS_API_URL` | _(required)_ | Base URL of your Gramps Web instance |
| `GRAMPS_USERNAME` | _(required)_ | Service account username |
| `GRAMPS_PASSWORD` | _(required)_ | Service account password |
| `GRAMPS_ALLOW_WRITES` | `false` | Master switch for all mutating tools |
| `MCP_TRANSPORT` | `http` | `http` (shared long-running server, several local clients) or `stdio` (strictly 1:1 with a single client) — see [Connecting a client](#connecting-a-client) |
| `MCP_ALLOWED_ORIGINS` | `http://localhost:8080,http://127.0.0.1:8080` | Comma-separated Origin allowlist for the `http` transport (DNS-rebinding defense — see [SECURITY.md](SECURITY.md)) |
| `MCP_LOG_LEVEL` | _(unset)_ | `None`/`Critical`/`Error`/`Warning`/`Information`/`Debug`/`Trace`. Unset preserves current per-transport defaults |
| `GRAMPS_MAX_PAGESIZE` | `100` | Hard cap on page size |
| `GRAMPS_SEARCH_DEFAULT_PAGESIZE` | `20` | Default `pagesize` for `gramps_search` when omitted — kept well below `GRAMPS_MAX_PAGESIZE` since each hit carries its full raw object |
| `GRAMPS_DEFAULT_PROFILE` | `self,families,events` | Default readable response shape |

Copy `.env.example` to `.env` to get started.

`gramps_search`'s `semantic` option is a Gramps Web server feature, not an MCP server setting —
there's no env var above for it. It requires `VECTOR_EMBEDDING_MODEL` to be set on the **Gramps
Web** instance itself; neither this repo's `docker-compose.yml` nor `docker-compose.test.yml` sets
it, so semantic search 503s by default. See the
[Gramps Web docs](https://www.grampsweb.org/) for configuring an embedding model if you want it.

## Running with Docker

```bash
cp .env.example .env   # fill in GRAMPS_API_URL / GRAMPS_USERNAME / GRAMPS_PASSWORD
docker compose up --build
```

This builds the multi-stage `Dockerfile` (SDK to publish, `aspnet:10.0-alpine` to run, non-root
user) and starts the server on `http://localhost:8080`, with `/health` wired up as the container
healthcheck.

To exercise write tools locally, use `docker-compose.dev.yml` instead — it also starts a local
Gramps Web instance (+ Redis/Celery) so `GRAMPS_ALLOW_WRITES=true` never points at anything but a
disposable local tree.

```bash
cp .env.dev.example .env.dev   # gitignored; points gramps-mcp at the local grampsweb container
docker compose -f docker-compose.dev.yml up
```

Owner account setup (`owner`/`owner`) is automatic. **Never** enable writes against
`demo.grampsweb.org` or any other shared instance.

Set `GRAMPS_SEED_EXAMPLE_DATA=true` to have the dev stack import the `example.gramps` demo
dataset bundled inside the `grampsweb` image (2000+ people, families, and events) instead of
starting from an empty tree — useful for exercising `gramps_search`/`gramps_relation`/
`gramps_timeline` against something realistic.

A separate, always-seeded, disposable stack (`docker-compose.test.yml`) backs the automated live
test tier — see [docs/testing.md](docs/testing.md).

A pre-built multi-arch image (`linux/amd64`, `linux/arm64`) is published to GHCR on every push to
`main` and on version tags. It's public, so no login is needed:

```bash
docker pull ghcr.io/zankul/gramps-mcp-dotnet:latest   # newest release
# or: :edge for the latest main commit, :X.Y.Z / :X.Y / :X for a specific released version
docker run --rm -p 127.0.0.1:8080:8080 --env-file .env ghcr.io/zankul/gramps-mcp-dotnet:latest
```

To build locally instead, `docker build -t gramps-mcp .`.

## Connecting a client

**Streamable HTTP** (the default transport, `MCP_TRANSPORT=http`) is a **shared, long-running
server**: one process, one token store, several local clients can point at it at once, and it
exposes `/health` for running under Docker Compose or Portainer. It is **unauthenticated by
design** and bound to loopback only (`docker-compose.yml` publishes `127.0.0.1:8080:8080`); Origin
validation (`MCP_ALLOWED_ORIGINS`) is the actual defense against DNS rebinding, not a substitute
for auth. Reaching it from another machine requires putting an authenticating reverse proxy in
front of it — never expose it directly. See [SECURITY.md](SECURITY.md) for the full posture.

**stdio** is **strictly 1:1**: each client spawns its own server process and gets its own token
store, and there is no network surface at all — none of the loopback/Origin considerations above
apply. Prefer it for a single desktop client (e.g. Claude Desktop). Run the server with
`MCP_TRANSPORT=stdio` and point the client at the process directly, e.g. for a `dotnet run`
checkout:

```json
{
  "mcpServers": {
    "gramps": {
      "command": "dotnet",
      "args": ["run", "--project", "/path/to/gramps-mcp-dotnet/src/GrampsMcp.Server"],
      "env": {
        "MCP_TRANSPORT": "stdio",
        "GRAMPS_API_URL": "https://your-tree.example.com",
        "GRAMPS_USERNAME": "your-service-account",
        "GRAMPS_PASSWORD": "your-password"
      }
    }
  }
}
```

Or against a built container image with `docker run -i --rm -e MCP_TRANSPORT=stdio -e ... gramps-mcp`
in place of the `dotnet run` command, if your client supports launching a container directly.

## Tool reference

Read tools are always registered. Write tools (marked **WRITE**) are only registered when
`GRAMPS_ALLOW_WRITES=true` *and* the service account's JWT permissions actually grant the
underlying Gramps operation.

| Tool | Purpose |
| --- | --- |
| `gramps_search` | Indexed full-text search across the tree (~2s). The default way to find people, places, events, etc. |
| `gramps_get` | Fetch a single object by type and Gramps ID or handle. |
| `gramps_list` | Paginated, sorted listing of objects by type — for a deliberate browse rather than a lookup. |
| `gramps_tree_summary` | Object counts per type, software/schema versions, optional surname list. |
| `gramps_types` | Default and custom type vocabularies, so callers know which type strings are valid. |
| `gramps_relation` | Kinship relationship between two people (e.g. "first cousin once removed"). |
| `gramps_timeline` | Chronological event timeline for a person or family. |
| `gramps_query` | GQL escape hatch for filters `gramps_search`/`gramps_list` can't express. Slow (10–15s), one type per call, no sorting/aggregation/joins — try `gramps_search` first. |
| `gramps_count` | Count objects matching a GQL expression without returning rows. Same cost/constraints as `gramps_query`. |
| `gramps_create` **WRITE** | Create a new object from a full payload matching the Gramps schema. |
| `gramps_update` **WRITE** | Read-modify-write update via a JSON Merge Patch (RFC 7396). |
| `gramps_delete` **WRITE** | Permanently delete an object. Destructive; recoverable via `gramps_history` + `gramps_undo` until further writes happen. |
| `gramps_merge` **WRITE** | Merge two objects of the same type; the "titanic" is absorbed into the "phoenix" and deleted. |
| `gramps_history` **WRITE** | List committed write transactions. |
| `gramps_undo` **WRITE** | Reverse a previously committed transaction by ID. |

## Licence

MIT — see [LICENSE](LICENSE). This project only speaks to the Gramps Web API over HTTP and
links none of its code, so it is not bound by Gramps' AGPL licence.
