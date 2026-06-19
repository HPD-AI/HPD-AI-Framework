import type { StorybookConfig } from '@storybook/svelte-vite';
import { svelte } from '@sveltejs/vite-plugin-svelte';

const config: StorybookConfig = {
  stories: ['../stories/**/*.stories.ts'],
  addons: [],
  framework: {
    name: '@storybook/svelte-vite',
    options: {},
  },
  async viteFinal(config) {
    config.resolve = {
      ...(config.resolve ?? {}),
      conditions: ['svelte', 'browser', ...(config.resolve?.conditions ?? [])],
    };
    config.plugins = [
      svelte({
        include: [/\.svelte$/],
      }),
      ...(config.plugins ?? []),
    ];
    return config;
  },
};

export default config;
