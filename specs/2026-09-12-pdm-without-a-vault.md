# Can a SOLIDWORKS PDM project be built and tested without a PDM Professional vault?

> Status: analysis
> Date: 2026-09-12
> Verdict: **partially — and the split is sharp enough to design around.**

## Note on the source spec

This was asked as "read Carbon's SOLIDWORKS PDM spec and work out what's buildable
without a vault." **That spec is not on this machine.** Searched: every Conductor
workspace and repo (only `damascus` exists, and its only spec is TrueMirror), every
branch and checkpoint commit in `mech`, `~/Documents`, `~/Downloads`, `~/Desktop`,
and the Claude session history. The `last30days` research file that would have fed
it — `mesh-to-solid-bom-to-erp-re-entry-and-pdm-alternatives-for-engineering-teams-raw-unsexy.md`
— mentions PDM only in its title and entity line; it contains no PDM evidence
clusters.

So this document answers the general question from the PDM API and licensing
facts. When the Carbon spec surfaces, the layer table below is the thing to check
its features against.

## The licence wall, stated first

Two facts decide most of this:

1. **The PDM API is Professional-only.** PDM Standard ships no API, no add-ins,
   and no Tasks. This is the headline reason teams upgrade — Standard cannot even
   auto-generate PDFs, because Tasks are the Professional mechanism for it.
2. **The student/education licence ships PDM *Standard*.** The Student Engineering
   Kit and SOLIDWORKS Education Edition both include PDM Standard, not
   Professional.

Consequence: a BITS student licence gets you **zero** PDM API access. Not a
degraded version — none. There is no `EPDM.Interop.epdm` code path that runs on
Standard, and Dispatch (the no-code scripting tool people reach for instead) is
also Professional-only.

This is not a "figure it out as you go" constraint. Anything whose demo is
"watch the add-in fire on check-in" is blocked until someone hands you a
Professional vault.

## The four layers, and which survive

| Layer | Needs a vault? | Testable without one? |
|---|---|---|
| **A.** Compile-time interop dependency | No | Yes — see below |
| **B.** Pure domain logic | No | **Fully**, with ordinary xUnit |
| **C.** Vault gateway | No, to *write* | Yes, against a fake; real vault only for the contract run |
| **D.** Add-in lifecycle, hooks, server behaviour | **Yes** | **No.** Do not pretend otherwise |

### Layer A — getting it to compile

`EPDM.Interop.epdm.dll` lives in `C:\Program Files\SOLIDWORKS PDM\` and ships with
the *client* install, so a reference to it normally implies a SOLIDWORKS install
on the build machine. Two ways around that:

- **Preferred: don't reference it outside one project.** Only the gateway
  implementation project references the interop, with `<Private>false</Private>`.
  Everything else compiles anywhere, including a Linux CI runner.
- **If you need the gateway project itself to compile in CI:** the PDM API is
  plain COM, so you can hand-author `[ComImport, Guid(...)]` declarations for the
  handful of interfaces you touch. Treat this strictly as a build-and-typecheck
  device — a hand-declared vtable that is one entry off will corrupt memory at
  runtime, not throw. Ship against the real interop.

Beware a known trap: `IEdmVault5` exists in *two* namespaces —
`Interop.EdmLib.dll` and `EPDM.Interop.epdm.dll`. Pick one and never mix them.

### Layer B — pure domain logic (all of it, free)

This is where most of a good PDM project's value actually lives, and none of it
touches a vault:

- Revision and serial-number schemes — generation, parsing, collision rules.
- **The workflow as a state machine.** States, transitions, role permissions,
  conditions. Property-test it: every state reachable, every state has an exit,
  no transition escalates privilege, no cycle silently re-issues a revision.
- Data-card field validation rules.
- BOM flattening, quantity rollup, where-used graph traversal, and — the useful
  one — **diffing two BOM snapshots**.
- Part-number normalisation and filename parsing.
- ERP export mapping and CSV/XML shaping.

Target `netstandard2.0` here and this layer builds and tests on any machine,
no Windows required.

### Layer C — the gateway, and the trick that de-risks the whole thing

Define `IVaultGateway` over the ~15 operations you actually need: `GetFile`,
`GetFolder`, `CheckOut`, `CheckIn`, `UndoCheckOut`, `GetVariable`, `SetVariable`,
`ChangeState`, `Search`, `GetHistory`, `GetWhereUsed`.

Then write **two** implementations:

- `EpdmVaultGateway` — thin, dumb, 1:1 over the interop. No logic, therefore
  nothing worth unit-testing. Keep it boring on purpose.
- `InMemoryVaultGateway` — **not a mock.** A behaviourally faithful fake vault
  that enforces the same invariants the server does: you cannot check in a file
  you do not hold, illegal state transitions are refused, variables are scoped
  per-configuration-per-version, history is append-only.

Write **one contract test suite** that both implementations must pass. Run it
against the fake on every commit. The day you get vault access, point the identical
suite at the real one. Every assertion that fails is a place your mental model of
PDM was wrong — which is exactly the list you want, and it arrives in an afternoon
instead of over a fortnight of manual clicking.

### Layer D — what genuinely cannot be done

State these as open risks rather than discovering them late:

- **Add-in registration.** An `IEdmAddIn5` DLL is installed *into the vault*
  through the Administration tool and stored in the vault database. No vault, no
  loading the add-in at all.
- **Hook callbacks.** `EdmCmd_PreState`, `EdmCmd_PostAdd`, `EdmCmd_PreLock` and
  friends fire only inside a real vault. Their exact firing order, re-entrancy,
  and which of them can veto an operation are not things a fake can teach you.
- Archive server, cold storage, replication, permissions and group model.
- Data card (`.crd`) binding behaviour.
- **Performance.** The PDM API is chatty. An operation that returns instantly
  against an in-memory dictionary can take minutes across a 200k-file vault with
  a remote archive server. Any performance claim made without a real vault is
  fiction — do not put one in a README.

## Recommended project shape

```
Carbon.Core/                 netstandard2.0, zero PDM refs. Domain logic. 100% tested.
Carbon.Vault.Abstractions/   IVaultGateway + DTOs. Zero PDM refs.
Carbon.Vault.InMemory/       Faithful fake vault. Backs every test.
Carbon.Vault.Epdm/           net48. The ONLY project referencing the interop. Thin.
Carbon.AddIn/                net48. IEdmAddIn5 shell. Thin.
Carbon.Contract.Tests/       Shared suite: fake in CI, real vault when available.
Carbon.Core.Tests/           Fast, pure, green forever.
```

Everything except `Carbon.Vault.Epdm` and `Carbon.AddIn` is buildable and testable
today on a laptop with no SOLIDWORKS on it.

## Getting an actual vault, in order of realism

1. **VAR trial.** GoEngineer, TriMech, Javelin, Hawk Ridge and the Indian VARs run
   PDM Professional demo and trial vaults. A 30-day window is enough to run the
   contract suite and record the demo. This is the highest-leverage ask.
2. **Someone else's sandbox.** Any company already on PDM Pro can run your contract
   test suite against a sandbox vault. It is a single `dotnet test` invocation
   against a connection string — a low-friction favour to ask.
3. **BITS lab**, if the campus licence happens to be a commercial one rather than
   the education kit. Worth thirty seconds to check, but do not plan on it.

## The strategic point worth saying out loud

A PDM **add-in** is licence-gated at every level: Professional to develop,
Professional to install, Professional to demo. For a resume project that is a
real liability — a reviewer cannot run it, and neither can you, most days.

A PDM **alternative** — which is literally the framing of the research file this
came from — has no licence wall at all. Git-or-filesystem-backed check-in/check-out,
revision control and BOM-to-ERP export for teams too small to buy PDM is the same
domain logic (Layer B, all of it), builds and demos on any machine, and has a
larger audience than PDM Professional add-in users.

If Carbon's spec turns out to be mostly Layer B, that is the strong signal: the
Layers C/D wrapper is the disposable part, and the project is better off without it.

## References

- [Javelin: PDM Standard and Professional comparison](https://www.javelin-tech.com/blog/2015/10/solidworks-pdm-standard-and-professional-comparison/) — Standard has no API
- [TriMech: why teams upgrade from PDM Standard to Professional](https://trimech.com/why-engineering-teams-upgrade-from-solidworks-pdm-standard-to-professional/) — Tasks/add-ins/API are Professional-only
- [SolidPractices: Getting Started with the SOLIDWORKS PDM API](https://my.solidworks.com/support/solid-practices/openpdf?docId=66&langId=1)
- [CodeStack: how to create a PDM Professional add-in](https://www.codestack.net/solidworks-pdm-api/getting-started/add-ins/create/)
- [IEdmVault5 interface reference](https://help.solidworks.com/2023/english/api/epdmapi/EPDM.Interop.epdm~EPDM.Interop.epdm.IEdmVault5.html)
- [PDM Professional API troubleshooting guide](https://help.solidworks.com/2019/English/api/epdmapi/Troubleshooting_Guide.htm)
- [UCI: Student Engineering Kit contents](https://laptops.eng.uci.edu/engineering-software/solidworks-student-engineering-kit-for-hssoe-students) — SEK includes PDM Standard
