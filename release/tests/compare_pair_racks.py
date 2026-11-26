import argparse
import csv
import json
from collections import Counter, defaultdict
from itertools import batched
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


def table(folder, name):
    with (folder / name).open(encoding="utf-8-sig", newline="") as stream:
        return list(csv.DictReader(stream, delimiter="\t"))


def rack_key(row):
    return row["module"], row["family"], row["variant"]


def matched_racks(base, cand):
    before = {rack_key(r): r for r in table(base, "rack-measurements.tsv")}
    after = {rack_key(r): r for r in table(cand, "rack-measurements.tsv")}
    if before.keys() != after.keys() or len(after) != 49:
        raise ValueError("Expected the same 49 placements in both results.")
    return [(before[key], new) for key, new in after.items()]


def regression_summary(base, cand):
    before = table(base, "room-regressions.tsv")
    after = table(cand, "room-regressions.tsv")
    routes = lambda rows: [r for r in rows if r["path"] != "loot"]
    old_routes, new_routes = routes(before), routes(after)
    key = lambda row: (row["module"], row["variant"], row["path"], row["sample_or_site"])
    old_keys = {key(row): row for row in old_routes}
    changed = [row for row in new_routes if old_keys.get(key(row)) != row]
    loot_counts = lambda rows: Counter((r["module"], r["variant"], r["pass"]) for r in rows if r["path"] == "loot")
    old_loot, new_loot = loot_counts(before), loot_counts(after)
    return {
        "route_samples": len(new_routes),
        "route_samples_baseline": len(old_routes),
        "route_changes_detected": old_routes != new_routes,
        "route_changes": changed,
        "loot_candidates_baseline_valid": sum(v for k, v in old_loot.items() if k[2] == "True"),
        "loot_candidates_candidate_valid": sum(v for k, v in new_loot.items() if k[2] == "True"),
        "loot_changed_cases": [list(k) + [old_loot[k], new_loot[k]] for k in old_loot.keys() | new_loot.keys() if old_loot[k] != new_loot[k]],
    }


def parsed_range(rows, field):
    values = [float(row[field]) for row in rows]
    if min(values) == max(values):
        return f"{min(values):.3f}"
    return f"{min(values):.3f}–{max(values):.3f}"


def write_table(base, cand, output):
    pairs = matched_racks(base, cand)
    groups = defaultdict(list)
    for before, after in pairs:
        groups[(after["module"], after["family"])].append((before, after))
    lines = [
        "# Замеры укрытий под стеллажами в Unity",
        "",
        "Габариты сняты на собранной геометрии в локальных координатах пола. Ширина и глубина измерены в свободном проёме на высоте 0,28 м, высота дана по нижней плоскости перекрытия. Для всех проходов использована пара капсул со смещением между центрами 0,52 м. Каждая траектория повторена со смещением ±0,016 м, пока напарник остаётся на месте на противоположной стороне.",
        "",
        "| Комната | Стеллаж | Пол | Ширина × глубина × высота, м | Варианты .52 | Варианты кандидата |",
        "| --- | --- | --- | --- | ---: | ---: |",
    ]
    passed = lambda rows: sum(r["stock"] == r["comfort"] == "True" for r in rows)
    for (room, family), variants in groups.items():
        originals, modified = zip(*variants)
        qty = len(modified)
        dimensions = " × ".join(parsed_range(modified, field) for field in ("clear_lane_span_m", "clear_lane_depth_m", "roof_clear_m"))
        lines.append(f"| `{room}` | `{family}` | `{modified[0]['floor']}` | {dimensions} | {passed(originals)}/{qty} | {passed(modified)}/{qty} |")
    total = len(pairs)
    originals, modified = zip(*pairs)
    all_dims = " × ".join(parsed_range(modified, field) for field in ("clear_lane_span_m", "clear_lane_depth_m", "roof_clear_m"))
    lines.extend(["", f"Два профиля вместе прошли в {passed(originals)}/{total} размещениях базы и в {passed(modified)}/{total} размещениях кандидата. Диапазон проверенных проёмов кандидата — {all_dims} м.", ""])
    (output / "rack-dimensions.md").write_text("\n".join(lines), encoding="utf-8")
    (output / "room-regression-comparison.json").write_text(json.dumps(regression_summary(base, cand), indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def draw_caption(draw, x, y, string, font, foreground):
    draw.text((x, y), string, font=font, fill=foreground)


def gallery(base, cand, output):
    fonts = Path("C:/Windows/Fonts")
    title = ImageFont.truetype(str(fonts / "segoeuib.ttf"), 33)
    label = ImageFont.truetype(str(fonts / "segoeuib.ttf"), 24)
    body = ImageFont.truetype(str(fonts / "segoeui.ttf"), 20)
    bold = ImageFont.truetype(str(fonts / "segoeuib.ttf"), 20)
    originals = {row["frame"]: row for row in table(base, "rack-frames.tsv")}
    candidates = table(cand, "rack-frames.tsv")
    if {row["frame"] for row in candidates} != originals.keys():
        raise ValueError("Baseline and candidate need the same labeled frames.")
    width, frame_width, frame_height = 1420, 650, 455
    colors = {"back": "#eaeef2", "panel": "#fafcfd", "ink": "#172433", "note": "#415368"}
    for page, rows in enumerate(batched(candidates, 5), 1):
        image = Image.new("RGB", (width, 154 + len(rows) * 530), colors["back"])
        draw = ImageDraw.Draw(image)
        draw_caption(draw, 36, 26, f"Стеллажи Контура-4  /  Сравнение в Unity  /  {page}", title, colors["ink"])
        draw_caption(draw, 38, 79, "A и B — капсулы Ø0,48 × 0,50 м на измеренных местах. Сцена собрана из проверяемого пакета.", body, colors["note"])
        draw_caption(draw, 48, 116, "База 0.2.52", label, colors["ink"])
        draw_caption(draw, 724, 116, "Кандидат 0.2.53", label, colors["ink"])
        for row_no, after in enumerate(rows):
            filename = after["frame"]
            y = 154 + row_no * 530
            draw.rounded_rectangle((32, y, width - 32, y + 514), radius=12, fill=colors["panel"])
            draw_caption(draw, 48, y + 12, after["label"].replace("/", "  /  "), body, colors["ink"])
            for x, root, row in [(48, base, originals[filename]), (724, cand, after)]:
                with Image.open(root / filename) as shot:
                    image.paste(shot.resize((frame_width, frame_height), Image.Resampling.LANCZOS), (x, y + 42))
                if row["body_visible"] == "True":
                    for player, color in (("a", "#18465d"), ("b", "#634010")):
                        cx = x + int(row[f"{player}_x"]) * frame_width / 1000
                        cy = y + 42 + int(row[f"{player}_y"]) * frame_height / 700
                        draw.ellipse((cx - 16, cy - 16, cx + 16, cy + 16), fill=color)
                        draw.text((cx, cy), player.upper(), anchor="mm", fill="white", font=bold)
                else:
                    draw.rounded_rectangle((x + 12, y + 440, x + 260, y + 479), radius=7, fill="#273b50")
                    draw_caption(draw, x + 24, y + 446, "Габарит пары не прошёл", body, "white")
        image.save(output / f"rack-gallery-{page}.jpg", quality=91, optimize=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("baseline", type=Path)
    parser.add_argument("candidate", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    write_table(args.baseline, args.candidate, args.output)
    gallery(args.baseline, args.candidate, args.output)


if __name__ == "__main__":
    main()
