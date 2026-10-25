# ADR 0001: Server-Sent Events instead of WebSockets

- **Status:** Accepted
- **Date:** 2026-08-08

## Context

SDKs must learn about configuration changes within a fraction of a second. Traffic is entirely
server → client: SDKs never push data over this channel (events are sent in batches over plain HTTP).

## Decision

Use **Server-Sent Events** on `GET /api/stream`.

- One-directional, which matches the need exactly.
- Plain HTTP: works through proxies, load balancers and HTTP/2 multiplexing without an upgrade handshake.
- Reconnection, `retry:` and `Last-Event-ID` are part of the protocol, and browsers ship a native client.
- Easy to debug with `curl -N`.

### Consistency model

Events carry the aggregate **version**. SDKs ignore anything older than what they already hold. On every
(re)connection they download the full configuration, so a missed event is repaired on reconnect. The
server prefers **disconnecting** a slow subscriber (its bounded buffer is full) over dropping an
individual message, because a dropped message would silently leave that SDK inconsistent.

### Authentication

The native browser `EventSource` cannot send headers. The stream endpoint therefore also accepts the key as
`?sdkKey=`, but **only SDK keys**: admin keys are never accepted in URLs, which end up in access logs. SDKs
that can send headers (the `fetch` transport and the .NET SDK) always use `Authorization: Bearer`.

## Consequences

- Fan-out is in-process (`SseBroadcaster`). Horizontal scaling needs a backplane (Postgres
  `LISTEN/NOTIFY` is the natural first choice, since Postgres is already a dependency).
- Each connected SDK holds one long-lived HTTP connection. The API's HTTP client for streaming must have
  no request timeout and no retry handler (the .NET SDK uses a dedicated named `HttpClient` for this).
