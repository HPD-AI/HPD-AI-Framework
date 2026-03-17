/**
 * Fragment-specific type definitions
 */

export interface FragmentIsolationConfig {
    enabled: boolean;
    endpoint?: string;
    piercing?: boolean;
    piercingClasses?: string[];
    routes?: string[];
    bound?: boolean;
}
