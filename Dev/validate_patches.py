"""Validate RimWorld PatchOperation XML against the decompiled game classes.

Catches the class of bug that a plain XPath simulation cannot see: XML that parses fine
and targets the right nodes, but does not bind to the C# fields RimWorld expects.

The specific trap it was written for: DirectXmlToObject.ClassTypeOf reads the Class
attribute off the node it is deserializing INTO A FIELD. So a PatchOperation-typed field
like PatchOperationConditional.match needs Class on <match> itself. Nesting an
<Operation Class="..."> inside <match> leaves the node class-less, RimWorld instantiates
the abstract base PatchOperation, and every apply logs "Attempted to use PatchOperation
directly; patch will always fail".

Usage:  python Dev/validate_patches.py [path-to-decompiled-source]

The decompile is not in this repo (Ludeon's code must not be committed). Regenerate with:
  ilspycmd -p -o <outdir> ".../RimWorldWin64_Data/Managed/Assembly-CSharp.dll"
"""
import glob
import os
import re
import sys
import xml.etree.ElementTree as ET

for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding="utf-8", errors="replace")
    except (AttributeError, ValueError):
        pass

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
# No sensible default: the decompile lives wherever it was last generated, and guessing a
# path only produces a confusing "not found" pointing somewhere it was never going to be.
DEFAULT_DECOMP = ""

# Fields RimWorld reads on every PatchOperation regardless of subclass.
BASE_FIELDS = {"success", "sourceFile"}

FIELD_RE = re.compile(
    r"^\s*(?:public|private|protected|internal)\s+"
    r"(?:static\s+|readonly\s+|virtual\s+|override\s+)*"
    r"([\w<>,.\[\]?]+)\s+(\w+)\s*(?:=[^;]*)?;", re.M)
CLASS_RE = re.compile(r"^\s*public\s+(?:abstract\s+|sealed\s+)?class\s+(\w+)\s*:\s*(\w+)", re.M)


def load_types(decomp):
    """Map class name -> (set of field names, base class name)."""
    types = {}
    for path in glob.glob(os.path.join(decomp, "**", "*.cs"), recursive=True):
        name = os.path.splitext(os.path.basename(path))[0]
        if not name.startswith("PatchOperation"):
            continue
        with open(path, "r", encoding="utf-8", errors="replace") as fh:
            src = fh.read()
        base = None
        m = CLASS_RE.search(src)
        if m and m.group(1) == name:
            base = m.group(2)
        fields = {f for _t, f in FIELD_RE.findall(src)}
        types[name] = (fields, base)
    return types


def fields_of(types, cls):
    """Field names on cls plus everything it inherits."""
    out, seen = set(BASE_FIELDS), set()
    while cls and cls in types and cls not in seen:
        seen.add(cls)
        fields, base = types[cls]
        out |= fields
        cls = base
    return out


def check_node(node, types, path, problems):
    cls = node.get("Class")
    if not cls:
        return
    if cls not in types:
        problems.append(f"{path}: unknown PatchOperation class '{cls}'")
        return
    if cls in ("PatchOperation",):
        problems.append(f"{path}: <{node.tag}> names the abstract base 'PatchOperation'")
        return
    allowed = fields_of(types, cls)
    for child in node:
        if child.tag not in allowed:
            problems.append(
                f"{path}: <{node.tag} Class=\"{cls}\"> has no field '{child.tag}' "
                f"(valid: {', '.join(sorted(allowed))})")


# Def.DisallowedLabelCharsRegex - these break grammar resolution and raise a config error
# unless the def sets ignoreIllegalLabelCharacterConfigError.
BAD_LABEL_CHARS = re.compile(r"[\[\]{}]")


def walk(node, types, path, problems):
    check_node(node, types, path, problems)
    if node.tag == "label" and node.text and BAD_LABEL_CHARS.search(node.text):
        bad = "".join(sorted(set(BAD_LABEL_CHARS.findall(node.text))))
        problems.append(
            f"{path}: <label> contains disallowed character(s) '{bad}'. RimWorld allows "
            f"anything except []{{}} - use parentheses, or set "
            f"ignoreIllegalLabelCharacterConfigError on the def.")
    for child in node:
        # A child mapped to a PatchOperation-typed field must carry Class itself.
        if child.tag in ("match", "nomatch") and child.get("Class") is None:
            problems.append(
                f"{path}/<{child.tag}>: missing Class attribute. It is a PatchOperation "
                f"field, so Class goes on <{child.tag}> itself, not on a nested <Operation>.")
        walk(child, types, path + "/" + child.tag, problems)


def main():
    decomp = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_DECOMP
    if not decomp:
        print("usage: python Dev/validate_patches.py <path-to-decompiled-source>")
        print("see the docstring for the ilspycmd command that generates it")
        return 2
    if not os.path.isdir(decomp):
        print(f"decompiled source not found at: {decomp}")
        return 2

    types = load_types(decomp)
    if not types:
        print(f"no PatchOperation*.cs found under {decomp}")
        return 2
    print(f"loaded {len(types)} PatchOperation types from {decomp}\n")

    files = sorted(glob.glob(os.path.join(REPO, "**", "*.xml"), recursive=True))
    files = [f for f in files if os.sep + "Patches" + os.sep in f]
    problems = []
    for f in files:
        rel = os.path.relpath(f, REPO)
        try:
            root = ET.parse(f).getroot()
        except ET.ParseError as e:
            problems.append(f"{rel}: XML parse error: {e}")
            continue
        before = len(problems)
        walk(root, types, rel, problems)
        print(f"  {'FAIL' if len(problems) > before else 'ok  '}  {rel}")

    if problems:
        print("\n" + "\n".join("  " + p for p in problems))
        print(f"\n{len(problems)} problem(s)")
        return 1
    print(f"\nall {len(files)} patch file(s) bind cleanly")
    return 0


if __name__ == "__main__":
    sys.exit(main())
