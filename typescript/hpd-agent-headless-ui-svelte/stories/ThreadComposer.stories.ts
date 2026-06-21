import type { Meta, StoryObj } from '@storybook/svelte-vite';
import ThreadComposerDemo from './ThreadComposerDemo.svelte';

const meta = {
  title: 'Thread/ThreadComposer',
  component: ThreadComposerDemo,
  tags: ['autodocs'],
  argTypes: {
    renderMode: {
      control: { type: 'inline-radio' },
      options: ['default', 'child'],
      description: 'Choose default form rendering or full child snippet control.',
    },
    submissionMode: {
      control: { type: 'inline-radio' },
      options: ['ready', 'busy', 'requesting', 'disabled'],
      description: 'Controls ready, active-work, pending-request, and local disabled states.',
    },
    autosize: {
      control: { type: 'inline-radio' },
      options: [false, 'pretext', 'custom'],
      description: 'Uses Pretext autosize, disables autosize, or applies a custom strategy.',
    },
    minRows: {
      control: { type: 'range', min: 1, max: 6, step: 1 },
      description: 'Minimum visual textarea rows.',
    },
    maxRows: {
      control: { type: 'range', min: 1, max: 12, step: 1 },
      description: 'Maximum visual textarea rows.',
    },
    clear: {
      control: { type: 'inline-radio' },
      options: ['on-submit', 'never'],
      description: 'Controls whether successful send clears the value.',
    },
    submitMode: {
      control: { type: 'inline-radio' },
      options: ['enter', 'mod-enter', 'none'],
      description: 'Controls keyboard submission. Shift+Enter always keeps a newline.',
    },
    initialValue: {
      control: 'text',
      description: 'Initial bound composer value.',
    },
    showRef: {
      control: 'boolean',
      description: 'Shows whether bind:textareaRef has attached.',
    },
  },
} satisfies Meta<ThreadComposerDemo>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {
  args: {
    renderMode: 'default',
    submissionMode: 'ready',
    autosize: 'pretext',
    minRows: 1,
    maxRows: 6,
    clear: 'on-submit',
    submitMode: 'enter',
    initialValue: '',
    showRef: true,
  },
};

export const ChildSnippet: Story = {
  args: {
    ...Default.args,
    renderMode: 'child',
  },
};

export const Busy: Story = {
  args: {
    ...Default.args,
    renderMode: 'child',
    submissionMode: 'busy',
    initialValue: 'This cannot submit while the thread is busy.',
  },
};

export const Requesting: Story = {
  args: {
    ...Default.args,
    renderMode: 'child',
    submissionMode: 'requesting',
    initialValue: 'This waits until the runtime request is answered.',
  },
};

export const CustomAutosize: Story = {
  args: {
    ...Default.args,
    renderMode: 'child',
    autosize: 'custom',
    minRows: 2,
    maxRows: 4,
    initialValue: 'The custom strategy controls the height.',
  },
};
