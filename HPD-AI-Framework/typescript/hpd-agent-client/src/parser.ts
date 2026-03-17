import type { AgentEvent } from './types/events.js';

/**
 * Parses SSE stream data.
 * Handles:
 * - UTF-8 split across chunk boundaries
 * - Multi-line data fields
 * - Event separation by double newlines
 */
export class SseParser {
  private decoder = new TextDecoder('utf-8', { fatal: false });
  private buffer = '';

  /**
   * Process a chunk of data and return any complete events.
   * @param chunk Raw bytes from the stream
   * @returns Array of parsed events (may be empty if event is incomplete)
   */
  processChunk(chunk: Uint8Array): AgentEvent[] {
    // Decode with stream: true to handle multi-byte UTF-8 split across chunks
    const text = this.decoder.decode(chunk, { stream: true });
    this.buffer += text;

    const events: AgentEvent[] = [];
    const parts = this.buffer.split('\n\n');

    // Keep incomplete event in buffer (last part without trailing \n\n)
    this.buffer = parts.pop() || '';

    for (const part of parts) {
      const event = this.parseEvent(part);
      if (event) {
        events.push(event);
      }
    }

    return events;
  }

  /**
   * Flush any remaining data (call on stream end).
   * @returns Array of any remaining events
   */
  flush(): AgentEvent[] {
    if (!this.buffer.trim()) return [];

    // Final decode to handle any remaining bytes
    this.buffer += this.decoder.decode();

    const event = this.parseEvent(this.buffer);
    this.buffer = '';

    return event ? [event] : [];
  }

  /**
   * Reset the parser state.
   */
  reset(): void {
    this.buffer = '';
    this.decoder = new TextDecoder('utf-8', { fatal: false });
  }

  /**
   * Parse a single SSE event block.
   * Handles multi-line data fields by joining them.
   */
  private parseEvent(eventText: string): AgentEvent | null {
    const lines = eventText.split('\n');
    const dataLines: string[] = [];

    for (const line of lines) {
      if (line.startsWith('data: ')) {
        dataLines.push(line.slice(6));
      } else if (line.startsWith('data:')) {
        // Handle "data:" without space (edge case)
        dataLines.push(line.slice(5));
      }
      // Ignore other SSE fields like event:, id:, retry:
    }

    if (dataLines.length === 0) return null;

    try {
      // Join multi-line data and parse as JSON
      const json = dataLines.join('\n');
      return JSON.parse(json) as AgentEvent;
    } catch {
      // Invalid JSON - skip this event
      return null;
    }
  }
}
