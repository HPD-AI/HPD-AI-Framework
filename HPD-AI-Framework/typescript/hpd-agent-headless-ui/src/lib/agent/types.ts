/**
 * Agent State Types
 *
 * Core type definitions for the Agent state manager.
 */

import type { AgentExecutionContext } from '@hpd/hpd-agent-client';

// ============================================
// Message Types
// ============================================

export type MessageRole = 'user' | 'assistant' | 'system';

export type MessageStatus = 'pending' | 'streaming' | 'thinking' | 'complete' | 'error';

export interface Message {
	id: string;
	role: MessageRole;
	content: string;
	streaming: boolean;
	thinking: boolean;
	timestamp: Date;
	executionContext?: AgentExecutionContext;
	toolCalls: ToolCall[];
	reasoning?: string;
}

// ============================================
// Tool Call Types
// ============================================

export type ToolCallStatus = 'pending' | 'executing' | 'complete' | 'error';

export interface ToolCall {
	callId: string;
	name: string;
	messageId: string;
	status: ToolCallStatus;
	args?: Record<string, unknown>;
	result?: string;
	error?: string;
	startTime: Date;
	endTime?: Date;
}

// ============================================
// Permission Types
// ============================================

export type PermissionChoice = 'ask' | 'allow_always' | 'deny_always';

export interface PermissionRequest {
	permissionId: string;
	sourceName: string;
	functionName: string;
	description?: string;
	callId: string;
	arguments?: Record<string, unknown>;
}

// ============================================
// Clarification Types
// ============================================

export interface ClarificationRequest {
	requestId: string;
	sourceName: string;
	question: string;
	agentName?: string;
	options?: string[];
}

// ============================================
// Client Tool Types
// ============================================

export interface ClientToolInvokeRequest {
	requestId: string;
	toolName: string;
	callId: string;
	arguments: Record<string, unknown>;
	description?: string;
}

// ============================================
// Agent State Options
// ============================================

export interface CreateAgentOptions {
	/** Base URL of the HPD Agent API */
	baseUrl: string;

	/** Session ID (optional, will be generated if not provided) */
	sessionId?: string;

	/** Branch ID (default: 'main') */
	branchId?: string;

	/** Transport type (default: 'sse') */
	transport?: 'sse' | 'websocket';

	/** Additional headers for requests */
	headers?: Record<string, string>;

	/** Client tool groups (pass clientToolKitDefinition[] from hpd-agent-client) */
	clientToolKits?: import('@hpd/hpd-agent-client').clientToolKitDefinition[];

	/** Client tool invocation handler */
	onClientToolInvoke?: (
		request: import('@hpd/hpd-agent-client').ClientToolInvokeRequest
	) => Promise<import('@hpd/hpd-agent-client').ClientToolInvokeResponse>;

	/** Error callback */
	onError?: (message: string) => void;

	/** Completion callback */
	onComplete?: () => void;
}
