export type StudioDisplayObservation =
  | Readonly<{ state: 'unobserved' }>
  | Readonly<{ state: 'loading'; hasPrevious: boolean }>
  | Readonly<{ state: 'current'; observedAt?: string }>
  | Readonly<{ state: 'stale'; code: string; observedAt?: string }>
  | Readonly<{ state: 'unavailable' | 'denied' | 'unsupported' | 'failed'; code: string }>;
export interface StudioDisplayColumn { readonly id: string; readonly label: string; readonly width: 'compact'|'standard'|'wide'; }
export interface StudioDisplayRow { readonly id: string; readonly label: string; readonly cells: Readonly<Record<string, string>>; readonly status?: string; }
export interface StudioDisplayRailItem { readonly id: string; readonly label: string; readonly kind: string; readonly selected: boolean; readonly pinned?: boolean; }
