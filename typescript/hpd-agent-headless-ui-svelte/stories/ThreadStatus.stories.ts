import type { Meta, StoryObj } from '@storybook/svelte-vite';
import ThreadStatusDemo from './ThreadStatusDemo.svelte';

const meta = {
  title: 'Thread/ThreadStatus',
  component: ThreadStatusDemo,
  tags: ['autodocs'],
  argTypes: {
    renderMode: {
      control: { type: 'inline-radio' },
      options: ['default', 'metrics', 'children', 'child'],
      description: 'Uses default status rendering, metrics, or Svelte snippets.',
    },
    scenario: {
      control: { type: 'inline-radio' },
      options: ['ready', 'loading', 'error', 'disconnected', 'requesting', 'working'],
      description: 'Changes the thread snapshot used by the status primitive.',
    },
  },
} satisfies Meta<ThreadStatusDemo>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Tutorial: Story = {
  args: {
    renderMode: 'default',
    scenario: 'working',
  },
};

export const CustomChildren: Story = {
  args: {
    ...Tutorial.args,
    renderMode: 'children',
  },
};

export const WithMetrics: Story = {
  args: {
    ...Tutorial.args,
    renderMode: 'metrics',
  },
};

export const FullDomControl: Story = {
  args: {
    ...Tutorial.args,
    renderMode: 'child',
  },
};

export const Requesting: Story = {
  args: {
    ...Tutorial.args,
    scenario: 'requesting',
  },
};

export const Error: Story = {
  args: {
    ...Tutorial.args,
    scenario: 'error',
  },
};
