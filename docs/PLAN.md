# Build Plan

## Context

We want AI agents to work with a Gramps family tree through the Model Context Protocol. Gramps
Web already exposes a capable REST API (v3.19.0, 127 endpoints, JWT auth); what's missing is an
MCP layer in front of it.

Prior art exists — [`cabout-me/gramps-mcp`](https://github.com/cabout-me/gramps-mcp) (Python),
[`Scormave/gramps-web-mcp`](https://github.com/Scormave/gramps-web-mcp) (.NET 8),
[`dsblank/gramps-ez-mcp`](https://github.com/dsblank/gramps-ez-mcp) — but this is an independent
build in .NET, deployed as a Docker container, targeting Gramps Web's existing API. No Gramps
internals, no database access: we are strictly an API client.

The goal is a server that is **fast by default, safe by default, and cheap in context** — the
three places a naive REST-to-MCP wrapper fails.

## Locked decisions

| Decision | Choice | Rationale |
| --- | --- | --- |
| Tool surface | Lean (~15 tools), fast paths first, GQL as documented slow escape hatch | Fewer tools means less context bloat and fewer wrong-tool errors |
| Write scope | Full CRUD implemented, **read-only by default** | A curated family tree is not somewhere to discover an agent's mistakes |
| Auth | Service account in env, server manages JWT refresh | Fits a self-hosted single-tree Docker deployment |
| Runtime | .NET 10 (LTS), MCP C# SDK v2.1.0 | Current LTS; SDK v2 implements the 2026-07-28 spec |
| Transport | Streamable HTTP (primary) + stdio (local/desktop) | Container-first, desktop-capable |
| Licence | MIT | Pure HTTP client of Gramps; not bound by its AGPL |

## Phases

Each phase is independently shippable and ends in a working state.

---

### Phase 0 — Repo & toolchain bootstrap

**Goal:** a repository that builds and tests green on CI with nothing in it yet.

- [x] `git init`, `.gitignore`, `.editorconfig`, MIT `LICENSE`, `README.md`, `.env.example`
- [x] Private GitHub repo, pushed
- [x] Install .NET 10 SDK locally
- [x] `GrampsMcp.sln` with `src/GrampsMcp.Server`, `tests/GrampsMcp.UnitTests`,
      `tests/GrampsMcp.IntegrationTests`
- [x] `Directory.Build.props`: `net10.0`, `Nullable=enable`, `TreatWarningsAsErrors=true`,
      `ImplicitUsings=enable`
- [x] `.github/workflows/ci.yml` — restore, build, test on push and PR

**Done when:** `dotnet build && dotnet test` passes locally and on CI.

---

### Phase 1 — Gramps API client & authentication

**Goal:** a typed, safe client for the Gramps Web API. No MCP yet.

- [x] `GrampsOptions` bound from environment, validated at startup (fail fast on missing URL or
      credentials)
- [x] `GrampsApiClient` as a typed `HttpClient`
- [x] `GrampsAuthHandler` (`DelegatingHandler`): attach bearer token; on 401 refresh via
      `/api/token/refresh/`; if refresh fails, re-authenticate with the service account. Token
      cached in memory, never logged
- [x] `GrampsPermissions` — decode the JWT `permissions` claim to learn our real ceiling
- [x] Central request builder owning the **guardrails**: force `page >= 1`, cap `pagesize`,
      apply default `profile`, pass through `keys`/`skipkeys`, read `x-total-count`
- [x] Error translation: map Gramps 4xx/5xx to clean tool errors, passing GQL 422 messages
      through **verbatim** (they name the exact parse position and let a model self-correct)
- [x] Retry for transient 5xx/network faults, via `Microsoft.Extensions.Http.Resilience`'s
      standard resilience handler (Polly-based) rather than a hand-rolled pipeline
- [x] Unit tests with a mocked `HttpMessageHandler`: refresh-on-401, page coercion, pagesize cap,
      permission parsing

**Done when:** unit tests cover the guardrails and a smoke test authenticates against
`demo.grampsweb.org` (read-only, credentials `owner`/`owner`).

---

### Phase 2 — Read tools (first working MCP server)

**Goal:** an agent can connect and explore the tree.

- [x] `Program.cs`: `AddMcpServer().WithHttpTransport().WithToolsFromAssembly()` + `app.MapMcp()`,
      transport switched by `MCP_TRANSPORT`. Under stdio **all logging goes to stderr** — stdout
      is the protocol channel
- [x] `gramps_search(query, types[], page, pagesize, semantic)` — indexed, ~instant; *the*
      default way to find anything
- [x] `gramps_get(type, id_or_handle, extend[], profile)` — accepts Gramps ID (`I0155`) or handle
- [x] `gramps_list(type, sort, page, pagesize, keys[])`
- [x] `gramps_tree_summary()` — object counts, versions, optional surname list
- [x] `gramps_types()` — default + custom vocabularies, so later writes use valid type strings
- [x] `/health` endpoint verifying Gramps connectivity

**Done when:** MCP Inspector lists the tools with correct schemas and a real client answers
"who is in this tree?" end to end.

---

### Phase 2.5 — Logging & observability

**Goal:** logging is configurable at deploy time — verbosity can be adjusted, or turned off
entirely — without editing `appsettings.json`.

- [x] `MCP_LOG_LEVEL` env var (`None`/`Critical`/`Error`/`Warning`/`Information`/`Debug`/`Trace`),
      read the same way other options are — via `GrampsOptions`-style explicit `IConfiguration`
      pull, validated at startup
- [x] Applied to `builder.Logging` on **both** transports: stdio's currently-hardcoded
      `LogToStandardErrorThreshold = Trace` (`Program.cs:21`) becomes driven by this setting, and
      the HTTP path (which today doesn't touch `builder.Logging` at all) gets the same override
- [x] `MCP_LOG_LEVEL=None` disables logging entirely (maps to `LogLevel.None`)
- [x] Default preserves current behaviour if unset (stdio: `Trace` to stderr; HTTP: whatever
      `appsettings.json` says) so this is additive, not a breaking change
- [x] README `## Configuration` table and `.env.example` gain an `MCP_LOG_LEVEL` row/entry
- [x] Unit test covering the new option's validation (valid level names accepted, invalid
      rejected) — first such test if no `GrampsOptionsTests.cs` exists yet

**Done when:** setting `MCP_LOG_LEVEL=None` produces no log output on either transport, and
setting it to `Debug`/`Trace` visibly increases verbosity beyond the current default.

---

### Phase 3 — Graph tools

**Goal:** the genealogy-specific questions a filter cannot express.

- [x] `gramps_relation(person_a, person_b)` — `/api/relations/{a}/{b}` kinship calculation
- [x] `gramps_timeline(type, id_or_handle, ...)` — person and family timelines
- [x] Both accept Gramps IDs and resolve to handles internally

**Done when:** "how are X and Y related?" returns a correct kinship string against the demo tree.

---

### Phase 4 — GQL tools

**Goal:** the advanced escape hatch, correctly framed so the model reaches for it last.

- [x] `gramps_query(type, gql, keys[], page, pagesize)`
- [x] `gramps_count(type, gql)` — `pagesize=1`, returns only `x-total-count`
- [x] Tool descriptions state plainly: **slow (10–15s), single-type only, no sorting or
      aggregation; prefer `gramps_search`**
- [x] Description documents the quoting rule — values containing spaces must be quoted, and real
      surnames look like `(6th Earl of) SUTHERLAND`
- [x] 422 parse errors returned verbatim for self-correction

**Done when:** a complex query (`child_ref_list.length >= 3`) returns correct rows, and a
deliberately malformed query returns a usable error.

---

### Phase 5 — Writes (gated)

**Goal:** full CRUD that stays invisible until explicitly switched on.

- [x] Write tools registered **only** when `GRAMPS_ALLOW_WRITES=true` *and* the JWT permission
      claim actually grants the right (not registered = not planned around by the model)
- [x] `gramps_create(type, payload)`
- [x] `gramps_update(type, handle, patch)` — read-modify-write: GET the full object, apply the
      merge patch, `PUT` it back. Required, because `PUT` **replaces** the whole object
- [x] `gramps_delete(type, handle)`
- [x] `gramps_merge(type, keep_handle, discard_handle)`
- [x] `gramps_history()` / `gramps_undo(transaction_id)` — `/api/transactions/history/{id}/undo`
- [x] Startup warning if writes are enabled but the account lacks `AddObject`/`EditObject`

**Testing constraint:** write paths are exercised **only** against a local Gramps Web container.
Never write to the public demo instance.

**Done when:** create → update → verify → undo round-trips against a local container.

---

### Phase 6 — Docker & packaging

**Goal:** `docker compose up` gives a working MCP server.

- [x] Multi-stage `Dockerfile`: `mcr.microsoft.com/dotnet/sdk:10.0-alpine` →
      `mcr.microsoft.com/dotnet/aspnet:10.0-alpine`, non-root user
- [x] `HEALTHCHECK` against `/health`
- [x] `docker-compose.yml` (server only) and `docker-compose.dev.yml` (server + a local Gramps
      Web instance to test writes against)
- [x] README: setup, full tool reference, client configuration examples

**Done when:** a fresh machine can go from clone to a connected agent using only the README.

---

### Phase 7 — Publish

**Goal:** a versioned, multi-arch image anyone can pull without building it themselves.

Only starts once Phase 6's image has been run and verified locally (built, healthcheck passing,
smoke-tested against a real Gramps Web instance) — publishing is a one-way action visible to
anyone with pull access, not a step to rush ahead of local verification.

- [ ] `docker-publish` CI job: `docker/setup-buildx-action` + `docker/build-push-action`, targets
      `linux/amd64,linux/arm64`
- [ ] Push to `ghcr.io/<owner>/gramps-mcp-dotnet`, tagged with both the git ref (`sha-<short>`,
      branch/tag name) and `latest` on `main`
- [ ] Gated behind an on/off repo variable (matching the existing `CLAUDE_REVIEW_ENABLED`
      pattern) so nothing publishes until explicitly enabled
- [ ] README: pull-and-run instructions using the published image, replacing the "build locally"
      note from Phase 6

**Done when:** `docker pull ghcr.io/<owner>/gramps-mcp-dotnet:latest && docker run ...` works on a
fresh machine with no local build step.

---

## Guardrails (cross-cutting)

These are the substance of the project, not decoration. Every phase upholds them:

- **Forced pagination** — `page < 1` is coerced, `pagesize` capped. Every list result reports
  `total` and `page` so the model iterates deliberately.
- **Read-only by default** — write tools absent, not merely erroring.
- **Permission ceiling** — verified at boot from the JWT, failing loudly rather than mid-task.
- **Response trimming** — `profile` + curated `keys`; no `lds_ord_list`-class noise by default.
- **Media safety** — metadata only; no binary into context. Any file access is separate,
  size-limited, MIME-allowlisted and off by default.
- **Private records** — respect `ViewPrivate`; exclude private records unless configured.
- **Secrets** — environment only; never logged, never echoed in tool output or errors.

## Verification strategy

| Layer | Approach |
| --- | --- |
| Unit | Guardrail logic — page coercion, pagesize cap, conditional tool registration, JWT parsing, refresh-on-401 |
| Integration (read) | Against `demo.grampsweb.org` — safe, read-only |
| Integration (write) | Against a local Gramps Web container only |
| Protocol | MCP Inspector — tool list, schemas, descriptions |
| End-to-end | A real client running a genuine research task, e.g. "find everyone with surname SUTHERLAND, then show how the earliest two are related" — confirming the model picks `gramps_search` over `gramps_query` and responses stay context-cheap |
| Performance | Search/list stay ~2s; assert no tool can return an unpaginated collection |

---

## Appendix — verified Gramps Web API facts

All measured live against `demo.grampsweb.org` (API 3.19.0, 4,663-person tree). These are
measurements, not assumptions, and they are why the design looks the way it does.

1. **`page=0` is the default and means _unpaginated_.** `pagesize=2` alone returned all 4,663
   people; `page=1&pagesize=2` returned 2. Forcing `page >= 1` is the single most important
   guardrail.
2. **GQL costs 10–15s per call regardless of result size** (7 rows → 15.0s; count-only → 10.0s)
   versus **2.0s** for an ordinary paginated fetch. Cause is upstream: `gramps-ql` scans the whole
   database in Python. Not fixable from .NET.
3. **GQL is accepted on exactly 10 endpoints** — `people`, `families`, `events`, `places`,
   `sources`, `citations`, `repositories`, `media`, `notes`, `tags`.
4. **GQL cannot cross types.** `class = family` sent to `/api/people/` returns 0 rows, so type
   must be an explicit tool argument.
5. **GQL has no sort, no aggregation, no joins, no native date comparison, and no writes.**
6. **GQL values containing spaces must be quoted** or the query fails to parse.
7. **GQL parse errors are precise:** HTTP 422,
   `Expected end of text, found '~' (at char 31), (line:1, col:32)` — worth surfacing verbatim.
8. **`x-total-count` is on every list response** → counts cost one request with `pagesize=1`.
9. **`profile=self,families,events` resolves handles into readable text** (`"about 1289"`, place
   names, event summaries). Raw person = 1,605 bytes of opaque handles; profiled = 3,554 bytes but
   self-contained, needing no follow-up calls.
10. **`/api/search/` is indexed and returns instantly**, supports `type[]` filtering and
    `semantic=true`. This is the default lookup path, not GQL.
11. **The JWT carries a `permissions` claim** — `AddObject`, `EditObject`, `DeleteObject`,
    `ViewPrivate`, `BatchDeleteObjects`, etc.
12. **Writes take the full object.** `PUT /api/{type}/{handle}` replaces. `/api/transactions/` is
    a *raw* endpoint requiring both `old` and `new` full object state — not a patch API.
13. **Undo exists**: `/api/transactions/history/` and `POST /api/transactions/history/{id}/undo`.

### Reference

- OpenAPI spec is served live by any instance: `GET /api/openapi.json`
- GQL reference: https://github.com/DavidMStraub/gramps-ql
- MCP C# SDK: https://github.com/modelcontextprotocol/csharp-sdk
