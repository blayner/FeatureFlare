# ADR 0003: MurmurHash3 bucketing shared across languages

- **Status:** Accepted
- **Date:** 2026-07-16

## Context

Percentage rollouts and experiment assignment must be:

1. **Sticky**: the same user always gets the same variation, without storing assignments.
2. **Independent** across flags: being in the 10% of flag A says nothing about flag B.
3. **Identical** in every runtime (server, Node, browsers, .NET).

## Decision

```
bucket = murmur3_x86_32(utf8("{key}.{salt}.{value}"), seed 0) % 10000
```

- MurmurHash3 x86 32-bit is fast, well distributed, and simple to implement exactly in any language
  (JavaScript only needs `Math.imul`). It is not a cryptographic hash, and it doesn't need to be.
- The per-flag **salt** gives independence and allows a deliberate reshuffle (rotate the salt).
- Buckets are basis points (0–9999), which allows weights such as 0.5%.
- Experiments use `{salt}.traffic` for the traffic gate and `{salt}` for the variant split, so changing
  the traffic allocation never moves users between variants.

Strings are **always** hashed as UTF-8. Hashing UTF-16 code units in one runtime and UTF-8 in another is
the classic way to break cross-SDK consistency for non-ASCII user ids.

## Consequences

- `spec/hash-vectors.json` (including multi-byte input such as `ação-çãé` and `日本語テキスト`) is the
  contract; all three test suites load it.
- Changing the formula is a breaking change that reassigns every user. It would require a
  versioned bucketing scheme.
