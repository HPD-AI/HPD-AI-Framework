<script module>
	import { defineMeta } from '@storybook/addon-svelte-csf';
	import FileAttachmentDemo from './FileAttachmentDemo.svelte';

	const { Story } = defineMeta({
		title: 'Components/FileAttachment',
		component: FileAttachmentDemo,
		tags: ['autodocs'],
		argTypes: {
			disabled: {
				control: 'boolean',
				description: 'Disables the attach button and prevents new uploads',
			},
			uploadMode: {
				control: { type: 'select' },
				options: ['success', 'error', 'slow'],
				description: 'Simulated upload outcome for the story',
			},
			sessionId: {
				control: 'text',
				description: 'Session ID passed to the upload function',
			},
			threadId: {
				control: 'text',
				description: 'Thread ID passed to the upload function',
			},
		},
		parameters: {
			docs: {
				description: {
					component: `
The **FileAttachment** headless component manages file upload state for a chat session.

A \`FileAttachmentState\` instance holds the list of pending attachments, tracks their upload
lifecycle (\`uploading\` → \`done\` | \`error\`), and exposes a list of \`resolvedContent\` ready to
include in \`workspace.send()\`.

## Key design points
- **Pre-constructed state pattern**: create \`new FileAttachmentState({ uploadFn, sessionId, threadId, disabled })\`
  outside the component so that \`state.resolvedContent\` is readable without entering the snippet.
- **Immediate upload**: calling \`add(files)\` kicks off uploads in parallel right away.
- **canSubmit**: \`false\` while any upload is in-progress or any entry has an error status.
- **retry / remove / clear**: full lifecycle control from snippet props.

## Usage
\`\`\`svelte
<script>
  import { FileAttachment, FileAttachmentState } from '@hpd-research/hpd-agent-svelte-headless-ui';

  const state = new FileAttachmentState({
    uploadFn: { get current() { return (sid, bid, file) => client.uploadContent(sid, bid, file); } },
    sessionId: { get current() { return activeSessionId; } },
    threadId: { get current() { return activeThreadId; } },
    disabled:  { get current() { return isStreaming; } },
  });
<\/script>

<FileAttachment.Root {state}>
  {#snippet children(s)}
    <button onclick={() => s.add(inputEl.files)}>Attach</button>
    {#each s.attachments as att}
      <span>{att.file.name} — {att.status}</span>
      <button onclick={() => s.remove(att.localId)}>✕</button>
    {/each}
  {/snippet}
</FileAttachment.Root>

<!-- Resolved contents are available outside the snippet: -->
<button onclick={() => workspace.send({ attachments: state.resolvedContent })}>Send</button>
\`\`\`
`,
				},
			},
		},
	});
</script>

<!-- ─── Default — successful uploads ─────────────────────── -->
<Story
	name="Default"
	args={{
		uploadMode: 'success',
		disabled: false,
		sessionId: 'demo-session',
		threadId: 'main',
	}}
/>

<!-- ─── Upload error ───────────────────────────────────────── -->
<Story
	name="Upload Error"
	args={{
		uploadMode: 'error',
		disabled: false,
		sessionId: 'demo-session',
		threadId: 'main',
	}}
/>

<!-- ─── Slow upload (shows uploading state) ───────────────── -->
<Story
	name="Slow Upload"
	args={{
		uploadMode: 'slow',
		disabled: false,
		sessionId: 'demo-session',
		threadId: 'main',
	}}
/>

<!-- ─── Disabled ──────────────────────────────────────────── -->
<Story
	name="Disabled"
	args={{
		uploadMode: 'success',
		disabled: true,
		sessionId: 'demo-session',
		threadId: 'main',
	}}
/>
