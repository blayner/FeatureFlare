import { bucket, pick } from './bucketing.js';
import { compareSemVer, parseSemVer } from './semver.js';
import type {
  AttributeValue,
  Clause,
  ClauseOperator,
  EvaluationContext,
  EvaluationDetail,
  ExperimentAssignment,
  ExperimentConfig,
  FlagConfig,
  JsonValue,
  SegmentConfig,
  Serve,
} from './types.js';

export interface ConfigSource {
  getFlag(key: string): FlagConfig | undefined;
  getSegment(key: string): SegmentConfig | undefined;
  getExperiment(key: string): ExperimentConfig | undefined;
}

const KEY_ATTRIBUTE = 'key';
const regexCache = new Map<string, RegExp | null>();

/**
 * Local implementation of the evaluation algorithm in spec/README.md. Pure: it never performs I/O, so it
 * can be called on every request without latency concerns.
 */
export class Evaluator {
  constructor(private readonly source: ConfigSource) {}

  evaluate(flagKey: string, context: EvaluationContext): EvaluationDetail<JsonValue | undefined> {
    const flag = this.source.getFlag(flagKey);
    if (!flag) {
      return { value: undefined, variation: null, reason: 'flagNotFound', error: `Flag '${flagKey}' not found` };
    }
    return this.evaluateFlag(flag, context);
  }

  evaluateFlag(flag: FlagConfig, context: EvaluationContext): EvaluationDetail<JsonValue | undefined> {
    if (!flag.enabled) {
      return this.variation(flag, flag.offVariation, 'off');
    }

    for (const rule of flag.rules) {
      if (rule.clauses.every((clause) => this.matchClause(clause, context, true))) {
        const variation = this.resolveServe(flag, rule.serve, context);
        return variation === null
          ? error(`Rule '${rule.id}' has an invalid serve`)
          : this.variation(flag, variation, 'ruleMatch', rule.id);
      }
    }

    const variation = this.resolveServe(flag, flag.fallthrough, context);
    return variation === null ? error('Fallthrough has an invalid serve') : this.variation(flag, variation, 'fallthrough');
  }

  assign(experimentKey: string, userId: string): ExperimentAssignment {
    const experiment = this.source.getExperiment(experimentKey);
    if (!experiment) return { experimentKey, variant: null, reason: 'experimentNotFound' };
    if (experiment.status !== 'running') return { experimentKey, variant: null, reason: 'notRunning' };

    if (bucket(experiment.key, `${experiment.salt}.traffic`, userId) >= experiment.trafficAllocation) {
      return { experimentKey, variant: null, reason: 'notInTraffic' };
    }

    const index = pick(experiment.variants, (v) => v.weight, bucket(experiment.key, experiment.salt, userId));
    return { experimentKey, variant: experiment.variants[index]?.key ?? null, reason: 'assigned' };
  }

  isInSegment(segment: SegmentConfig, context: EvaluationContext): boolean {
    if (segment.excluded.includes(context.userId)) return false;
    if (segment.included.includes(context.userId)) return true;
    // Segment rules cannot reference segments (enforced by the server), hence allowSegments = false.
    return segment.rules.some((rule) => rule.clauses.every((c) => this.matchClause(c, context, false)));
  }

  private resolveServe(flag: FlagConfig, serve: Serve, context: EvaluationContext): string | null {
    if (serve.variation) return serve.variation;
    const rollout = serve.rollout;
    if (!rollout || rollout.variations.length === 0) return null;

    const bucketValue = bucketingValue(context, rollout.bucketBy);
    if (bucketValue === undefined) return rollout.variations[0]!.variation;

    const index = pick(rollout.variations, (v) => v.weight, bucket(flag.key, flag.salt, bucketValue));
    return rollout.variations[index]!.variation;
  }

  private matchClause(clause: Clause, context: EvaluationContext, allowSegments: boolean): boolean {
    if (clause.operator === 'inSegment') {
      if (!allowSegments) return false;
      const member = clause.values.some((key) => {
        const segment = this.source.getSegment(key);
        return segment !== undefined && this.isInSegment(segment, context);
      });
      return clause.negate ? !member : member;
    }

    const actual = attributeValues(context, clause.attribute);
    // Missing attributes never match, even when negated (see spec §3).
    if (actual.length === 0) return false;

    const matched = actual.some((a) => clause.values.some((e) => matchValue(clause.operator, a, e)));
    return clause.negate ? !matched : matched;
  }

  private variation(
    flag: FlagConfig,
    key: string,
    reason: 'off' | 'fallthrough' | 'ruleMatch',
    ruleId?: string,
  ): EvaluationDetail<JsonValue | undefined> {
    const variation = flag.variations.find((v) => v.key === key);
    if (!variation) return error(`Variation '${key}' not found in flag '${flag.key}'`);
    return reason === 'ruleMatch'
      ? { value: variation.value, variation: variation.key, reason, ruleId: ruleId ?? '' }
      : { value: variation.value, variation: variation.key, reason };
  }
}

function error(message: string): EvaluationDetail<JsonValue | undefined> {
  return { value: undefined, variation: null, reason: 'error', error: message };
}

/** Normalises attribute values to strings exactly like the server (spec §2). */
export function attributeValues(context: EvaluationContext, attribute: string): string[] {
  if (attribute === KEY_ATTRIBUTE) return [context.userId];
  return normalize(context.attributes?.[attribute]);
}

function normalize(value: AttributeValue): string[] {
  if (value === null || value === undefined) return [];
  if (Array.isArray(value)) return value.flatMap((v) => normalize(v as AttributeValue));
  if (typeof value === 'boolean') return [value ? 'true' : 'false'];
  return [String(value)];
}

function bucketingValue(context: EvaluationContext, bucketBy: string | null | undefined): string | undefined {
  if (!bucketBy || bucketBy === KEY_ATTRIBUTE) return context.userId;
  return attributeValues(context, bucketBy)[0];
}

export function matchValue(operator: ClauseOperator, actual: string, expected: string): boolean {
  switch (operator) {
    case 'in':
      return actual === expected;
    case 'contains':
      return actual.includes(expected);
    case 'startsWith':
      return actual.startsWith(expected);
    case 'endsWith':
      return actual.endsWith(expected);
    case 'matches':
      return matchRegex(actual, expected);
    case 'lessThan':
      return compareNumbers(actual, expected, (c) => c < 0);
    case 'lessThanOrEqual':
      return compareNumbers(actual, expected, (c) => c <= 0);
    case 'greaterThan':
      return compareNumbers(actual, expected, (c) => c > 0);
    case 'greaterThanOrEqual':
      return compareNumbers(actual, expected, (c) => c >= 0);
    case 'semVerEqual':
      return compareVersions(actual, expected, (c) => c === 0);
    case 'semVerLessThan':
      return compareVersions(actual, expected, (c) => c < 0);
    case 'semVerGreaterThan':
      return compareVersions(actual, expected, (c) => c > 0);
    case 'inSegment':
      return false;
  }
}

function matchRegex(actual: string, pattern: string): boolean {
  let regex = regexCache.get(pattern);
  if (regex === undefined) {
    try {
      regex = new RegExp(pattern);
    } catch {
      regex = null;
    }
    regexCache.set(pattern, regex);
  }
  return regex !== null && regex.test(actual);
}

function toNumber(text: string): number | null {
  if (text.trim() === '') return null;
  const n = Number(text);
  return Number.isFinite(n) ? n : null;
}

function compareNumbers(actual: string, expected: string, predicate: (c: number) => boolean): boolean {
  const a = toNumber(actual);
  const b = toNumber(expected);
  return a !== null && b !== null && predicate(Math.sign(a - b));
}

function compareVersions(actual: string, expected: string, predicate: (c: number) => boolean): boolean {
  const a = parseSemVer(actual);
  const b = parseSemVer(expected);
  return a !== null && b !== null && predicate(compareSemVer(a, b));
}
