import type { Meta, StoryObj } from '@storybook/svelte-vite';
import FileAttachmentDemo from './FileAttachmentDemo.svelte';

const meta = {
  title: 'Thread/FileAttachment',
  component: FileAttachmentDemo,
  tags: ['autodocs'],
  argTypes: {
    renderMode: {
      control: { type: 'inline-radio' },
      options: ['default', 'child'],
      description: 'Uses default picker rendering or full child snippet control.',
    },
    uploadMode: {
      control: { type: 'inline-radio' },
      options: ['ready', 'slow', 'error'],
      description: 'Controls fake upload completion for Storybook inspection.',
    },
    includeDropzone: {
      control: 'boolean',
      description: 'Shows drag/drop backed by the same FileAttachmentState.',
    },
    disabled: {
      control: 'boolean',
      description: 'Disables picker and dropzone interactions.',
    },
  },
} satisfies Meta<FileAttachmentDemo>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {
  args: {
    renderMode: 'default',
    uploadMode: 'ready',
    includeDropzone: true,
    disabled: false,
  },
};

export const CustomDom: Story = {
  args: {
    ...Default.args,
    renderMode: 'child',
  },
};

export const SlowUpload: Story = {
  args: {
    ...Default.args,
    uploadMode: 'slow',
  },
};

export const UploadError: Story = {
  args: {
    ...Default.args,
    uploadMode: 'error',
    renderMode: 'child',
  },
};
