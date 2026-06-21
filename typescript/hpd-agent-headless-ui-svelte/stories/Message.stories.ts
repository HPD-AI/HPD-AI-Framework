import type { Meta, StoryObj } from '@storybook/svelte-vite';
import MessageDemo from './MessageDemo.svelte';

const meta = {
  title: 'Message/Message',
  component: MessageDemo,
  tags: ['autodocs'],
  argTypes: {
    renderMode: {
      control: { type: 'inline-radio' },
      options: ['default', 'parts', 'child'],
      description: 'Uses default Message rendering, custom MessageParts, or full root replacement.',
    },
    scenario: {
      control: { type: 'inline-radio' },
      options: ['text', 'structured', 'working'],
      description: 'Controls the projected message shape.',
    },
    showActions: {
      control: 'boolean',
      description: 'Shows the MessageActionBar inside the Message root.',
    },
  },
} satisfies Meta<MessageDemo>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Text: Story = {
  args: {
    renderMode: 'default',
    scenario: 'text',
    showActions: true,
  },
};

export const StructuredParts: Story = {
  args: {
    ...Text.args,
    renderMode: 'parts',
    scenario: 'structured',
  },
};

export const Working: Story = {
  args: {
    ...Text.args,
    scenario: 'working',
  },
};

export const FullRootControl: Story = {
  args: {
    ...Text.args,
    renderMode: 'child',
    scenario: 'structured',
  },
};
