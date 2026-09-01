# @hpd-research/hpd-gateway-studio

Optional Svelte 5 HPD Gateway module for the product-neutral HPD Studio shell.

The module provides explicit namespace/target context, outcome-first Overview,
one lossless declaration editor, and governed immutable-revision workflows.
Operate uses only the generated Gateway client and preserves the distinction
between accepted, desired, delivered, active, and effective truth. It owns no
credentials, target discovery, browser persistence, Gateway authority, or
independent configuration model.

Operate also exposes bounded audit plus explicit Provision, Backup, and Purge
reviews. Diagnose presents serving/publication disagreement, effective
provenance, activation evidence, validated HTTPS observability links, and a
redacted deterministic local observation export capped at 1 MiB. Neither
workspace reads HPD.Base, YARP internals, or telemetry stores directly.

The package exports one authorization-neutral `studioModuleDescriptor` and one
`activateStudioModule` entrypoint. The shared Studio shell supplies the exact sealed
`gateway.admin` generated-client binding for the current principal generation. There is
no public module factory, bearer-token input, base-URL option, raw transport, string-named
context, or module-owned authentication lifecycle.
