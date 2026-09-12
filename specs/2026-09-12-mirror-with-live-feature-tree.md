# TrueMirror — opposite-hand SOLIDWORKS parts with a live, editable feature tree

> Status: draft
> Date: 2026-09-12
> Target: free, open-source SOLIDWORKS add-in (C#, .NET Framework 4.8, xCAD.NET)

## TLDR

SOLIDWORKS can give you a mirrored part that stays linked to its parent, or an
independent part with no feature tree. It cannot give you both. `TrueMirror` adds
a `Mirror → Independent Part` command that walks the source part's feature tree,
reflects each feature's *definition* about the chosen plane, and re-authors it as
a native feature in a brand-new part document. The output has no external
references and a fully parametric tree: open `Extrude1`, change `25mm` to `30mm`,
rebuild.

The technical core is not the mirroring math. It is **reference re-resolution** —
features refer to faces and edges, those entities do not exist in the new
document, and SOLIDWORKS persistent reference IDs are per-document so they cannot
be carried across. Solving that with geometric matching is the contribution.

## Problem statement

Posted to r/SolidWorks on 2026-08-28, 76 upvotes, 35 comments:

> "It's 2026 and there's still no clean way to mirror a part/assembly into an
> independent, fully editable copy."

The complaint is accurate. Per the SOLIDWORKS 2024 help page
[Creating Opposite-Hand Versions of Parts](https://help.solidworks.com/2024/English/SolidWorks/sldworks/t_Mirror_Part.htm),
`Insert > Mirror Part` creates a *derived* part. Under **Transfer** you may bring
across custom properties, cut-list properties, sketches, planes, axes and model
dimensions. Under **Link** you may click **Break link to original part**, and the
help is explicit: "Once you break the link to the original, you cannot restore
it."

What never transfers, in either mode, is **the feature operations themselves**.
CATI's walkthrough of the same workflow confirms the shape of the result: you can
transfer sketches and Hole Wizard data, "but it will be referencing the original
part," and after breaking the link you hold a mirrored body plus some loose
sketches. There is no `Extrude1`, no `Cut-Extrude2`, no editable depth.

So today's two options are:

| Option | Independent? | Editable feature tree? |
|---|---|---|
| Mirror Part, keep link | No — parent drives it | No (derived) |
| Mirror Part, break link | Yes | **No — imported body + loose sketches** |
| Remodel by hand | Yes | Yes, at the cost of hours |
| **TrueMirror** | **Yes** | **Yes** |

## Thesis

A parametric part is *sketches plus an ordered list of operations on them*. If you
reflect every sketch about the mirror plane and replay the same operations in the
same order with reflected parameters, you get a genuine opposite-hand part with a
native tree — not a copy of a solid, but a copy of the *recipe*.

## What makes this non-trivial (read before scoping)

### 1. The obvious write path does not exist

The tempting design is: read each feature with `IFeature::GetDefinition`, mutate
the returned `…FeatureData` object, and re-create it with
`IFeatureManager::CreateDefinition` + `CreateFeature`. **This does not work for
the features that matter.** The
[CreateDefinition documentation](https://help.solidworks.com/2024/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IFeatureManager~CreateDefinition.html)
enumerates every supported `swFeatureNameID_e`, and the list contains patterns,
sheet-metal features, sweeps, fillet, thread, library features, bounding box,
ground plane, mirror-components, projection curve, belt/chain, tab-and-slot and
mate controller.

It does **not** contain extrude, cut-extrude, revolve, cut-revolve, loft, shell,
draft, rib, or Hole Wizard. Those are the features in essentially every part.

Therefore the architecture is **asymmetric**:

- **Read** a feature's parameters via `IFeature::GetTypeName2()` → `GetDefinition()`
  → cast to the typed data interface (`IExtrudeFeatureData2`,
  `ISimpleFilletFeatureData2`, …). Reading works for far more types than
  `CreateDefinition` can construct.
- **Write** via the direct typed creation methods on `IFeatureManager`:
  `FeatureExtrusion3`, `FeatureCut4`, `FeatureRevolve2`, `FeatureFillet3`,
  `FeatureLinearPattern5`, `FeatureCircularPattern5`, `InsertFeatureShell`, and
  friends. Use `CreateDefinition` only for the types where it genuinely is the
  supported path (patterns, sweeps, sheet metal, thread).

Getting this backwards is the single most likely way to burn two weeks.

### 2. Reference re-resolution is the actual hard problem

Feature parameters are not all numbers. A cut is "blind 12mm" but it may also be
"up to this face." A fillet is "3mm on *these four edges*." Those are pointers to
topological entities in the source document.

SOLIDWORKS offers persistent reference IDs via
`IModelDocExtension::GetPersistReference3` / `GetObjectByPersistReference3`, but
they identify an object **within one model document**. A byte array from the
source part is meaningless in a newly created part. `GetCorrespondingEntity` maps
between a component and its part in an *assembly* context, which is also not our
case.

So entity references must be re-resolved **geometrically**. The approach:

1. Before rebuilding feature *N*, the in-progress target body is, by construction,
   the mirror image of the source body at state *N-1*.
2. For each source entity `e` referenced by feature *N*, compute a
   mirror-invariant signature: reflected centroid (`IFace2::GetBox` /
   surface point evaluation), area or length, surface/curve type
   (`ISurface::IsPlane` / `IsCylinder` …), and radius where defined.
3. Reflect the signature about the mirror plane and search the target body for
   the unique entity whose signature matches within tolerance.
4. Zero matches or multiple matches → do not guess. Record an unresolved
   reference and trigger the fallback (§4).

This is a real, bounded engineering problem with a clean correctness criterion. It
is also the part worth writing up.

### 3. Mirroring reverses handedness, and some things must not flip

Reflection has determinant −1. Consequences to handle deliberately:

- **Arc and circle direction** reverses. Reflecting endpoints alone silently
  flips CW/CCW; normals and start/end order need explicit correction.
- **Draft angles** change sign.
- **Extrude direction flags** (`Dir`, reverse-direction) may need inverting
  depending on the sketch plane's orientation relative to the mirror plane.
- **Thread handedness is an engineering decision, not a geometric one.** A
  reflected right-hand thread is a left-hand thread. For an opposite-hand bracket
  the fasteners should almost always stay right-hand. Expose this as an explicit
  checkbox, default **"preserve thread handedness"**, and record the choice in the
  fidelity report.
- **Handed sketch relations.** Horizontal/vertical survive when the mirror plane
  is aligned with the sketch axes and need remapping otherwise.

The thread case is the one a naive implementation gets wrong and a reviewer will
immediately ask about.

### 4. Graceful degradation, not silent failure

The tool must never produce a wrong part quietly. When a feature type is
unsupported or a reference cannot be resolved, fall back:

1. Stop native transcription at feature *N*.
2. Insert the mirrored body as it stands into the target part.
3. Continue transcribing from *N+1* where the remaining features permit it.

The result is a hybrid tree — parametric up to *N*, imported after it — which is
still strictly better than today's single imported body. Every run emits a
**fidelity report**:

```
TrueMirror — Bracket_LH.sldprt
  31 features in source
  27 rebuilt natively        (87%)
   3 fell back  (Loft1, Loft2 unsupported; Fillet7 ambiguous edge match)
   1 skipped    (suppressed in source)
  Mass properties vs. SOLIDWORKS Mirror Part: volume Δ 0.0e+00, CoM Δ 1.2e-12 m
```

That report is the demo, the regression test, and the benchmark number.

## Architecture

```
TrueMirror.AddIn/          xCAD.NET add-in shell: CommandManager tab,
                           PropertyManagerPage, DI wiring
TrueMirror.Core/           engine — no SolidWorks UI dependencies
  Reader/                  feature tree walk + typed parameter extraction
  Transform/               mirror math; sketch, vector and angle reflection
  Resolver/                geometric entity re-resolution (§2)
  Writer/                  per-feature-type emitters (IFeatureManager.Feature*)
  Report/                  fidelity report + mass-property diff
TrueMirror.Tests/          corpus-driven regression suite
```

**On xCAD.NET's role, honestly:** use it for what it is good at — the add-in
shell, the CommandManager tab, PropertyManagerPage binding, icons, and DI. The
transcription core will talk to raw `SolidWorks.Interop.sldworks` most of the
time, because xCAD's abstractions do not cover per-feature-type parameter round
tripping. Do not fight the framework; drop to interop in `Reader/` and `Writer/`
and keep xCAD at the edges. Note that xCAD's public NuGet is 0.8.x on
.NET Framework, with .NET 8/9 support only on the 0.9 alpha feed — pin 4.8 and
avoid that question entirely for v1.

### Pipeline

```
select mirror plane
  └─ walk source tree (IPartDoc.FirstFeature → GetNextFeature)
      └─ classify (IFeature.GetTypeName2)
          └─ extract params (GetDefinition → typed data interface)
              └─ reflect params + reflect owned sketch
                  └─ resolve entity refs against in-progress target body
                      └─ emit native feature (IFeatureManager.Feature*)
                          └─ ForceRebuild3 + GetErrorCode2 check
  └─ mass-property diff vs. SOLIDWORKS Mirror Part output
  └─ fidelity report
```

Rebuild and verify after **every** feature, not at the end. A failure at feature 9
must be reported as feature 9, not as a broken part.

## Feature coverage, by phase

Ship v0.1 narrow and working rather than wide and broken.

| Phase | Feature types | Why here |
|---|---|---|
| **v0.1** | Sketch, Extrude (boss), Cut-Extrude, Revolve, Cut-Revolve — blind/through-all end conditions only, sketches on the three standard planes | Proves the whole loop end to end. Covers a large share of simple parts with zero entity references. |
| **v0.2** | Fillet (constant radius), Chamfer, up-to-face / up-to-surface end conditions, sketches on planar model faces | Introduces §2, the real contribution. This is the version worth writing about. |
| **v0.3** | Linear/Circular pattern, Mirror feature, Shell, Draft, Hole Wizard | Breadth. Hole Wizard is fiddly and deserves its own slice. |
| **v0.4** | Sweep, Loft, reference geometry (planes/axes/curves), multibody | Diminishing returns; only if the earlier phases land. |

**Explicit non-goals for v1.** Surface bodies; weldments; sheet metal (SOLIDWORKS
already mirrors this acceptably and `CreateDefinition` covers it separately);
configurations and design tables; in-context and external references; equations
and global variables; assemblies. Say so in the README — a stated non-goal reads
as judgment, an unstated one reads as a bug.

## Validation

Correctness must be measurable, not eyeballed.

1. **Geometric.** Build the same mirror two ways — `TrueMirror`, and SOLIDWORKS'
   own `Insert > Mirror Part` with the link broken. Compare via
   `IModelDocExtension::CreateMassProperty`: volume and surface area within
   1e-9 relative; center of mass equal to the source CoM reflected about the
   plane. This is the strongest possible oracle, and it is free — SOLIDWORKS
   ships the reference implementation of the geometry you are trying to match.
2. **Parametric.** The property today's workflow cannot deliver. Drive a
   dimension in the rebuilt part (`IDimension::SystemValue`), `ForceRebuild3`,
   assert zero feature errors via `IFeature::GetErrorCode2`, and assert the mass
   delta matches the same edit applied to the source.
3. **Corpus.** 20–30 parts of increasing complexity, checked into
   `TrueMirror.Tests/corpus/`. Report aggregate native-rebuild percentage per
   release. That single number is your headline metric and your changelog.

## Build plan

| Milestone | Deliverable | Done when |
|---|---|---|
| M0 | xCAD.NET add-in loads, CommandManager tab appears, dumps the source feature tree to a text file | Tree dump matches the FeatureManager for 5 test parts |
| M1 | Sketch reflection: read `ISketch` segments + relations, re-author mirrored in a new part | Reflected sketch is fully defined and geometrically matches |
| M2 | v0.1 feature set, single body, no entity refs | Mass-property test passes on 10 simple parts |
| M3 | Geometric entity resolver + fillet/chamfer/up-to-face | Resolver reports zero false matches on the corpus |
| M4 | Fidelity report, fallback path, PropertyManagerPage UI | Never produces a silently wrong part |
| M5 | Public release: README with a GIF, MIT licence, corpus number | Posted to r/SolidWorks |

M0–M2 is the demoable core. M3 is the part that makes it interesting.

## Risks

| Risk | Mitigation |
|---|---|
| Entity resolution is ambiguous on symmetric parts (a symmetric bracket has genuinely indistinguishable faces) | Ambiguity is detectable — multiple signature matches. Fall back rather than guess; symmetric parts barely need mirroring anyway |
| Feature type count balloons | Phase gating plus the fallback means partial coverage is still a shippable product |
| Reading a `…FeatureData` object requires `AccessSelections` and can dirty the source document | Open the source read-only, and always `ReleaseSelectionAccess`; assert the source is unmodified at the end of every test |
| No SOLIDWORKS licence | Student licence via BITS, or the free maker edition; the corpus tests need a real installation |
| Someone ships it first | Checked 2026-09-12: no GitHub project does this. Highest-starred SOLIDWORKS add-in repo overall is 50 stars, last touched 2017 |

## Why this is worth building

- The gap is **documented by the vendor** — "once you break the link you cannot
  restore it" — and **complained about by users**, with a dated, upvoted thread to
  cite.
- It is demoable in thirty seconds: two windows side by side, change a dimension
  in the mirrored part, watch it rebuild.
- It has a **number**: percent of features rebuilt natively across the corpus.
- The hard part generalises. "Rebuild a parametric feature tree and re-resolve
  entity references geometrically" is the same problem that stops AI-generated
  CAD from being editable, which is currently the open problem in the field.
  A mirror tool is a tractable place to solve a small version of it first.

## References

- [Creating Opposite-Hand Versions of Parts (SW 2024)](https://help.solidworks.com/2024/English/SolidWorks/sldworks/t_Mirror_Part.htm)
- [IFeatureManager::CreateDefinition — supported type list](https://help.solidworks.com/2024/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IFeatureManager~CreateDefinition.html)
- [IFeatureManager::FeatureExtrusion3](https://help.solidworks.com/2024/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IFeatureManager~FeatureExtrusion3.html)
- [IModelDocExtension::GetPersistReference3](https://help.solidworks.com/2022/English/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IModelDocExtension~GetPersistReference3.html)
- [xarial/xcad](https://github.com/xarial/xcad) — add-in framework
- [xarial/codestack](https://github.com/xarial/codestack) — SOLIDWORKS API example library
- [CATI: Mirroring Parts in SOLIDWORKS with Features and Sketches](https://www.cati.com/blog/mirror-part-with-sketches/)
- [r/SolidWorks: "It's 2026 and there's still no clean way to mirror a part…"](https://www.reddit.com/r/SolidWorks/comments/1w0la8k/its_2026_and_theres_still_no_clean_way_to_mirror/)
