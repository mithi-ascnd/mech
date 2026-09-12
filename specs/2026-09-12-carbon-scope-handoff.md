# Carbon — scope decision and handoff

> Status: handoff note, not a spec
> Date: 2026-09-12
> Written in the `damascus` worktree; Carbon itself belongs in a **new workspace**.

## Why this file exists

A fresh agent in the new Carbon workspace starts cold and will not have the
conversation that produced these decisions. This is that context, compressed.
The reasoning behind it is in
`/Users/mithieleshbabu/conductor/workspaces/mech/damascus/specs/2026-09-12-pdm-without-a-vault.md`
— read that first, it is the analysis this conclusion falls out of.

## Decision 1 — build a PDM *alternative*, not a PDM *add-in*

Confirmed with Mithielesh on 2026-09-12.

The rejected option was a SOLIDWORKS PDM Professional add-in. It fails on
licensing, not on engineering:

- The PDM API is **Professional-only**. PDM Standard has no API, no add-ins, no
  Tasks, and Dispatch is Professional-only too.
- The **student/education licence ships PDM Standard**, so there is no dev path
  at all on a BITS licence.
- An `IEdmAddIn5` DLL installs *into the vault* via the Administration tool. No
  vault means the add-in cannot even load, let alone demo.

For a resume project that is fatal: a reviewer cannot run it, and most days
neither can you.

The PDM alternative has the same domain logic, no licence wall, builds and demos
on a Mac, and targets teams too small to buy PDM — a larger audience than
Professional add-in users. Source research:
`~/Documents/Last30Days/mesh-to-solid-bom-to-erp-re-entry-and-pdm-alternatives-for-engineering-teams-raw-unsexy.md`
(note: that file is thin on PDM specifically — it names the topic but carries no
PDM evidence clusters. Fresh research is worth doing before committing to
positioning.)

## Decision 2 — new workspace, not this one

Three agents were concurrently writing into `damascus`: TrueMirror (C#/.NET 4.8,
`src/TrueMirror.*`), mesh-to-solid (Python, `mesh-to-solid/` + `.venv/`), and this
one. A root-level `Directory.Build.props` added here applied to TrueMirror's
projects and had to be reverted. Carbon needs its own workspace so it can own its
repo root — solution file, build props, CI.

## What carries over from the rejected design

The layered architecture from the analysis survives almost intact, because the
value was never in the PDM interop:

- **Domain core** — revision schemes, workflow-as-state-machine, BOM
  flatten/rollup/where-used/diff, part-number normalisation, ERP export mapping.
  This was "Layer B", the part that never needed a vault. It is now simply *the
  product* rather than the testable fraction of one.
- **The storage seam** — what was `IVaultGateway` over PDM becomes the seam over
  Git/filesystem. Keep the two-implementations-plus-one-contract-suite shape: a
  real backend and an in-memory fake that enforces the same invariants, with a
  single contract suite both must pass. It was the right idea for a reason
  unrelated to licensing.
- **The invariants worth enforcing** — cannot check in a file you do not hold,
  illegal state transitions refused, history append-only, cycle detection on
  assembly references.

Dropped entirely: `EPDM.Interop.epdm`, `IEdmAddIn5`, hook callbacks, data cards,
archive server, and the whole `[ComImport]` compile-without-SOLIDWORKS problem.

## Environment already prepared

- **.NET 8 SDK 8.0.425** installed at `~/.dotnet` (no sudo, user-local). Add
  `export PATH="$HOME/.dotnet:$PATH"`. This machine is arm64 macOS.
- No .NET was present before; nothing else was installed.

## Open questions for the new workspace

1. **Language.** .NET is installed and matches the SOLIDWORKS ecosystem, but
   nothing about a PDM alternative requires C# — if the tool is CLI-first,
   Python or Go may serve better. Decide deliberately rather than by inertia.
2. **What "check-out" means** when the backend is Git. Locking is the hard part:
   CAD files are binary and unmergeable, so the whole product hinges on
   advisory-or-enforced locks. `git-lfs` has a file-locking feature worth
   evaluating before building one.
3. **Scope of the first demo.** The analysis argued the headline should be a
   number or a thirty-second demo. Pick it before writing code.
