# Unlocked Schematics

Two features for colonies that do not research.

1. Lets research schematics generate for projects vanilla hides behind a second unlock step
   (Royalty techprints, Biotech mechtech, Odyssey gravtech) without weakening the gate itself.
2. Applies techprints and analyses mech chips where the item lies, so no research bench is
   needed for either. Unconditional, because the bench contributes nothing to either job —
   PLAN.md §9.

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

## Why the code is shaped this way
Code comments are one-liners by the user's rule (parent `CLAUDE.md`), so the reasons live here.
- `Patch_SchematicEligibility`: vanilla `IsValid` rejects any project with a techprint
  requirement and ignores the other gates; the Anomaly book inherits `IsValid`, so it is covered.
  `Unlocked()` must run **before** `techprintCount` is zeroed — `TechprintRequirementMet` reads
  it and reports "met" at 0. Restore in a **Finalizer**, not a Postfix, so a throw cannot leave
  the def at 0; it takes no `__exception`, so exceptions still propagate.
- `Patch_TechprintInPlace`: replaces vanilla's option outright (the bench adds nothing).
  Checks `ModLister.RoyaltyInstalled`, not `ModLister.CheckRoyalty`, which logs when Royalty is
  missing. `HaulAIUtility.PawnCanAutomaticallyHaul` is called only for its side effect — it
  sets `JobFailReason` for forbidden/out-of-area techprints. Labels and fail reasons are
  vanilla's keys, so no new translations.
- `JobDriver_ApplyTechprintInPlace`: vanilla's driver casts targetA to
  `Building_ResearchBench`, so it cannot be reused. 600 ticks = vanilla. No `activeSkill`: the
  XP comes from `ResearchManager.ApplyTechprint`, and vanilla sets none either.
- `UnlockedSchematics_AnalyzeInPlace.xml`: `[not(canStudyInPlace)]` avoids a duplicate node
  when another mod sets it; `success Always` because a game without Biotech has no such comps.
- `UnlockedSchematics_SchematicExcludes.xml`: the exclude list is plain def data — `IsValid`
  never reads `requiresMechanitor`/`requireGravEngineInspected`. The real gates stay in
  `CanStartNow`; `Patch_SchematicEligibility` keeps locked projects out of the pool.

## Before touching any patch XML

Run the validator. It checks the things an XML parser and an XPath test both miss:

```bash
python Dev/validate_patches.py <path-to-decompiled-source>
```

The decompile is deliberately not in this repo — Ludeon's code must not be committed to a
public GitHub repo. Regenerate it into a scratch dir with:

```bash
ilspycmd -p -o <outdir> "C:/Program Files (x86)/Steam/steamapps/common/RimWorld/Assembly-CSharp.dll"
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
- **The research bench contributes nothing to applying a techprint or analysing a chip.**
  `JobDriver_ApplyTechprint` is a flat `Toils_General.Wait(600)` with no stat read at all,
  and `JobDriver_AnalyzeItem`'s duration is `analysisDurationHours * 2500 / pawn
  ResearchSpeed` with no bench term. `ResearchSpeedFactor` — the stat benches carry — is
  read in only two places, and in `JobDriver_StudyItem` it is read off the *item being
  studied*, not a bench (and `JobDriver_AnalyzeItem` overrides that method anyway). This is
  why §9 is unconditional: preserving a bench path preserves nothing. **Check the duration
  formula before assuming a workbench matters to a job.**
- **In-place analysis is a vanilla feature you switch on, not one you build.**
  `CompProperties_Analyzable.canStudyInPlace` exists, Anomaly ships three things that set
  it, and the Biotech chips just never opt in — so Change 5 is one `PatchOperationAdd` and
  no C# at all. Look for a flag like this before writing toils.
- **A private method needs no `TargetMethod()` override — just name it as a string in the
  attribute.** `[HarmonyPatch(typeof(T), "PrivateName")]` resolves through
  `AccessTools.DeclaredMethod`, whose binding flags include `NonPublic`. `nameof` not
  working is not a reason to reach for `TargetMethod()`. Its two real uses: a target
  `DeclaredMethod` cannot reach because it is declared on a *base* type, and things with no
  attribute form. (A property getter does have one: `MethodType.Getter`.) There is no
  static-field target convention in Harmony — only the attribute, `TargetMethod()`, and
  `TargetMethods()`.
- **A def field a job driver re-reads at job start cannot be patched with a
  prefix/finalizer flip.** `canStudyInPlace` is read by `CompAnalyzable.CanInteract` and
  `OrderForceTarget` when the player clicks, then *again* by
  `JobDriver_StudyItem.CanStudyInPlace` when the job actually starts — by which point any
  finalizer has restored it. Cost a whole design in §9 before the unconditional XML route
  replaced it. Applies to any temporary def mutation, not just this field.
- **Both bench lookups accept the *simple* research bench**, not just the hi-tech one: the
  request is `ThingRequestGroup.ResearchBench`, i.e. any `Building_ResearchBench`, of which
  Core ships two. They also disagree about power — `StudyUtility.TryFindResearchBench`
  (chips) requires `CompPowerTrader.PowerOn`, `CompTechprint.CompFloatMenuOptions`
  (techprints) does not. "You need a powered hi-tech bench for techprints" is wrong twice.
- **`JobDefOf.StudyItem` has no creator anywhere in the assembly.** It exists in
  `Jobs_Work.xml` as modder-facing surface only; `JobDriver_StudyItem`'s only live use is
  `AnalyzeItem`, created solely by `CompAnalyzable.OrderForceTarget`. Worth knowing before
  assuming a patch there has wide blast radius.
- **This mod deliberately does not fix that.** It gates *generation* only. A book that
  already exists stays readable and can still finish locked research — that is vanilla
  behaviour and it is wanted (PLAN.md §2). A clamp on `AddProgress` was written for the
  original design and removed 2026-08-26 once generation became the gate; don't reintroduce
  it because "the gate must hold" sounds right. Read §2 first.
