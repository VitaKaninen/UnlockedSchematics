# Unlocked Schematics

Two features for colonies that do not research.

1. Lets research schematics generate for projects vanilla hides behind a second unlock step
   (Royalty techprints, Biotech mechtech, Odyssey gravtech) without weakening the gate itself.
2. Lets a colony with no usable research bench apply techprints and analyse mech chips where
   the item lies, instead of having to keep a bench for that one interaction. Falls back only
   when no bench is available — PLAN.md §9.

Findings, design and build order: [PLAN.md](PLAN.md). Original brief:
[UnlockedSchematics-brief.md](UnlockedSchematics-brief.md).
Folder-wide rules (commit/push, `.rimignore`, `loadFolders` shadowing, and the XML `Class`
attribute gotcha) live in the parent folder's CLAUDE.md.

## Building

```bash
cd Source/UnlockedSchematics && dotnet build -c Release
```

Output goes straight to `Assemblies/UnlockedSchematics.dll`, which **is** committed — the
repo is the mod. Package versions are pinned on purpose; see PLAN.md Phase 3 before
changing them.

## Before touching any patch XML

Run the validator. It checks the things an XML parser and an XPath test both miss:

```bash
python Dev/validate_patches.py <path-to-decompiled-source>
```

The decompile is deliberately not in this repo — Ludeon's code must not be committed to a
public GitHub repo. Regenerate it into a scratch dir with:

```bash
ilspycmd -p -o <outdir> "C:/Program Files (x86)/Steam/steamapps/common/RimWorld/RimWorldWin64_Data/Managed/Assembly-CSharp.dll"
```

## Gotchas

- **A patch that parses and whose XPath matches can still be wrong.** Simulating the XPath
  against the shipped def proves only that the *target* is right, never that the XML binds
  to the C# fields. Those are separate failure modes and need separate checks.
- **A schematic test that samples generated books cannot distinguish "patch failed" from
  "mod not enabled".** Both empty the candidate pool, and `OnBookGenerated`'s fallback
  branch then scans every project while skipping the `IsFinished` *and*
  `PrerequisitesCompleted` filters — which also happens naturally on a fully-researched
  colony. Predict the pool with `Dev/simulate_pool.py` and compare, rather than eyeballing
  a sample.
- **Don't test with a mod that replaces the candidate pool.** A harness using
  `BookOutcomeProperties_GainResearch.include` replaces the pool wholesale, and the include
  branch of `OnBookGenerated` skips the `PrerequisitesCompleted` check the normal branch
  applies — so it yields subjects whose prerequisites are unresearched, which reads exactly
  like a mod bug. Cost a full round of confusion 2026-08-26; see PLAN.md §6a.
- **`ResearchProjectDef.IsFinished` is `ProgressReal >= Cost`, and nothing else.**
  `BuildableDef.IsResearchFinished` — the gate behind the build menu — just loops
  `researchPrerequisites[i].IsFinished`, so a project is unlocked the moment progress
  reaches `Cost`, whether or not `FinishProject` ran. `AnalyzedThingsRequirementsMet`
  appears in exactly two places in the assembly (`CanStartNow` and the research tab UI) and
  in neither unlock path. **Consequence, true in vanilla with no mods:** reading a wastepack
  atomizer book to 100% completes research the bench refuses to start, with the
  NanostructuringChip never studied. Verified in play 2026-08-26.
- **But don't overstate that — finishing the research is not the binding constraint.** The
  atomizer's `costList` still demands a physical `NanostructuringChip`, and analysing a
  chip needs a mechanitor (`CompProperties_CompAnalyzableUnlockResearch.requiresMechanitor`).
  The book saves the chip you'd consume on analysis, not the one the building eats. Same
  for mechtech: unlocked with no mechanitor, it unlocks defs a mechanitor has to operate.
  Check the material and usability costs before calling a research bypass exploitable.
- **Pawns already prefer books that still have something to give — don't build that.**
  `BookUtility.TryGetRandomBookToRead` (the only selection path, used by both
  `JoyGiver_Read` and `LearningGiver_Reading`) picks from books where
  `ProvidesOutcome` holds and falls back to all books only when none do, and a
  schematic stops providing an outcome once every project in it is finished.
  Investigated and declined 2026-08-26 — including why textbook competition is not
  worth fixing. Read PLAN.md §7a before re-opening it.
- **In-place analysis is a vanilla feature you can switch on, not one you have to build.**
  `CompProperties_Analyzable.canStudyInPlace` exists and Anomaly ships three things that
  set it; the Biotech chips just never opt in. Check for a flag like this before writing
  toils. **But the flag is read at two different times** — `CompAnalyzable.CanInteract` and
  `OrderForceTarget` read it when the player clicks, `JobDriver_StudyItem.CanStudyInPlace`
  reads it again when the *job starts*. A prefix/finalizer that flips it for one call is
  long gone by then, so the driver has to be told separately (PLAN.md §9 reads the job's
  empty `targetB` instead). Same trap for any def field a job driver re-reads later.
- **The two bench lookups disagree about power, so don't unify them.**
  `StudyUtility.TryFindResearchBench` (chips) requires `CompPowerTrader.PowerOn`; the inline
  query in `CompTechprint.CompFloatMenuOptions` (techprints) does not, and neither does
  `JobDriver_ApplyTechprint`. An unpowered hi-tech bench therefore satisfies techprints and
  not chips. Also: **both accept the *simple* research bench** — the request is
  `ThingRequestGroup.ResearchBench`, i.e. any `Building_ResearchBench`, of which Core ships
  two. "You need a hi-tech bench for techprints" is wrong.
- **`JobDefOf.StudyItem` has no creator anywhere in the assembly.** It exists in
  `Jobs_Work.xml` as modder-facing surface only; `JobDriver_StudyItem`'s only live use is
  `AnalyzeItem`, created solely by `CompAnalyzable.OrderForceTarget`. Worth knowing before
  assuming a patch there has wide blast radius.
- **This mod deliberately does not fix that.** It gates *generation* only. A book that
  already exists stays readable and can still finish locked research — that is vanilla
  behaviour and it is wanted (PLAN.md §2). A clamp on `AddProgress` was written for the
  original design and removed 2026-08-26 once generation became the gate; don't reintroduce
  it because "the gate must hold" sounds right. Read §2 first.
