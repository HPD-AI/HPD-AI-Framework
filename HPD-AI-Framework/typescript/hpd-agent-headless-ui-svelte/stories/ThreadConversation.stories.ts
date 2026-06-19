import type { Meta, StoryObj } from '@storybook/svelte-vite';
import ThreadConversationDemo from './ThreadConversationDemo.svelte';

const meta = {
  title: 'Thread/Composed Conversation',
  component: ThreadConversationDemo,
  tags: ['autodocs'],
  argTypes: {
    renderMode: {
      control: { type: 'inline-radio' },
      options: ['shell', 'custom'],
      description: 'Uses the default ThreadConversation shell or the custom tutorial composition.',
    },
    scenario: {
      control: { type: 'inline-radio' },
      options: ['starter', 'empty', 'busy'],
      description: 'Changes the initial story thread state.',
    },
    reverse: {
      control: 'boolean',
      description: 'Reverses the transcript message order in this story.',
    },
    autosize: {
      control: { type: 'inline-radio' },
      options: [false, 'pretext', 'custom'],
      description: 'Controls composer autosize behavior.',
    },
    submitMode: {
      control: { type: 'inline-radio' },
      options: ['enter', 'mod-enter', 'none'],
      description: 'Controls keyboard submission.',
    },
    clear: {
      control: { type: 'inline-radio' },
      options: ['on-submit', 'never'],
      description: 'Controls whether successful send clears the composer.',
    },
    showRunConfig: {
      control: 'boolean',
      description: 'Shows story controls that feed ThreadComposer runConfig.',
    },
    triggerRuntimeRequests: {
      control: 'boolean',
      description: 'Triggers pending runtime requests in the composed thread.',
    },
    requestScenario: {
      control: { type: 'inline-radio' },
      options: ['permission', 'clarification', 'client-tool', 'custom', 'mixed'],
      description: 'Chooses which runtime request fixtures are triggered.',
    },
  },
} satisfies Meta<ThreadConversationDemo>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Tutorial: Story = {
  args: {
    renderMode: 'shell',
    scenario: 'starter',
    reverse: false,
    autosize: 'pretext',
    submitMode: 'enter',
    clear: 'on-submit',
    showRunConfig: true,
    triggerRuntimeRequests: false,
    requestScenario: 'mixed',
  },
};

export const EmptyThread: Story = {
  args: {
    ...Tutorial.args,
    scenario: 'empty',
  },
};

export const BusyThread: Story = {
  args: {
    ...Tutorial.args,
    scenario: 'busy',
  },
};

export const RuntimeRequests: Story = {
  args: {
    ...Tutorial.args,
    triggerRuntimeRequests: true,
    requestScenario: 'mixed',
  },
};

export const PlainComposer: Story = {
  args: {
    ...Tutorial.args,
    renderMode: 'shell',
    showRunConfig: false,
  },
};
