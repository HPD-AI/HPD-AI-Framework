/**
 * Message Component Tests
 *
 * Tests for the Message component with AI-specific features.
 */

import { describe, it, expect, beforeEach } from 'vitest';
import { MessageState } from '../message.svelte.ts';
import type { Message } from '../../agent/types.ts';

describe('MessageState', () => {
	let testMessage: Message;

	beforeEach(() => {
		testMessage = {
			id: 'msg-123',
			role: 'assistant',
			content: 'Hello, this is a test message.',
			streaming: false,
			thinking: false,
			reasoning: '',
			toolCalls: [],
			timestamp: new Date('2024-12-23T00:00:00Z')
		};
	});

	describe('Basic initialization', () => {
		it('should initialize with message data', () => {
			const state = new MessageState(testMessage);

			expect(state.id).toBe('msg-123');
			expect(state.role).toBe('assistant');
			expect(state.content).toBe('Hello, this is a test message.');
			expect(state.streaming).toBe(false);
			expect(state.thinking).toBe(false);
		});

		it('should handle user messages', () => {
			const userMessage: Message = {
				...testMessage,
				role: 'user',
				content: 'User question'
			};

			const state = new MessageState(userMessage);

			expect(state.role).toBe('user');
			expect(state.content).toBe('User question');
		});

		it('should handle system messages', () => {
			const systemMessage: Message = {
				...testMessage,
				role: 'system',
				content: 'System message'
			};

			const state = new MessageState(systemMessage);

			expect(state.role).toBe('system');
		});
	});

	describe('AI-specific states', () => {
		it('should track streaming state', () => {
			const streamingMessage: Message = {
				...testMessage,
				streaming: true,
				content: 'Partial mes'
			};

			const state = new MessageState(streamingMessage);

			expect(state.streaming).toBe(true);
			expect(state.status).toBe('streaming');
		});

		it('should track thinking state', () => {
			const thinkingMessage: Message = {
				...testMessage,
				thinking: true
			};

			const state = new MessageState(thinkingMessage);

			expect(state.thinking).toBe(true);
			expect(state.status).toBe('thinking');
		});

		it('should track reasoning text', () => {
			const reasoningMessage: Message = {
				...testMessage,
				reasoning: 'Let me think about this...'
			};

			const state = new MessageState(reasoningMessage);

			expect(state.hasReasoning).toBe(true);
			expect(state.reasoning).toBe('Let me think about this...');
		});

		it('should track tool execution', () => {
			const messageWithTools: Message = {
				...testMessage,
				toolCalls: [
					{
						callId: 'tool-1',
						name: 'search',
						messageId: 'msg-123',
						status: 'executing',
						args: { query: 'test' },
						startTime: new Date()
					}
				]
			};

			const state = new MessageState(messageWithTools);

			expect(state.hasTools).toBe(true);
			expect(state.hasActiveTools).toBe(true);
			expect(state.status).toBe('executing');
		});
	});

	describe('Status derivation', () => {
		it('should prioritize streaming status', () => {
			const state = new MessageState({
				...testMessage,
				streaming: true,
				thinking: true
			});

			expect(state.status).toBe('streaming');
		});

		it('should show thinking when not streaming', () => {
			const state = new MessageState({
				...testMessage,
				streaming: false,
				thinking: true
			});

			expect(state.status).toBe('thinking');
		});

		it('should show executing when tools are active', () => {
			const state = new MessageState({
				...testMessage,
				streaming: false,
				thinking: false,
				toolCalls: [
					{
						callId: 'tool-1',
						name: 'search',
						messageId: 'msg-123',
						status: 'pending',
						startTime: new Date()
					}
				]
			});

			expect(state.status).toBe('executing');
		});

		it('should show complete when idle', () => {
			const state = new MessageState({
				...testMessage,
				streaming: false,
				thinking: false,
				toolCalls: []
			});

			expect(state.status).toBe('complete');
		});

		it('should show complete when tools are done', () => {
			const state = new MessageState({
				...testMessage,
				toolCalls: [
					{
						callId: 'tool-1',
						name: 'search',
						messageId: 'msg-123',
						status: 'complete',
						result: 'Done',
						startTime: new Date(),
						endTime: new Date()
					}
				]
			});

			expect(state.status).toBe('complete');
			expect(state.hasActiveTools).toBe(false);
		});
	});

	describe('HTML props generation', () => {
		it('should generate data attributes', () => {
			const state = new MessageState(testMessage);
			const props = state.props;

			expect(props['data-message-id']).toBe('msg-123');
			expect(props['data-role']).toBe('assistant');
			expect(props['data-status']).toBe('complete');
		});

		it('should generate ARIA attributes for streaming', () => {
			const state = new MessageState({
				...testMessage,
				streaming: true
			});

			expect(state.props['aria-live']).toBe('polite');
			expect(state.props['aria-busy']).toBe(true);
		});

		it('should generate ARIA attributes for thinking', () => {
			const state = new MessageState({
				...testMessage,
				thinking: true
			});

			expect(state.props['aria-busy']).toBe(true);
		});

		it('should set aria-live to off when not streaming', () => {
			const state = new MessageState({
				...testMessage,
				streaming: false
			});

			expect(state.props['aria-live']).toBe('off');
		});

		it('should include conditional data attributes', () => {
			const state = new MessageState({
				...testMessage,
				streaming: true,
				reasoning: 'Thinking...',
				toolCalls: [
					{
						callId: 'tool-1',
						name: 'test',
						messageId: 'msg-123',
						status: 'pending',
						startTime: new Date()
					}
				]
			});

			expect(state.props['data-streaming']).toBe('');
			expect(state.props['data-has-reasoning']).toBe('');
			expect(state.props['data-has-tools']).toBe('');
		});
	});

	describe('Snippet props generation', () => {
		it('should generate all snippet props', () => {
			const state = new MessageState({
				...testMessage,
				streaming: true,
				thinking: false,
				reasoning: 'Let me think...',
				toolCalls: []
			});

			const snippetProps = state.snippetProps;

			expect(snippetProps.content).toBe('Hello, this is a test message.');
			expect(snippetProps.role).toBe('assistant');
			expect(snippetProps.streaming).toBe(true);
			expect(snippetProps.thinking).toBe(false);
			expect(snippetProps.hasReasoning).toBe(true);
			expect(snippetProps.reasoning).toBe('Let me think...');
			expect(snippetProps.toolCalls).toEqual([]);
			expect(snippetProps.hasActiveTools).toBe(false);
			expect(snippetProps.status).toBe('streaming');
		});
	});

	describe('State updates', () => {
		it('should update content when update() is called', () => {
			const state = new MessageState(testMessage);

			expect(state.content).toBe('Hello, this is a test message.');

			state.update({
				...testMessage,
				content: 'Updated message content'
			});

			expect(state.content).toBe('Updated message content');
		});

		it('should update streaming state', () => {
			const state = new MessageState(testMessage);

			expect(state.streaming).toBe(false);

			state.update({
				...testMessage,
				streaming: true
			});

			expect(state.streaming).toBe(true);
			expect(state.status).toBe('streaming');
		});

		it('should update all fields', () => {
			const state = new MessageState(testMessage);

			state.update({
				...testMessage,
				content: 'New content',
				streaming: true,
				thinking: true,
				reasoning: 'New reasoning',
				toolCalls: [
					{
						callId: 'tool-1',
						name: 'test',
						messageId: 'msg-123',
						status: 'pending',
						startTime: new Date()
					}
				]
			});

			expect(state.content).toBe('New content');
			expect(state.streaming).toBe(true);
			expect(state.thinking).toBe(true);
			expect(state.reasoning).toBe('New reasoning');
			expect(state.toolCalls).toHaveLength(1);
		});
	});
});
