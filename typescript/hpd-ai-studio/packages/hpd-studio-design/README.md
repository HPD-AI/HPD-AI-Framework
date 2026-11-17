# HPD Studio Design

Shared Tailwind v4 theme tokens, base rules, and public `studio-*` utilities for HPD AI Platform module packages.

`hpd-ai-studio` owns the final CSS build and imports this package. Module packages use this package as their public visual contract.

## Public Contract

- `theme.css`: primitive material, semantic roles, and the Tailwind v4 `@theme inline` bridge for color, type, radius, shadow, motion, and control sizing.
- `base.css`: global base rules for typography, focus, wrapping, selection, and box sizing.
- `utilities.css`: narrow `studio-*` decisions for panels, buttons, nav controls, badges, labels, focus rings, and text hardening.

Each module exports and composes its domain behavior while shared Svelte
components own routine presentation. Module packages should prefer these
primitives before adding package-local CSS. If a Svelte component uses `@apply`
inside a scoped `<style>` block, add `@reference "tailwindcss"` or reference the
host stylesheet so Tailwind can resolve utilities without duplicating output.

Run `npm run check` to validate custom-property resolution, public class
references, installed-module source registration, and raw module colors. The
Studio shell runs this check before every production build.
