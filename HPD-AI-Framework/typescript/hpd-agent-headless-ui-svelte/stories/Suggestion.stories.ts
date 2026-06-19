import type { Meta, StoryObj } from '@storybook/svelte-vite';
import SuggestionDemo from './SuggestionDemo.svelte';

const meta = {
  title: 'Thread/Suggestion',
  component: SuggestionDemo,
  tags: ['autodocs'],
  argTypes: {
    renderMode: {
      control: { type: 'inline-radio' },
      options: ['default', 'children', 'child'],
      description: 'Uses default button rendering or Svelte snippets.',
    },
    scenario: {
      control: { type: 'inline-radio' },
      options: ['populate', 'send-ready', 'send-busy'],
      description: 'Chooses populate mode, direct send, or blocked direct send.',
    },
  },
} satisfies Meta<SuggestionDemo>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Populate: Story = {
  args: {
    renderMode: 'default',
    scenario: 'populate',
  },
};

export const SendReady: Story = {
  args: {
    ...Populate.args,
    scenario: 'send-ready',
  },
};

export const SendBusy: Story = {
  args: {
    ...Populate.args,
    scenario: 'send-busy',
  },
};

export const ChildrenSnippet: Story = {
  args: {
    ...Populate.args,
    renderMode: 'children',
  },
};

export const FullDomControl: Story = {
  args: {
    ...Populate.args,
    renderMode: 'child',
  },
};

