import { eventComponents, eventSchemas } from './generated/event-schemas.js';

export interface EventSchema {
  kind: string;
  nullable?: boolean;
  reference?: string;
  primitive?: string;
  literal?: string;
  enumValues?: string[];
  composition?: string;
  properties?: { name: string; required?: boolean; type: EventSchema }[];
  item?: EventSchema;
  value?: EventSchema;
  branches?: EventSchema[];
}

export function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

export function validateEvent(type: string, event: Record<string, unknown>): boolean | undefined {
  const schema = Object.hasOwn(eventSchemas, type) ? eventSchemas[type] : undefined;
  return schema === undefined ? undefined : matches(schema, event);
}

function matches(schema: EventSchema, value: unknown): boolean {
  if (value === null && schema.nullable) return true;

  switch (schema.kind) {
    case 'reference': {
      const id = schema.reference?.split('/').at(-1);
      const target = id && eventComponents[id];
      return !!target && matches(target, value);
    }
    case 'object':
      return isRecord(value) && (schema.properties ?? []).every(property =>
        Object.hasOwn(value, property.name) ? matches(property.type, value[property.name]) : !property.required);
    case 'map':
      return isRecord(value) && Object.values(value).every(item => matches(schema.value!, item));
    case 'array':
      return Array.isArray(value) && value.every(item => matches(schema.item!, item));
    case 'union':
      return schema.composition === 'allOf'
        ? schema.branches!.every(branch => matches(branch, value))
        : schema.branches!.some(branch => matches(branch, value));
    case 'enum':
      return typeof value === 'string' && schema.enumValues!.includes(value);
    case 'literal':
      return value === schema.literal;
    case 'primitive':
      if (schema.primitive === 'integer') return typeof value === 'number' && Number.isInteger(value);
      if (schema.primitive === 'null') return value === null;
      return typeof value === schema.primitive;
    case 'any-json':
      return true;
    default:
      return false;
  }
}
