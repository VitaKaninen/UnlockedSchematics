"""Predict which research projects a Schematic can be generated about.

Reimplements ReadingOutcomeDoerGainResearch.OnBookGenerated's main loop and IsValid
against the shipped def XML, so an in-game observation can be checked against a
prediction instead of a hunch. Answers "should I be seeing this book?".

    python Dev/simulate_pool.py "<path-to-RimWorld>/Data" [--finished-all-ungated]

Without a state flag it assumes a fresh colony (nothing researched). With
--finished-all-ungated it assumes every project that has no second unlock step is
finished - the common late-game test state.

Caveat that bit us: RimWorld defs inherit through ParentName, and the mechtech line
gets requiresMechanitor from an abstract MechtechBase parent. A scan that only reads
concrete defs reports no mechanitor gate anywhere. inherited() below resolves the chain.
"""

import argparse
import glob
import os
import sys
import xml.etree.ElementTree as ET


def load(data_dir):
    """Return (concrete defs by defName, abstract defs by Name)."""
    nodes, byname = {}, {}
    pattern = os.path.join(data_dir, "*", "Defs", "**", "*.xml")
    for path in glob.glob(pattern, recursive=True):
        try:
            root = ET.parse(path).getroot()
        except ET.ParseError:
            continue
        dlc = path[len(data_dir) + 1:].split(os.sep)[0]
        for node in root.iter("ResearchProjectDef"):
            defname, name = node.findtext("defName"), node.get("Name")
            if defname:
                nodes[defname] = (node, dlc)
            if name:
                byname[name] = node
    return nodes, byname


def inherited(node, tag, byname, depth=0):
    """Resolve a field through the ParentName chain, the way RimWorld does."""
    if depth > 10:
        return None
    found = node.find(tag)
    if found is not None:
        return found
    parent = node.get("ParentName")
    if parent and parent in byname:
        return inherited(byname[parent], tag, byname, depth + 1)
    return None


def text(node, tag, byname, default=None):
    found = inherited(node, tag, byname)
    return (found.text or "").strip() if found is not None and found.text else default


def prerequisites(node, byname):
    found = inherited(node, "prerequisites", byname)
    return [li.text for li in found] if found is not None else []


def base_cost(node, byname):
    try:
        return float(text(node, "baseCost", byname, "0"))
    except ValueError:
        return 0.0


def techprints(node, byname):
    raw = text(node, "techprintCount", byname, "0")
    return int(raw) if raw.isdigit() else 0


# The seven names hardcoded in the Schematic ThingDef's <exclude> list.
EXCLUDED = {
    "BasicMechtech", "StandardMechtech", "HighMechtech", "UltraMechtech",
    "BasicGravtech", "StandardGravtech", "AdvancedGravtech",
}


def is_valid(defname, node, byname, mod_on):
    """ReadingOutcomeDoerGainResearch.IsValid, conditions (a) through (e)."""
    if inherited(node, "generalRules", byname) is None:
        return False                                              # (a)
    if not mod_on and defname in EXCLUDED:
        return False                                              # (b) exclude list
    if base_cost(node, byname) == 0:
        return False                                              # (c) ignoreZeroBaseCost
    if text(node, "tab", byname) not in (None, "Main"):
        return False                                              # (d) tabs = Main
    if not mod_on and techprints(node, byname) > 0:
        return False                                              # (e) hardcoded
    return True


def main():
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, ValueError):
            pass

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("data_dir", help="RimWorld's Data folder")
    parser.add_argument("--finished-all-ungated", action="store_true",
                        help="assume every project with no second unlock step is researched")
    parser.add_argument("--unfinished", nargs="*", default=[], metavar="DEFNAME",
                        help="ungated projects the colony has NOT researched (e.g. "
                             "WastepackAtomizer). Only these can differ from the "
                             "--finished-all-ungated assumption, and the script cannot "
                             "infer them - read them off the in-game research tab.")
    args = parser.parse_args()

    if not os.path.isdir(args.data_dir):
        sys.exit(f"not a directory: {args.data_dir}")

    nodes, byname = load(args.data_dir)
    if not nodes:
        sys.exit(f"no ResearchProjectDefs found under {args.data_dir}")

    gated = {d for d, (n, _) in nodes.items()
             if techprints(n, byname) > 0 or d in EXCLUDED}

    unknown = [d for d in args.unfinished if d not in nodes]
    if unknown:
        sys.exit(f"unknown defName(s): {', '.join(unknown)}")

    if args.finished_all_ungated:
        # Everything ungated is done apart from whatever the caller named. The script
        # cannot infer that set: a project can sit unresearched because the colony lacks
        # a required facility (WastepackAtomizer needs a MultiAnalyzer) or simply because
        # nobody bothered, and neither is visible in the defs.
        unfinished = set(gated) | set(args.unfinished)
        state = "all ungated research finished"
        if args.unfinished:
            state += f", except {', '.join(sorted(args.unfinished))}"
    else:
        unfinished = set(nodes)
        state = "fresh colony, nothing researched"

    def finished(defname):
        return defname not in unfinished and base_cost(nodes[defname][0], byname) > 0

    print(f"assumed state: {state}\n")

    for mod_on in (False, True):
        pool = sorted(
            f"{d} [{k}]" for d, (n, k) in nodes.items()
            if d in unfinished
            and all(p in nodes and finished(p) for p in prerequisites(n, byname))
            and is_valid(d, n, byname, mod_on)
        )
        print(f"=== mod {'ON ' if mod_on else 'OFF'} -> {len(pool)} eligible subject(s) ===")
        for entry in pool:
            print("   ", entry)
        print()

    print("=== gated, but blocked by an unfinished prerequisite ===")
    for defname in sorted(gated):
        node = nodes[defname][0]
        waiting = [p for p in prerequisites(node, byname) if p in unfinished]
        if waiting:
            print(f"    {defname}  <- waiting on {waiting}")


if __name__ == "__main__":
    main()
