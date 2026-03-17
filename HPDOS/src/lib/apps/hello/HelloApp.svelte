<script lang="ts">
	import { invokeAppCommand } from '../../ipc/client';
	import { isHybridWebView } from '../../ipc/bridge';

	interface Props {
		initialState?: { message?: string; response?: string | null };
		tabId?: string;
	}

	let { initialState = {}, tabId = 'default' }: Props = $props();

	let message = $state((initialState as any).message || 'World');
	let response = $state<string | null>((initialState as any).response || null);
	let loading = $state(false);
	let error = $state<string | null>(null);

	async function greet() {
		loading = true;
		error = null;
		try {
			const result = await invokeAppCommand('hello-world', 'greet', { name: message });
			if (result && typeof result === 'object' && 'greeting' in result) {
				response = (result as { greeting: string }).greeting;
			}
		} catch (e: any) {
			error = e.message || 'Unknown error';
		} finally {
			loading = false;
		}
	}
</script>

<div class="hello-app">
	<div class="header">
		<h1>👋 Hello World</h1>
		<p class="tab-id">Tab: <code>{tabId}</code></p>
	</div>

	<div class="tier-info">
		<span class="badge frontend">Frontend: Svelte</span>
		<span class="badge backend">Backend: C#</span>
	</div>

	{#if isHybridWebView()}
		<div class="content">
			<div class="input-box">
				<label for="name-input">Your name:</label>
				<input id="name-input" bind:value={message} placeholder="Enter your name" disabled={loading} />
				<button onclick={greet} disabled={loading} class="btn-primary">
					{loading ? 'Loading...' : 'Greet from C# 💜'}
				</button>
			</div>

			{#if error}
				<div class="error-box">
					<p class="box-title">❌ Error</p>
					<p class="box-body">{error}</p>
				</div>
			{/if}

			{#if response}
				<div class="response-box">
					<p class="box-title">Response from C#:</p>
					<p class="response-value">{response}</p>
				</div>
			{/if}
		</div>
	{:else}
		<div class="warning-box">
			<p class="box-title">⚠️ Web Mode</p>
			<p class="box-body">Backend not available — run inside the HPDOS app.</p>
		</div>
	{/if}

	<div class="footer">
		<p class="hint">Frontend (Svelte) → Backend (C#) via HybridWebView IPC</p>
	</div>
</div>

<style>
	.hello-app {
		display: flex;
		flex-direction: column;
		height: 100%;
		padding: 2rem;
		background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
		color: white;
		overflow-y: auto;
	}

	.header {
		text-align: center;
		margin-bottom: 1.5rem;
	}

	.header h1 {
		font-size: 2rem;
		font-weight: bold;
		margin: 0 0 0.5rem;
	}

	.tab-id { opacity: 0.8; font-size: 0.875rem; margin: 0; }
	.tab-id code {
		background: rgba(255,255,255,0.2);
		padding: 0.2rem 0.4rem;
		border-radius: 0.25rem;
		font-family: monospace;
	}

	.tier-info {
		display: flex;
		gap: 0.5rem;
		justify-content: center;
		margin-bottom: 2rem;
	}

	.badge {
		padding: 0.4rem 0.9rem;
		border-radius: 0.5rem;
		font-size: 0.8rem;
		font-weight: 600;
	}
	.badge.frontend { background: #ff3e00; }
	.badge.backend  { background: #ce422b; }

	.content {
		display: flex;
		flex-direction: column;
		gap: 1.5rem;
		max-width: 560px;
		margin: 0 auto;
		width: 100%;
	}

	.input-box {
		background: rgba(255,255,255,0.1);
		backdrop-filter: blur(10px);
		padding: 1.5rem;
		border-radius: 1rem;
		border: 1px solid rgba(255,255,255,0.2);
	}

	.input-box label {
		display: block;
		font-size: 0.8rem;
		opacity: 0.9;
		margin-bottom: 0.5rem;
		text-transform: uppercase;
		letter-spacing: 0.05em;
	}

	.input-box input {
		width: 100%;
		padding: 0.75rem 1rem;
		border-radius: 0.5rem;
		border: 1px solid rgba(255,255,255,0.3);
		background: rgba(255,255,255,0.2);
		color: white;
		font-size: 1rem;
		margin-bottom: 1rem;
		box-sizing: border-box;
	}

	.input-box input::placeholder { color: rgba(255,255,255,0.5); }
	.input-box input:disabled { opacity: 0.5; cursor: not-allowed; }

	.btn-primary {
		width: 100%;
		padding: 0.75rem 1.5rem;
		border-radius: 0.5rem;
		font-weight: 600;
		font-size: 1rem;
		cursor: pointer;
		border: none;
		background: white;
		color: #667eea;
		transition: all 0.2s;
	}
	.btn-primary:hover:not(:disabled) { transform: translateY(-2px); box-shadow: 0 4px 12px rgba(0,0,0,0.2); }
	.btn-primary:disabled { opacity: 0.5; cursor: not-allowed; }

	.error-box, .response-box, .warning-box {
		backdrop-filter: blur(10px);
		padding: 1.5rem;
		border-radius: 1rem;
	}
	.error-box    { background: rgba(239,68,68,0.2);   border: 1px solid rgba(239,68,68,0.5); }
	.response-box { background: rgba(34,197,94,0.2);   border: 1px solid rgba(34,197,94,0.5); }
	.warning-box  { background: rgba(251,191,36,0.2);  border: 1px solid rgba(251,191,36,0.5); text-align: center; margin: 2rem auto; max-width: 560px; }

	.box-title { font-weight: 600; margin: 0 0 0.5rem; font-size: 1rem; }
	.box-body  { margin: 0; font-size: 0.875rem; opacity: 0.9; }

	.response-value { font-size: 1.4rem; font-weight: 600; margin: 0; }

	.footer { margin-top: 2rem; text-align: center; }
	.hint {
		font-size: 0.8rem;
		opacity: 0.8;
		background: rgba(0,0,0,0.2);
		padding: 0.75rem 1rem;
		border-radius: 0.5rem;
		margin: 0;
	}
</style>
