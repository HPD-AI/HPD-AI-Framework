import type { Meta, StoryObj } from '@storybook/svelte-vite';
import DiffViewerDemo from './DiffViewerDemo.svelte';

const meta = {
  title: 'Message/DiffViewer',
  component: DiffViewerDemo,
  tags: ['autodocs'],
  argTypes: {
    variant: {
      control: { type: 'inline-radio' },
      options: ['unified', 'split', 'folded', 'markdown', 'custom'],
      description: 'Chooses unified, split, folded, MarkdownText integration, or custom snippets.',
    },
  },
} satisfies Meta<DiffViewerDemo>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Unified: Story = {
  args: { variant: 'unified' },
};

export const Split: Story = {
  args: { variant: 'split' },
};

export const Folded: Story = {
  args: { variant: 'folded' },
};

export const MarkdownIntegration: Story = {
  args: { variant: 'markdown' },
};

export const CustomSnippet: Story = {
  args: { variant: 'custom' },
};
