# TrueMirror

A free, open-source SOLIDWORKS add-in for creating opposite-hand parts that keep a
live, editable feature tree.

SOLIDWORKS today gives you a mirrored part that stays linked to its parent, or an
independent part with no feature tree. Not both. TrueMirror transcribes the
*recipe* rather than copying the *solid*, so the output is a native part with no
external references and a tree you can actually edit.

Full design: [`specs/2026-09-12-mirror-with-live-feature-tree.md`](specs/2026-09-12-mirror-with-live-feature-tree.md)

## Status: compiles, unit-tested, never run against SOLIDWORKS

- **Builds clean.** All three projects compile with 0 errors, 0 warnings
  (verified on macOS via `dotnet build`, using the .NET Framework reference
  assemblies package).
- **38 unit tests pass.** They cover the mirroring math, the arc-direction rule,
  and the entity resolver, and they run on any machine with no SOLIDWORKS and no
  licence: `dotnet test tests/TrueMirror.Tests -f net8.0`.
- **Nothing has ever executed against a live SOLIDWORKS.** Every interop
  signature was verified by reflecting over the shipped interop assembly, but
  verified-to-compile is not verified-to-work.

To try it on a Windows machine, follow **[TESTING.md](TESTING.md)** — it needs
only the .NET 8 SDK (~200 MB), not Visual Studio.

## Requirements

- Windows (SOLIDWORKS is Windows-only)
- SOLIDWORKS 2021 or later, 64-bit
- **.NET 8 SDK** (~200 MB) — that is all. Visual Studio is optional; the project
  pulls `Microsoft.NETFramework.ReferenceAssemblies` so `dotnet build` handles
  net48 on a bare machine.
- Administrator rights, for the COM registration step only

Pinned to .NET Framework 4.8 on purpose: xCAD's stable release (0.8.3, which adds
SOLIDWORKS 2026 support) ships `lib/net461`. Its .NET 8/9 support exists only on a
0.9 alpha feed.

## Build and install

SOLIDWORKS add-ins are COM servers. The `Xarial.XCad.SolidWorks` package registers
the built DLL automatically by running `RegAsm.exe /codebase` in an `AfterBuild`
target. That writes to `HKEY_LOCAL_MACHINE`, so:

> **Open PowerShell as Administrator.** Without it the build fails at the
> registration step, and the error usually does not say "you need admin."

```
dotnet build TrueMirror.sln
```

Then start SOLIDWORKS and enable the add-in under **Tools > Add-Ins > TrueMirror**.

### If the add-in does not appear

1. Check the build output contains `Registering SOLIDWORKS add-in dll`.
2. Confirm the platform is x64. A 32-bit build will never load.
3. Register by hand:
   ```
   %windir%\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe ^
     "src\TrueMirror.AddIn\bin\Debug\net48\TrueMirror.AddIn.dll" /codebase
   ```
4. Check `HKLM\SOFTWARE\SolidWorks\Addins\{B824F3E0-3A47-4746-A0E6-E63A13D2CDDA}`.

### Debugging

Set the AddIn project's debug target to `SLDWORKS.exe` and F5.

## Commands

| Command | What it does |
|---|---|
| **Mirror to Independent Part** | The product. Select a planar face or plane, run it, get a mirrored part with a native tree. |
| **Dump Feature Tree** | Diagnostic. Writes the tree plus a transcription-coverage summary. Use it to triage a corpus. |
| **Verify Sketch Coordinate Space** | One-off. Resolves the single largest unverified assumption in the codebase — run it first. |

## Run these three things in order

**1. Run the unit tests.** No SOLIDWORKS needed. They cover the mirroring math,
the arc-direction rule, and the entity resolver.

```
dotnet test tests/TrueMirror.Tests -f net8.0
```

All 38 pass today. `-f net8.0` runs them without .NET Framework, so this works on
any machine — including a Mac or a CI runner.

**2. Run *Verify Sketch Coordinate Space* on a scratch part.** `SketchWriter`
assumes the sketch API takes model-space coordinates. For a sketch on a standard
plane through the origin, model and sketch coordinates coincide, which is why
almost every published example fails to disambiguate it — and why a wrong
assumption here would only show up on tilted planes. The diagnostic creates an
offset plane so the discrepancy, if any, is glaring. **Do this before trusting
any mirrored output.**

**3. Run *Dump Feature Tree* over 15–20 parts.** `FeatureSupport.Map` maps
`GetTypeName2` strings to phase support, and some entries are certainly wrong —
those strings are poorly documented. Each report ends with an `UNKNOWN TYPES`
section. Add them, repeat until it is near empty, and you will know with a number
rather than a guess whether the v0.1 scope is worth building on.

> If v0.1 + v0.2 does not clear roughly **70%** of a typical part, re-scope the
> phases before writing more transcription code.

## Project layout

```
src/TrueMirror.Core/
  Geometry/      mirror math - no SOLIDWORKS dependency, fully unit-tested
  Model/         captured sketches and features as plain data
  Resolver/      geometric entity matching (replaces persistent reference IDs)
  Interop/       every raw SOLIDWORKS call, isolated here on purpose
  Transcribe/    orchestration, ordering, fallback
  Report/        fidelity report
src/TrueMirror.AddIn/    xCAD add-in shell
tests/TrueMirror.Tests/  xUnit tests - run without SOLIDWORKS
tools/                   Python reference implementations of the math
specs/                   design documents
```

## The three things most likely to be wrong

Stated plainly so you know where to look when something misbehaves.

1. **Sketch coordinate space** — see step 2 above. Isolated to `SketchWriter`.
2. **`FeatureRevolve2` parameter order** — taken from the documented parameter
   list rather than a syntax block. Extrude and cut were verified against full
   signatures; revolve is the weaker of the three.
3. **`InsertMirrorFeature2` argument order** — the fallback path. It is guarded:
   if the mirror step fails, the report says the body is the *wrong hand* and
   tells you to discard the result rather than quietly shipping a right-hand part
   labelled left.

## Design notes worth knowing

**The single rule everything derives from.** Reflection has determinant −1, so
reflecting all three axes of a sketch frame yields a *left*-handed frame, which
SOLIDWORKS rejects. Right-handedness is restored by negating one in-plane axis,
which means a sketch point at `(u, v)` lands at `(u, −v)`. That one sign flip is
why arcs reverse sweep, why angles negate, and why draft angles change sign.

**Arc direction is the subtle killer.** Reflecting an arc's three points without
flipping its direction flag produces the *complementary* arc. The Python
reference measured this at **20 mm of error on a 10 mm arc** — geometry that
still renders plausibly. There is a test that deliberately reproduces the bug so
nobody "simplifies" the fix away.

**Extrude direction passes through unchanged.** Counter-intuitive, but the
mirrored sketch normal already equals the reflected source normal, so "along the
sketch normal" reflects itself. Flipping it would extrude backwards.

**The resolver refuses to guess.** On a symmetric part two faces can be genuinely
indistinguishable. Ambiguity triggers fallback rather than a coin toss.

**Thread handedness is an engineering decision, not a geometric one.** A mirrored
right-hand thread is left-hand. An opposite-hand bracket still takes right-hand
fasteners, so the default preserves handedness and the report says so.

**The source part is never modified.** Reading feature references needs
`AccessSelections`, which takes an edit lock; every one is paired with
`ReleaseSelectionAccess` in a `finally`. The imported-body fallback deliberately
uses the source's *final* body rather than rolling the tree back to an
intermediate state, because rolling back would modify the user's part.

## Licence

MIT.
