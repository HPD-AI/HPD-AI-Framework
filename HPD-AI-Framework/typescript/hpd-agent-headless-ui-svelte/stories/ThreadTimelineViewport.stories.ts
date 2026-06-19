import type { Meta, StoryObj } from '@storybook/svelte-vite';
import ThreadTimelineViewportDemo from './ThreadTimelineViewportDemo.svelte';

const meta = {
  title: 'Thread/ThreadTimelineViewport',
  component: ThreadTimelineViewportDemo,
  tags: ['autodocs'],
  argTypes: {
    autoScroll: {
      control: 'boolean',
      description: 'Enables automatic scrolling while the user stays near the active turn.',
    },
    turnAnchor: {
      control: { type: 'inline-radio' },
      options: ['top', 'bottom'],
      description: 'Controls whether new user turns anchor near the top or pin to the bottom.',
    },
    scrollBehavior: {
      control: { type: 'inline-radio' },
      options: ['auto', 'instant', 'smooth'],
      description: 'Native scroll motion passed to scrollTo()/scrollIntoView().',
    },
    anchorBlock: {
      control: { type: 'inline-radio' },
      options: ['start', 'center', 'end', 'nearest'],
      description: 'Native block-axis alignment for anchored timeline items.',
    },
    anchorInline: {
      control: { type: 'inline-radio' },
      options: ['start', 'center', 'end', 'nearest'],
      description: 'Native inline-axis alignment for anchored timeline items.',
    },
    scrollContainer: {
      control: { type: 'inline-radio' },
      options: ['nearest', 'all'],
      description: 'Native scrollIntoView container scope.',
    },
    renderMode: {
      control: { type: 'inline-radio' },
      options: ['default', 'custom'],
      description: 'Uses default ThreadTimeline rendering or a custom child snippet.',
    },
    scenario: {
      control: { type: 'inline-radio' },
      options: ['long', 'streaming', 'empty'],
      description: 'Changes the timeline content shown in the viewport.',
    },
    atBottomThreshold: {
      control: { type: 'range', min: 0, max: 160, step: 8 },
      description: 'Distance from bottom, in pixels, considered pinned.',
    },
    showJumpControl: {
      control: 'boolean',
      description: 'Shows a jump-to-bottom control wired through the viewport API.',
    },
  },
} satisfies Meta<ThreadTimelineViewportDemo>;

export default meta;

type Story = StoryObj<typeof meta>;

export const TopAnchored: Story = {
  args: {
    anchorBlock: 'start',
    anchorInline: 'nearest',
    autoScroll: true,
    renderMode: 'custom',
    scenario: 'long',
    scrollBehavior: 'auto',
    scrollContainer: 'nearest',
    atBottomThreshold: 48,
    showJumpControl: true,
    turnAnchor: 'top',
  },
};

export const BottomPinned: Story = {
  args: {
    ...TopAnchored.args,
    turnAnchor: 'bottom',
  },
};

export const NoAutoScroll: Story = {
  args: {
    ...TopAnchored.args,
    autoScroll: false,
  },
};

export const StreamingWork: Story = {
  args: {
    ...TopAnchored.args,
    scenario: 'streaming',
  },
};

export const DefaultTimeline: Story = {
  args: {
    ...TopAnchored.args,
    renderMode: 'default',
  },
};

export const Empty: Story = {
  args: {
    ...TopAnchored.args,
    scenario: 'empty',
  },
};
