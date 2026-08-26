# Dev

Dev-only material. Excluded from the shipped mod by `.rimignore`; kept in git.

## validate_patches.py

Checks patch XML for the failure modes an XML parser and an XPath test both miss — a
`Class` attribute on the wrong node, an illegal label character. Takes the path to a
decompiled `Assembly-CSharp` (see the root `CLAUDE.md` for the `ilspycmd` command):

    python Dev/validate_patches.py <path-to-decompiled-source>

## simulate_pool.py

Predicts which projects a schematic can be generated about, by reimplementing
`ReadingOutcomeDoerGainResearch.OnBookGenerated`'s main loop and `IsValid` against the
shipped def XML. Use it to get an expected result *before* looking in game, so an
observation can be compared against a prediction rather than a hunch:

    python Dev/simulate_pool.py "<RimWorld>/Data" --finished-all-ungated \
        --unfinished WastepackAtomizer --techprints-applied CataphractArmor

Colony state it cannot infer has to be passed in — which ungated projects are unresearched,
and which unlock gates are satisfied (`--mechanitor`, `--grav-engine`, `--analyzed`).

## TestHarness — removed 2026-08-26

A throwaway mod that forced the candidate pool to the seven blocklisted projects. It did
its job (it proved Change 1's `<exclude>` removal applies) and was deleted once the mod was
verified in play.

Worth knowing if anyone is tempted to rebuild it: it became actively misleading. Its
`<include>` list *replaces* the whole pool, and the include branch of `OnBookGenerated`
skips the `PrerequisitesCompleted` check the normal branch applies — so leaving it enabled
produced mechtech subjects whose prerequisite tiers were unresearched, which read exactly
like a mod bug. `simulate_pool.py` answers the same questions without touching the game.
