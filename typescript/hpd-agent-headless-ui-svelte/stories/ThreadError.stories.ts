import type { Meta, StoryObj } from '@storybook/svelte-vite';
import ThreadErrorDemo from './ThreadErrorDemo.svelte';

const meta = {
  title: 'Thread/ThreadError',
  component: ThreadErrorDemo,
  tags: ['autodocs'],
  argTypes: {
    renderMode: {
      control: { type: 'inline-radio' },
      options: ['default', 'children', 'child'],
      description: 'Uses the default error rendering or Svelte snippets.',
    },
    scenario: {
      control: { type: 'inline-radio' },
      options: ['thread', 'run', 'work', 'tool', 'multiple', 'none'],
      description: 'Changes the normalized error source in the thread snapshot.',
    },
    showAll: {
      control: 'boolean',
      description: 'Shows every normalized error in the default renderer.',
    },
  },
} satisfies Meta<ThreadErrorDemo>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Tutorial: Story = {
  args: {
    renderMode: 'default',
    scenario: 'thread',
    showAll: false,
  },
};

export const AllErrors: Story = {
  args: {
    ...Tutorial.args,
    scenario: 'multiple',
    showAll: true,
  },
};

export const CustomChildren: Story = {
  args: {
    ...Tutorial.args,
    renderMode: 'children',
  },
};

export const FullDomControl: Story = {
  args: {
    ...Tutorial.args,
    renderMode: 'child',
    scenario: 'tool',
  },
};

export const Empty: Story = {
  args: {
    ...Tutorial.args,
    scenario: 'none',
  },
};

