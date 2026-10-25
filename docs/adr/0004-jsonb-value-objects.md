# ADR 0004: Store rules and variations as jsonb

- **Status:** Accepted
- **Date:** 2026-09-14

## Context

A flag owns variations, an ordered list of rules, clauses inside rules, and serves that are either a
variation or a weighted rollout. A normalized schema would need five or more tables and multi-table
writes for every change, while these value objects are always read and written **as a whole** with their
aggregate.

## Decision

Map aggregates to one row each and store the value-object graphs in `jsonb` columns, through EF Core
value converters that use the same `System.Text.Json` settings as the public API. Explicit segment
membership lists map to native `text[]` columns. `Version` is an optimistic concurrency token.

## Consequences

- One row per aggregate, and atomic updates without explicit transactions.
- The stored JSON matches the wire format, which makes debugging and manual inspection straightforward.
- Querying *inside* rules ("which flags target country BR?") needs jsonb operators. That isn't needed
  today; the one cross-aggregate check (segment still referenced) is done in the application layer.
- Value comparers serialize to detect changes. That's fine at the size of a flag; it would not be for
  very large documents.
- Analytics events are deliberately **not** stored this way: `experiment_events` is a narrow,
  append-only table indexed for the aggregation queries.
