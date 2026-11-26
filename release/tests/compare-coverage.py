"""Compare exact Unity renderer slots and classify the visible retained finishes."""
import csv
import json
from collections import Counter, defaultdict
from pathlib import Path
import re
import sys


FIELDS = ("stage", "room", "path", "slot", "shown", "role", "shader", "material",
          "diffuse", "flags", "finish", "queue", "mesh", "submeshes", "center", "size")
FAMILIES = ("", "concrete", "plaster", "painted-metal", "wood", "plywood-edge", "polymer", "rolled-metal")


def rows(folder):
    with (folder / "renderer-slots.tsv").open(encoding="utf-8-sig", newline="") as stream:
        return list(csv.DictReader(stream, fieldnames=FIELDS, delimiter="\t"))


def solid_visible(row):
    return row["shown"] == "True" and not row["stage"].startswith("packed-")


def name(row):
    return re.sub(r"\[\d+\]$", "", row["path"].rsplit("/", 1)[-1]).lower()


def paint_disposition(row):
    if row["stage"].startswith("packed-"):
        return "serialized asset before its placement finish pass"
    if row["shown"] != "True":
        return "inactive alternate or hidden renderer"
    if int(row["finish"]):
        return FAMILIES[int(row["finish"])]
    source, part = row["material"].lower(), name(row)
    if row["diffuse"] or any(value in row["flags"] for value in ("_NORMALMAP", "_DETAIL_MULX2", "_METALLICGLOSSMAP")):
        return "existing authored texture"
    if "_EMISSION" in row["flags"] or any(value in source for value in ("lens", "diffuser")):
        return "optical or emissive fixture"
    if any(value in source for value in ("glass", "glazing", "frost")) or "_ALPHA" in row["flags"]:
        return "glazing or transparent insert"
    if row["shader"] != "Standard":
        return "separate authored shader"
    if "exposed technical components" in row["path"].lower() or source == "contour-4 interior circuit":
        return "authored visible electronics"
    if source in ("contour-4 dusty silk",):
        return "environmental story detail"
    if source in ("contour-4 interior paper", "site numbering", "contour-4 terminal legend",
                  "contour-4 enamel backing", "contour-4 dull edge metal", "contour-4 institute enamel",
                  "contour-4 warning ochre", "contour-4 ivory stencil") and (
                      source != "contour-4 institute enamel" or "institute surface details" in part):
        return "graphic, plate trim, paper or control legend"
    if any(value in part for value in ("status", "signal", "marking", "indicator", "stencil",
                                     "numeral", "screen", "guide", "label", "decal", "legend",
                                     "readout", "display", "nameplate", "scale graduation",
                                     "instrument face", "control fascia", "marker", "lens", "lamp",
                                     "calibration stand dial", "meter", "badge", "glass")):
        return "legible display, symbol or optical part"
    if source in ("contour-4 interior red", "contour-4 interior amber", "v2_ivory", "institute-ivory"):
        return "flat accent or indicator ceramic"
    return "remaining opaque neutral"


def main(baseline, candidate, out):
    before, after = rows(baseline), rows(candidate)
    assert len(before) == len(after), (len(before), len(after))
    assert (baseline / "physical.tsv").read_bytes() == (candidate / "physical.tsv").read_bytes()
    assert (baseline / "scenes.tsv").read_bytes() == (candidate / "scenes.tsv").read_bytes()
    catalog = (candidate / "catalog.txt").read_text(encoding="utf-8-sig").splitlines()
    scenes = (candidate / "scenes.tsv").read_text(encoding="utf-8-sig").splitlines()
    assert len(set(catalog)) == 30, catalog
    expected = {tuple(entry.split("\t")[:2]) for entry in scenes}
    assert all((stage, area) in expected for stage in ("prepared", "forced-fallback", "packed-prefab") for area in catalog)
    assert all((stage, area) in expected for stage, area in (
        ("receiving-with-research", "sample-receiving"), ("receiving-without-research", "sample-receiving"),
        ("packed-receiving-with-research", "sample-receiving"), ("packed-receiving-without-research", "sample-receiving"),
        ("dispatch-with-research", "freight-dispatch"), ("dispatch-without-research", "freight-dispatch"),
        ("direct-freight-fold", "freight-gravity-span"), ("direct-freight-fold", "freight-gravity-rise"),
        ("direct-freight-fold", "freight-gravity-drop"), ("starter", "pressure-dock"),
        ("starter", "service-garage"), ("assembled-ring", "ring"), ("ring-entrance", "ring-entrance")))

    by_scope = defaultdict(Counter)
    totals = Counter()
    changes, breaks, residue = [], [], []
    for previous, current in zip(before, after):
        assert all(previous[key] == current[key] for key in (
            "stage","room","path","slot","shown","role","mesh","submeshes","center","size")), (previous, current)
        category = paint_disposition(current)
        case = (current["stage"], current["room"])
        protected = ("_EMISSION" in previous["flags"] or "exposed technical components" in previous["path"].lower() or
                     previous["material"].lower() in ("contour-4 ivory stencil", "contour-4 warning ochre"))
        if solid_visible(current) and protected:
            assert all(previous[key] == current[key] for key in ("shader","material","diffuse","flags")), (previous, current)
        by_scope[case][category] += 1
        totals[category] += 1
        if solid_visible(current) and int(previous["finish"]) and not int(current["finish"]):
            breaks.append(current)
        if solid_visible(current) and category == "remaining opaque neutral":
            residue.append(current)
        if previous["finish"] != current["finish"]:
            changes.append(current)
        current["disposition"] = category
        current["previousFinish"] = FAMILIES[int(previous["finish"])]
        current["previousMaterial"] = previous["material"]
    with (out / "slot-dispositions.tsv").open("w",encoding="utf-8",newline="") as stream:
        writer = csv.DictWriter(stream,fieldnames=(*FIELDS,"disposition","previousFinish","previousMaterial"),delimiter="\t")
        writer.writeheader()
        writer.writerows(after)
    report = {
        "catalogue":catalog,"inspectedScenes":len(scenes),"slotObservations":len(before),"dispositions":totals,
        "changedFinishSlots":len(changes),"wearToUntexturedRegressions":breaks,"opaqueUnassigned":residue,
        "scopes":[{"route":section,"module":area,"dispositions":counts}
                  for (section,area),counts in sorted(by_scope.items())],
    }
    (out/"coverage-summary.json").write_text(json.dumps(report,indent=2,ensure_ascii=False),encoding="utf-8")
    print(json.dumps({k:report[k] for k in ("inspectedScenes","slotObservations","changedFinishSlots")},indent=2))
    print("wear-to-untextured regressions:",len(breaks))
    print("opaque unassigned:",len(residue))
    assert not breaks, [(row["stage"],row["path"],row["previousMaterial"]) for row in breaks[:12]]
    assert not residue, [(row["stage"],row["path"],row["material"]) for row in residue[:12]]
    target = [row for row in after if row["stage"]=="prepared" and row["room"]=="shaft-plant"
              and name(row)=="isolator and bypass pumps"]
    assert len(target)==1 and target[0]["finish"]=="3", target
    sphere_decks = [row for row in after if row["stage"]=="prepared" and row["room"].startswith("spherical-chamber")
                   and name(row)=="diameter-deck"]
    assert len(sphere_decks)==8 and all(row["finish"]=="1" for row in sphere_decks), sphere_decks


main(*(Path(path) for path in sys.argv[1:4]))
