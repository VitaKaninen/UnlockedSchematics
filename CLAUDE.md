# Unlocked Schematics

Lets research schematics generate for projects vanilla hides behind a second unlock step
(Royalty techprints, Biotech mechtech, Odyssey gravtech) without weakening the gate itself.

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
  atomizer book to 100% makes the atomizer buildable without ever studying the
  NanostructuringChip.
- **This mod deliberately does not fix that.** It gates *generation* only. A book that
  already exists stays readable and can still finish locked research — that is vanilla
  behaviour and it is wanted (PLAN.md §2). A clamp on `AddProgress` was written for the
  original design and removed 2026-08-26 once generation became the gate; don't reintroduce
  it because "the gate must hold" sounds right. Read §2 first.
