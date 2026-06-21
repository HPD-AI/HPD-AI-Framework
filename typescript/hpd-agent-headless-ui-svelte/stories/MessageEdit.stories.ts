import type { Meta, StoryObj } from '@storybook/svelte-vite';
import MessageEditDemo from './MessageEditDemo.svelte';

const meta = {
  title: 'Message/Message Edit',
  component: MessageEditDemo,
  tags: ['autodocs'],
  argTypes: {
    initialContent: {
      control: 'text',
      description: 'Initial user message copied into the edit draft.',
    },
    failSave: {
      control: 'boolean',
      description: 'Forces the fake revision client to fail the fork.',
    },
    submitMode: {
      control: { type: 'inline-radio' },
      options: ['enter', 'mod-enter', 'none'],
      description: 'Controls keyboard submission; Shift+Enter remains newline.',
    },
  },
} satisfies Meta<MessageEditDemo>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {
  args: {
    initialContent: 'Explain the thread projection in one paragraph.',
    failSave: false,
    submitMode: 'enter',
  },
};

export const Failure: Story = {
  args: {
    ...Default.args,
    failSave: true,
  },
};
