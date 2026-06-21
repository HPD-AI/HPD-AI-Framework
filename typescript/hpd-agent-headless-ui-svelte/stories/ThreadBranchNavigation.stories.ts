import type { Meta, StoryObj } from '@storybook/svelte-vite';
import ThreadBranchNavigationDemo from './ThreadBranchNavigationDemo.svelte';

const meta = {
  title: 'Thread/ThreadBranchNavigation',
  component: ThreadBranchNavigationDemo,
  tags: ['autodocs'],
  argTypes: {
    layout: {
      control: { type: 'inline-radio' },
      options: ['timeline', 'pager', 'list', 'tree'],
      description: 'Shows different visual treatments for the same fork-group state.',
    },
  },
} satisfies Meta<ThreadBranchNavigationDemo>;

export default meta;

type Story = StoryObj<typeof meta>;

export const TimelineInlineControls: Story = {
  args: {
    layout: 'timeline',
  },
};

export const CompactPager: Story = {
  args: {
    layout: 'pager',
  },
};

export const ForkGroupList: Story = {
  args: {
    layout: 'list',
  },
};

export const BranchTree: Story = {
  args: {
    layout: 'tree',
  },
};
