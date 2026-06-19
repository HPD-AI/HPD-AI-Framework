import type { Meta, StoryObj } from '@storybook/svelte-vite';
import MarkdownTextDemo from './MarkdownTextDemo.svelte';

const meta = {
  title: 'Message/MarkdownText',
  component: MarkdownTextDemo,
  tags: ['autodocs'],
  argTypes: {
    variant: {
      control: { type: 'inline-radio' },
      options: ['default', 'streaming', 'message-parts', 'custom'],
      description: 'Chooses direct rendering, streaming policy, MessageParts integration, or custom snippets.',
    },
  },
} satisfies Meta<MarkdownTextDemo>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {
  args: {
    variant: 'default',
  },
};

export const Streaming: Story = {
  args: {
    variant: 'streaming',
  },
};

export const MessagePartsIntegration: Story = {
  args: {
    variant: 'message-parts',
  },
};

export const CustomSnippet: Story = {
  args: {
    variant: 'custom',
  },
};
