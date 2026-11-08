import type { AgentEvent } from './types/events.js';
import type { AgentEventDelivery } from './types/event-delivery.js';

export interface AgentEventSseMessage {
  kind: 'agent-event' | 'live-agent-event';
  id: string | null;
  delivery: AgentEventDelivery;
}

export interface ControlSseMessage {
  kind: 'control';
  id: string | null;
  eventName: string;
  data: unknown;
}

export type SseMessage = AgentEventSseMessage | ControlSseMessage;

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
  processChunk(chunk: Uint8Array): SseMessage[] {
    // Decode with stream: true to handle multi-byte UTF-8 split across chunks
    const text = this.decoder.decode(chunk, { stream: true });
    this.buffer += text;

    const events: SseMessage[] = [];
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
  flush(): SseMessage[] {
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
  private parseEvent(eventText: string): SseMessage | null {
    const lines = eventText.split('\n');
    const dataLines: string[] = [];
    let id: string | null = null;
    let eventName = 'message';

    for (const line of lines) {
      if (line.startsWith('data: ')) {
        dataLines.push(line.slice(6));
      } else if (line.startsWith('data:')) {
        // Handle "data:" without space (edge case)
        dataLines.push(line.slice(5));
      } else if (line.startsWith('id:')) {
        id = line.slice(3).trim();
      } else if (line.startsWith('event:')) {
        eventName = line.slice(6).trim();
      }
    }

    if (dataLines.length === 0) return null;

    try {
      // Join multi-line data and parse as JSON
      const json = dataLines.join('\n');
      const parsed = JSON.parse(json);
      if (isAgentEventDelivery(parsed)) {
        return {
          kind: eventName === 'live-agent-event' ? 'live-agent-event' : 'agent-event',
          id,
          delivery: parsed,
        };
      }
      return eventName !== 'message'
        ? { kind: 'control', id, eventName, data: parsed }
        : null;
    } catch {
      // Invalid JSON - skip this event
      return null;
    }
  }
}

function isAgentEventLike(value: unknown): value is AgentEvent {
  return value !== null &&
    typeof value === 'object' &&
    typeof (value as { type?: unknown }).type === 'string';
}

function isAgentEventDelivery(value: unknown): value is AgentEventDelivery {
  if (value === null || typeof value !== 'object') return false;
  const delivery = value as { event?: unknown; route?: unknown };
  if (!isAgentEventLike(delivery.event) || delivery.route === null || typeof delivery.route !== 'object') return false;
  const route = delivery.route as { origin?: unknown; path?: unknown };
  if (!isThreadKey(route.origin) || !Array.isArray(route.path) ||
      route.path.length === 0 || !route.path.every(isThreadKey)) return false;
  const origin = route.origin as { sessionId: string; threadId: string };
  const tail = route.path[route.path.length - 1] as { sessionId: string; threadId: string };
  return tail.sessionId === origin.sessionId && tail.threadId === origin.threadId;
}

function isThreadKey(value: unknown): boolean {
  return value !== null && typeof value === 'object' &&
    typeof (value as { sessionId?: unknown }).sessionId === 'string' &&
    (value as { sessionId: string }).sessionId.trim().length > 0 &&
    typeof (value as { threadId?: unknown }).threadId === 'string' &&
    (value as { threadId: string }).threadId.trim().length > 0;
}
