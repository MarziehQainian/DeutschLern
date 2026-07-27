from __future__ import annotations

import argparse
import json
import re
from collections import defaultdict
from pathlib import Path

import pdfplumber


def extract_entries(source: Path) -> list[dict[str, object]]:
    entries: list[dict[str, object]] = []

    with pdfplumber.open(source) as pdf:
        for page_index in range(8, 21):
            rows: dict[int, list[dict[str, object]]] = defaultdict(list)
            for word in pdf.pages[page_index].extract_words(x_tolerance=2, y_tolerance=3):
                rows[round(float(word["top"]))].append(word)

            for top, words in sorted(rows.items()):
                if top < 100 or top > 790:
                    continue

                left = " ".join(
                    str(word["text"])
                    for word in words
                    if 130 <= float(word["x0"]) < 230
                ).strip()
                right = " ".join(
                    str(word["text"])
                    for word in words
                    if float(word["x0"]) >= 230
                ).strip()

                if left:
                    if re.fullmatch(r"[A-ZÄÖÜ]", left) and not right:
                        continue
                    entries.append(
                        {
                            "id": len(entries) + 1,
                            "page": page_index + 1,
                            "raw": left,
                            "example": right,
                        }
                    )
                elif right and entries:
                    entries[-1]["example"] = (
                        f'{entries[-1]["example"]} {right}'.strip()
                    )

    return entries


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("source", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()

    entries = extract_entries(args.source)
    if len(entries) != 532:
        raise ValueError(f"Expected 532 entries, extracted {len(entries)}.")

    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(
        json.dumps(entries, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )


if __name__ == "__main__":
    main()
