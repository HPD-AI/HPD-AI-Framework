<script lang="ts">
  import MessageEdit from '../../src/message-edit/message-edit.svelte';
  import type { Message } from '@hpd-research/hpd-agent-headless-ui';
  import type { ThreadRevisionState } from '../../src/thread-revisions.js';

  let {
    message,
    revisions,
  }: {
    message: Message;
    revisions: Pick<ThreadRevisionState, 'forkAndEditMessage'>;
  } = $props();
</script>

<MessageEdit {message} {revisions} autosize={false}>
  {#snippet view({ actions, message })}
    <button data-testid="start" type="button" onclick={actions.startEdit}>
      Edit {message.id}
    </button>
  {/snippet}

  {#snippet edit({ actionProps, actions, props, textareaAttachment })}
    <textarea {...props.textarea} {@attach textareaAttachment}></textarea>
    <button {...actionProps.cancel} data-testid="cancel" onclick={actions.cancel}>Cancel</button>
    <button {...actionProps.save} data-testid="save" onclick={actions.save}>Save</button>
  {/snippet}
</MessageEdit>
