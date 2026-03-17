import { api } from './api.js';

// ── Types ──────────────────────────────────────────────────────────────────

export interface StoredEntryInfo {
	id: string;
	methodLabel: string;
	accountId?: string;
	expiresAt?: string;
	isExpired: boolean;
	source: string;
}

export interface AuthSummary {
	providerId: string;
	displayName: string;
	isAuthenticated: boolean;
	source?: string;
	expiresAt?: string;
	accountId?: string;
	isExpired: boolean;
	hasModels: boolean;
	supportsFreeModels: boolean;
	activeEntryId?: string;
	storedEntries?: StoredEntryInfo[];
}

export interface AuthMethodInfo {
	index: number;
	label: string;
	description?: string;
	isRecommended: boolean;
}

export interface ModelInfo {
	id: string;
	description: string;
	isRecommended: boolean;
	isFree: boolean;
	supportsTools: boolean;
}

export interface DefaultsResponse {
	providerKey: string;
	modelId: string;
}

// Login flow responses
export type LoginResponse =
	| { status: 'ok'; source: string }
	| { status: 'pending'; message?: string; url?: string; userCode?: string }
	| { status: 'needs_input'; prompt: string; inputLabel?: string }
	| { status: 'cancelled' };

// ── Store ──────────────────────────────────────────────────────────────────

class ProvidersStore {
	providers = $state<AuthSummary[]>([]);
	defaults = $state<DefaultsResponse | null>(null);
	loading = $state(false);
	error = $state<string | null>(null);

	async load() {
		this.loading = true;
		this.error = null;
		try {
			const [providersRes, defaultsRes] = await Promise.all([
				api('/api/providers'),
				api('/api/defaults'),
			]);
			if (providersRes.ok) this.providers = await providersRes.json();
			if (defaultsRes.ok) this.defaults = await defaultsRes.json();
		} catch (e) {
			this.error = String(e);
		} finally {
			this.loading = false;
		}
	}

	async getMethods(providerId: string): Promise<AuthMethodInfo[]> {
		const res = await api(`/api/providers/${providerId}/methods`);
		if (!res.ok) throw new Error(`Failed to get methods for ${providerId}`);
		return res.json();
	}

	async getModels(providerId: string, live = false): Promise<ModelInfo[]> {
		const res = await api(`/api/providers/${providerId}/models${live ? '?live=true' : ''}`);
		if (!res.ok) throw new Error(`Failed to get models for ${providerId}`);
		return res.json();
	}

	async login(providerId: string, methodIndex = 0): Promise<LoginResponse> {
		const res = await api(`/api/providers/${providerId}/login?method=${methodIndex}`, { method: 'POST' });
		return res.json();
	}

	async loginComplete(providerId: string, methodIndex: number, input: string): Promise<LoginResponse> {
		const res = await api(`/api/providers/${providerId}/login/complete?method=${methodIndex}`, {
			method: 'POST',
			headers: { 'Content-Type': 'application/json' },
			body: JSON.stringify({ input }),
		});
		return res.json();
	}

	async logout(providerId: string): Promise<void> {
		await api(`/api/providers/${providerId}`, { method: 'DELETE' });
		await this.load();
	}

	async removeEntry(providerId: string, entryId: string): Promise<void> {
		await api(`/api/providers/${providerId}/entries/${entryId}`, { method: 'DELETE' });
		await this.load();
	}

	async setActive(providerId: string, entryId: string): Promise<void> {
		await api(`/api/providers/${providerId}/active`, {
			method: 'PUT',
			headers: { 'Content-Type': 'application/json' },
			body: JSON.stringify({ entryId }),
		});
		await this.load();
	}

	async setDefaults(providerKey: string, modelId: string): Promise<void> {
		await api('/api/defaults', {
			method: 'PATCH',
			headers: { 'Content-Type': 'application/json' },
			body: JSON.stringify({ providerKey, modelId }),
		});
		this.defaults = { providerKey, modelId };
	}
}

export const providers = new ProvidersStore();
