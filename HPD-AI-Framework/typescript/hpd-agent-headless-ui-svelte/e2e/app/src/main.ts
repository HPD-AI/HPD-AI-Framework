import App from './thread-fork-controls-app.svelte';
import { mount } from 'svelte';

const target = document.getElementById('app');
if (!target) throw new Error('Missing app mount target');

mount(App, { target });
