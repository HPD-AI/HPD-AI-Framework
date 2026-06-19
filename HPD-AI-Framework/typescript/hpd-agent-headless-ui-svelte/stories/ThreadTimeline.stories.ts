import type { Meta, StoryObj } from '@storybook/svelte-vite';
import ThreadTimelineDemo from './ThreadTimelineDemo.svelte';

const meta = {
  title: 'Thread/ThreadTimeline',
  component: ThreadTimelineDemo,
  tags: ['autodocs'],
  argTypes: {
    renderMode: {
      control: { type: 'inline-radio' },
      options: ['default', 'custom'],
      description: 'Uses default leaves or custom snippets for timeline items.',
    },
    scenario: {
      control: { type: 'inline-radio' },
      options: ['mixed', 'transcript', 'work', 'requests', 'empty'],
      description: 'Changes which timeline item kinds are shown.',
    },
    compactWork: {
      control: 'boolean',
      description: 'Shows completed work collapsed instead of active work expanded.',
    },
  },
} satisfies Meta<ThreadTimelineDemo>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Tutorial: Story = {
  args: {
    renderMode: 'custom',
    scenario: 'mixed',
    compactWork: true,
  },
};

export const DefaultLeaves: Story = {
  args: {
    ...Tutorial.args,
    renderMode: 'default',
  },
};

export const ActiveWork: Story = {
  args: {
    ...Tutorial.args,
    scenario: 'work',
    compactWork: false,
  },
};

export const TranscriptOnly: Story = {
  args: {
    ...Tutorial.args,
    scenario: 'transcript',
  },
};
