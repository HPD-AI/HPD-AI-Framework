export type GatewayJsonNode = GatewayJsonNull | GatewayJsonBoolean | GatewayJsonString | GatewayJsonNumber | GatewayJsonArray | GatewayJsonObject;
export type GatewayJsonNull = Readonly<{ kind: 'null' }>;
export type GatewayJsonBoolean = Readonly<{ kind: 'boolean'; value: boolean }>;
export type GatewayJsonString = Readonly<{ kind: 'string'; value: string }>;
export type GatewayJsonNumber = Readonly<{ kind: 'number'; lexeme: string }>;
export type GatewayJsonArray = Readonly<{ kind: 'array'; items: readonly GatewayJsonNode[] }>;
export type GatewayJsonObject = Readonly<{ kind: 'object'; entries: readonly Readonly<{ name: string; value: GatewayJsonNode }>[] }>;

export interface GatewayJsonDiagnostic { readonly code: string; readonly path: string; readonly offset: number; }
export type GatewayJsonParseResult =
  | Readonly<{ ok: true; graph: GatewayJsonObject }>
  | Readonly<{ ok: false; diagnostic: GatewayJsonDiagnostic }>;

const encoder = new TextEncoder();
const maximumBytes = 4 * 1024 * 1024;
const maximumDepth = 64;
const maximumTokens = 500_000;
const maximumProperties = 256;
const maximumItems = 10_000;
const maximumStringBytes = 16 * 1024;

export function parseGatewayJson(source: string): GatewayJsonParseResult {
  try {
    if (encoder.encode(source).byteLength > maximumBytes) return failure('document-too-large', '', 0);
    const parser = new Parser(source);
    parser.space();
    const value = parser.value(0, '');
    parser.space();
    if (!parser.end()) parser.raise('trailing-content', '');
    if (value.kind !== 'object') parser.raise('root-must-be-object', '');
    return Object.freeze({ ok: true, graph: value as GatewayJsonObject });
  } catch (error) {
    if (error instanceof ParseFailure) return failure(error.code, error.path, error.offset);
    return failure('malformed-json', '', 0);
  }
}

export function serializeGatewayJson(node: GatewayJsonNode): string {
  switch (node.kind) {
    case 'null': return 'null';
    case 'boolean': return node.value ? 'true' : 'false';
    case 'string': return JSON.stringify(node.value);
    case 'number': return node.lexeme;
    case 'array': return `[${node.items.map(serializeGatewayJson).join(',')}]`;
    case 'object': return `{${node.entries.map(entry => `${JSON.stringify(entry.name)}:${serializeGatewayJson(entry.value)}`).join(',')}}`;
  }
}

export function gatewayJsonSemanticEqual(left: GatewayJsonNode, right: GatewayJsonNode): boolean {
  if (left.kind !== right.kind) return left.kind === 'number' && right.kind === 'number' && numericEqual(left.lexeme, right.lexeme);
  switch (left.kind) {
    case 'null': return true;
    case 'boolean': return left.value === (right as GatewayJsonBoolean).value;
    case 'string': return left.value === (right as GatewayJsonString).value;
    case 'number': return numericEqual(left.lexeme, (right as GatewayJsonNumber).lexeme);
    case 'array': return left.items.length === (right as GatewayJsonArray).items.length && left.items.every((value, index) => gatewayJsonSemanticEqual(value, (right as GatewayJsonArray).items[index]!));
    case 'object': {
      const other = right as GatewayJsonObject;
      if (left.entries.length !== other.entries.length) return false;
      const map = new Map(other.entries.map(entry => [entry.name, entry.value]));
      return left.entries.every(entry => map.has(entry.name) && gatewayJsonSemanticEqual(entry.value, map.get(entry.name)!));
    }
  }
}

class Parser {
  private index = 0;
  private tokens = 0;
  constructor(private readonly source: string) {}
  end(): boolean { return this.index === this.source.length; }
  space(): void { while (/\s/u.test(this.source[this.index] ?? '')) this.index++; }
  value(depth: number, path: string): GatewayJsonNode {
    if (depth > maximumDepth) this.raise('depth-exceeded', path);
    if (++this.tokens > maximumTokens) this.raise('token-bound-exceeded', path);
    const current = this.source[this.index];
    if (current === '{') return this.object(depth, path);
    if (current === '[') return this.array(depth, path);
    if (current === '"') return Object.freeze({ kind: 'string', value: this.string(path) });
    if (current === 't' && this.take('true')) return Object.freeze({ kind: 'boolean', value: true });
    if (current === 'f' && this.take('false')) return Object.freeze({ kind: 'boolean', value: false });
    if (current === 'n' && this.take('null')) return Object.freeze({ kind: 'null' });
    const number = this.number();
    if (number !== null) return Object.freeze({ kind: 'number', lexeme: number });
    this.raise('invalid-value', path);
  }
  object(depth: number, path: string): GatewayJsonObject {
    this.index++; this.space();
    const entries: { name: string; value: GatewayJsonNode }[] = [];
    const names = new Set<string>();
    if (this.source[this.index] === '}') { this.index++; return Object.freeze({ kind: 'object', entries: Object.freeze(entries) }); }
    while (true) {
      if (entries.length >= maximumProperties) this.raise('property-bound-exceeded', path);
      if (this.source[this.index] !== '"') this.raise('property-name-required', path);
      const name = this.string(path);
      if (names.has(name)) this.raise('duplicate-property', `${path}/${escapePointer(name)}`);
      names.add(name); this.space();
      if (this.source[this.index++] !== ':') this.raise('colon-required', path);
      this.space(); const childPath = `${path}/${escapePointer(name)}`;
      entries.push(Object.freeze({ name, value: this.value(depth + 1, childPath) })); this.space();
      const delimiter = this.source[this.index++];
      if (delimiter === '}') break;
      if (delimiter !== ',') this.raise('object-delimiter-required', path);
      this.space();
    }
    return Object.freeze({ kind: 'object', entries: Object.freeze(entries) });
  }
  array(depth: number, path: string): GatewayJsonArray {
    this.index++; this.space(); const items: GatewayJsonNode[] = [];
    if (this.source[this.index] === ']') { this.index++; return Object.freeze({ kind: 'array', items: Object.freeze(items) }); }
    while (true) {
      if (items.length >= maximumItems) this.raise('array-bound-exceeded', path);
      items.push(this.value(depth + 1, `${path}/${items.length}`)); this.space();
      const delimiter = this.source[this.index++];
      if (delimiter === ']') break;
      if (delimiter !== ',') this.raise('array-delimiter-required', path);
      this.space();
    }
    return Object.freeze({ kind: 'array', items: Object.freeze(items) });
  }
  string(path: string): string {
    const start = this.index++;
    while (this.index < this.source.length) {
      const current = this.source.charCodeAt(this.index++);
      if (current === 0x22) {
        const lexeme = this.source.slice(start, this.index);
        let value: string;
        try { value = JSON.parse(lexeme) as string; } catch { this.raise('invalid-string', path); }
        if (!wellFormed(value!) || encoder.encode(value!).byteLength > maximumStringBytes) this.raise('invalid-string', path);
        return value!;
      }
      if (current < 0x20) this.raise('invalid-string', path);
      if (current === 0x5c) {
        const escaped = this.source[this.index++];
        if (escaped === 'u') {
          if (!/^[0-9a-fA-F]{4}$/u.test(this.source.slice(this.index, this.index + 4))) this.raise('invalid-string-escape', path);
          this.index += 4;
        } else if (!'"\\/bfnrt'.includes(escaped ?? '')) this.raise('invalid-string-escape', path);
      }
    }
    this.raise('unterminated-string', path);
  }
  number(): string | null {
    const match = /^-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?/u.exec(this.source.slice(this.index));
    if (match === null) return null;
    this.index += match[0].length; return match[0];
  }
  take(value: string): boolean { if (!this.source.startsWith(value, this.index)) return false; this.index += value.length; return true; }
  raise(code: string, path: string): never { throw new ParseFailure(code, path, this.index); }
}

class ParseFailure extends Error { constructor(readonly code: string, readonly path: string, readonly offset: number) { super(code); } }
function failure(code: string, path: string, offset: number): GatewayJsonParseResult { return Object.freeze({ ok: false, diagnostic: Object.freeze({ code, path, offset }) }); }
function escapePointer(value: string): string { return value.replaceAll('~', '~0').replaceAll('/', '~1'); }
function wellFormed(value: string): boolean { for (let i = 0; i < value.length; i++) { const c = value.charCodeAt(i); if (c >= 0xd800 && c <= 0xdbff) { const n = value.charCodeAt(++i); if (!(n >= 0xdc00 && n <= 0xdfff)) return false; } else if (c >= 0xdc00 && c <= 0xdfff) return false; } return true; }
function numericEqual(left: string, right: string): boolean { try { return normalizeNumber(left) === normalizeNumber(right); } catch { return false; } }
function normalizeNumber(value: string): string {
  const match = /^(-?)(\d+)(?:\.(\d+))?(?:[eE]([+-]?\d+))?$/u.exec(value)!;
  let digits = `${match[2]}${match[3] ?? ''}`.replace(/^0+/u, '') || '0';
  let scale = (match[3]?.length ?? 0) - Number(match[4] ?? 0);
  while (digits.endsWith('0') && scale > 0) { digits = digits.slice(0, -1); scale--; }
  return digits === '0' ? '0' : `${match[1]}${digits}e${-scale}`;
}
