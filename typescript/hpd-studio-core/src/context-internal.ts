import type { StudioModuleContextHandle, StudioModuleContextReader, StudioModuleContextWriter } from './contracts.ts';

const readers = new WeakMap<StudioModuleContextHandle, StudioModuleContextReader>();

export function createModuleContexts(moduleId: string): {
  handle: StudioModuleContextHandle;
  reader: StudioModuleContextReader;
  writer: StudioModuleContextWriter;
  clear(): void;
} {
  const values = new Map<string, unknown>();
  let active = true;
  const validate = (name: string) => {
    if (!/^[a-z0-9](?:[a-z0-9.-]{0,62}[a-z0-9])?$/.test(name)) {
      throw new StudioContextError('studio.context.nameInvalid');
    }
  };
  const reader: StudioModuleContextReader = Object.freeze({
    get<T>(name: string): T | undefined {
      validate(name);
      return active ? values.get(name) as T | undefined : undefined;
    }
  });
  const writer: StudioModuleContextWriter = Object.freeze({
    get: reader.get,
    set<T>(name: string, value: T) {
      validate(name);
      if (!active) throw new StudioContextError('studio.context.disposed');
      if (!values.has(name) && values.size >= 64) throw new StudioContextError('studio.context.capacityExceeded');
      values.set(name, value);
    },
    delete(name: string) {
      validate(name);
      if (active) values.delete(name);
    }
  });
  const handle = Object.freeze({ moduleId });
  readers.set(handle, reader);
  return {
    handle,
    reader,
    writer,
    clear() {
      active = false;
      values.clear();
      readers.delete(handle);
    }
  };
}

export function resolveModuleContext(handle: StudioModuleContextHandle): StudioModuleContextReader {
  const reader = readers.get(handle);
  if (!reader) throw new StudioContextError('studio.context.unavailable');
  return reader;
}

export class StudioContextError extends Error {
  readonly code: string;
  constructor(code: string) {
    super(code);
    this.name = 'StudioContextError';
    this.code = code;
  }
}
