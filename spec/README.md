# Evaluation specification

This folder is the contract shared by the server and every SDK. Any implementation that passes the
vectors in [`hash-vectors.json`](hash-vectors.json) and follows the rules below produces the same
variation for the same user, whether the flag is evaluated on the server or locally in an SDK.

## 1. Hashing and bucketing

```
hash   = murmur3_x86_32(utf8(input), seed = 0)      // unsigned 32-bit
bucket = hash % 10000                              // 0..9999, i.e. basis points
input  = "{key}.{salt}.{value}"
```

| Use                     | key              | salt                       | value                          |
|-------------------------|------------------|----------------------------|--------------------------------|
| Flag percentage rollout | flag key         | flag salt                  | context attribute `bucketBy` (default: context key) |
| Experiment traffic gate | experiment key   | `{experiment salt}.traffic`| context key                    |
| Experiment variant      | experiment key   | experiment salt            | context key                    |

Using a different salt for the traffic gate makes the "is the user in the experiment?" decision
independent from "which variant does the user get?" — increasing traffic from 10% to 20% only adds
users, it never moves existing users between variants.

### Picking a slot

Weights are integers in basis points that sum to 10000. Walk the slots accumulating weights; the first
slot where `bucket < cumulative` wins. If weights sum to less than 10000 the remainder goes to the last slot.

## 2. Attribute normalisation

Clauses compare **strings**. Context attribute values are converted before matching:

| Value type          | Normalised as                                |
|---------------------|----------------------------------------------|
| string              | itself                                       |
| boolean             | `"true"` / `"false"`                         |
| number              | shortest round-trip representation (`1.5`, `2`) |
| array               | each element, recursively (match if **any** element matches) |
| null / missing      | attribute is absent                          |

The reserved attribute `key` is the context key (user id).

## 3. Clauses

A clause matches when **any** normalised attribute value matches **any** clause value.
A missing attribute never matches — `negate` is not applied in that case.

| Operator              | Semantics                                         |
|-----------------------|---------------------------------------------------|
| `in`                  | exact, case-sensitive equality                    |
| `contains`, `startsWith`, `endsWith` | ordinal substring checks           |
| `matches`             | regular expression (invalid pattern → no match)   |
| `lessThan`, `lessThanOrEqual`, `greaterThan`, `greaterThanOrEqual` | numeric; non-numbers never match |
| `semVerEqual`, `semVerLessThan`, `semVerGreaterThan` | lenient semver (`v` prefix and missing minor/patch allowed) |
| `inSegment`           | context belongs to any of the listed segments     |

## 4. Flag evaluation

1. Flag disabled → serve `offVariation` (reason `off`).
2. Rules in order; a rule matches when **all** its clauses match → serve the rule (reason `ruleMatch`).
3. Otherwise serve `fallthrough` (reason `fallthrough`).

A serve is either a fixed `variation` or a `rollout` (see §1). If the `bucketBy` attribute is missing the
first rollout slot is served.

## 5. Segments

1. Context key in `excluded` → not a member.
2. Context key in `included` → member.
3. Any segment rule whose clauses all match → member.

Segment rules cannot reference other segments (no recursion).
