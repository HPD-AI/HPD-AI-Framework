import type {
  AiFunctionCallContent,
  AiFunctionResultContent,
  AiTextContent,
  AiTextReasoningContent,
  ThreadMessage,
} from '@hpd-research/hpd-agent-client';
import type { Message, ToolCall } from '../thread/types.js';

export function mapThreadMessages(messages: ThreadMessage[]): Message[] {
  return messages
    .filter((message) => message.role !== 'tool')
    .map(mapThreadMessage);
}

export function mapThreadMessage(message: ThreadMessage): Message {
  let content = '';
  let reasoning: string | undefined;
  const toolCalls: ToolCall[] = [];

  for (const item of message.contents) {
    switch (item.$type) {
      case 'text':
        content += (item as AiTextContent).text;
        break;
      case 'reasoning':
        reasoning = (reasoning ?? '') + (item as AiTextReasoningContent).text;
        break;
      case 'functionCall': {
        const call = item as AiFunctionCallContent;
        toolCalls.push({
          callId: call.callId,
          name: call.name,
          messageId: message.id,
          status: 'complete',
          args: call.arguments,
          startTime: new Date(message.timestamp),
        });
        break;
      }
      case 'functionResult': {
        const result = item as AiFunctionResultContent;
        const match = toolCalls.find((tool) => tool.callId === result.callId);
        if (match) {
          match.resultText = typeof result.result === 'string'
            ? result.result
            : JSON.stringify(result.result);
        }
        break;
      }
      default:
        break;
    }
  }

  return {
    id: message.id,
    role: message.role,
    content,
    streaming: false,
    thinking: false,
    timestamp: new Date(message.timestamp),
    toolCalls,
    reasoning,
    authorName: message.authorName,
  };
}
