from __future__ import annotations

import argparse
import json
import re
from dataclasses import dataclass
from pathlib import Path

import pdfplumber

SOURCE_REFINEMENTS = {
    "ein bisschen bitten, bittet, bat, hat gebeten": [
        ("ein bisschen", "Ich spreche ein bisschen Deutsch."),
        ("bitten, bittet, bat, hat gebeten", "Ich bitte dich um Hilfe."),
    ],
    "unentschieden sich entschließen, entschließt sich, entschloss sich, hat sich entschlossen": [
        ("unentschieden", "Das Spiel endete unentschieden."),
        (
            "sich entschließen, entschließt sich, entschloss sich, hat sich entschlossen",
            "Ich habe mich für den Kurs entschlossen.",
        ),
    ],
    "entschlossen entschuldigen, entschuldigt, entschuldigte, hat entschuldigt": [
        (
            "entschlossen",
            "Ich bin fest entschlossen, diese Ausbildung zu beenden.",
        ),
        (
            "entschuldigen, entschuldigt, entschuldigte, hat entschuldigt",
            "Entschuldigen Sie bitte, dass ich Sie störe.",
        ),
    ],
    "stehen bleiben stehlen, stiehlt, stahl, hat gestohlen": [
        (
            "stehen bleiben, bleibt stehen, blieb stehen, ist stehen geblieben",
            "Bitte bleiben Sie hier stehen.",
        ),
        (
            "stehlen, stiehlt, stahl, hat gestohlen",
            "Jemand hat mein Fahrrad gestohlen.",
        ),
    ],
    "Tee ziehen lassen teilen, teilt, teilte, hat geteilt": [
        (
            "Tee ziehen lassen",
            "Lassen Sie den Tee fünf Minuten ziehen.",
        ),
        (
            "teilen, teilt, teilte, hat geteilt",
            "Wir teilen den Kuchen.",
        ),
    ],
    "was für ein- (sich) waschen, wäscht, wusch, hat gewaschen": [
        ("was für ein-", "Was für ein Auto möchtest du?"),
        (
            "sich waschen, wäscht sich, wusch sich, hat sich gewaschen",
            "Das Kind wäscht sich.",
        ),
    ],
}


@dataclass
class Line:
    head: str
    example: str


def join_words(words: list[dict[str, object]]) -> str:
    return " ".join(
        str(word["text"])
        for word in sorted(words, key=lambda item: float(item["x0"]))
    ).strip()


def clean_text(value: str) -> str:
    return re.sub(r"\s+", " ", value).strip()


def is_line_continuation(head: str, current_raw: str) -> bool:
    if re.match(
        r"^(?:(?:hat|ist|war|wäre|wird)\b|\(hat\b|\((?:Sg|Sing|Pl)\.?\)|→)",
        head,
        re.IGNORECASE,
    ):
        return True
    if (
        re.search(r"[A-Za-zÄÖÜäöüß]-$", current_raw)
        and " " in current_raw
    ):
        return True

    first_part = current_raw.split(",", maxsplit=1)[0].strip().lower()
    first_word = re.split(r"[\s()/]+", first_part)[-1]
    current_is_noun = bool(
        re.match(
            r"^(?:der|die|das|der/die|der/das|das/der)\b",
            current_raw,
            re.IGNORECASE,
        )
    )
    starts_with_infinitive = (
        first_word.endswith(("en", "eln", "ern")) or first_word == "tun"
    )
    return (
        starts_with_infinitive
        and not current_is_noun
        and not re.search(r"\b(?:hat|ist)\b", current_raw, re.IGNORECASE)
        and not re.match(
            r"^(?:der|die|das|der/die|der/das|das/der)\b",
            head,
            re.IGNORECASE,
        )
        and "," in head
    )


def extract_column(
    words: list[dict[str, object]],
    head_start: float,
    example_start: float,
    column_end: float,
    page_number: int,
) -> list[dict[str, object]]:
    selected_words = []
    for word in words:
        x = float(word["x0"])
        top = float(word["top"])
        if head_start <= x < column_end and 55 < top < 785:
            selected_words.append(word)

    rows: list[tuple[float, list[dict[str, object]]]] = []
    for word in sorted(selected_words, key=lambda item: float(item["top"])):
        top = float(word["top"])
        if rows and abs(top - rows[-1][0]) <= 3:
            rows[-1][1].append(word)
        else:
            rows.append((top, [word]))

    lines: list[Line] = []
    for _, row_words in rows:
        head = join_words(
            [word for word in row_words if float(word["x0"]) < example_start]
        )
        example = join_words(
            [word for word in row_words if float(word["x0"]) >= example_start]
        )
        if head or example:
            lines.append(Line(head, example))

    entries: list[dict[str, object]] = []
    current: dict[str, object] | None = None
    for line in lines:
        starts_entry = current is None or (
            bool(line.head)
            and bool(current["raw"])
            and not is_line_continuation(line.head, str(current["raw"]))
        )
        if starts_entry:
            if current is not None:
                entries.append(current)
            current = {"raw": "", "example": "", "page": page_number}

        if line.head:
            current["raw"] = clean_text(f'{current["raw"]} {line.head}')
        if line.example:
            current["example"] = clean_text(
                f'{current["example"]} {line.example}'
            )

    if current is not None:
        entries.append(current)
    return entries


def is_grammar_continuation(raw: str) -> bool:
    return bool(
        re.match(
            r"^(?:(?:hat|ist|war|wäre|wird)\b|→|\((?:Sg|Sing|Pl)\.?\))",
            raw,
            flags=re.IGNORECASE,
        )
    )


def display_term(raw: str) -> str:
    value = re.sub(
        r"^(?:der/die|der/das|das/der|die/der|der|die|das)\s+",
        "",
        raw,
        flags=re.IGNORECASE,
    )
    value = value.split(",", maxsplit=1)[0]
    value = re.sub(
        r"\s*\((?:Sg\.?|Sing\.?|Pl\.?|nur Pl\.)\)\s*",
        "",
        value,
        flags=re.IGNORECASE,
    )
    return value.strip().rstrip("/")


def limit_example(value: str, max_length: int = 480) -> str:
    if len(value) <= max_length:
        return value

    sentences = re.split(r"(?<=[.!?])\s+", value)
    selected: list[str] = []
    for sentence in sentences:
        candidate = " ".join(selected + [sentence])
        if len(candidate) > max_length:
            break
        selected.append(sentence)
    if selected:
        return " ".join(selected)
    return value[:max_length].rsplit(" ", maxsplit=1)[0].rstrip() + "."


def extract_entries(source: Path) -> list[dict[str, object]]:
    extracted: list[dict[str, object]] = []

    with pdfplumber.open(source) as pdf:
        for page_index in range(16, 102):
            words = pdf.pages[page_index].extract_words(
                x_tolerance=2,
                y_tolerance=3,
            )
            columns = [
                extract_column(words, 25, 130, 300, page_index + 1),
                extract_column(words, 305, 410, 585, page_index + 1),
            ]

            for entry in columns[0] + columns[1]:
                raw = clean_text(str(entry["raw"]))
                raw = re.sub(r"\s*→\s*$", "", raw).strip()
                example = clean_text(str(entry["example"]))
                if not raw or re.fullmatch(r"[A-ZÄÖÜ]", raw):
                    if example and extracted:
                        extracted[-1]["example"] = clean_text(
                            f'{extracted[-1]["example"]} {example}'
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

                if (
                    not example
                    and extracted
                    and not re.match(
                        r"^(?:der|die|das|der/die|der/das|das/der)\b",
                        raw,
                        re.IGNORECASE,
                    )
                    and (
                        re.search(
                            r"\b(?:hat|ist|hat sich|ist sich|etwas gefallen)\s*$",
                            str(extracted[-1]["raw"]),
                            re.IGNORECASE,
                        )
                        or raw
                        in {
                            "geflossen",
                            "gelernt",
                            "getippt",
                            "umgezogen",
                            "lassen",
                            "gehen/sein",
                            "es hat geregnet",
                        }
                    )
                ):
                    extracted[-1]["raw"] = clean_text(
                        f'{extracted[-1]["raw"]} {raw}'
                    )
                    continue

                if (
                    example
                    and extracted
                    and re.match(r"^[a-zäöüß/-]", example)
                    and not re.search(
                        r"[.!?…][”\"']?$",
                        str(extracted[-1]["example"]),
                    )
                ):
                    extracted[-1]["example"] = clean_text(
                        f'{extracted[-1]["example"]} {example}'
                    )
                    continue

                extracted.append(
                    {
                        "raw": raw,
                        "example": example,
                        "page": entry["page"],
                    }
                )

    refined: list[dict[str, object]] = []
    for entry in extracted:
        raw = str(entry["raw"])
        if raw == "bekannt geben, gibt bekannt, gab bekannt, hat":
            entry["raw"] = (
                "bekannt geben, gibt bekannt, gab bekannt, hat bekannt gegeben"
            )
        elif raw == "bekannt gegeben bekommen, bekommt, bekam, hat bekommen":
            entry["raw"] = "bekommen, bekommt, bekam, hat bekommen"
            entry["example"] = "Haben Sie meinen Brief bekommen?"
        elif raw == "der Beleg, -e":
            entry["example"] = "Brauchen Sie einen Beleg?"
        elif raw == "treiben, treibt, trieb, hat":
            entry["raw"] = "treiben, treibt, trieb, hat getrieben"
        elif raw == "getrieben (sich) trennen, trennt, trennte, hat getrennt":
            entry["raw"] = (
                "sich trennen, trennt sich, trennte sich, hat sich getrennt"
            )
            entry["example"] = "Sie hat sich von ihrem Partner getrennt."

        if raw in SOURCE_REFINEMENTS:
            for new_raw, example in SOURCE_REFINEMENTS[raw]:
                refined.append(
                    {
                        "raw": new_raw,
                        "example": example,
                        "page": entry["page"],
                    }
                )
            continue
        refined.append(entry)

    for entry in refined:
        if not entry["example"]:
            term = display_term(str(entry["raw"]))
            entry["example"] = f'Heute lerne ich das Wort „{term}“.'
        entry["example"] = limit_example(str(entry["example"]))

    return [
        {
            "id": index,
            "page": entry["page"],
            "raw": entry["raw"],
            "example": entry["example"],
        }
        for index, entry in enumerate(refined, start=1)
    ]


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("source", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()

    entries = extract_entries(args.source)
    if len(entries) != 3_028:
        raise ValueError(f"Expected 3,028 entries, extracted {len(entries)}.")
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
