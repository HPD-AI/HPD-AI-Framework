/**
 * Artifact State - Reactive State Manager for Compositional Artifact System
 *
 * This component provides shared state and behavior for artifact teleportation.
 * Content defined in one place (Artifact.Slot) renders elsewhere (Artifact.Panel).
 *
 * Key Features:
 * - Global artifact registry using SvelteMap for granular reactivity
 * - Content teleportation via snippets stored in context
 * - Single artifact open at a time
 * - Animation support via PresenceManager
 *
 * Architecture:
 * - ArtifactProviderState: Global registry and open/close state
 * - ArtifactRootState: Individual artifact instance state
 * - ArtifactSlotState: Registers content with provider
 * - ArtifactPanelState: Renders current artifact with presence
 */

import { SvelteMap } from 'svelte/reactivity';
import { Context } from 'runed';
import { boxWith, type ReadableBoxedValues, type WritableBoxedValues } from 'svelte-toolbelt';
import { createHPDAttrs } from '$lib/internal/attrs.js';
import { PresenceManager } from '$lib/internal/presence-manager.svelte.js';
import type { OnChangeFn } from '$lib/internal/types.js';
import type { Snippet } from 'svelte';
import type { ArtifactSlotData } from './types.js';

// ============================================
// Data Attributes for CSS Hooks
// ============================================

const artifactAttrs = createHPDAttrs({
	component: 'artifact',
	parts: ['provider', 'root', 'slot', 'trigger', 'panel', 'title', 'content', 'close'] as const
});

export { artifactAttrs };

// ============================================
// Context
// ============================================

const ArtifactProviderContext = new Context<ArtifactProviderState>('Artifact.Provider');
const ArtifactRootContext = new Context<ArtifactRootState>('Artifact.Root');

// ============================================
// Provider State (Global Artifact Registry)
// ============================================

interface ArtifactProviderStateOpts
	extends ReadableBoxedValues<{
		onOpenChange?: (open: boolean, id: string | null) => void;
	}> {}

export class ArtifactProviderState {
	static create(opts: ArtifactProviderStateOpts) {
		return ArtifactProviderContext.set(new ArtifactProviderState(opts));
	}

	static get() {
		return ArtifactProviderContext.get();
	}

	readonly opts: ArtifactProviderStateOpts;

	// Use SvelteMap for granular reactivity on map operations
	#slots = new SvelteMap<string, ArtifactSlotData>();

	// Track which artifact is open
	#openId = $state<string | null>(null);

	// Track which artifact is mounted (for animation timing)
	#mountedId = $state<string | null>(null);

	// Derived state
	readonly open = $derived(this.#openId !== null);
	readonly openId = $derived(this.#openId);
	readonly mountedId = $derived(this.#mountedId);

	readonly currentSlot = $derived.by(() => {
		const id = this.#mountedId ?? this.#openId;
		return id ? this.#slots.get(id) ?? null : null;
	});

	readonly title = $derived(this.currentSlot?.title ?? null);
	readonly content = $derived(this.currentSlot?.content ?? null);

	constructor(opts: ArtifactProviderStateOpts) {
		this.opts = opts;
	}

	// Open an artifact by ID
	openArtifact(id: string): void {
		const previousId = this.#openId;
		this.#openId = id;
		this.#mountedId = id;

		// Call onOpenChange callback when opening a new artifact
		const onOpenChange = this.opts.onOpenChange?.current;
		if (onOpenChange && previousId !== id) {
			onOpenChange(true, id);
		}
	}

	// Close the current artifact
	closeArtifact(): void {
		if (this.#openId === null) return;

		const closedId = this.#openId;
		this.#openId = null;
		// mountedId stays for animation - cleared by PresenceManager

		// Call onOpenChange callback
		const onOpenChange = this.opts.onOpenChange?.current;
		if (onOpenChange) {
			onOpenChange(false, closedId);
		}
	}

	// Clear mounted ID (called after close animation)
	clearMounted(): void {
		if (this.#openId === null) {
			this.#mountedId = null;
		}
	}

	// Register a slot's content
	registerSlot(id: string, data: ArtifactSlotData): void {
		this.#slots.set(id, data);
	}

	// Unregister a slot
	unregisterSlot(id: string): void {
		this.#slots.delete(id);
		// Close if the unregistered slot was open
		if (this.#openId === id) {
			this.closeArtifact();
		}
		if (this.#mountedId === id) {
			this.#mountedId = null;
		}
	}

	// Check if an artifact is registered
	hasSlot(id: string): boolean {
		return this.#slots.has(id);
	}

	// Get HPD attribute for a part
	getHPDAttr: typeof artifactAttrs.getAttr = (part) => {
		return artifactAttrs.getAttr(part);
	};

	// Shared props for provider
	readonly sharedProps = $derived.by(
		() =>
			({
				'data-open': this.open ? '' : undefined,
				'data-artifact-id': this.openId ?? undefined
			}) as const
	);
}

// ============================================
// Root State (Individual Artifact Instance)
// ============================================

interface ArtifactRootStateOpts
	extends ReadableBoxedValues<{
		id: string;
		defaultOpen?: boolean;
		onOpenChange?: OnChangeFn<boolean>;
	}> {}

export class ArtifactRootState {
	static create(opts: ArtifactRootStateOpts) {
		return ArtifactRootContext.set(new ArtifactRootState(opts, ArtifactProviderState.get()));
	}

	static get() {
		return ArtifactRootContext.get();
	}

	readonly opts!: ArtifactRootStateOpts;
	readonly provider!: ArtifactProviderState;

	constructor(opts: ArtifactRootStateOpts, provider: ArtifactProviderState) {
		this.opts = opts;
		this.provider = provider;

		// Handle defaultOpen
		if (opts.defaultOpen?.current) {
			this.provider.openArtifact(opts.id.current);
		}
	}

	// Current ID
	readonly id = $derived(this.opts.id.current);

	// Whether this artifact is open
	readonly open = $derived(this.provider.openId === this.opts.id.current);

	// Set open state
	setOpen(open: boolean): void {
		const id = this.opts.id.current;
		if (open) {
			this.provider.openArtifact(id);
		} else if (this.provider.openId === id) {
			this.provider.closeArtifact();
		}

		// Call instance-level onOpenChange callback
		const onOpenChange = this.opts.onOpenChange?.current;
		if (onOpenChange) {
			onOpenChange(open);
		}
	}

	// Toggle open state
	toggle(): void {
		this.setOpen(!this.open);
	}

	// Get HPD attribute for a part
	getHPDAttr: typeof artifactAttrs.getAttr = (part) => {
		return artifactAttrs.getAttr(part);
	};

	// Shared props for root
	readonly sharedProps = $derived.by(
		() =>
			({
				'data-open': this.open ? '' : undefined,
				'data-artifact-id': this.id
			}) as const
	);
}

// ============================================
// Slot State (Content Registration)
// ============================================

interface ArtifactSlotStateOpts
	extends ReadableBoxedValues<{
		title?: Snippet | null;
		content?: Snippet | null;
	}> {}

export class ArtifactSlotState {
	readonly root!: ArtifactRootState;
	readonly provider!: ArtifactProviderState;
	readonly opts!: ArtifactSlotStateOpts;

	constructor(opts: ArtifactSlotStateOpts, root: ArtifactRootState, provider: ArtifactProviderState) {
		this.opts = opts;
		this.root = root;
		this.provider = provider;
	}

	static create(opts: ArtifactSlotStateOpts) {
		return new ArtifactSlotState(opts, ArtifactRootState.get(), ArtifactProviderState.get());
	}

	// Register content with provider
	register(): void {
		this.provider.registerSlot(this.root.id, {
			id: this.root.id,
			title: this.opts.title?.current ?? null,
			content: this.opts.content?.current ?? null
		});
	}

	// Update content
	update(): void {
		this.register();
	}

	// Unregister from provider
	unregister(): void {
		this.provider.unregisterSlot(this.root.id);
	}

	// Whether this slot is mounted (rendered in panel)
	readonly isMounted = $derived(this.provider.mountedId === this.root.id);

	// Get HPD attribute for a part
	getHPDAttr: typeof artifactAttrs.getAttr = (part) => {
		return artifactAttrs.getAttr(part);
	};
}

// ============================================
// Panel State (Render Target with Presence)
// ============================================

interface ArtifactPanelStateOpts
	extends WritableBoxedValues<{
		ref: HTMLElement | null;
	}> {}

export class ArtifactPanelState {
	readonly provider!: ArtifactProviderState;
	readonly presence!: PresenceManager;

	constructor(opts: ArtifactPanelStateOpts, provider: ArtifactProviderState) {
		this.provider = provider;

		// Create presence manager for animations
		this.presence = new PresenceManager({
			open: boxWith(() => provider.open),
			ref: opts.ref,
			onComplete: () => {
				// Clear mounted ID after close animation completes
				provider.clearMounted();
			}
		});
	}

	static create(opts: ArtifactPanelStateOpts) {
		return new ArtifactPanelState(opts, ArtifactProviderState.get());
	}

	// Pass through from provider
	readonly open = $derived(this.provider.open);
	readonly openId = $derived(this.provider.openId);
	readonly title = $derived(this.provider.title);
	readonly content = $derived(this.provider.content);
	readonly shouldRender = $derived(this.presence.shouldRender);

	// Close the current artifact
	close(): void {
		this.provider.closeArtifact();
	}

	// Get HPD attribute for a part
	getHPDAttr: typeof artifactAttrs.getAttr = (part) => {
		return artifactAttrs.getAttr(part);
	};

	// Shared props for panel
	readonly sharedProps = $derived.by(
		() =>
			({
				'data-open': this.open ? '' : undefined,
				'data-state': this.open ? 'open' : 'closed',
				'data-artifact-id': this.openId ?? undefined
			}) as const
	);
}

// ============================================
// Trigger State
// ============================================

export class ArtifactTriggerState {
	readonly root!: ArtifactRootState;

	constructor(root: ArtifactRootState) {
		this.root = root;
	}

	static create() {
		return new ArtifactTriggerState(ArtifactRootState.get());
	}

	readonly open = $derived(this.root.open);

	toggle(): void {
		this.root.toggle();
	}

	// Get HPD attribute for a part
	getHPDAttr: typeof artifactAttrs.getAttr = (part) => {
		return artifactAttrs.getAttr(part);
	};

	// Shared props for trigger
	readonly sharedProps = $derived.by(
		() =>
			({
				'data-open': this.open ? '' : undefined
			}) as const
	);
}

// ============================================
// Close State
// ============================================

export class ArtifactCloseState {
	readonly provider!: ArtifactProviderState;

	constructor(provider: ArtifactProviderState) {
		this.provider = provider;
	}

	static create() {
		return new ArtifactCloseState(ArtifactProviderState.get());
	}

	readonly open = $derived(this.provider.open);

	close(): void {
		this.provider.closeArtifact();
	}

	// Get HPD attribute for a part
	getHPDAttr: typeof artifactAttrs.getAttr = (part) => {
		return artifactAttrs.getAttr(part);
	};

	// Shared props for close
	readonly sharedProps = $derived.by(
		() =>
			({
				'data-open': this.open ? '' : undefined
			}) as const
	);
}
