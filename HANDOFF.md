# TrueMirror — handoff to a fresh session

> **You are picking this up cold on a Windows laptop that has SOLIDWORKS.**
> Read this whole file before doing anything. It is the compressed version of a
> long conversation you did not see.
>
> Last updated: 2026-09-14

---

## 1. What this project is

A free, open-source **SOLIDWORKS add-in** that makes opposite-hand (mirrored)
parts that are **independent AND still editable**.

The problem it solves: SOLIDWORKS gives you two options and both are bad.

| Option | Independent of the original? | Editable feature tree? |
|---|---|---|
| `Insert > Mirror Part`, keep link | No | No (derived) |
| `Insert > Mirror Part`, break link | Yes | **No — dumb imported body** |
| Remodel by hand | Yes | Yes, at the cost of hours |
| **TrueMirror** | **Yes** | **Yes** |

The insight: SOLIDWORKS copies the **cake**. TrueMirror copies the **recipe**.
A part is really an ordered list of operations (sketch → extrude → cut → fillet).
TrueMirror reads that list, reflects each step, and replays the steps into a new
document. Output is a native part with no external references and a live tree.

Grounded in a real, dated complaint:
[r/SolidWorks, 76 upvotes](https://www.reddit.com/r/SolidWorks/comments/1w0la8k/its_2026_and_theres_still_no_clean_way_to_mirror/)
— *"It's 2026 and there's still no clean way to mirror a part/assembly into an
independent, fully editable copy."*

This is a portfolio/resume project for a mechanical engineering student. It does
not need to be complete; it needs to be **real, demoable, and honest**.

---

## 2. Current state — read the distinction carefully

**Verified working (has actually happened on real hardware):**

- All three projects compile. 0 errors, 0 warnings.
- **38 xUnit tests pass.** They cover the mirroring math and the entity resolver
  and need no SOLIDWORKS: `dotnet test tests/TrueMirror.Tests -f net8.0`
- **The add-in builds, COM-registers, loads into SOLIDWORKS, and its three
  commands all execute.** Confirmed on SOLIDWORKS Student Edition via screenshots.
- **The sketch coordinate-space question is SETTLED.** This was the single largest
  unknown in the codebase. The `Verify Sketch Coordinate Space` diagnostic asked
  for model `(0, 0, 0.05)` and read back `(0, 0, 0.05)`. **The model-space
  assumption in `SketchWriter` is correct.** Do not re-open this.
- `Dump Feature Tree` ran successfully and captured 38 features from a real part.

**NOT yet verified — this is the whole remaining risk:**

- **`Mirror to Independent Part` has never successfully run on a genuinely
  parametric part.** It has only been tried on an imported STEP file, where
  0% is the correct answer (see §3). **This is the next thing to test.**
- `FeatureSupport.Map` (in `src/TrueMirror.Core/FeatureSupport.cs`) maps
  SOLIDWORKS `GetTypeName2` strings to support phases. Some entries are
  **certainly wrong** — those strings are badly documented. No real data yet.
- `FeatureRevolve2` parameter order — taken from the docs' parameter list, not a
  full syntax block. Weaker than extrude/cut, which were verified exactly.
- `InsertMirrorFeature2` argument order — the fallback path. Guarded: if it
  fails, the report says the body is the **wrong hand** and to discard it.

---

## 3. What the last test session found (important context)

The mirror command was run and reported **`0 of 20 features rebuilt natively`**.

**This was not a bug.** The test part was `Wheel2_Straight_Grousers.STEP` — an
**imported STEP file**. STEP files contain only the finished shape; the exporting
CAD system discards the feature history. There was no recipe to copy, so falling
back on all 20 was correct behaviour.

A fix has since been added: TrueMirror now **detects imported geometry** (bodies
present, zero sketches) and says so plainly instead of reporting a confusing 0%.

**Implication for you: do not test the mirror on imported/STEP parts.** It cannot
work on them, by definition. Test on parts modelled in SOLIDWORKS.

---

## 4. Do these, in this order

### Step 1 — prove the build (5 min)

```
dotnet test tests/TrueMirror.Tests -f net8.0
```

Expect `Passed! 38`. No admin, no SOLIDWORKS needed.

Then, in **PowerShell as Administrator** (required — COM registration writes to
HKLM):

```
dotnet build TrueMirror.sln
```

Expect `Build succeeded` and `Registering SOLIDWORKS add-in dll`.

Enable in SOLIDWORKS: **Tools → Add-Ins → TrueMirror** (tick both boxes).

### Step 2 — THE KEY TEST: mirror a real parametric part

This is the one thing that matters. Build a test part **by hand in SOLIDWORKS**:

1. File → New → Part
2. Sketch a rectangle on the **Front Plane** → Extruded Boss/Base
3. Sketch a circle on a face → Extruded Cut
4. Add a Fillet to one edge
5. **Save it** (the fallback path needs a file path on disk)

Tree should read: `Sketch1`, `Boss-Extrude1`, `Sketch2`, `Cut-Extrude1`, `Fillet1`.

Then select the **Front Plane** in the tree and click **Mirror to Independent
Part**.

**Success looks like:** a high native percentage, and `Volume delta vs. source:
0.00E+00`. Then open `Boss-Extrude1` in the new part, change a dimension, rebuild
— if it updates, **the entire thesis of the project is proven.**

**If it fails:** copy the full exception text. The add-in deliberately shows the
real stack trace rather than a friendly message. That text is the most valuable
thing you can produce.

### Step 3 — collect coverage data

Run **Dump Feature Tree** on 10–20 parts. Each writes `<partname>.truemirror.txt`
beside the part. The bottom of each file has:

```
COVERAGE
  v0.1     5   83.3%
  ...
UNKNOWN TYPES - add these to FeatureSupport.Map
  HoleWzd    x2   e.g. M6 Tapped Hole1
```

Add every `UNKNOWN TYPES` entry to `FeatureSupport.Map` with the right
`SupportLevel`. Repeat until the list is near empty.

> **Decision rule:** if v0.1 + v0.2 does not clear roughly **70%** of a typical
> part, re-scope the phases before writing more transcription code.

---

## 5. Gotchas that will waste your time

| Symptom | Cause | Fix |
|---|---|---|
| `error MSB3073 ... RegAsm.exe ... exited with code 1` | Not running as Administrator | Reopen PowerShell as admin. Title bar must say "Administrator" |
| `project file does not exist` | Wrong directory | `cd` to the folder containing `TrueMirror.sln` |
| Mirror reports 0% | Imported STEP/IGES part | Expected. Use a part modelled in SOLIDWORKS |
| Fallback says "never been saved" | Unsaved source part | Save the part first |
| Add-in missing from Tools → Add-Ins | Registration failed | See README "If the add-in does not appear" |

**Do not** re-investigate the sketch coordinate-space question. It is settled
(§2). **Do not** assume compile-verified means runtime-correct — every interop
signature was checked against the shipped assembly, but that is a different claim.

---

## 6. Design facts you must not accidentally "fix"

These are non-obvious, hard-won, and each has a test guarding it.

**The one rule everything derives from.** Reflection has determinant −1, so
reflecting all three axes of a sketch frame gives a **left**-handed frame, which
SOLIDWORKS rejects. Right-handedness is restored by negating one in-plane axis →
a sketch point at `(u, v)` lands at `(u, −v)`. Arc reversal, angle negation and
draft sign inversion all follow from that single flip.

**Arc direction is the subtle killer.** Reflecting an arc's three points *without*
flipping its direction flag produces the **complementary** arc — wrong by **20 mm
on a 10 mm arc**, while still rendering plausibly. `SketchMirrorTests` deliberately
reproduces this bug so it fails loudly if someone removes the flip.

**Extrude direction passes through UNCHANGED.** Counter-intuitive. The mirrored
sketch normal already equals the reflected source normal, so "along the sketch
normal" reflects itself. Flipping it extrudes backwards.

**The resolver refuses to guess.** SOLIDWORKS persistent reference IDs are
per-document and cannot cross into a new part, so fillet edges etc. are re-matched
geometrically. On a symmetric part two faces can be genuinely indistinguishable —
ambiguity triggers fallback, never a coin toss.

**Thread handedness is an engineering decision, not a geometric one.** A mirrored
right-hand thread is left-hand. An opposite-hand bracket still takes right-hand
fasteners, so the default preserves handedness.

**The source part is never modified.** `AccessSelections` takes an edit lock; every
call is paired with `ReleaseSelectionAccess` in a `finally`. The imported-body
fallback uses the source's *final* body rather than rolling the tree back, because
rolling back would modify the user's file.

---

## 7. Where things are

```
src/TrueMirror.Core/
  Geometry/      mirror math — no SOLIDWORKS dependency, fully unit-tested
  Model/         captured sketches and features as plain data
  Resolver/      geometric entity matching
  Interop/       every raw SOLIDWORKS call, isolated here on purpose
  Transcribe/    orchestration, ordering, fallback
  Report/        the fidelity report
src/TrueMirror.AddIn/    the xCAD add-in shell (3 commands)
tests/TrueMirror.Tests/  38 xUnit tests, run without SOLIDWORKS
tools/                   Python reference implementations of the math
macros/                  VBA macro version — no admin needed, for locked-down PCs
specs/                   full design documents
```

`TrueMirror.Core` multi-targets `net48;net8.0`. The net8.0 target deliberately
**excludes** everything SOLIDWORKS-dependent, which is what lets the tests run
anywhere. If you add a file using `SolidWorks.Interop`, add it to the exclusion
list in `TrueMirror.Core.csproj` or the net8.0 build breaks — that is intentional,
it keeps the pure/impure boundary honest.

**Note on Citrix:** if SOLIDWORKS is only available through Citrix/VDI, the add-in
**cannot** be installed (no HKLM write access, non-persistent image). Use
`macros/DumpFeatureTree.bas` instead — paste into Tools → Macro → New, press F5.
No admin required.

---

## 8. Honest framing

The hard, novel part of this project is **re-establishing entity references across
a document boundary** — the same problem that stops AI-generated CAD from being
editable. A mirror tool is a tractable place to solve a small version of it.

Do not oversell. The correct claim today is: *"builds clean, 38 tests pass, loads
into SOLIDWORKS, one of three commands verified end-to-end on real hardware."*
Not *"it works."*
