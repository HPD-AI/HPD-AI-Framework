import { beforeAll } from 'vitest';
import { setProjectAnnotations } from '@storybook/svelte';
import * as projectAnnotations from './preview';

const project = setProjectAnnotations([projectAnnotations]);

beforeAll(project.beforeAll);
