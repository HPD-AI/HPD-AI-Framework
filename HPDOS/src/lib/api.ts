const base = () => (window as any).__HPDOS_API_BASE as string ?? ''

export const api = (path: string, init?: RequestInit) =>
    fetch(`${base()}${path}`, init)
