import readline from 'node:readline';
import type { InboundMessage } from '../types/acp.js';

export type MessageHandler = (message: InboundMessage) => void;
export type ErrorHandler = (error: Error) => void;

/**
 * Reads newline-delimited JSON-RPC 2.0 messages from stdin.
 * Each line is a complete JSON message per the ACP spec.
 */
export class AcpReader {
  readonly #rl: readline.Interface;
  #messageHandler: MessageHandler | null = null;
  #errorHandler: ErrorHandler | null = null;

  constructor(input: NodeJS.ReadableStream = process.stdin) {
    this.#rl = readline.createInterface({ input, crlfDelay: Infinity });

    this.#rl.on('line', (line) => {
      const trimmed = line.trim();
      if (!trimmed) return;
      this.#parseLine(trimmed);
    });

    this.#rl.on('close', () => {
      // stdin closed — editor terminated the process; exit cleanly
      process.exit(0);
    });
  }

  onMessage(handler: MessageHandler): void {
    this.#messageHandler = handler;
  }

  onError(handler: ErrorHandler): void {
    this.#errorHandler = handler;
  }

  #parseLine(line: string): void {
    try {
      const parsed = JSON.parse(line) as InboundMessage;
      this.#messageHandler?.(parsed);
    } catch {
      this.#errorHandler?.(new Error(`Failed to parse JSON-RPC message: ${line.slice(0, 200)}`));
    }
  }
}
