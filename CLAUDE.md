# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

An MCP (Model Context Protocol) server, in .NET 10, that gives AI agents structured access to a
[Gramps Web](https://www.grampsweb.org/) genealogy database. It is a pure HTTP client of the
existing Gramps Web REST API (127 endpoints) — no Gramps internals, no direct database access.
See `docs/PLAN.md` for the full phased build plan (Phases 0–7, all complete), locked design
decisions, and a set of measured facts about the upstream API that motivate the guardrails below.
`README.md` covers configuration and design goals for end users.

The project's whole reason to exist is being **fast, safe, and cheap in context** — a naive
REST-to-MCP wrapper around Gramps Web fails on all three. Any change touching request building,
tool registration, or response shaping should be checked against the guardrails section below.

## Commands

```bash
dotnet build                                     # build
dotnet format --verify-no-changes                # CI's format gate — run before pushing
dotnet test                                      # fast offline tests only (see below)
dotnet test --filter "FullyQualifiedName~GrampsQueryBuilderTests"   # a single test class
dotnet test --filter "Method_Scenario_ExpectedOutcome"              # a single test
```

Tests are split into tiers via `[Trait("Tier", "Live")]` / `[Trait("Tier", "Demo")]` (Live needs a
local Docker stack; Demo hits the public `demo.grampsweb.org`). `tests/default.runsettings`
(wired up via `RunSettingsFilePath` in `Directory.Build.props`) excludes both by default, so a bare
`dotnet test` only runs what works offline with no setup. **A CLI `--filter` does not override
that exclusion — vstest ANDs it with the runsettings filter**, so `dotnet test --filter "Tier=Live"`
alone silently matches nothing. To opt into a tier, clear the runsettings property first:
`dotnet test --filter "Tier=Live" -p:RunSettingsFilePath=`. See `docs/testing.md` for how to bring
up the Live tier's stack (`docker compose -f docker-compose.test.yml -p gramps-test up -d --wait`),
measured timings, and the dataset fingerprint.

CI (`.github/workflows/ci.yml`) runs, on every push/PR: restore → `dotnet format --verify-no-changes`
→ build (Release, warnings-as-errors) → test (excluding Live/Demo, with Cobertura coverage
collected and uploaded as a build artifact) → a separate job checking for known-vulnerable NuGet
packages via `dotnet list package --vulnerable`.

`TreatWarningsAsErrors` and `EnforceCodeStyleInBuild` are on (`Directory.Build.props`), so style
violations and analyzer warnings — including SecurityCodeScan findings — fail the build, not just
`dotnet format`.

The Demo tier runs against the public `demo.grampsweb.org` instance — a community-run sandbox we
don't control, reset by its maintainers every couple of hours — and exists as a **canary**: it
proves our client still works against a real-world Gramps Web deployment, not just this repo's own
pinned image in `docker-compose.test.yml`. It's opt-in locally the same way as Live: excluded by
`tests/default.runsettings`, selected explicitly with
`dotnet test --filter "Tier=Demo" -p:RunSettingsFilePath=`. `.github/workflows/live-tests.yml` runs
it nightly and on PRs carrying the `run-live-tests` label, as its own `continue-on-error: true`
job — a third-party outage must never block a merge, so this is observability, not a gate.

## Architecture

Three layers, one direction of dependency: **Tools → GrampsApiClient → HttpClient pipeline.**

### Request pipeline (`src/GrampsMcp.Server/Gramps/`)

- `GrampsApiClient` is a typed `HttpClient` and the only thing tools talk to. Reads:
  `ListAsync`, `ListTimelineAsync`, `QueryAsync` (GQL), `CountAsync`, `ListHistoryAsync`,
  `SearchAsync`, `GetAsync` (by handle), `FindByGrampsIdAsync` (resolves a human-facing Gramps ID
  like `I0155` via the collection's `gramps_id` filter, since the API has no direct by-ID lookup).
  Writes: `CreateAsync`, `ReplaceAsync`, `DeleteAsync`, `PostAsync`, `UndoTransactionAsync`.
- Every outgoing request passes through `GrampsAuthHandler` (a `DelegatingHandler`), which attaches
  the bearer token, and on 401 buffers+retries the request once after forcing a refresh via
  `GrampsTokenStore`. `GrampsTokenStore` tries the refresh token first, then falls back to
  re-authenticating with the service account; tokens live in memory only and are never logged.
- `GrampsQueryBuilder` is where the pagination/shaping guardrails actually live (see below) — one
  builder per endpoint shape, each with its own guardrail rationale: `Build` (list endpoints),
  `BuildForSearch` (`/api/search/` doesn't honor the `profile`/`extend`/`keys`/`skipkeys` params
  the other list endpoints accept — measured live for `profile` (`200 OK`, silently ignored, not
  rejected with an error); `extend`/`keys`/`skipkeys` are assumed to behave the same but that
  hasn't been individually confirmed), `BuildForGql`, `BuildForGet`, `BuildForIdLookup` (added in
  #71 to force `page=1&pagesize=1` on the `FindByGrampsIdAsync` collection lookup), `BuildForTimeline`,
  and `BuildForHistory`.
- `GrampsHandleResolver` resolves a caller-supplied Gramps ID or handle into a raw handle for
  endpoints (relations, timelines) that take a handle directly in the URL path — the same
  ID-vs-handle logic `gramps_get` uses inline (see Tools below), extracted so `gramps_relation`
  and `gramps_timeline` share one implementation.
- `GrampsObjectTypes` is the single source of valid type strings: maps the singular names tool
  callers use (`person`, `family`, ...) to their REST collection path (`/api/people/`, ...).
- `GrampsJsonMergePatch` applies an RFC 7396 JSON Merge Patch client-side; `gramps_update` needs it
  because Gramps Web's `PUT` replaces the entire object, so a patch has to be merged onto the full
  current object before writing it back.
- `GrampsErrorTranslator` maps a failed HTTP response to `GrampsApiException`, preferring the
  upstream `error`/`message`/`detail` JSON field but always falling back to the raw body — this is
  deliberate, since GQL 422 errors name the exact parse position and are meant to reach the model
  verbatim for self-correction.
- `GrampsPermissionsProvider` decodes the JWT's `permissions` claim (`GrampsJwt`) so callers can
  check the service account's real ceiling (`AddObject`, `EditObject`, `ViewPrivate`, ...) before
  attempting a write — this is what gates write-tool registration (see Write tools below).
- `GrampsServiceCollectionExtensions.AddGrampsClient()` is the single DI entry point: binds
  `GrampsOptions` from env vars and validates them at startup (fail fast), registers the token
  store, auth handler, and wires `Microsoft.Extensions.Http.Resilience`'s standard resilience
  handler (Polly-based) for transient 5xx/network faults — not a hand-rolled retry loop.

### Tools (`src/GrampsMcp.Server/Tools/`)

Each tool is a `[McpServerToolType]` class with one `[McpServerTool]` method, taking `GrampsApiClient`
via primary-constructor injection. Every tool body is wrapped in `GrampsToolErrors.TranslateAsync`,
which converts `GrampsApiException`/`ArgumentException` into `McpException` — without this, the MCP
SDK replaces the real message with a generic "An error occurred invoking '...'" for any exception
type it doesn't recognise as safe to expose, which would swallow exactly the GQL error detail the
error translator worked to preserve.

`gramps_get` shows the ID-vs-handle pattern: `GrampsIdentifiers.LooksLikeGrampsId` decides whether
to resolve via `FindByGrampsIdAsync` (Gramps ID) or `GetAsync` (handle), and
`GrampsIdentifiers.IsValidHandle` guards the handle path against path-traversal before it's
concatenated into a URL. New tools taking a raw ID-or-handle argument should reuse this pattern
rather than re-deriving it.

Tool descriptions are user-facing prompt content for the calling model, not just documentation —
when adding or editing a tool, the `[Description]` attributes are what steers a model toward
`gramps_search` over `gramps_query`, toward correct type strings, etc. Treat wording changes there
with the same care as the guardrail logic itself.

### Write tools — gated registration

Write tools (`gramps_create`, `gramps_update`, `gramps_delete`, `gramps_merge`, `gramps_history`,
`gramps_undo`) carry a `[GrampsWriteTool(...requiredPermissions)]` attribute naming the Gramps JWT
permission(s) they need. `GrampsToolTypeResolver.ResolveAsync` (called from `Program.cs`) excludes
any type carrying the attribute from the assembly scan — a positive marker on the type, not
membership in a hand-maintained list, so a write tool always fails closed (stays unregistered)
rather than leaking through as an ungated read tool. `GrampsWriteTools.All` is discovered by
reflecting over the same attribute, so it and the read-tool exclusion can't drift apart the way two
independently maintained lists could. Extending or adding a write tool means only attributing the
class with `[GrampsWriteTool("SomePermission")]` — nothing else to remember to update. Registration
then adds back whichever tools `GrampsWriteTools.SelectRegistrable` permits, based on a one-off JWT
permission check made before the host is built. Never exercise write paths against the public demo
instance — a local Gramps Web container only (`docker-compose.dev.yml`, see below).

### Program.cs — transport switch

`MCP_TRANSPORT` picks between `WithHttpTransport()` and `WithStdioServerTransport()` at startup
(default `http`) — there is no single ASP.NET Core pipeline shared between them. The real
distinction isn't "container vs local", it's **shared long-running server vs. strictly 1:1**: HTTP
is one server instance with one token store that several local clients can share, exposes
`/health`, and is meant to run long-running under Compose/Portainer — it is also unauthenticated by
design (see `SECURITY.md`), bound to loopback, and gated by Origin-validation middleware
(`GrampsMcp.Server.Http.OriginValidationMiddlewareExtensions.UseOriginValidation`, registered ahead
of `app.MapMcp()`) as the actual defense against DNS rebinding. stdio is strictly 1:1: each client
spawns its own process and gets its own token store, and there is no network surface at all, which
is why it's preferred for a single desktop client. **Under stdio, stdout is the MCP protocol
channel**, so all logging is redirected to stderr (`ClearProviders()` +
`AddConsole(LogToStandardErrorThreshold = Trace)`). Never write to stdout on the stdio path.
`MCP_LOG_LEVEL` (`None`/`Critical`/`Error`/`Warning`/`Information`/`Debug`/`Trace`), applied on top
of each transport's default, is validated at startup by `GrampsOptions`; unset preserves current
per-transport behaviour. The HTTP path also exposes `/health`, which pings `/api/metadata/` and
reports 503 on any of the known transient-failure exception types. `public partial class Program;`
at the bottom exists solely so `WebApplicationFactory<Program>` works in tests
(`tests/GrampsMcp.UnitTests/Http/OriginValidationMiddlewareTests.cs` is the first to use it) —
don't remove it.

### Docker & compose

Three compose files, distinct roles: `docker-compose.yml` runs the server alone against an
external Gramps Web (`.env`, gitignored, copied from `.env.example`). `docker-compose.dev.yml`
adds a local Gramps Web instance for exercising write tools (`.env.dev`, gitignored,
`GRAMPS_ALLOW_WRITES=true`) — never point `GRAMPS_ALLOW_WRITES=true` at anything but this local
container. `docker-compose.test.yml` is the disposable stack the Live test tier runs against,
seeded with the bundled `example.gramps` dataset; see `docs/testing.md` for bring-up. Dev and test
stacks run side by side without port collisions (`8090`/`8080` vs `8091`/`8081`) — `.env` and
`.env.dev` are easy to conflate since only the filename distinguishes "points at a real tree" from
"local disposable sandbox".

## Guardrails (read before touching request/response shaping)

These come from `docs/PLAN.md`'s "Guardrails (cross-cutting)" section and are backed by measured
facts about the live Gramps Web API (also in that doc) — not arbitrary defaults:

- **Forced pagination.** An **omitted** `page` query key is what makes Gramps Web return the whole
  collection — a naive list call floods the model's context. (An *explicit* `page=0` is 422-rejected,
  not treated as unpaginated; and `pagesize` alone, without an explicit `page`, only bounds
  `/api/search/` — `/api/people/`/GQL ignore it and return everything.) `GrampsQueryBuilder` coerces
  `page < 1` to `1` and always sends an explicit `page`, so our client never hits either quirk, and
  clamps `pagesize` to `GrampsOptions.MaxPageSize`. Every list result reports `total`/`page`
  (`GrampsListResult<T>`) so callers can page deliberately.
- **Search over GQL.** `/api/search/` is indexed and answers in ~2s; GQL costs 10–15s regardless
  of result size because it scans the whole database upstream. `gramps_search` is the tool a model
  should reach for by default; `gramps_query`/`gramps_count` (GQL) say so plainly in their
  description and pass 422 parse errors through verbatim.
- **Default `profile` resolves handles into readable text.** Without it, a person record is opaque
  handles; `GrampsOptions.DefaultProfile` (`self,families,events`) makes responses self-contained
  without follow-up calls. Passing `profile=none` opts out explicitly.
- **Read-only by default.** `GRAMPS_ALLOW_WRITES` gates whether write tools are *registered at
  all*, not merely whether they error — an unregistered tool can't be planned around by the model.
  The JWT's permission claim additionally has to actually grant the right before a write tool is
  registered (see Write tools above).
- **Secrets never logged or echoed.** Tokens live only in `GrampsTokenStore`'s private fields;
  `Log.cs` calls must not include token values, and tool error paths must not leak credentials.
