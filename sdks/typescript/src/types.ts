// Wire contract shared with the server. See spec/README.md for semantics.

export type JsonPrimitive = string | number | boolean | null;
export type JsonValue = JsonPrimitive | JsonValue[] | { [key: string]: JsonValue };

export type FlagType = 'boolean' | 'string' | 'number' | 'json';

/** Maps a flag type to the TypeScript type of its values. */
export interface FlagValueMap {
  boolean: boolean;
  string: string;
  number: number;
  json: JsonValue;
}

export type ClauseOperator =
  | 'in'
  | 'contains'
  | 'startsWith'
  | 'endsWith'
  | 'matches'
  | 'lessThan'
  | 'lessThanOrEqual'
  | 'greaterThan'
  | 'greaterThanOrEqual'
  | 'semVerEqual'
  | 'semVerLessThan'
  | 'semVerGreaterThan'
  | 'inSegment';

export interface Clause {
  attribute: string;
  operator: ClauseOperator;
  values: string[];
  negate?: boolean;
}

export interface WeightedVariation {
  variation: string;
  /** Basis points, 0..10000. */
  weight: number;
}

export interface Rollout {
  variations: WeightedVariation[];
  bucketBy?: string | null;
}

/** Either a fixed variation or a percentage rollout. */
export type Serve =
  | { variation: string; rollout?: null }
  | { variation?: null; rollout: Rollout };

export interface TargetingRule {
  id: string;
  clauses: Clause[];
  serve: Serve;
  description?: string | null;
}

export interface Variation {
  key: string;
  value: JsonValue;
}

export interface FlagConfig {
  key: string;
  type: FlagType;
  enabled: boolean;
  salt: string;
  variations: Variation[];
  offVariation: string;
  fallthrough: Serve;
  rules: TargetingRule[];
  version: number;
}

export interface SegmentRule {
  clauses: Clause[];
  description?: string | null;
}

export interface SegmentConfig {
  key: string;
  included: string[];
  excluded: string[];
  rules: SegmentRule[];
  version: number;
}

export type ExperimentStatus = 'draft' | 'running' | 'paused' | 'concluded';

export interface ExperimentVariant {
  key: string;
  weight: number;
  payload?: JsonValue | null;
}

export interface ExperimentConfig {
  key: string;
  status: ExperimentStatus;
  trafficAllocation: number;
  salt: string;
  controlVariant: string;
  primaryMetric: string;
  variants: ExperimentVariant[];
  version: number;
}

export interface SdkConfig {
  flags: FlagConfig[];
  segments: SegmentConfig[];
  experiments: ExperimentConfig[];
  generatedAt: string;
}

export interface Deletion {
  key: string;
  version: number;
}

/** Messages published on the SSE stream, discriminated by event name. */
export type StreamEvent =
  | { type: 'flag.updated'; data: FlagConfig }
  | { type: 'flag.deleted'; data: Deletion }
  | { type: 'segment.updated'; data: SegmentConfig }
  | { type: 'segment.deleted'; data: Deletion }
  | { type: 'experiment.updated'; data: ExperimentConfig }
  | { type: 'experiment.deleted'; data: Deletion };

export type StreamEventType = StreamEvent['type'];

export const STREAM_EVENT_TYPES: readonly StreamEventType[] = [
  'flag.updated',
  'flag.deleted',
  'segment.updated',
  'segment.deleted',
  'experiment.updated',
  'experiment.deleted',
];

// ---------------------------------------------------------------------------------------------------------
// Evaluation

export type AttributeValue = string | number | boolean | null | undefined | ReadonlyArray<string | number | boolean>;

export interface EvaluationContext {
  /** Stable identifier of the subject (user id, account id...). Exposed to rules as attribute `key`. */
  userId: string;
  attributes?: Readonly<Record<string, AttributeValue>>;
}

export type EvaluationReason = 'off' | 'ruleMatch' | 'fallthrough' | 'flagNotFound' | 'wrongType' | 'error';

export type EvaluationDetail<T> =
  | { value: T; variation: string; reason: 'off' | 'fallthrough' }
  | { value: T; variation: string; reason: 'ruleMatch'; ruleId: string }
  | { value: T; variation: null; reason: 'flagNotFound' | 'wrongType' | 'error'; error: string };

export type AssignmentReason = 'assigned' | 'notRunning' | 'notInTraffic' | 'experimentNotFound';

export interface ExperimentAssignment {
  experimentKey: string;
  variant: string | null;
  reason: AssignmentReason;
}

// ---------------------------------------------------------------------------------------------------------
// Events sent to the server

export type TrackEvent =
  | { kind: 'exposure'; experimentKey: string; variant: string; userId: string; timestamp: string }
  | {
      kind: 'conversion';
      experimentKey: string;
      variant: string | null;
      userId: string;
      metric: string | null;
      value: number | null;
      timestamp: string;
    };
