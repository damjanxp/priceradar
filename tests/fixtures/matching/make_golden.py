"""Generates the matching golden set (spec §6.4): a small catalogue and ~150 labelled listings.

    python tests/fixtures/matching/make_golden.py

catalog.csv  — canonical books (title, author)
golden.csv   — listing title/author as an ad would spell it, the expected catalogue key (or NEW), and the kind of variant

Labels are known by construction for the generated variants; the traps at the bottom are written by hand. Titles and
authors are public bibliographic facts. Deterministic (seed 7).
"""
from __future__ import annotations

import csv
import random
import unicodedata
from pathlib import Path

OUT = Path(__file__).resolve().parent

CATALOG = [
    ("Na Drini ćuprija", "Ivo Andrić"), ("Prokleta avlija", "Ivo Andrić"), ("Travnička hronika", "Ivo Andrić"),
    ("Gospođica", "Ivo Andrić"), ("Derviš i smrt", "Meša Selimović"), ("Tvrđava", "Meša Selimović"),
    ("Seobe", "Miloš Crnjanski"), ("Seobe 2", "Miloš Crnjanski"), ("Roman o Londonu", "Miloš Crnjanski"),
    ("Dnevnik o Čarnojeviću", "Miloš Crnjanski"), ("Grobnica za Borisa Davidoviča", "Danilo Kiš"),
    ("Bašta, pepeo", "Danilo Kiš"), ("Enciklopedija mrtvih", "Danilo Kiš"), ("Peščanik", "Danilo Kiš"),
    ("Hazarski rečnik", "Milorad Pavić"), ("Predeo slikan čajem", "Milorad Pavić"),
    ("Kad su cvetale tikve", "Dragoslav Mihailović"), ("Bašta sljezove boje", "Branko Ćopić"),
    ("Orlovi rano lete", "Branko Ćopić"), ("Gorski vijenac", "Petar Petrović Njegoš"), ("Nečista krv", "Borisav Stanković"),
    ("Koštana", "Borisav Stanković"), ("Pop Ćira i pop Spira", "Stevan Sremac"), ("Zona Zamfirova", "Stevan Sremac"),
    ("Sumnjivo lice", "Branislav Nušić"), ("Gospođa ministarka", "Branislav Nušić"), ("Autobiografija", "Branislav Nušić"),
    ("Pesme", "Desanka Maksimović"), ("Pesme", "Branko Radičević"), ("Kako upokojiti vampira", "Borislav Pekić"),
    ("Besnilo", "Borislav Pekić"), ("Vreme čuda", "Borislav Pekić"), ("Kiklop", "Ranko Marinković"),
    ("Zločin i kazna", "Fjodor Dostojevski"), ("Braća Karamazovi", "Fjodor Dostojevski"), ("Ana Karenjina", "Lav Tolstoj"),
    ("Majstor i Margarita", "Mihail Bulgakov"), ("Sto godina samoće", "Gabrijel Garsija Markes"),
    ("Mali princ", "Antoan de Sent Egziperi"), ("Alhemičar", "Paulo Koeljo"), ("Lovac na zmajeve", "Haled Hoseini"),
    ("Hiljadu čudesnih sunaca", "Haled Hoseini"),
]

# Real books that are NOT in the catalogue: the matcher must create a new book (several share an author or a word).
NEW = [
    ("Ex Ponto", "Ivo Andrić"), ("Ćorkan i Švabica", "Ivo Andrić"), ("Lirika Itake", "Miloš Crnjanski"),
    ("Mansarda", "Danilo Kiš"), ("Rani jadi", "Danilo Kiš"), ("Unutrašnja strana vetra", "Milorad Pavić"),
    ("Ostrvo", "Meša Selimović"), ("Hajduci", "Branko Ćopić"), ("Ivkova slava", "Stevan Sremac"),
    ("Protekcija", "Branislav Nušić"), ("Narodni poslanik", "Branislav Nušić"), ("Pokondirena tikva", "Jovan Sterija Popović"),
    ("Kir Janja", "Jovan Sterija Popović"), ("Rat i mir", "Lav Tolstoj"), ("Idiot", "Fjodor Dostojevski"),
    ("Bele noći", "Fjodor Dostojevski"), ("Proces", "Franc Kafka"), ("Stranac", "Alber Kami"),
    ("Ljubav u doba kolere", "Gabrijel Garsija Markes"), ("Zlatna knjiga bajki", "Desanka Maksimović"),
    ("Pesme o ljubavi", "Vasko Popa"), ("Seobe Srba", "Dejan Medaković"), ("Travnička hronologija", "Petar Petrović"),
    ("Grobnica za kraljeve", "Milan Kostić"), ("Majstor i učenik", "Jovan Jovanović"),
]

LAT_TO_CYR = [("dž", "џ"), ("Dž", "Џ"), ("lj", "љ"), ("Lj", "Љ"), ("nj", "њ"), ("Nj", "Њ")] + [
    (l, c) for l, c in zip("abvgdđežzijklmnoprstćufhcčš", "абвгдђежзијклмнопрстћуфхцчш")] + [
    (l, c) for l, c in zip("ABVGDĐEŽZIJKLMNOPRSTĆUFHCČŠ", "АБВГДЂЕЖЗИЈКЛМНОПРСТЋУФХЦЧШ")]
ASCII = str.maketrans({"č": "c", "ć": "c", "š": "s", "ž": "z", "đ": "dj", "Č": "C", "Ć": "C", "Š": "S", "Ž": "Z", "Đ": "Dj"})
NOISE = [" - kao nova", " (tvrd povez)", " odlično očuvana", ", 1977.", " NOVO", " 2. izdanje"]


def key(title: str, author: str) -> str:
    return f"{author} | {title}"


def cyr(t: str) -> str:
    for a, b in LAT_TO_CYR:
        t = t.replace(a, b)
    return t


def typo(rng: random.Random, t: str) -> str:
    letters = [i for i, c in enumerate(t) if c.isalpha()]
    i = rng.choice(letters[1:-1]) if len(letters) > 3 else letters[0]
    if rng.random() < 0.5:
        return t[:i] + t[i + 1:]                       # dropped letter
    return t[:i] + t[i + 1] + t[i] + t[i + 2:] if i + 1 < len(t) else t   # swapped letters


VARIANTS = ["exact", "ascii", "cyrillic", "upper", "noise", "author_in_title", "swapped", "initials", "typo", "no_author"]


def variant(rng: random.Random, kind: str, title: str, author: str) -> tuple[str, str]:
    first, *rest = author.split()
    return {
        "exact": (title, author),
        "ascii": (title.translate(ASCII), author.translate(ASCII)),
        "cyrillic": (cyr(title), cyr(author)),
        "upper": (title.upper(), author.upper()),
        "noise": (title + rng.choice(NOISE), author),
        "author_in_title": (f"{author} - {title}", ""),
        "swapped": (title, " ".join(rest + [first])),
        "initials": (title, f"{first[0]}. {' '.join(rest)}"),
        "typo": (typo(rng, title), author),
        "no_author": (title, ""),
    }[kind]


def main() -> None:
    rng = random.Random(7)
    rows = []
    for title, author in CATALOG:                          # 3 different spellings per catalogue book
        for kind in rng.sample(VARIANTS, 3):
            t, a = variant(rng, kind, title, author)
            rows.append((t, a, key(title, author), kind))
    for title, author in NEW:                               # each new book once, in a random spelling
        kind = rng.choice([k for k in VARIANTS if k != "no_author"])
        t, a = variant(rng, kind, title, author)
        rows.append((t, a, "NEW", f"new:{kind}"))

    traps = [   # hand-written: the cases that go wrong in real matchers
        ("Seobe 2", "Miloš Crnjanski", key("Seobe 2", "Miloš Crnjanski"), "trap:volume"),
        ("Seobe II", "Milos Crnjanski", key("Seobe 2", "Miloš Crnjanski"), "trap:roman-numeral-volume"),   # II = 2
        ("Seobe knjiga druga", "Crnjanski", "NEW", "trap:volume-in-words"),
        ("Pesme", "Branko Radičević", key("Pesme", "Branko Radičević"), "trap:same-title"),
        ("PESME", "DESANKA MAKSIMOVIC", key("Pesme", "Desanka Maksimović"), "trap:same-title"),
        ("Pesme", "", "AMBIGUOUS", "trap:same-title-no-author"),
        ("Na Drini cuprija", "", key("Na Drini ćuprija", "Ivo Andrić"), "trap:no-author"),
        ("Prokleta avlija - Ivo Andric - tvrd povez", "", key("Prokleta avlija", "Ivo Andrić"), "trap:double-dash"),
        ("Andric - Prokleta avlija", "", key("Prokleta avlija", "Ivo Andrić"), "trap:surname-only"),
        ("Zlocin i kazna", "Dostojevski", key("Zločin i kazna", "Fjodor Dostojevski"), "trap:surname-only-field"),
        ("Bracha Karamazovi", "F. M. Dostojevski", key("Braća Karamazovi", "Fjodor Dostojevski"), "trap:transliteration-typo"),
        ("Мајстор и Маргарита", "Михаил Булгаков", key("Majstor i Margarita", "Mihail Bulgakov"), "trap:cyrillic-foreign"),
        ("Mali princ", "Antoine de Saint-Exupéry", key("Mali princ", "Antoan de Sent Egziperi"), "trap:original-spelling"),
        ("Gospodja ministarka", "Branislav Nusic", key("Gospođa ministarka", "Branislav Nušić"), "trap:dj-spelling"),
        ("Tvrdjava", "Mesa Selimovic", key("Tvrđava", "Meša Selimović"), "trap:dj-spelling"),
        ("Idiot", "Fjodor Mihajlovič Dostojevski", "NEW", "trap:new-same-author"),
        ("Hazarski rečnik - muški primerak", "Milorad Pavić", key("Hazarski rečnik", "Milorad Pavić"), "trap:edition-subtitle"),
        ("Kad su cvetale tikve, roman", "Dragoslav Mihailovic", key("Kad su cvetale tikve", "Dragoslav Mihailović"), "trap:noise-word"),
    ]
    rows.extend(traps)

    with (OUT / "catalog.csv").open("w", encoding="utf-8", newline="") as f:
        w = csv.writer(f, lineterminator="\n")
        w.writerow(["title", "author"])
        w.writerows(CATALOG)
    with (OUT / "golden.csv").open("w", encoding="utf-8", newline="") as f:
        w = csv.writer(f, lineterminator="\n")
        w.writerow(["listing_title", "listing_author", "expected", "kind"])
        w.writerows(rows)
    print(f"catalog.csv: {len(CATALOG)} books, golden.csv: {len(rows)} rows "
          f"({sum(r[2] == 'NEW' for r in rows)} NEW, {len(traps)} hand-written traps)")


if __name__ == "__main__":
    main()
