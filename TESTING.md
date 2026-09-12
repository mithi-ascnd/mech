# Testing TrueMirror on a borrowed Windows laptop

Written for a short session on someone else's machine. Ordered so that if you run
out of time, you still got the valuable part.

**Total install footprint: about 200 MB.** You do NOT need Visual Studio.

---

## Before you go: check these two things

Ask your friend, or check yourself when you sit down.

1. **Do you have Administrator on that laptop?** Registering a SOLIDWORKS add-in
   writes to `HKEY_LOCAL_MACHINE`. Without admin, Steps 3-5 will not work.
   Steps 1-2 still will, and they are worth doing on their own.
2. **Which SOLIDWORKS version?** Help > About. Anything 2021+ is fine.

Also: **bring a part file of your own** (a simple bracket with an extrude, a cut,
and a fillet). Testing on your friend's work files is a bad idea.

---

## Step 0 — get the code on the machine (2 min)

Easiest is a USB stick or GitHub. If using git:

```
git clone <your-repo-url>
cd TrueMirror
```

No repo yet? Copy the folder across. You need `src/`, `tests/`, `TrueMirror.sln`.

---

## Step 1 — install the .NET SDK (5 min, ~200 MB)

Download the **.NET 8 SDK** from <https://dotnet.microsoft.com/download>.
Run the installer. That is the only install required.

Verify in a new PowerShell window:

```
dotnet --version
```

You should see `8.x.x`.

> You do not need Visual Studio, the .NET Framework developer pack, or the
> targeting pack. The project pulls `Microsoft.NETFramework.ReferenceAssemblies`
> from NuGet, which is how CI machines build .NET Framework projects.

---

## Step 2 — run the unit tests (2 min) — DO THIS EVEN IF NOTHING ELSE WORKS

```
dotnet test tests/TrueMirror.Tests -f net8.0
```

Expected:

```
Passed!  - Failed: 0, Passed: 38, Skipped: 0, Total: 38
```

**38 tests.** No SOLIDWORKS, no admin, no licence needed. They cover the
mirroring math, the arc-direction rule, and the entity resolver — the parts most
likely to be subtly wrong.

If this passes you have already proved the hardest third of the project works.
Everything after this is about whether the SOLIDWORKS plumbing is hooked up right.

---

## Step 3 — build the add-in (3 min, needs admin)

Open PowerShell **as Administrator** (right-click > Run as administrator), then:

```
cd <path to TrueMirror>
dotnet build TrueMirror.sln
```

Expected: `Build succeeded`, and a line mentioning
`Registering SOLIDWORKS add-in dll`.

### If it fails

| What you see | What it means | Fix |
|---|---|---|
| `error MSB3073 ... RegAsm.exe ... exited with code 1` | Not running as admin | Reopen PowerShell as Administrator |
| `error CS...` | A genuine compile error | Send me the exact text — this build is verified on macOS, so a Windows-only error is interesting |
| `NU1101 ... Xarial.XCad.SolidWorks` | No internet / blocked NuGet | Connect to the internet; NuGet must be reachable once |

### Registering by hand (if the automatic step misbehaves)

```
%windir%\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe ^
  "src\TrueMirror.AddIn\bin\Debug\net48\TrueMirror.AddIn.dll" /codebase
```

---

## Step 4 — load it in SOLIDWORKS (2 min)

1. Start SOLIDWORKS.
2. **Tools > Add-Ins**.
3. Tick **TrueMirror** (both boxes: Active Add-ins, and Start Up if you want it
   to persist).
4. A **TrueMirror** tab should appear in the CommandManager.

**Not there?** Check the DLL is x64 (it is, by default) and that the registry key
`HKLM\SOFTWARE\SolidWorks\Addins\{B824F3E0-3A47-4746-A0E6-E63A13D2CDDA}` exists.

---

## Step 5 — the three commands, in this order

### 5a. Verify Sketch Coordinate Space (30 sec) — RUN THIS FIRST

Open a **new, empty part** (File > New > Part). Do not use a real part; this
command adds a reference plane and a sketch.

Click **Verify Sketch Coordinate Space**. You get a message box like:

```
VERIFY: asked for model (0, 0, 0.05); read back (0, 0, 0.05).
```

- **Z reads ~0.05** → the model-space assumption holds. Good, carry on.
- **Z reads ~0** → the sketch API is sketch-local, and `SketchWriter` needs to
  stop calling `Frame.ToModel`. **Tell me this number.** It is the single largest
  unverified assumption in the codebase and this is exactly the experiment that
  settles it.

### 5b. Dump Feature Tree (1 min per part) — the highest-value data

Open one of your own parts. Click **Dump Feature Tree**. It writes
`<partname>.truemirror.txt` beside the part file.

Open that file. The bottom has two sections that matter:

```
COVERAGE
  v0.1     5   83.3%
  v0.2     1   16.7%
  through v0.2: 100.0% of 6 features

UNKNOWN TYPES - add these to FeatureSupport.Map
  HoleWzd    x2   e.g. M6 Tapped Hole1
```

Do this on **as many parts as you can** — 10 to 20 if you have time. Then send me
the `UNKNOWN TYPES` lists. That converts my guesses about SOLIDWORKS type names
into real data, and tells us with a number whether the v0.1 scope is worth
building on.

> Decision rule from the spec: if v0.1 + v0.2 does not clear roughly **70%** of a
> typical part, we re-scope before writing more transcription code.

### 5c. Mirror to Independent Part (the actual product)

1. Open a **simple** part first — one extrude, ideally. Not your most complex model.
2. **Save a copy first.** This is alpha code on a borrowed laptop.
3. Click a planar face or a plane (Front/Top/Right) in the tree to select it.
4. Click **Mirror to Independent Part**.

You get a summary box and a `.truemirror-report.txt` file.

**What success looks like:**

```
6 of 6 features rebuilt natively (100.0%).
Volume delta vs. source: 0.00E+00 (a correct mirror is 0).
```

Then, in the new part: open `Boss-Extrude1`, change a dimension, rebuild. If it
updates, the whole thesis of the project is proven — that is the thing SOLIDWORKS
cannot currently give you.

**What partial success looks like** (still useful):

```
VERDICT: hybrid result - parametric for 4 of 6 features, with the remaining 2
covered by an imported mirrored body.
```

**What to treat as a failure and discard:**

Anything saying `WRONG HAND` or `NOT APPLIED`. The report is written to be blunt
about this rather than let you ship a right-hand part labelled left.

---

## What to bring back to me

In rough priority order:

1. **Any `error CS` text from Step 3.** The build is verified on macOS, so a
   Windows compile error is genuinely new information.
2. **The Z number from Step 5a.**
3. **The `UNKNOWN TYPES` sections** from as many parts as you dumped.
4. **The fidelity report** from Step 5c, especially the `FELL BACK` lines.
5. Any SOLIDWORKS crash or exception dialog — copy the whole stack trace, the
   add-in deliberately shows the real exception rather than a friendly message.

---

## Honest expectations

The code compiles and all 38 unit tests pass, but **nothing has ever run against
a live SOLIDWORKS**. Realistically:

- Step 2 (tests) — should just work.
- Step 3 (build) — should work; admin is the likely snag.
- Step 4 (load) — should work.
- Step 5b (dump) — good odds. Read-only, simple API calls.
- Step 5c (mirror) — **this is the one that may well not work first time.** It is
  the deepest stack: selection, plane matching, sketch writing, feature creation.

If 5c fails, that is expected at this stage and not a wasted trip. Steps 5a and 5b
are the ones that produce information I can act on, and they are the cheap ones.

Do not spend the whole session fighting 5c. Get 5a and 5b done first.
