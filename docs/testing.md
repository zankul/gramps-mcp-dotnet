# Testing

## Tiers

| Tier | Trait | Runs | Needs |
| --- | --- | --- | --- |
| Unit | (default, no trait) | Every PR (CI) | Nothing — offline, no Docker |
| Live | `[Trait("Tier", "Live")]` | Nightly / manual dispatch | `docker-compose.test.yml` stack, see below |
| Demo | `[Trait("Tier", "Demo")]` | Opt-in (`GRAMPS_RUN_DEMO_SMOKE_TEST=true`) | The public `demo.grampsweb.org` |

Live tests exercise real HTTP calls against a real Gramps Web instance seeded with a known dataset
(the `example.gramps` demo tree bundled in the `grampsweb` image, ~2157 people) — this is where
`gramps_search`/`gramps_relation`/`gramps_timeline`/write-tool round-trips get checked against
actual API responses rather than mocked ones. They are not part of the default `dotnet test` run
(see `CLAUDE.md`'s runsettings note); a later PR adds the test project(s) and CI workflow for this
tier. This file documents the stack those tests will run against.

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

Once the stack is up and healthy (once the test project(s) land in a later PR):

```bash
dotnet test --filter "Tier=Live" -p:RunSettingsFilePath=
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

This sets the timeout budget for a later CI live-test workflow: the import script's own
`OVERALL_TIMEOUT_S` is 180s and the healthcheck allows up to `5s x 60 = 300s` — both comfortably
above the ~50s observed (>3x margin) while still failing fast if something is genuinely stuck,
rather than the 900s/1200s this was originally set to (an estimate that turned out to be an order
of magnitude too pessimistic once the real import was measured). A CI step timeout of 5 minutes
for `up --wait` has headroom without masking a genuine regression.

**Seeding state machine.** `grampsweb_init`'s script (identical in both compose files apart from
the dev file's opt-in `if` gate and best-effort `|| echo` swallow) decides what to do from two
signals: the current `people` count and whether `QUEUED_MARKER`
(`/app/cache/.example_seed_queued`) exists. The order is deliberate and was the subject of a
review round that caught a real bug: checking `people > 0` *before* the marker trips on the very
first record committed — nowhere near done — and would report healthy on a half-imported tree. The
fix gates "already seeded, skip" on **no marker and `people >= PEOPLE_FLOOR`** specifically;
anything else (a marker present, or a nonzero sub-floor count with no marker at all) falls through
to `wait_for_import`'s quiescence check instead of being fast-tracked past it. This was verified by
restarting `grampsweb_init` mid-import (after the queue, before completion) and confirming it took
the "already queued, waiting" branch rather than a false "skip" — it did not report healthy until
the tree was genuinely complete, landing on the same 2157/10536 fingerprint with no duplication.

One empirical note from that verification: this Gramps Web version's import appears to commit
**atomically** from the API's perspective — polling `/api/metadata/` every 250ms during a live
import showed `object_counts` sitting at all-zero and then jumping straight to the final
2157-people/10536-object state in one step, never a gradual climb. A literal "500 of 2157
committed" partial read could not be reproduced against the real import for that reason. The fix
above doesn't depend on that being reproducible, though: it also covers (and was verified against)
a queued-but-never-completed import (celery stopped right after the POST), which reliably produces
a genuine stuck `people == 0` state.

**Duplicate-import marker and its poisoning.** The marker is written *before* POSTing the import
(closes the duplicate-import race: a restart between "queued" and "finished" waits instead of
re-queuing) and is removed in three places: after a successful quiescent completion, after the
file-read/POST itself fails, and after `wait_for_import` times out with `people` still below
`PEOPLE_FLOOR` (a timeout at or above the floor leaves the marker, since that's very likely a
live-but-slow import rather than a dead one, and re-queuing on top of it would be worse than
waiting). All three paths were verified directly: a forced failure between the marker write and
the POST left the marker removed and the tree still empty; stopping the celery worker right after
queuing left `people` at 0 through a shortened timeout window and the marker was removed
afterward; in both cases the very next normal start queued and completed a real import rather than
waiting on a phantom one.

**Reboot-race defense.** Docker's restart policies don't honour `depends_on` — after a host reboot
or daemon restart, `grampsweb_init` (`restart: unless-stopped`) can start before `grampsweb` does,
since the ordering `grampsweb_bootstrap` provided during the original `up` no longer applies.
`get_token_with_retry` (bounded at 120s) replaces a bare `get_token()` call for exactly this case.
Verified by creating (but not starting) `grampsweb` and starting `grampsweb_init` standalone: it
sat in `starting` health state, retrying silently, for the whole gap, then completed a normal
seeded run the moment `grampsweb`/`grampsweb_bootstrap` came up — at no point did it report
healthy against the unseeded tree.

**Test stack fails fast, not silently.** `docker-compose.test.yml`'s `grampsweb_init` uses
`restart: "no"` (not `unless-stopped`, unlike every other service in that file): with
`unless-stopped`, a failure exit just restarts the container and the healthcheck's failure counter
resets on every restart, so it never reaches the unhealthy threshold — `up --wait` has no timeout
of its own and would hang indefinitely instead of failing. Verified by forcing an immediate seeding
failure (a broken import file path): the container exited once, stayed exited, and
`docker compose up -d --wait` returned a real, prompt failure (`dependency failed to start:
container ... exited (1)`, exit code 1) rather than hanging.

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

## Capturing ExampleTree (placeholder — for a later PR)

Live tests that assert on specific people/families/events need real Gramps IDs and names from the
seeded tree to use as anchors (e.g. "person I0044 is named X and has Y children"). A later PR
should capture those the same way the fingerprint above was captured — **never** write an ID or
name from memory or from the upstream Gramps project's own docs, since `example.gramps`'s exact
contents are what's actually running, not what's assumed:

1. Bring up `docker-compose.test.yml` per above.
2. Authenticate (`POST /api/token/` with `owner`/`owner`) and hit `GET /api/metadata/` to confirm
   `object_counts` still matches the fingerprint above (if it doesn't, the anchors below need
   re-capturing, not just this metadata block).
3. Pull a `keys=gramps_id,primary_name` page (e.g. `GET /api/people/?keys=gramps_id,primary_name
   &pagesize=20`) and paste the returned Gramps IDs and names verbatim into the test fixtures —
   do not transcribe or guess them.
4. Repeat for whichever other object types (families, events, places) the new tests anchor on.
