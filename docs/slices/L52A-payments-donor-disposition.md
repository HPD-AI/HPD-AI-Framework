# L52A Payments donor disposition

Payments is donor-owned under the L52A integration map. The selected donor tip is
`863d34a7`, including the reproducibility sequence `99bb1a43`, `c819309d`, and
`863d34a7`.

Current main contained the earlier ADHR checkpoint and a later repository import,
but not the donor's completed route-proof runtime or native artifact
canonicalization. Applying only the final three patches would have restored pieces
of a deleted intermediate proof runner without its owning architecture. L52A
therefore ports the complete Payments tree at `863d34a7` and adapts its one BASE
restore call to current L53 by declaring `InPlaceRecovery` explicitly.

Package locks are not copied as final authority. They are regenerated with
`dotnet restore HPD-Payments.slnx --force-evaluate` against current main and then
validated with a locked restore. The resulting tree must pass command-manifest
validation and a Release solution build with zero warnings and errors.
