# ADR 0002: Local evaluation in SDKs

- **Status:** Accepted
- **Date:** 2026-08-20

## Context

Flags are evaluated on hot paths, often several times per request. A network round-trip per evaluation
adds latency and turns the flag service into a single point of failure for every consumer.

## Decision

Server-side SDKs download the full configuration (`GET /api/sdk/config`) and evaluate **locally** with a pure
function. The snapshot is kept fresh by the SSE stream, with polling as a fallback.

`POST /api/evaluate` stays available for **untrusted** clients (browsers, mobile apps), which should not
download targeting rules because those can contain user lists and business logic.

## Consequences

- Evaluation needs no I/O. If FeatureFlare is down, SDKs keep serving the last known snapshot, or the
  caller's defaults if they never initialized.
- The evaluation algorithm is implemented three times. Drift between them is the main risk, mitigated by
  a written spec (`spec/README.md`), shared hash vectors, and mirrored test cases across the suites.
- Every SDK instance holds the whole configuration in memory. That is fine for thousands of flags, but
  large segments (tens of thousands of explicit keys) should use attribute rules instead (the server
  enforces a limit).
