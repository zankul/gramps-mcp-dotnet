# Changelog

All notable changes to this project are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.0.1] - 2026-10-18

First public release.

### Added
- MCP server (.NET 10) giving AI agents structured access to a [Gramps Web](https://www.grampsweb.org/)
  genealogy database through its REST API, over HTTP (loopback-bound, Origin-validated, `/health`)
  or stdio.
- Read tools: `gramps_search`, `gramps_get`, `gramps_list`, `gramps_tree_summary`, `gramps_types`,
  `gramps_relation`, `gramps_timeline`, and the slower GQL tools `gramps_query` and `gramps_count`.
- Write tools: `gramps_create`, `gramps_update`, `gramps_delete`, `gramps_merge`, `gramps_history`
  and `gramps_undo`. Read-only by default: they are only registered when `GRAMPS_ALLOW_WRITES=true`
  and the service account's JWT grants the required permissions.
- Guardrails against context bloat: forced pagination with a capped page size, readable-text
  `profile` resolution by default, and search-first tool guidance.
- Token handling: bearer auth with refresh, then service-account re-login; tokens are kept in memory
  only and never logged.
- Multi-arch (`linux/amd64`, `linux/arm64`) container image published to GHCR.
- Offline unit tests, plus Live (local Docker stack) and Demo (`demo.grampsweb.org`) test tiers.

[0.0.1]: https://github.com/zankul/gramps-mcp-dotnet/releases/tag/v0.0.1
