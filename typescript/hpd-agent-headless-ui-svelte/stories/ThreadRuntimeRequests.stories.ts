import type { Meta, StoryObj } from '@storybook/svelte-vite';
import ThreadRuntimeRequestsDemo from './ThreadRuntimeRequestsDemo.svelte';

const meta = {
  title: 'Thread/ThreadRuntimeRequests',
  component: ThreadRuntimeRequestsDemo,
  tags: ['autodocs'],
  argTypes: {
    renderMode: {
      control: { type: 'inline-radio' },
      options: ['default', 'custom'],
      description: 'Uses default request rendering or a custom request snippet.',
    },
    scenario: {
      control: { type: 'inline-radio' },
      options: ['mixed', 'known-only', 'custom-only', 'empty'],
      description: 'Changes the pending runtime request mix.',
    },
    useThread: {
      control: 'boolean',
      description: 'Uses a fake ThreadState store instead of static requests.',
    },
  },
} satisfies Meta<ThreadRuntimeRequestsDemo>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Tutorial: Story = {
  args: {
    renderMode: 'default',
    scenario: 'mixed',
    useThread: true,
  },
};

export const CustomRendering: Story = {
  args: {
    ...Tutorial.args,
    renderMode: 'custom',
  },
};

export const CustomOnly: Story = {
  args: {
    ...Tutorial.args,
    scenario: 'custom-only',
    renderMode: 'custom',
  },
};

export const Empty: Story = {
  args: {
    ...Tutorial.args,
    scenario: 'empty',
  },
};
