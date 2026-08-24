import { sha256 } from '@noble/hashes/sha2.js';
import { bytesToHex } from '@noble/hashes/utils.js';

const encoder = new TextEncoder();

/** Builds the exact purpose-bound big-endian canonical byte framing shared with Studio Runtime. */
export class StudioCanonicalWriter {
  readonly #chunks: Uint8Array[] = [];
  boolean(value: boolean): void { this.#chunks.push(Uint8Array.of(value ? 1 : 0)); }
  discriminator(value: number): void {
    if (!Number.isInteger(value) || value < 0 || value > 255) throw new RangeError('Studio discriminator is invalid.');
    this.#chunks.push(Uint8Array.of(value));
  }
  int32(value: number): void {
    if (!Number.isInteger(value) || value < -2_147_483_648 || value > 2_147_483_647) throw new RangeError('Studio int32 is invalid.');
    const bytes = new Uint8Array(4); new DataView(bytes.buffer).setInt32(0, value, false); this.#chunks.push(bytes);
  }
  int64(value: string | bigint): void {
    const number = typeof value === 'string' ? BigInt(value) : value;
    if (number < -9_223_372_036_854_775_808n || number > 9_223_372_036_854_775_807n) throw new RangeError('Studio int64 is invalid.');
    const bytes = new Uint8Array(8); new DataView(bytes.buffer).setBigInt64(0, number, false); this.#chunks.push(bytes);
  }
  string(value: string): void { this.bytes(encoder.encode(value)); }
  bytes(value: Uint8Array): void {
    if (value.length > 0xffff_ffff) throw new RangeError('Studio byte value is too large.');
    const length = new Uint8Array(4); new DataView(length.buffer).setUint32(0, value.length, false);
    this.#chunks.push(length, value.slice());
  }
  checksum(value: string): void {
    if (!/^[a-f0-9]{64}$/u.test(value)) throw new TypeError('Studio checksum is invalid.');
    this.#chunks.push(Uint8Array.from(value.match(/../gu)!, part => Number.parseInt(part, 16)));
  }
  count(value: number): void {
    if (!Number.isSafeInteger(value) || value < 0 || value > 0xffff_ffff) throw new RangeError('Studio count is invalid.');
    const bytes = new Uint8Array(4); new DataView(bytes.buffer).setUint32(0, value, false); this.#chunks.push(bytes);
  }
  /** @internal Appends fixed protocol framing without a length prefix. */
  raw(value: Uint8Array): void { this.#chunks.push(value.slice()); }
  finish(): Uint8Array {
    const size = this.#chunks.reduce((sum, value) => sum + value.length, 0); const result = new Uint8Array(size);
    let offset = 0; for (const value of this.#chunks) { result.set(value, offset); offset += value.length; } return result;
  }
}

/** Computes one exact purpose-bound Studio SHA-256 checksum. */
export function studioCanonicalHash(purpose: string, encode: (writer: StudioCanonicalWriter) => void): string {
  if (!/^[A-Za-z0-9.-]+$/u.test(purpose)) throw new TypeError('Studio checksum purpose is invalid.');
  const writer = new StudioCanonicalWriter(); writer.raw(encoder.encode(purpose));
  writer.raw(Uint8Array.of(0, 1)); encode(writer); return bytesToHex(sha256(writer.finish()));
}
