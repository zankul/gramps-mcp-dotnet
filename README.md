# gramps-mcp-dotnet

An MCP (Model Context Protocol) server that gives AI agents structured access to a
[Gramps Web](https://www.grampsweb.org/) genealogy database. Built with .NET 10 and shipped as a
Docker container.

It is a pure client of the existing [Gramps Web API](https://gramps-project.github.io/gramps-web-api/) —
no Gramps internals, no direct database access.

> **Status: early development.** See [docs/PLAN.md](docs/PLAN.md) for the phased build plan.

## Design goals

The hard part of wrapping a 127-endpoint REST API in MCP isn't coverage — it's being **fast,
safe, and cheap in context**. Three measured facts from the Gramps Web API drive the design:

- **`page=0` is the API default and means _unpaginated_.** A naive list call returns the entire
  collection. This server forces `page >= 1` and caps `pagesize`, so no single tool call can
  flood the model's context.
- **GQL (Gramps Query Language) costs 10–15s per call regardless of result size**, because it
  scans the whole database upstream. The indexed `/api/search/` endpoint answers in ~2s. So
  search is the default path and GQL is an explicitly-labelled advanced escape hatch.
- **`profile=self,families,events` resolves opaque handles into readable text** (dates, place
  names, event summaries). That's the default response shape — the model gets usable answers
  without chasing follow-up calls.

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
- **Undoable.** Every write is a transaction Gramps Web tracks; `gramps_history` lists them and
  `gramps_undo` reverses one.
- **Credentials from environment only** — never logged, never echoed into tool output.
- **No binary media in context** — media tools return metadata, not file contents.

## Configuration

| Variable | Default | Purpose |
| --- | --- | --- |
| `GRAMPS_API_URL` | _(required)_ | Base URL of your Gramps Web instance |
| `GRAMPS_USERNAME` | _(required)_ | Service account username |
| `GRAMPS_PASSWORD` | _(required)_ | Service account password |
| `GRAMPS_ALLOW_WRITES` | `false` | Master switch for all mutating tools |
| `MCP_TRANSPORT` | `http` | `http` (container) or `stdio` (local/desktop) |
| `MCP_API_KEY` | _(unset)_ | If set, required on every HTTP request |
| `MCP_LOG_LEVEL` | _(unset)_ | `None`/`Critical`/`Error`/`Warning`/`Information`/`Debug`/`Trace`. Unset preserves current per-transport defaults |
| `GRAMPS_MAX_PAGESIZE` | `100` | Hard cap on page size |
| `GRAMPS_DEFAULT_PROFILE` | `self,families,events` | Default readable response shape |

Copy `.env.example` to `.env` to get started.

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
disposable local tree. See the comments at the top of that file for the one-time owner-account
setup. **Never** enable writes against `demo.grampsweb.org` or any other shared instance.

A pre-built multi-arch image (`linux/amd64`, `linux/arm64`) is published to GHCR on every push to
`main` and on version tags. It's currently private, so pulling it requires a GitHub PAT with
`read:packages` scope:

```bash
echo $GITHUB_PAT | docker login ghcr.io -u <your-github-username> --password-stdin
docker pull ghcr.io/zankul/gramps-mcp-dotnet:latest   # newest release
# or: :edge for the latest main commit, :sha-<short> for a specific build
docker run --rm -p 8080:8080 --env-file .env ghcr.io/zankul/gramps-mcp-dotnet:latest
```

To build locally instead, `docker build -t gramps-mcp .`.

## Connecting a client

**Streamable HTTP** (the default transport, `MCP_TRANSPORT=http`) — point your MCP client at
`http://<host>:8080/`. If `MCP_API_KEY` is set, the client must send it (consult your client's
docs for how it passes custom headers/auth for HTTP MCP servers).

**stdio** (local desktop clients such as Claude Desktop) — run the server with
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
