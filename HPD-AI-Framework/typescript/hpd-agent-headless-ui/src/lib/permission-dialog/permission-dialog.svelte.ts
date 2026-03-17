/**
 * PermissionDialog State - Reactive State Manager for Permission Requests
 *
 * This component handles HPD PERMISSION_REQUEST events and provides a modal dialog
 * for users to approve or deny tool execution.
 *
 * Key Features:
 * - Auto-opens when permission request arrives
 * - Bidirectional async flow (blocks agent until user responds)
 * - Supports multiple permission choices (ask, allow_always, deny_always)
 * - Full ARIA accessibility
 *
 * Architecture:
 * - Simple root state (no trigger, auto-opens based on pendingPermissions)
 * - Uses agent.approve() and agent.deny() methods
 * - Simpler dialog pattern (no manual open/close)
 */

import { boxWith, onDestroyEffect, type ReadableBoxedValues, type WritableBoxedValues } from 'svelte-toolbelt';
import { Context, watch } from 'runed';
import { kbd } from '$lib/internal/kbd.js';
import type {
	HPDKeyboardEvent,
	HPDMouseEvent,
	OnChangeFn,
	WithRefOpts
} from '$lib/internal/types.js';
import { createHPDAttrs, getDataOpenClosed } from '$lib/internal/attrs.js';
import { PresenceManager } from '$lib/internal/presence-manager.svelte.js';
import type { Workspace } from '$lib/workspace/types.js';
import type { PermissionRequest, PermissionChoice } from '$lib/agent/types.js';

// ============================================
// Data Attributes for CSS Hooks
// ============================================

const permissionDialogAttrs = createHPDAttrs({
	component: 'permission-dialog',
	parts: ['content', 'overlay', 'header', 'description', 'actions', 'approve', 'deny']
});

// ============================================
// Context
// ============================================

const PermissionDialogRootContext = new Context<PermissionDialogRootState>('PermissionDialog.Root');

// ============================================
// Root State (Manages Dialog Open/Close)
// ============================================

interface PermissionDialogRootStateOpts
	extends ReadableBoxedValues<{
		agent: Workspace;
		onOpenChangeComplete?: OnChangeFn<boolean>;
	}> {}

export class PermissionDialogRootState {
	static create(opts: PermissionDialogRootStateOpts) {
		return PermissionDialogRootContext.set(new PermissionDialogRootState(opts));
	}

	readonly opts: PermissionDialogRootStateOpts;
	contentNode = $state<HTMLElement | null>(null);
	overlayNode = $state<HTMLElement | null>(null);
	headerId = $state<string | undefined | null>(undefined);
	descriptionId = $state<string | undefined | null>(undefined);
	contentPresence: PresenceManager;
	overlayPresence: PresenceManager;

	// Track if we're currently processing a response  
	#isResponding = $state(false);

	// Current pending request (first in queue)
	readonly currentRequest = $derived.by(() => {
		const requests = this.opts.agent.current.state?.pendingPermissions;
		return requests && requests.length > 0 ? requests[0] : null;
	});

	// Dialog is open when there's a pending request
	readonly isOpen = $derived(this.currentRequest !== null);

	// Status tracking: inProgress/executing/complete
	readonly status = $derived.by((): import('./types.ts').PermissionDialogStatus => {
		if (this.#isResponding) return 'complete';
		if (this.currentRequest !== null) return 'executing';
		return 'inProgress';
	});

	constructor(opts: PermissionDialogRootStateOpts) {
		this.opts = opts;

		// Create boxWith that references the reactive isOpen property
		const isOpenBox = boxWith(() => this.isOpen);

		this.contentPresence = new PresenceManager({
			ref: boxWith(() => this.contentNode),
			open: isOpenBox,
			enabled: true,
			onComplete: () => {
				const callback = this.opts.onOpenChangeComplete?.current;
				if (callback) {
					callback(isOpenBox.current);
				}
			}
		});

		this.overlayPresence = new PresenceManager({
			ref: boxWith(() => this.overlayNode),
			open: isOpenBox,
			enabled: true
		});
	}

	// Approve the current permission request
	async approve(choice: PermissionChoice = 'ask') {
		const request = this.currentRequest;
		if (!request) return;

		this.#isResponding = true;
		try {
			await this.opts.agent.current.approve(request.permissionId, choice);
		} finally {
			this.#isResponding = false;
		}
	}

	// Deny the current permission request
	async deny(reason?: string) {
		const request = this.currentRequest;
		if (!request) return;

		this.#isResponding = true;
		try {
			await this.opts.agent.current.deny(request.permissionId, reason);
		} finally {
			this.#isResponding = false;
		}
	}

	getHPDAttr: typeof permissionDialogAttrs.getAttr = (part) => {
		return permissionDialogAttrs.getAttr(part);
	};

	readonly sharedProps = $derived.by(
		() =>
			({
				'data-state': getDataOpenClosed(this.isOpen)
			}) as const
	);
}

// ============================================
// Content State (Modal Content Container)
// ============================================

interface PermissionDialogContentStateOpts extends WithRefOpts {}

export class PermissionDialogContentState {
	static create(opts: PermissionDialogContentStateOpts) {
		return new PermissionDialogContentState(opts, PermissionDialogRootContext.get());
	}

	readonly opts: PermissionDialogContentStateOpts;
	readonly root: PermissionDialogRootState;

	readonly setRef = (v: HTMLElement | null) => {
		this.opts.ref.current = v;
		this.root.contentNode = v;
	};

	constructor(opts: PermissionDialogContentStateOpts, root: PermissionDialogRootState) {
		this.opts = opts;
		this.root = root;
	}

	get shouldRender() {
		return this.root.contentPresence.shouldRender;
	}

	readonly props = $derived.by(
		() =>
			({
				id: this.opts.id.current,
				role: 'dialog',
				'aria-modal': 'true',
				'aria-labelledby': this.root.headerId,
				'aria-describedby': this.root.descriptionId,
				[this.root.getHPDAttr('content')]: '',
				...this.root.sharedProps
			}) as const
	);
}

// ============================================
// Overlay State (Modal Backdrop)
// ============================================

interface PermissionDialogOverlayStateOpts extends WithRefOpts {}

export class PermissionDialogOverlayState {
	static create(opts: PermissionDialogOverlayStateOpts) {
		return new PermissionDialogOverlayState(opts, PermissionDialogRootContext.get());
	}

	readonly opts: PermissionDialogOverlayStateOpts;
	readonly root: PermissionDialogRootState;

	readonly setRef = (v: HTMLElement | null) => {
		this.opts.ref.current = v;
		this.root.overlayNode = v;
	};

	constructor(opts: PermissionDialogOverlayStateOpts, root: PermissionDialogRootState) {
		this.opts = opts;
		this.root = root;
	}

	get shouldRender() {
		return this.root.overlayPresence.shouldRender;
	}

	readonly props = $derived.by(
		() =>
			({
				id: this.opts.id.current,
				[this.root.getHPDAttr('overlay')]: '',
				...this.root.sharedProps
			}) as const
	);
}

// ============================================
// Header State (Dialog Title)
// ============================================

interface PermissionDialogHeaderStateOpts
	extends WithRefOpts,
		ReadableBoxedValues<{ level: 1 | 2 | 3 | 4 | 5 | 6 }> {}

export class PermissionDialogHeaderState {
	static create(opts: PermissionDialogHeaderStateOpts) {
		return new PermissionDialogHeaderState(opts, PermissionDialogRootContext.get());
	}

	readonly opts: PermissionDialogHeaderStateOpts;
	readonly root: PermissionDialogRootState;

	readonly setRef = (v: HTMLElement | null) => {
		this.opts.ref.current = v;
	};

	constructor(opts: PermissionDialogHeaderStateOpts, root: PermissionDialogRootState) {
		this.opts = opts;
		this.root = root;
		this.root.headerId = this.opts.id.current;

		// Update root headerId when id changes
		watch.pre(
			() => this.opts.id.current,
			(id) => {
				this.root.headerId = id;
			}
		);
	}

	readonly props = $derived.by(
		() =>
			({
				id: this.opts.id.current,
				[this.root.getHPDAttr('header')]: '',
				...this.root.sharedProps
			}) as const
	);
}

// ============================================
// Description State (Permission Details)
// ============================================

interface PermissionDialogDescriptionStateOpts extends WithRefOpts {}

export class PermissionDialogDescriptionState {
	static create(opts: PermissionDialogDescriptionStateOpts) {
		return new PermissionDialogDescriptionState(opts, PermissionDialogRootContext.get());
	}

	readonly opts: PermissionDialogDescriptionStateOpts;
	readonly root: PermissionDialogRootState;

	readonly setRef = (v: HTMLElement | null) => {
		this.opts.ref.current = v;
	};

	constructor(opts: PermissionDialogDescriptionStateOpts, root: PermissionDialogRootState) {
		this.opts = opts;
		this.root = root;
		this.root.descriptionId = this.opts.id.current;

		// Update root descriptionId when id changes
		watch.pre(
			() => this.opts.id.current,
			(id) => {
				this.root.descriptionId = id;
			}
		);
	}

	readonly props = $derived.by(
		() =>
			({
				id: this.opts.id.current,
				[this.root.getHPDAttr('description')]: '',
				...this.root.sharedProps
			}) as const
	);
}

// ============================================
// Actions State (Button Container)
// ============================================

interface PermissionDialogActionsStateOpts extends WithRefOpts {}

export class PermissionDialogActionsState {
	static create(opts: PermissionDialogActionsStateOpts) {
		return new PermissionDialogActionsState(opts, PermissionDialogRootContext.get());
	}

	readonly opts: PermissionDialogActionsStateOpts;
	readonly root: PermissionDialogRootState;

	readonly setRef = (v: HTMLElement | null) => {
		this.opts.ref.current = v;
	};

	constructor(opts: PermissionDialogActionsStateOpts, root: PermissionDialogRootState) {
		this.opts = opts;
		this.root = root;
	}

	readonly props = $derived.by(
		() =>
			({
				id: this.opts.id.current,
				[this.root.getHPDAttr('actions')]: '',
				...this.root.sharedProps
			}) as const
	);
}

// ============================================
// Approve Button State
// ============================================

interface PermissionDialogApproveStateOpts
	extends WithRefOpts,
		ReadableBoxedValues<{ disabled: boolean; choice: PermissionChoice }> {}

export class PermissionDialogApproveState {
	static create(opts: PermissionDialogApproveStateOpts) {
		return new PermissionDialogApproveState(opts, PermissionDialogRootContext.get());
	}

	readonly opts: PermissionDialogApproveStateOpts;
	readonly root: PermissionDialogRootState;

	readonly setRef = (v: HTMLElement | null) => {
		this.opts.ref.current = v;
	};

	constructor(opts: PermissionDialogApproveStateOpts, root: PermissionDialogRootState) {
		this.opts = opts;
		this.root = root;
		this.onclick = this.onclick.bind(this);
		this.onkeydown = this.onkeydown.bind(this);
	}

	onclick(e: HPDMouseEvent) {
		if (this.opts.disabled.current) return;
		if (e.button > 0) return;
		this.root.approve(this.opts.choice.current);
	}

	onkeydown(e: HPDKeyboardEvent) {
		if (this.opts.disabled.current) return;
		if (e.key === kbd.SPACE || e.key === kbd.ENTER) {
			e.preventDefault();
			this.root.approve(this.opts.choice.current);
		}
	}

	readonly props = $derived.by(
		() =>
			({
				id: this.opts.id.current,
				[this.root.getHPDAttr('approve')]: '',
				onclick: this.onclick,
				onkeydown: this.onkeydown,
				disabled: this.opts.disabled.current ? true : undefined,
				tabindex: 0,
				...this.root.sharedProps
			}) as const
	);
}

// ============================================
// Deny Button State
// ============================================

interface PermissionDialogDenyStateOpts
	extends WithRefOpts,
		ReadableBoxedValues<{ disabled: boolean; reason?: string }> {}

export class PermissionDialogDenyState {
	static create(opts: PermissionDialogDenyStateOpts) {
		return new PermissionDialogDenyState(opts, PermissionDialogRootContext.get());
	}

	readonly opts: PermissionDialogDenyStateOpts;
	readonly root: PermissionDialogRootState;

	readonly setRef = (v: HTMLElement | null) => {
		this.opts.ref.current = v;
	};

	constructor(opts: PermissionDialogDenyStateOpts, root: PermissionDialogRootState) {
		this.opts = opts;
		this.root = root;
		this.onclick = this.onclick.bind(this);
		this.onkeydown = this.onkeydown.bind(this);
	}

	onclick(e: HPDMouseEvent) {
		if (this.opts.disabled.current) return;
		if (e.button > 0) return;
		this.root.deny(this.opts.reason?.current);
	}

	onkeydown(e: HPDKeyboardEvent) {
		if (this.opts.disabled.current) return;
		if (e.key === kbd.SPACE || e.key === kbd.ENTER) {
			e.preventDefault();
			this.root.deny(this.opts.reason?.current);
		}
	}

	readonly props = $derived.by(
		() =>
			({
				id: this.opts.id.current,
				[this.root.getHPDAttr('deny')]: '',
				onclick: this.onclick,
				onkeydown: this.onkeydown,
				disabled: this.opts.disabled.current ? true : undefined,
				tabindex: 0,
				...this.root.sharedProps
			}) as const
	);
}
