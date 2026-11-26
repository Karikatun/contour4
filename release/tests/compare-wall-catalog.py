import argparse
import csv
import json
import math
import re
from collections import Counter, defaultdict
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFont


FIELDS = "stage id path slot visible role shader material main flags kind queue mesh submeshes center size expected supported maps appearance frame_error local_center up_a up_b from_a from_b v0 v1 v4 v5 floors wear_min wear_max profile chip_a chip_b chip_c frame inverse".split()
WALL = {"wall", "closed-plug"}
SPHERE_PREFIX = "spherical-chamber"
SWEPT = re.compile(r"(?:^(?:[a-z-]+-)?(?:bridge|helix|turn)-\d+-(?:left|right)$)|(?:^wall-(?:left|right)-\d+$)")
PROBE_VIEWS = {
    "freight-dispatch": "Диспетчерская",
    "junction-corridor": "Коридор с ответвлением",
    "sample-storage": "Хранилище образцов",
    "extraction-chamber": "Помещение сдачи",
    "mobius-hall-rotated": "Зал Мёбиуса, повёрнутый экземпляр",
    "right-angle-chambers-rotated": "Составной блок, повёрнутый экземпляр",
    "ring-Loot1": "Кольцо, повёрнутая комната Loot1",
    "pressure-dock": "Стартовый шлюз",
    "sample-receiving": "Приёмная",
    "instrument-service": "Мастерская",
    "spherical-chamber": "Сферическая камера",
}


def records(folder):
    data = {}
    with (folder / "renderer-slots.tsv").open(encoding="utf-8-sig") as file:
        for row in csv.DictReader(file, fieldnames=FIELDS, delimiter="\t"):
            key = tuple(row[field] for field in ("stage", "id", "path", "slot"))
            if key in data or None in row or None in row.values():
                raise ValueError(f"Invalid or duplicate renderer slot {key}")
            data[key] = row
    if not data:
        raise ValueError(f"No observations in {folder}")
    return data


def visible(row):
    return row["visible"] == "True"


def painted(row):
    return (row["kind"], row["shader"], row["supported"]) == (
        "8", "Contour4/Room surface wear", "True"
    ) and "_WearMap=painted_plaster_wall_diff_1k" in row["maps"].split(";")


def junction_batch(row):
    return row["id"] == "junction-corridor" and "/Institute surface details[" in row["path"]


def point(value):
    return tuple(map(float, value.split(",")))


def dot(a, b):
    return sum(x * y for x, y in zip(a, b))


def image_difference(first, second):
    with Image.open(first) as before, Image.open(second) as after:
        diff = ImageChops.difference(before.convert("RGB"), after.convert("RGB"))
        peak = diff.getextrema()
        histogram = diff.histogram()
        return {"samePixels": diff.getbbox() is None, "peakChannelDelta": max(maximum for _, maximum in peak),
                "meanAbsoluteChannelDelta": round(sum(index % 256 * count for index, count in enumerate(histogram)) / (3 * diff.width * diff.height), 6)}


def gallery(before, after, out, items):
    width, height, gap = 460, 306, 16
    margin, top, label_h = 16, 49, 27
    font_dir = Path("C:/Windows/Fonts")
    font = ImageFont.truetype(str(font_dir / "segoeui.ttf"), 18)
    bold = ImageFont.truetype(str(font_dir / "segoeuib.ttf"), 20)
    sheet = Image.new("RGB", (2 * width + gap + 2 * margin, top + (label_h + height + gap) * len(items)), "#11191e")
    canvas = ImageDraw.Draw(sheet)
    canvas.text((margin, 11), "0.2.51", font=bold, fill="#eef3f5")
    canvas.text((margin + width + gap, 11), "0.2.52", font=bold, fill="#eef3f5")
    for index, (name, label) in enumerate(items):
        y = top + index * (label_h + height + gap)
        canvas.text((margin, y - 3), label, font=font, fill="#dce5e9")
        for column, folder in enumerate((before, after)):
            with Image.open(folder / (name + ".png")) as screenshot:
                panel = screenshot.convert("RGB").resize((width, height), Image.Resampling.LANCZOS)
            sheet.paste(panel, (margin + column * (width + gap), y + label_h))
    sheet.save(out)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("baseline", type=Path)
    parser.add_argument("candidate", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    args.output.mkdir(exist_ok=True, parents=True)
    baseline = records(args.baseline)
    candidate = records(args.candidate)
    baseline_catalog = (args.baseline / "catalog.txt").read_text(encoding="utf-8-sig").splitlines()
    candidate_catalog = (args.candidate / "catalog.txt").read_text(encoding="utf-8-sig").splitlines()
    spheres = [module for module in baseline_catalog if module.startswith(SPHERE_PREFIX)]
    issues = []
    if baseline_catalog != candidate_catalog or len(set(baseline_catalog)) != 30 or len(spheres) != 8:
        issues.append(["catalog-identity", baseline_catalog, candidate_catalog])
    baseline_required = {key for key, row in baseline.items() if visible(row) and row["expected"] in WALL}
    baseline_painted = sum(painted(baseline[key]) for key in baseline_required)
    if not 0 < baseline_painted < len(baseline_required):
        issues.append(["baseline-failed-to-reproduce-coverage-gap", baseline_painted, len(baseline_required)])
    checked_sweep = checked_point = 0
    counts = defaultdict(Counter)
    for key, row in candidate.items():
        stage, room, path, slot = key
        wall_name = re.sub(r"\[\d+\]$", "", path.rsplit("/", 1)[-1])
        pair = counts[(stage, room)]
        pair["slots"] += 1
        if row["expected"] == "legacy-overlay" and visible(row):
            issues.append(["visible-legacy-overlay", key])
        if row["expected"] == "sphere":
            pair["sphereSlots"] += 1
            if baseline.get(key) != row:
                issues.append(["sphere-slot-changed", key])
        if not visible(row) or row["expected"] not in WALL:
            if visible(row) and painted(row):
                issues.append(["unexpected-paint", key])
            if row["expected"] not in {"legacy-overlay", "sphere"} and not junction_batch(row) and baseline.get(key) != row:
                issues.append(["non-wall-slot-changed", key])
            continue

        pair["wallSlots"] += 1
        old = baseline.get(key)
        if old and painted(old):
            pair["baselinePainted"] += 1
        if not painted(row):
            issues.append(["missing-wall-finish", key])
            continue
        pair["painted"] += 1
        if old is None or any(row[field] != old[field] for field in ("visible", "mesh", "submeshes", "size", "center")):
            issues.append(["wall-geometry-changed", key])
        # The ring aggregate includes room-local finishes. Its separate room snapshots verify those frames.
        ring_inner = stage == "assembled-ring" and not ("Shaft closure" in wall_name or "Bridge side" in wall_name)
        if not ring_inner and stage != "ring-entrance" and float(row["frame_error"]) > .001:
            issues.append(["wear-frame-detached", key, row["frame_error"]])
        up_a, up_b = point(row["up_a"]), point(row["up_b"])
        if old and painted(old) and row["up_a"] == "0,0,0,0":
            # The two .50 rooms retain the original profile and manually placed wear coordinates.
            if any(row[field] != old[field] for field in ("chip_a", "chip_b", "chip_c", "frame", "profile")):
                issues.append(["reference-paint-coordinates-changed", key])
            continue
        if any(abs(math.sqrt(dot(up[:3], up[:3])) - 1) > .001 for up in (up_a, up_b)):
            issues.append(["invalid-wall-up", key, row["up_a"], row["up_b"]])
            continue
        if SWEPT.match(wall_name):
            checked_sweep += 1
            a, b = point(row["from_a"]), point(row["from_b"])
            delta = dot(up_a[:3], up_b[:3])
            if not (.7 < delta <= 1.0001 and abs(dot(a[:3], up_a[:3])) < .003 and abs(dot(b[:3], up_b[:3])) < .003):
                issues.append(["invalid-swept-wall-frame", key, row["up_a"], row["up_b"], row["from_a"], row["from_b"]])
            if wall_name.startswith("wall-"):
                continue  # Corner Slice source quads have a different vertex ordering.
            v0, v1, v4, v5 = (point(row[field]) for field in ("v0", "v1", "v4", "v5"))
            # The swept side begins at the underside of a 25 cm deck. Paint height starts on top.
            if not (dot(up_a[:3], [y - x for x, y in zip(v0, v1)]) > .95 * math.dist(v0, v1) and
                    dot(up_b[:3], [y - x for x, y in zip(v4, v5)]) > .95 * math.dist(v4, v5) and
                    abs(dot(up_a, (*v0, 1)) + .25) < .015 and abs(dot(up_b, (*v4, 1)) + .25) < .015):
                issues.append(["swept-wall-frame-off-deck", key])
        if stage in ("starter", "assembled-ring-room") or stage == "ring-entrance":
            checked_point += 1
            if abs(up_a[1] - 1) > .001 or abs(up_a[3]) > .03:
                issues.append(["authored-up-not-floor-relative", key, row["up_a"]])
        if room in ("sample-storage", "sample-storage-narrow", "extraction-chamber", "junction-corridor"):
            checked_point += 1
            if abs(up_a[1] - 1) > .001 or abs(up_a[3]) > .03:
                issues.append(["flat-wall-up-not-floor-relative", key, row["up_a"]])
        if room in {"floor-to-floor-chambers", "top-to-top-chambers", "niche-chamber", "gallery-chamber",
                    "u-shaft", "mobius-hall", "right-angle-chambers"} and row["role"] == "Shell" and not SWEPT.match(wall_name):
            checked_point += 1
            floor_planes = [floor.split(":") for floor in row["floors"].split(";")]
            expected_prefix = None
            if room == "right-angle-chambers":
                expected_prefix = "airlock" if wall_name.startswith("airlock-") else "distribution-hall"
            has_floor = any((expected_prefix is None or floor == expected_prefix) and
                            dot(up_a[:3], point(floor_up)) > .999 and abs(dot(up_a, (*point(origin), 1))) < .02
                            for floor, origin, floor_up in floor_planes)
            low, high = point(row["v0"]), point(row["v1"])
            points_up = dot(up_a[:3], [b - a for a, b in zip(low, high)]) > .95 * math.dist(low, high)
            # The Corner Slice extrudes each cross-section point into two vertices along Z.
            if not has_floor or (expected_prefix is None and not points_up):
                issues.append(["compound-wall-does-not-follow-local-floor", key, row["up_a"]])

    for key in baseline.keys() - candidate.keys():
        if not junction_batch(baseline[key]):
            issues.append(["renderer-slot-removed", key])

    # The wall treatment exposes plaster by removing only the legacy olive backdrop in the junction batch.
    for group in {(row["stage"], row["path"]) for row in baseline.values() if junction_batch(row)}:
        old = Counter((row["shader"], row["material"], row["maps"], row["appearance"])
                      for row in baseline.values() if (row["stage"], row["path"]) == group
                      and row["material"] != "Contour-4 olive wall paint (Contour-4 wear)")
        new = Counter((row["shader"], row["material"], row["maps"], row["appearance"])
                      for row in candidate.values() if (row["stage"], row["path"]) == group)
        if old != new:
            issues.append(["junction-batch-kept-materials-changed", group, list((old - new).items()), list((new - old).items())])

    for module in baseline_catalog:
        for stage in ("prepared", "forced-fallback"):
            count = counts[(stage, module)]
            if not count["sphereSlots" if module in spheres else "wallSlots"]:
                issues.append(["catalog-room-unobserved", stage, module])
    for key in baseline_required:
        row = candidate.get(key)
        if not row or not visible(row) or row["expected"] not in WALL:
            issues.append(["structural-surface-disappeared", key])

    geometry_before = (args.baseline / "physical.tsv").read_bytes()
    geometry_after = (args.candidate / "physical.tsv").read_bytes()
    if geometry_before != geometry_after:
        issues.append(["collider-or-light-changed"])

    pngs = {name: image_difference(args.baseline / (name + ".png"), args.candidate / (name + ".png"))
            for name in set(PROBE_VIEWS) | set(spheres)}
    for module in spheres:
        if not pngs[module]["samePixels"]:
            issues.append(["sphere-render-changed", module, pngs[module]])
    for module in ("junction-corridor", "freight-dispatch", "sample-storage", "extraction-chamber", "pressure-dock", "ring-Loot1"):
        if pngs[module]["samePixels"]:
            issues.append(["new-wall-not-visible-in-inspection-view", module])

    summary = {
        "pass": not issues,
        "catalog": {"total": len(baseline_catalog), "sphereVariants": len(spheres)},
        "baselinePainted": baseline_painted,
        "requiredWallSlots": len(baseline_required),
        "candidatePainted": sum(painted(candidate[key]) for key in baseline_required if key in candidate),
        "baselineSlots": len(baseline), "candidateSlots": len(candidate),
        "sameColliderAndLightObservations": geometry_before == geometry_after,
        "colliderAndLightObservations": len(geometry_before.splitlines()),
        "curvedAndFlatHeightChecks": {"curved": checked_sweep, "flat": checked_point},
        "identicalSphereSlots": sum(row["expected"] == "sphere" and baseline.get(key) == row for key, row in candidate.items()),
        "cameraComparisons": dict(sorted(pngs.items())),
        "issues": issues,
    }
    (args.output / "wall-audit.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding="utf-8")
    with (args.output / "wall-audit.tsv").open("w", encoding="utf-8", newline="") as file:
        writer = csv.writer(file, delimiter="\t")
        writer.writerow(("stage", "id", "slots", "wallSlots", "baselinePainted", "candidatePainted", "sphereSlots"))
        for (stage, room), count in sorted(counts.items()):
            writer.writerow((stage, room, *(count[key] for key in ("slots", "wallSlots", "baselinePainted", "painted", "sphereSlots"))))
    views = list(PROBE_VIEWS.items())
    gallery(args.baseline, args.candidate, args.output / "wall-gallery.png", views[:6])
    gallery(args.baseline, args.candidate, args.output / "wall-gallery-ring-start-sphere.png", views[6:])
    print(json.dumps({name: value for name, value in summary.items() if name not in ("issues", "cameraComparisons")}))
    print(f"{len(issues)} issues");
    for issue in issues[:10]:
        print(issue)
    return bool(issues)


if __name__ == "__main__":
    raise SystemExit(main())
