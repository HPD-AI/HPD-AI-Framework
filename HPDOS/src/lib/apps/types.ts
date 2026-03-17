/**
 * App Registry Type Definitions
 */

export interface AppManifest {
    /** Unique app identifier (kebab-case) */
    id: string;

    /** Human-readable display name */
    name: string;

    /** Semantic version string (e.g., "1.0.0") */
    version: string;

    /** Icon/emoji for UI display */
    icon: string;

    /** Lazy-loaded Svelte component */
    component: () => Promise<{ default: any }>;

    /** Short description */
    description?: string;

    /** App category for organization */
    category?: AppCategory;

    /** Keywords for search */
    keywords?: string[];

    /** Default state cloned for each new tab instance */
    defaultState?: Record<string, unknown>;

    /** Backend IApplication ID (first-party apps) */
    backendAppId?: string;

    /** Required capabilities */
    requiredCapabilities?: AppCapability[];

    /**
     * Fragment isolation — runs the app in its own iframe JS context via web-fragments.
     * When enabled, DynamicAppLoader renders a <web-fragment> element instead of
     * mounting the Svelte component directly.
     */
    isolation?: {
        /** Enable fragment isolation (default: false) */
        enabled: boolean;
        /** Fragment endpoint URL (defaults to /apps/{app-id}) */
        endpoint?: string;
        /** Enable server-side rendering / piercing for this fragment */
        piercing?: boolean;
        /** CSS classes that pierce through shadow DOM */
        piercingClasses?: string[];
        /** Additional route patterns */
        routes?: string[];
        /** true = share navigation with shell (bound), false = standalone iframe */
        bound?: boolean;
    };

    /** Called when tab is opened */
    onMount?: (tab: AppTab) => void | Promise<void>;

    /** Called when tab is closed */
    onUnmount?: (tab: AppTab) => void | Promise<void>;
}

export type AppCategory =
    | 'productivity'
    | 'development'
    | 'media'
    | 'communication'
    | 'utilities'
    | 'games'
    | 'custom';

/** Runtime app tab instance */
export interface AppTab {
    id: string;
    appId: string;
    label: string;
    icon: string;
    state: Record<string, unknown>;
    isDirty?: boolean;
    createdAt: Date;
    lastActive?: Date;
}

/** Props received by every app component */
export interface AppComponentProps {
    state: Record<string, unknown>;
    tabId: string;
    manifest?: AppManifest;
}

/** Capability descriptor */
export interface AppCapability {
    kind: string;
    paths?: string[];
    hosts?: string[];
    commands?: string[];
}
