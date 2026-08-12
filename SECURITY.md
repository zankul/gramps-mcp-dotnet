# Security

This is a local, single-user tool, not a multi-user network service — read this before exposing
it beyond your own machine.

## What's at stake

The container holds long-lived Gramps Web admin/service-account credentials (`GRAMPS_USERNAME` /
`GRAMPS_PASSWORD`) and, if `GRAMPS_ALLOW_WRITES=true`, can create, edit, delete, and merge records
in your genealogy tree. Treat its `.env` file with the same care as any other credential store.

## The HTTP transport is unauthenticated by design

`MCP_TRANSPORT=http` ships with **no built-in auth scheme** — there is no API key, no bearer
token, nothing checked on incoming MCP requests. This is deliberate, not an oversight: it matches
the tool's single-user, run-on-your-own-machine design goal, and a half-implemented auth layer
would be worse than none (false confidence).

Two things stand between that transport and the outside world:

- **Loopback binding.** `docker-compose.yml` publishes the container port as
  `127.0.0.1:8080:8080`, not `8080:8080` — only processes on the host machine can reach it at all.
- **Origin validation.** Every HTTP request with a present `Origin` header is checked against an
  allowlist (`MCP_ALLOWED_ORIGINS`, default `http://localhost:8080,http://127.0.0.1:8080`) before
  it reaches the MCP endpoint. This is what defends against DNS rebinding — a malicious web page
  tricking a browser into sending same-origin-looking requests to a server it shouldn't be able to
  reach. Requests with no `Origin` header (most non-browser MCP clients) pass through unchecked,
  since Origin validation is a browser-specific defense, not a general auth mechanism. Matching is
  an **exact string comparison** against each entry, so `MCP_ALLOWED_ORIGINS` only accepts
  well-formed `scheme://host[:port]` values — no wildcard, subdomain, or CIDR support
  (`*`, `*.example.com`, `192.168.0.0/24`, etc. are not patterns; they'd just never match a real
  `Origin` header). Anything else, including `*`, is rejected **at startup**, not silently ignored,
  since a malformed entry that matched nothing would otherwise disable this defense without saying
  so. List every concrete origin — scheme, host, and port — that needs access.

Neither of these is authentication. Anything that can reach the bound port and present an allowed
(or absent) `Origin` gets full access.

## Reaching it remotely

If you need the HTTP transport reachable from another machine, put an authenticating reverse
proxy (e.g. Traefik, nginx) in front of it and never publish the container port beyond loopback.
The server itself must keep assuming everything that reaches it is trusted.

## Prefer stdio for a single desktop client

`MCP_TRANSPORT=stdio` has no network surface at all — the client spawns the server as a child
process and talks to it over stdin/stdout. None of the above considerations apply. If you're
running a single desktop client (e.g. Claude Desktop) against your own tree, prefer stdio over
HTTP.

## Reporting a vulnerability

Please open a GitHub issue on this repository. If the report involves sensitive details you'd
rather not post publicly, say so in the issue and we'll follow up privately.
