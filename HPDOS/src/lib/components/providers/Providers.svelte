<script lang="ts">
	import { providers, type AuthSummary, type AuthMethodInfo, type LoginResponse } from '../../providers.svelte.js';

	// Which provider's detail panel is open
	let selected = $state<AuthSummary | null>(null);
	let methods = $state<AuthMethodInfo[]>([]);
	let methodsLoading = $state(false);

	// Login flow state
	let loginStep = $state<'idle' | 'pending' | 'needs_input' | 'done' | 'error'>('idle');
	let loginResponse = $state<LoginResponse | null>(null);
	let inputValue = $state('');
	let selectedMethodIndex = $state(0);
	let actionLoading = $state(false);

	$effect(() => {
		providers.load();
	});

	async function selectProvider(p: AuthSummary) {
		selected = p;
		loginStep = 'idle';
		loginResponse = null;
		inputValue = '';
		selectedMethodIndex = 0;
		methodsLoading = true;
		try {
			methods = await providers.getMethods(p.providerId);
		} finally {
			methodsLoading = false;
		}
	}

	async function startLogin() {
		if (!selected) return;
		actionLoading = true;
		try {
			const res = await providers.login(selected.providerId, selectedMethodIndex);
			loginResponse = res;
			if (res.status === 'ok') {
				loginStep = 'done';
				await providers.load();
				selected = providers.providers.find(p => p.providerId === selected!.providerId) ?? selected;
			} else if (res.status === 'pending') {
				loginStep = 'pending';
			} else if (res.status === 'needs_input') {
				loginStep = 'needs_input';
			} else {
				loginStep = 'idle';
			}
		} catch (e) {
			loginStep = 'error';
		} finally {
			actionLoading = false;
		}
	}

	async function completeLogin() {
		if (!selected || !inputValue.trim()) return;
		actionLoading = true;
		try {
			const res = await providers.loginComplete(selected.providerId, selectedMethodIndex, inputValue.trim());
			loginResponse = res;
			if (res.status === 'ok') {
				loginStep = 'done';
				inputValue = '';
				await providers.load();
				selected = providers.providers.find(p => p.providerId === selected!.providerId) ?? selected;
			} else {
				loginStep = 'error';
			}
		} catch {
			loginStep = 'error';
		} finally {
			actionLoading = false;
		}
	}

	async function logout() {
		if (!selected) return;
		actionLoading = true;
		try {
			await providers.logout(selected.providerId);
			loginStep = 'idle';
			loginResponse = null;
			selected = providers.providers.find(p => p.providerId === selected!.providerId) ?? selected;
		} finally {
			actionLoading = false;
		}
	}
</script>

<div class="providers-page">
	<div class="providers-list">
		<h2 class="providers-heading">Providers</h2>
		{#if providers.loading && providers.providers.length === 0}
			<div class="providers-loading">Loading…</div>
		{:else}
			{#each providers.providers as p (p.providerId)}
				<button
					class="provider-row"
					class:active={selected?.providerId === p.providerId}
					onclick={() => selectProvider(p)}
				>
					<span class="provider-name">{p.displayName}</span>
					<span class="provider-badge" class:connected={p.isAuthenticated} class:expired={p.isExpired}>
						{#if p.isExpired}expired{:else if p.isAuthenticated}connected{:else}not connected{/if}
					</span>
				</button>
			{/each}
		{/if}
	</div>

	<div class="provider-detail">
		{#if !selected}
			<div class="provider-empty">Select a provider to configure it.</div>
		{:else}
			<div class="provider-detail-inner">
				<h3 class="provider-detail-name">{selected.displayName}</h3>

				{#if selected.isAuthenticated}
					<div class="provider-status-row">
						<span class="status-dot connected"></span>
						<span>Connected{selected.accountId ? ` as ${selected.accountId}` : ''}</span>
						{#if selected.expiresAt}
							<span class="status-expiry">· expires {new Date(selected.expiresAt).toLocaleDateString()}</span>
						{/if}
					</div>

					{#if selected.storedEntries && selected.storedEntries.length > 1}
						<div class="section-label">Stored credentials</div>
						<div class="entries-list">
							{#each selected.storedEntries as entry (entry.id)}
								<div class="entry-row" class:entry-active={entry.id === selected.activeEntryId}>
									<span class="entry-label">{entry.methodLabel}</span>
									{#if entry.accountId}<span class="entry-account">{entry.accountId}</span>{/if}
									<div class="entry-actions">
										{#if entry.id !== selected.activeEntryId}
											<button class="btn-sm" onclick={() => providers.setActive(selected!.providerId, entry.id)}>
												Set active
											</button>
										{:else}
											<span class="entry-active-label">Active</span>
										{/if}
										<button class="btn-sm btn-danger" onclick={() => providers.removeEntry(selected!.providerId, entry.id)}>
											Remove
										</button>
									</div>
								</div>
							{/each}
						</div>
					{/if}

					{#if loginStep === 'done'}
						<div class="login-success">✓ Connected successfully</div>
					{/if}

					<div class="detail-actions">
						<button class="btn-secondary" onclick={() => { loginStep = 'idle'; loginResponse = null; }} disabled={actionLoading}>
							Add another credential
						</button>
						<button class="btn-danger-outline" onclick={logout} disabled={actionLoading}>
							{actionLoading ? 'Disconnecting…' : 'Disconnect all'}
						</button>
					</div>
				{/if}

				<!-- Connect / add credential flow -->
				{#if !selected.isAuthenticated || loginStep === 'idle'}
					{#if methodsLoading}
						<div class="providers-loading">Loading methods…</div>
					{:else if methods.length > 0}
						<div class="section-label">{selected.isAuthenticated ? 'Add credential' : 'Connect'}</div>
						<div class="methods-list">
							{#each methods as m (m.index)}
								<label class="method-option">
									<input
										type="radio"
										name="method"
										value={m.index}
										checked={selectedMethodIndex === m.index}
										onchange={() => { selectedMethodIndex = m.index; loginStep = 'idle'; loginResponse = null; }}
									/>
									<span class="method-label">
										{m.label}
										{#if m.isRecommended}<span class="recommended">Recommended</span>{/if}
									</span>
									{#if m.description}<span class="method-desc">{m.description}</span>{/if}
								</label>
							{/each}
						</div>

						{#if loginStep === 'idle' || loginStep === 'error'}
							{#if loginStep === 'error'}
								<div class="login-error">Something went wrong. Please try again.</div>
							{/if}
							<button class="btn-primary" onclick={startLogin} disabled={actionLoading}>
								{actionLoading ? 'Connecting…' : 'Connect'}
							</button>
						{/if}

						{#if loginStep === 'pending' && loginResponse?.status === 'pending'}
							<div class="login-pending">
								<p>{loginResponse.message ?? 'Complete authentication in your browser.'}</p>
								{#if loginResponse.url}
									<a href={loginResponse.url} target="_blank" rel="noopener" class="login-url">{loginResponse.url}</a>
								{/if}
								{#if loginResponse.userCode}
									<div class="device-code">{loginResponse.userCode}</div>
								{/if}
								<p class="login-hint">After approving in the browser, come back here.</p>
								<button class="btn-secondary" onclick={() => { loginStep = 'idle'; loginResponse = null; }}>Cancel</button>
							</div>
						{/if}

						{#if loginStep === 'needs_input' && loginResponse?.status === 'needs_input'}
							<div class="login-input-flow">
								<p>{loginResponse.prompt}</p>
								<input
									class="text-input"
									type="password"
									placeholder={loginResponse.inputLabel ?? 'Enter value…'}
									bind:value={inputValue}
									onkeydown={(e) => e.key === 'Enter' && completeLogin()}
								/>
								<div class="login-input-actions">
									<button class="btn-primary" onclick={completeLogin} disabled={actionLoading || !inputValue.trim()}>
										{actionLoading ? 'Saving…' : 'Save'}
									</button>
									<button class="btn-secondary" onclick={() => { loginStep = 'idle'; loginResponse = null; inputValue = ''; }}>Cancel</button>
								</div>
							</div>
						{/if}
					{/if}
				{/if}
			</div>
		{/if}
	</div>
</div>

<style>
	.providers-page {
		display: flex;
		height: 100%;
		overflow: hidden;
	}

	.providers-list {
		width: 220px;
		flex: none;
		border-right: 1px solid rgb(255 255 255 / 0.07);
		overflow-y: auto;
		padding: 1rem 0;
	}

	.providers-heading {
		font-size: 0.7rem;
		font-weight: 600;
		letter-spacing: 0.08em;
		text-transform: uppercase;
		color: rgb(var(--color-text-tertiary));
		padding: 0 1rem 0.5rem;
		margin: 0;
	}

	.providers-loading {
		padding: 1rem;
		color: rgb(var(--color-text-tertiary));
		font-size: 0.8rem;
	}

	.provider-row {
		display: flex;
		align-items: center;
		justify-content: space-between;
		width: 100%;
		padding: 0.5rem 1rem;
		background: none;
		border: none;
		color: rgb(var(--color-text-primary));
		font-size: 0.85rem;
		cursor: pointer;
		text-align: left;
		gap: 0.5rem;
		transition: background 0.1s;
	}
	.provider-row:hover { background: rgb(255 255 255 / 0.04); }
	.provider-row.active { background: rgb(255 255 255 / 0.07); }

	.provider-badge {
		font-size: 0.7rem;
		padding: 0.1rem 0.4rem;
		border-radius: 4px;
		flex: none;
		background: rgb(255 255 255 / 0.06);
		color: rgb(var(--color-text-tertiary));
	}
	.provider-badge.connected {
		background: rgb(var(--color-accent-primary) / 0.15);
		color: rgb(var(--color-accent-primary));
	}
	.provider-badge.expired {
		background: rgb(var(--color-error) / 0.15);
		color: rgb(var(--color-error));
	}

	.provider-detail {
		flex: 1;
		overflow-y: auto;
		padding: 1.5rem;
	}

	.provider-empty {
		color: rgb(var(--color-text-tertiary));
		font-size: 0.875rem;
		margin-top: 2rem;
		text-align: center;
	}

	.provider-detail-name {
		font-size: 1.1rem;
		font-weight: 600;
		margin: 0 0 1rem;
		color: rgb(var(--color-text-primary));
	}

	.provider-status-row {
		display: flex;
		align-items: center;
		gap: 0.5rem;
		font-size: 0.85rem;
		color: rgb(var(--color-text-secondary));
		margin-bottom: 1rem;
	}
	.status-dot {
		width: 8px;
		height: 8px;
		border-radius: 50%;
		flex: none;
		background: rgb(var(--color-text-tertiary));
	}
	.status-dot.connected { background: rgb(var(--color-success)); }
	.status-expiry { color: rgb(var(--color-text-tertiary)); }

	.section-label {
		font-size: 0.7rem;
		font-weight: 600;
		letter-spacing: 0.06em;
		text-transform: uppercase;
		color: rgb(var(--color-text-tertiary));
		margin: 1.25rem 0 0.5rem;
	}

	.entries-list {
		display: flex;
		flex-direction: column;
		gap: 0.375rem;
		margin-bottom: 0.75rem;
	}
	.entry-row {
		display: flex;
		align-items: center;
		gap: 0.5rem;
		padding: 0.5rem 0.75rem;
		border-radius: 8px;
		background: rgb(255 255 255 / 0.04);
		border: 1px solid rgb(255 255 255 / 0.06);
		font-size: 0.8rem;
	}
	.entry-row.entry-active {
		border-color: rgb(var(--color-accent-primary) / 0.3);
	}
	.entry-label { color: rgb(var(--color-text-primary)); }
	.entry-account { color: rgb(var(--color-text-tertiary)); font-size: 0.75rem; }
	.entry-actions { margin-left: auto; display: flex; gap: 0.375rem; }
	.entry-active-label { font-size: 0.7rem; color: rgb(var(--color-accent-primary)); }

	.methods-list {
		display: flex;
		flex-direction: column;
		gap: 0.5rem;
		margin-bottom: 1rem;
	}
	.method-option {
		display: flex;
		flex-wrap: wrap;
		align-items: baseline;
		gap: 0.375rem;
		cursor: pointer;
		font-size: 0.85rem;
		color: rgb(var(--color-text-primary));
	}
	.method-label { display: flex; align-items: center; gap: 0.375rem; }
	.method-desc {
		flex: 0 0 100%;
		padding-left: 1.25rem;
		font-size: 0.78rem;
		color: rgb(var(--color-text-tertiary));
	}
	.recommended {
		font-size: 0.68rem;
		padding: 0.1rem 0.35rem;
		border-radius: 4px;
		background: rgb(var(--color-accent-primary) / 0.15);
		color: rgb(var(--color-accent-primary));
	}

	.login-pending, .login-input-flow {
		display: flex;
		flex-direction: column;
		gap: 0.75rem;
		padding: 1rem;
		border-radius: 10px;
		background: rgb(255 255 255 / 0.04);
		border: 1px solid rgb(255 255 255 / 0.07);
		margin-bottom: 0.75rem;
		font-size: 0.85rem;
		color: rgb(var(--color-text-secondary));
	}
	.login-url {
		font-size: 0.78rem;
		color: rgb(var(--color-accent-primary));
		word-break: break-all;
	}
	.device-code {
		font-family: monospace;
		font-size: 1.25rem;
		font-weight: 700;
		letter-spacing: 0.15em;
		color: rgb(var(--color-text-primary));
		background: rgb(255 255 255 / 0.06);
		padding: 0.5rem 0.75rem;
		border-radius: 6px;
		text-align: center;
	}
	.login-hint { font-size: 0.78rem; color: rgb(var(--color-text-tertiary)); }
	.login-input-actions { display: flex; gap: 0.5rem; }
	.login-success { color: rgb(var(--color-success)); font-size: 0.85rem; margin: 0.5rem 0; }
	.login-error { color: rgb(var(--color-error)); font-size: 0.85rem; margin-bottom: 0.5rem; }

	.text-input {
		width: 100%;
		padding: 0.5rem 0.75rem;
		border-radius: 8px;
		border: 1px solid rgb(255 255 255 / 0.12);
		background: rgb(255 255 255 / 0.05);
		color: rgb(var(--color-text-primary));
		font-size: 0.875rem;
		font-family: inherit;
		outline: none;
		transition: border-color 0.15s;
	}
	.text-input:focus { border-color: rgb(var(--color-accent-primary) / 0.5); }

	.detail-actions {
		display: flex;
		gap: 0.5rem;
		margin-top: 1.25rem;
		flex-wrap: wrap;
	}

	/* Shared button styles */
	.btn-primary, .btn-secondary, .btn-danger-outline, .btn-sm, .btn-danger {
		padding: 0.45rem 1rem;
		border-radius: 8px;
		border: none;
		font-size: 0.85rem;
		font-family: inherit;
		cursor: pointer;
		transition: all 0.15s;
	}
	.btn-primary {
		background: rgb(var(--color-accent-primary));
		color: #fff;
	}
	.btn-primary:hover:not(:disabled) { opacity: 0.85; }
	.btn-primary:disabled { opacity: 0.4; cursor: default; }

	.btn-secondary {
		background: rgb(255 255 255 / 0.07);
		color: rgb(var(--color-text-primary));
		border: 1px solid rgb(255 255 255 / 0.1);
	}
	.btn-secondary:hover:not(:disabled) { background: rgb(255 255 255 / 0.1); }

	.btn-danger-outline {
		background: transparent;
		color: rgb(var(--color-error));
		border: 1px solid rgb(var(--color-error) / 0.4);
	}
	.btn-danger-outline:hover:not(:disabled) { background: rgb(var(--color-error) / 0.1); }
	.btn-danger-outline:disabled { opacity: 0.4; cursor: default; }

	.btn-sm {
		padding: 0.2rem 0.5rem;
		font-size: 0.75rem;
		background: rgb(255 255 255 / 0.06);
		color: rgb(var(--color-text-secondary));
		border: 1px solid rgb(255 255 255 / 0.08);
	}
	.btn-sm:hover { background: rgb(255 255 255 / 0.1); }
	.btn-danger {
		padding: 0.2rem 0.5rem;
		font-size: 0.75rem;
		background: rgb(var(--color-error) / 0.1);
		color: rgb(var(--color-error));
		border: 1px solid rgb(var(--color-error) / 0.2);
	}
	.btn-danger:hover { background: rgb(var(--color-error) / 0.2); }
</style>
