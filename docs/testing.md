# Testing

## Tiers

| Tier | Trait | Runs | Needs |
| --- | --- | --- | --- |
| Unit | (default, no trait) | Every PR (CI) | Nothing — offline, no Docker |
| Live | `[Trait("Tier", "Live")]` | Nightly, and on PRs labelled `run-live-tests`; also runnable on manual dispatch | `docker-compose.test.yml` stack, see below |
| Demo | `[Trait("Tier", "Demo")]` | Opt-in locally (`--filter "Tier=Demo" -p:RunSettingsFilePath=`); also runs nightly in CI as a `continue-on-error` canary | The public `demo.grampsweb.org` |

Live tests exercise real HTTP calls against a real Gramps Web instance seeded with a known dataset
(the `example.gramps` demo tree bundled in the `grampsweb` image, ~2157 people) — this is where
`gramps_search`/`gramps_relation`/`gramps_timeline`/write-tool round-trips get checked against
actual API responses rather than mocked ones. They are not part of the default `dotnet test` run
(see `CLAUDE.md`'s runsettings note). `tests/GrampsMcp.LiveTests/LiveStack/` holds the fixture
scaffolding (readiness probe, DI-graph fixture, the `LiveStackGate`/`LoopbackGuard` guards); the
suite itself spans `Contract/`, `Protocol/`, and `Writes/`, plus `SeededTreeTests.cs` guarding the
seeded dataset's fingerprint against drift. This file documents the stack those tests run against.

## Bringing up the live-test stack

```bash
docker compose -f docker-compose.test.yml -p gramps-test up -d --wait
```

This is a disposable, always-cold stack — see `docker-compose.test.yml`'s top-of-file comment for
why it uses no named volumes. `--wait` blocks until the example dataset has *finished* importing
(not merely been queued), so once the command returns, the tree is fully seeded. It runs alongside
`docker-compose.dev.yml` without conflict:

| | `docker-compose.dev.yml` | `docker-compose.test.yml` |
| --- | --- | --- |
| grampsweb | `:8090` | `:8091` |
| gramps-mcp | `:8080` | `:8081` |
| Project name | (default) | `gramps-test` |
| Seeding | opt-in (`GRAMPS_SEED_EXAMPLE_DATA=true`) | always on |
| Volumes | named, persistent | anonymous, destroyed on `down --volumes` |

## Running live tests

Once the stack is up and healthy:

```bash
dotnet test tests/GrampsMcp.LiveTests --filter "Tier=Live" -p:RunSettingsFilePath=
```

(the explicit `-p:RunSettingsFilePath=` is required — see `CLAUDE.md`; a bare `--filter` doesn't
override the default runsettings exclusion).

## Tearing down

```bash
docker compose -f docker-compose.test.yml -p gramps-test down --volumes
```

`--volumes` (`-v`) is required here — unlike `docker-compose.dev.yml`, where you should *never*
pass it — because this stack has nothing worth protecting and a plain `down` would otherwise leave
orphaned anonymous volumes lying around. The next `up --wait` after a `down --volumes` seeds again
from a genuinely cold tree; this was verified directly (see measurements below), not assumed.

## Measured timings (2026-08-08)

Measured cold, back-to-back, on the machine this PR was verified on. Both `ghcr.io/gramps-project
/grampsweb:latest` and the `gramps-mcp` image were already present locally (built/pulled by a
previous run) for these runs, so they exclude image-pull time — see the note below.

| Phase | Duration | Notes |
| --- | --- | --- |
| `gramps-mcp` image build (warm cache, source-only change) | ~13s | `docker compose build gramps-mcp`; `dotnet publish` itself is ~7s of that. A fully cold build (no cached base layers) additionally pays for pulling `mcr.microsoft.com/dotnet/sdk:10.0-alpine` and `aspnet:10.0-alpine`. |
| grampsweb boot + owner bootstrap (`grampsweb_bootstrap`) | ~10s | From container start to HTTP responding + owner account created. |
| Example dataset import (queue → quiescent) | ~35-36s | Includes a fixed 20s quiescence-confirmation window (`STABILITY_WINDOW_S`) after the last `object_counts` change — the actual XML import of 2157 people / 10536 objects finishes in roughly 10-16s of that. |
| Healthcheck detection → `gramps-mcp` starts | ~5s | Bounded by the 5s healthcheck interval. |
| **Total: cold `up --wait` → fully seeded and healthy** | **~46-51s** | Measured twice on `docker-compose.test.yml` (46.6s, 51.1s) and once seeded on `docker-compose.dev.yml` (46.1s) via a port-remapped override. The un-seeded `docker-compose.dev.yml` path (no import) came up in ~17s. |

This sets the timeout budget for `.github/workflows/live-tests.yml`'s CI live-test job: the import script's own
`OVERALL_TIMEOUT_S` is 180s and the healthcheck allows up to `5s x 90 = 450s` (that 90-retry figure
covers `get_token_with_retry`'s 120s reboot-race bound *plus* the 180s import timeout, plus
margin — both are comfortably above the ~50s normally observed, while still failing fast if
something is genuinely stuck, rather than the 900s/1200s this was originally set to). A CI step
timeout for `up --wait` should be at least ~8 minutes to clear that 450s healthcheck budget with
room to spare — 5 minutes would cut off a run the healthcheck is still legitimately retrying.

**Seeding state machine.** `grampsweb_init`'s script (identical in both compose files apart from
the dev file's opt-in `if` gate and best-effort `|| echo` swallow, and one deliberate divergence
covered below) decides what to do from two signals: the current `people` count and whether
`QUEUED_MARKER` (`/app/cache/.example_seed_queued`) exists, giving four distinct states:

1. No marker, `people >= PEOPLE_FLOOR` — already fully seeded, skip immediately.
2. No marker, `people == 0` — genuinely empty, safe to queue a new import.
3. No marker, `0 < people < PEOPLE_FLOOR` — see below; this is where dev and test diverge.
4. Marker present (any count) — an import is tracked as in flight; confirm via
   `wait_for_import`'s quiescence check rather than guessing from a single reading.

The order matters and was the subject of two review rounds. First bug: checking `people > 0`
*before* the marker collapses states 1 and 3 into "skip", tripping on the very first record
committed in state 4 and reporting healthy on a half-imported tree. Verified fixed by restarting
`grampsweb_init` mid-import (after the queue, before completion) and confirming it took the
"already queued, waiting" branch rather than a false "skip" — it did not report healthy until the
tree was genuinely complete, landing on the same 2157/10536 fingerprint with no duplication.

Second bug, in the fix for the first: re-queuing in state 3 looks tempting (nothing else in the
`else` branch can make progress there) but is wrong on the dev stack specifically. A nonzero,
sub-floor, unmarked count is indistinguishable from a developer's own small GEDCOM, or a person
added through the UI — from `object_counts` alone there is no way to tell that apart from "an
import that died partway through". Re-queuing would silently merge the example dataset into
someone's real tree, which is worse than the deadlock this file exists to avoid. So the two stacks
now handle state 3 differently, deliberately: **dev** logs a clear warning naming the count and the
floor, does not import, does not wait, and proceeds to touch the sentinel — treating the tree as
the developer's own. **test** fails fast, because that stack always starts cold (see the volumes
note above), so state 3 there can't mean "someone's real data" — it can only mean something is
genuinely wrong.

Empirical note, relevant to how much weight to put on state 3 at all: this Gramps Web version's
import appears to commit **atomically** from the API's perspective — polling `/api/metadata/`
every 250ms during a live import showed `object_counts` sitting at all-zero and then jumping
straight to the final 2157-people/10536-object state in one step, never a gradual climb. A literal
"500 of 2157 committed" partial read could not be reproduced against the real import for that
reason, which means state 3 looks to be unreachable *through this script's own import path* — an
import that dies mid-transaction leaves no trace, not a partial count. It's only reachable by a
tree gaining people some other way, which on dev is exactly the "developer's own data" case the
warning handles, and on test shouldn't happen at all. The state-3 handling is accordingly more
built-out than the importer's actual behavior currently requires; it's there for the case where
that assumption stops holding (a different Gramps Web version, a differently-configured import),
not because it's been observed to trigger.

**Duplicate-import marker and its poisoning.** The marker is written *before* POSTing the import
(closes the duplicate-import race: a restart between "queued" and "finished" waits instead of
re-queuing) and is removed in three places: after a successful quiescent completion, after the
file-read/POST itself fails, and after `wait_for_import` times out having *actually observed* a
sub-floor count (a timeout at or above the floor leaves the marker, since that's very likely a
live-but-slow import rather than a dead one, and re-queuing on top of it would be worse than
waiting). That "actually observed" qualifier is load-bearing: `people` defaults to `0` and is only
updated on a successful poll, so if every poll in the timeout window raised (grampsweb restarting,
a sustained 502), the timeout path used to see the untouched default `0` and delete the marker on
data it never actually read — turning a transient outage into the same duplicate-import race the
marker exists to prevent. A `saw_successful_poll` flag now gates the removal on having genuinely
observed a sub-floor count, not merely defaulted to one. All three removal paths were verified
directly: a forced failure between the marker write and the POST left the marker removed and the
tree still empty; stopping the celery worker right after queuing left `people` at a genuinely
observed 0 through a shortened timeout window and the marker was removed afterward; in both cases
the very next normal start queued and completed a real import rather than waiting on a phantom
one.

**Reboot-race defense — bounded, not closed, on dev.** Docker's restart policies don't honour
`depends_on` — after a host reboot or daemon restart, `grampsweb_init` (`restart: unless-stopped`)
can start before `grampsweb` does, since the ordering `grampsweb_bootstrap` provided during the
original `up` no longer applies. `get_token_with_retry` (bounded at 120s) replaces a bare
`get_token()` call for exactly this case, and was verified directly: creating (but not starting)
`grampsweb` and starting `grampsweb_init` standalone left it sitting in `starting` health state,
retrying silently, for the whole gap, then it completed a normal seeded run the moment
`grampsweb`/`grampsweb_bootstrap` came up — it did not report healthy against the unseeded tree
*within that 120s window*. Past 120s, dev's behavior is unchanged from its existing best-effort
design: `get_token_with_retry` raises, the outer `python3 <<'PYEOF' || echo "..."` swallows it, and
the script still touches `/run/init-done` — a `grampsweb` outage longer than 120s makes dev report
healthy without having seeded, same as any other seeding failure on that stack. That's a
deliberate trade-off (seeding is opt-in and best-effort; the empty-but-usable tree still works),
not a gap in the retry itself — the retry's job is only to shrink the race window, not eliminate
it. The test stack has no such gap: a `get_token_with_retry` failure there is not swallowed and
propagates to the same `raise SystemExit(1)` / `restart: "no"` fail-fast path as any other error.

**Test stack fails fast, not silently.** `docker-compose.test.yml`'s `grampsweb_init` uses
`restart: "no"` (not `unless-stopped`, unlike every other service in that file): with
`unless-stopped`, a failure exit just restarts the container and the healthcheck's failure counter
resets on every restart, so it never reaches the unhealthy threshold — `up --wait` has no timeout
of its own and would hang indefinitely instead of failing. Verified by forcing an immediate seeding
failure (a broken import file path): the container exited once, stayed exited, and
`docker compose up -d --wait` returned a real, prompt failure (`dependency failed to start:
container ... exited (1)`, exit code 1) rather than hanging.

**Request timeouts make the other bounds real.** `urlopen` calls had no `timeout=`, so Python's
default (block forever) applied. A hung gunicorn worker under import load — the heaviest thing
this stack does — could wedge a single call indefinitely, and neither `get_token_with_retry`'s
120s bound nor `OVERALL_TIMEOUT_S`'s 180s would ever fire, since both are only checked *between*
calls, not while one is blocked. Every `urlopen` now passes `timeout=30`.

**Configurable completion floor.** `PEOPLE_FLOOR` (default 2000, overridable via
`GRAMPS_SEED_PEOPLE_FLOOR`) stays a hard gate on completion rather than a soft warning: quiescence
alone can't distinguish "finished" from "a worker died mid-import and the count will never move
again" (both look identical — a frozen, non-changing count), and the floor is what catches that.
The trade-off is that `grampsweb:latest` is unpinned, so a future image shipping a smaller
`example.gramps` would need `GRAMPS_SEED_PEOPLE_FLOOR` lowered via an environment variable — not a
code change, but not fully automatic either. That's judged the better trade: a quiescence-only
gate would accept a stalled partial import as "done" the moment it goes quiet, which is the exact
failure mode this floor exists to catch.

**Image pull time** was not measured — `ghcr.io/gramps-project/grampsweb:latest` was already
cached locally in this session. For reference, the image is ~6.7GB uncompressed (it bundles
Tesseract OCR language packs for ~40 languages); budget accordingly for a CI runner pulling it
cold, and account for cache warm-up on the first live-test CI run.

## Image digest and dataset fingerprint (2026-08-08)

For drift detection in later PRs — if either of these changes, the live-test fixtures/anchors may
need revisiting:

```
ghcr.io/gramps-project/grampsweb@sha256:6f81fa942f1d447a953e24eeddf17b28db9f5d55e5ba10ce2a76cfcf8896bb59
```

Full `object_counts` from a freshly seeded tree (`GET /api/metadata/` against
`docker-compose.test.yml`, captured verbatim):

```json
{
  "citations": 2854,
  "events": 3432,
  "families": 762,
  "media": 7,
  "notes": 19,
  "people": 2157,
  "places": 1296,
  "repositories": 3,
  "sources": 4,
  "tags": 2
}
```

(sum: 10536, matching the search index's `search.sifts.count` in `/api/metadata/` — this is the
"total objects" figure the import-quiescence poll in both compose files watches.)

## Capturing ExampleTree

`tests/GrampsMcp.LiveTests/ExampleTree.cs` holds the anchor Gramps IDs, names, relationship string,
search hit count, and `object_counts` fingerprint that `SeededTreeTests.cs` guards against drift.
Every value in it was read back from a real running stack — **never** write an ID, name, or count
from memory or from the upstream Gramps project's own docs, since `example.gramps`'s exact contents
are what's actually running, not what's assumed. To re-capture (e.g. after an upstream image
refresh changes the bundled dataset):

1. Bring up the stack: `docker compose -f docker-compose.test.yml -p gramps-test up -d --wait`.
2. Record the image digest: `docker image inspect ghcr.io/gramps-project/grampsweb:latest --format
   '{{index .RepoDigests 0}}'`.
3. Authenticate: `POST /api/token/` with `{"username": "owner", "password": "owner"}` against
   `http://localhost:8091`, and use the returned `access_token` as a `Bearer` header on every
   request below.
4. Fingerprint: `GET /api/metadata/` and read `object_counts` — compare against the block above; if
   it has drifted, the anchors below need re-capturing too, not just this block.
5. Anchor person: page through `GET /api/people/?keys=gramps_id,primary_name,handle,family_list,
   event_ref_list&pagesize=200&page=N`, and pick one with a non-empty surname and several
   families/events (favor breadth of coverage over any specific individual — the goal is a stable,
   well-connected anchor, not a "correct" person). Record its `gramps_id`, `handle`, and
   `primary_name.first_name`/`primary_name.surname_list[0].surname` verbatim.
6. Anchor family/place/event: `GET /api/people/{handle}?profile=self,families,events` on the anchor
   person returns its families inline (take the first family's `gramps_id`/`handle`) and its
   `profile.events` (take the birth event's summary). Resolve the *raw* event handle from the
   person's own `event_ref_list[0].ref` via `GET /api/events/{handle}` (records `gramps_id`,
   `handle`, `type`), then follow that event's `place` field via `GET /api/places/{handle}`
   (records `gramps_id`, `handle`, `name.value`).
7. Known relationship: pick a second person clearly related to the anchor (e.g. a child from one of
   the families in step 6 — read their `handle`/`gramps_id` straight off the family record's
   `child_ref_list`), then call `GET /api/relations/{anchorHandle}/{otherHandle}` — **note this
   endpoint takes handles, not Gramps IDs** — and record `relationship_string` verbatim.
8. Common search surname: `GET /api/search/?query={surname}&pagesize=1` on the anchor's surname (or
   any other surname expected to have many hits) and record the `X-Total-Count` response header as
   the hit count.
9. Paste every value into `ExampleTree.cs` verbatim, update its header comment with the digest from
   step 2 and today's date, and re-run `SeededTree_ObjectCounts_MatchCapturedFingerprint` /
   `SeededTree_AnchorIds_AllResolve` against the same stack to confirm the new constants resolve.

Captured 2026-08-09 against digest
`sha256:6f81fa942f1d447a953e24eeddf17b28db9f5d55e5ba10ce2a76cfcf8896bb59` (unchanged from the
2026-08-08 capture above): anchor person `I0027` ("Matthias, Jr." Ball, handle
`B3UJQCZHDXII99AWW4`), anchor family `F0007`, anchor place `P1349` (Amarillo), anchor event `E1322`
(Birth), son `I0033` (Jasper Ball) with `relationship_string` `"son"`, and 68 search hits for
`Ball`. See `ExampleTree.cs` for the full set.
