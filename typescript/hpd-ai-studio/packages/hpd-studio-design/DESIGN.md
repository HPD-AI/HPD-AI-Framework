# HPD Studio Design

HPD Studio Design is the shared visual contract for HPD AI Platform and its module packages.

## Product Register

HPD AI Platform is a product UI: a quiet, dense, task-focused workbench for building, testing, debugging, observing, and operating HPD systems. The design favors earned familiarity over novelty.

## Ownership

- `hpd-ai-studio/shell` owns app composition and the final Tailwind CSS build.
- `hpd-ai-studio/packages/hpd-studio-design` owns shared tokens, base rules, and public `studio-*` utilities.
- Module packages own components, routes, and package-local behavior.
- Module packages may use shared `studio-*` utilities and `studio-*` theme tokens as a stable contract.

## Typography

- Use one sans family across shell, modules, labels, controls, and data.
- Use fixed rem-based type sizes, not viewport-fluid typography.
- Keep labels compact and predictable.
- Use mono only for code, IDs, logs, JSON, and technical values.

## Color

- Use neutral surfaces first.
- Use accent color for primary actions, current selection, and meaningful state only.
- Avoid decorative gradients, glow, glass effects, and heavy inactive color.
- State colors must map consistently to success, warning, danger, info, loading, disabled, selected, and focus.
- Tokens use OKLCH values so future ramps can be adjusted by perceptual lightness and chroma.

## Layout

- Use structural responsive behavior: collapse navigation, reflow panels, and adapt tables.
- Use spacing scale values, not arbitrary one-off gaps.
- Prefer `gap` for sibling spacing.
- Cards are for bounded repeated items, modals, and framed tools. Do not nest cards inside cards.
- Every fixed-format UI element needs stable dimensions and overflow handling.
- Use logical properties in shared CSS so module packages inherit better RTL and writing-mode behavior.
- Prefer container-aware module layouts over viewport-only assumptions once modules gain real pages.

## Components

Every shared interactive component should support:

- Default
- Hover
- Focus
- Active
- Disabled
- Loading
- Error
- Success

## Current Public Utilities

- `studio-panel`: shared panel surface with border, radius, background, and shadow.
- `studio-button`: neutral button/link control with hover, active, disabled, and focus states.
- `studio-button-sm`: compact companion for toolbar actions.
- `studio-nav-control`: dark-navigation select/input control.
- `studio-nav-divider`: dark-navigation separator.
- `studio-nav-item`: dark-navigation link with hover, focus, and current-page states.
- `studio-badge`: neutral status badge.
- `studio-badge-good`, `studio-badge-danger`, `studio-badge-warning`, `studio-badge-info`: semantic badge tones.
- `studio-label`, `studio-label-on-nav`: compact product labels.
- `studio-focus-ring`: shared focus treatment for custom controls.
- `studio-text-safe`: wrapping guard for unpredictable strings.
- `studio-truncate`: single-line overflow guard.

This list should stay intentionally small. Add a utility only when multiple module packages need the same visual contract.
