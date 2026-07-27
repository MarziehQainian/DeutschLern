from __future__ import annotations

import argparse
import json
import re
from dataclasses import dataclass
from pathlib import Path

import pdfplumber

CUSTOM_EXAMPLES = {
    "die Ankunft, -¨e": "Die Ankunft des Zuges ist um 18 Uhr.",
    "die Bewerbung, -en": "Ich schicke meine Bewerbung per E-Mail.",
    "der Chef, -s /": "Mein Chef ist heute nicht im Büro.",
    "die Disko, -s": "Am Samstag gehen wir in die Disko.",
    "die Ehefrau, -en/": "Seine Ehefrau arbeitet als Ärztin.",
    "der Ehepartner, -": "Mein Ehepartner kommt später.",
    "die Kollegin, -nen": "Meine Kollegin hilft mir bei der Arbeit.",
    "die Kundin, -nen": "Die Kundin bezahlt an der Kasse.",
    "die Partnerin, -nen": "Seine Partnerin heißt Anna.",
    "die Rentnerin, -nen": "Die Rentnerin reist gern.",
    "die Studentin, -nen": "Die Studentin lernt in der Bibliothek.",
    "die Touristin, -nen": "Die Touristin besucht Berlin.",
    "die Vermieterin, -nen": "Meine Vermieterin wohnt im Erdgeschoss.",
}


@dataclass
class Line:
    top: float
    head: str
    example: str


def join_words(words: list[dict[str, object]]) -> str:
    return " ".join(str(word["text"]) for word in sorted(words, key=lambda x: float(x["x0"]))).strip()


def extract_column(
    words: list[dict[str, object]],
    head_start: float,
    example_start: float,
    column_end: float,
    page_number: int,
) -> list[dict[str, str]]:
    rows: dict[float, list[dict[str, object]]] = {}
    for word in words:
        x = float(word["x0"])
        top = round(float(word["top"]), 1)
        if head_start <= x < column_end and 105 < top < 780:
            rows.setdefault(top, []).append(word)

    lines: list[Line] = []
    for top, row_words in sorted(rows.items()):
        head = join_words([word for word in row_words if float(word["x0"]) < example_start])
        example = join_words([word for word in row_words if float(word["x0"]) >= example_start])
        if head or example:
            lines.append(Line(top, head, example))

    entries: list[dict[str, object]] = []
    current: dict[str, str] | None = None
    for line in lines:
        starts_entry = current is None or (
            bool(line.head)
            and bool(current["raw"])
            and not is_line_continuation(line.head, current["raw"])
        )
        if starts_entry:
            if current is not None:
                entries.append(current)
            current = {"raw": "", "example": "", "page": page_number}

        if line.head:
            current["raw"] = f'{current["raw"]} {line.head}'.strip()
        if line.example:
            current["example"] = f'{current["example"]} {line.example}'.strip()

    if current is not None:
        entries.append(current)
    return entries


def clean_text(value: str) -> str:
    cleaned = re.sub(r"\s+", " ", value).strip()
    return cleaned.replace(
        "Aufdiesem FahrplanstehtnurdieAnkunftder Züge.",
        "Auf diesem Fahrplan steht nur die Ankunft der Züge.",
    )


def is_line_continuation(head: str, current_raw: str) -> bool:
    if re.match(r"^(?:hat|ist|\(hat\b)", head, re.IGNORECASE):
        return True
    if current_raw.rstrip().endswith("-") and "," not in current_raw:
        return True

    first_part = current_raw.split(",", maxsplit=1)[0].strip().lower()
    first_word = re.split(r"[\s()/]+", first_part)[-1]
    starts_with_infinitive = (
        first_word.endswith(("en", "eln", "ern")) or first_word == "tun"
    )
    return (
        starts_with_infinitive
        and not re.search(r"\b(?:hat|ist)\b", current_raw, re.IGNORECASE)
        and not re.match(r"^(?:der|die|das|der/die)\b", head, re.IGNORECASE)
        and "," in head
    )


def is_grammar_continuation(raw: str) -> bool:
    if re.match(
        r"^(?:hat|ist|war|wäre|wird|kann|muss|soll|darf|möchte|\(hat\b|\((?:Sg|Sing|Pl)\.?\))",
        raw,
        flags=re.IGNORECASE,
    ):
        return True

    return False


def extract_entries(source: Path) -> list[dict[str, object]]:
    extracted: list[dict[str, object]] = []

    with pdfplumber.open(source) as pdf:
        for page_index in range(7, 31):
            words = pdf.pages[page_index].extract_words(x_tolerance=2, y_tolerance=3)
            columns = [
                extract_column(words, 30, 105, 294, page_index + 1),
                extract_column(words, 300, 370, 580, page_index + 1),
            ]

            for entry in columns[0] + columns[1]:
                raw = clean_text(entry["raw"])
                example = clean_text(entry["example"])
                if raw == "ALPHABETISCHER":
                    continue
                if not raw or re.fullmatch(r"[A-ZÄÖÜ]", raw):
                    if example and extracted:
                        extracted[-1]["example"] = clean_text(
                            f'{extracted[-1]["example"]} {example}'
                        )
                    continue

                if not example and raw not in CUSTOM_EXAMPLES and extracted:
                    extracted[-1]["raw"] = clean_text(
                        f'{extracted[-1]["raw"]} {raw}'
                    )
                    continue

                if is_grammar_continuation(raw) and extracted:
                    extracted[-1]["raw"] = clean_text(
                        f'{extracted[-1]["raw"]} {raw}'
                    )
                    extracted[-1]["example"] = clean_text(
                        f'{extracted[-1]["example"]} {example}'
                    )
                    continue

                extracted.append(
                    {"raw": raw, "example": example, "page": entry["page"]}
                )

    entries = [
        {
            "id": index,
            "page": entry["page"],
            "raw": entry["raw"],
            "example": entry["example"] or CUSTOM_EXAMPLES.get(str(entry["raw"]), ""),
        }
        for index, entry in enumerate(extracted, start=1)
    ]
    return entries


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("source", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()

    entries = extract_entries(args.source)
    if len(entries) != 1_192:
        raise ValueError(f"Expected 1,192 entries, extracted {len(entries)}.")
    if any(not entry["example"] for entry in entries):
        raise ValueError("Every extracted entry must have an example sentence.")

    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(
        json.dumps(entries, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    print(f"Extracted {len(entries)} entries.")


if __name__ == "__main__":
    main()
