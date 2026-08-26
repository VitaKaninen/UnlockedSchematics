# Unlocked Schematics

Lets research schematics generate for projects vanilla hides behind a second unlock step
(Royalty techprints, Biotech mechtech, Odyssey gravtech) without weakening the gate itself.

Findings, design and build order: [PLAN.md](PLAN.md). Original brief:
[UnlockedSchematics-brief.md](UnlockedSchematics-brief.md).
Folder-wide rules (commit/push, `.rimignore`, `loadFolders` shadowing) live in the parent
folder's CLAUDE.md.

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

- **`Class` goes on the field's own node, not a nested `<Operation>`.** For a
  `PatchOperation`-typed field such as `PatchOperationConditional.match` / `.nomatch`, write
  `<match Class="PatchOperationReplace">`. `DirectXmlToObject.ClassTypeOf` reads the `Class`
  attribute off the node it is deserializing into the field, so wrapping an
  `<Operation Class="...">` inside `<match>` leaves that node class-less; RimWorld
  instantiates the abstract base and every apply logs *"Attempted to use PatchOperation
  directly; patch will always fail"*. `<li Class="...">` inside a `List<PatchOperation>`
  (e.g. `PatchOperationSequence.operations`) is the different, correct case — don't
  generalise from it. Cost a test cycle 2026-08-26; `Dev/validate_patches.py` now catches it.
- **A patch that parses and whose XPath matches can still be wrong.** Simulating the XPath
  against the shipped def proves only that the *target* is right, never that the XML binds
  to the C# fields. Those are separate failure modes and need separate checks.
- **A schematic test that samples generated books cannot distinguish "patch failed" from
  "mod not enabled".** Both empty the candidate pool, and `OnBookGenerated`'s fallback
  branch then scans every project while skipping the `IsFinished` *and*
  `PrerequisitesCompleted` filters — which also happens naturally on a fully-researched
  colony. `Dev/TestHarness` writes the answer onto the item label instead; prefer that.
- **The test harness must load *after* this mod.** Patches apply in mod load order, so
  above it the harness reads the blocklist before removal and always reports FAIL.
- **`ResearchProjectDef.IsFinished` is `ProgressReal >= Cost`, and nothing else.**
  Recipes and buildings test it directly, so a project is effectively unlocked the moment
  progress reaches `Cost` — whether or not `FinishProject` ran. Any "bank progress but keep
  it locked" behaviour has to hold progress *strictly below* `Cost`; suppressing the
  completion call is not enough.
