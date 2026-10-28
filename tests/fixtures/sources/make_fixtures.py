"""Generates the fixture listing files used by FixtureListingSource and the tests.

    python tests/fixtures/sources/make_fixtures.py

Output (deterministic, seed 2026):
  fixture/run-001.json .. run-003.json  — the presence scenario from spec §7 (120 / 118 / 122 listings)
  kp/run-001.json                       — invented ads in KupujemProdajem's style (spec D-39)

All ads are invented. Book titles and authors are public bibliographic facts; nothing is copied from a
marketplace. Do not replace these files with scraped data (spec §4.5 rule 7).
"""
from __future__ import annotations

import json
import random
from datetime import datetime, timedelta, timezone
from pathlib import Path

OUT = Path(__file__).resolve().parent

# (canonical title, canonical author, typical price in RSD)
BOOKS = [
    ("Na Drini ćuprija", "Ivo Andrić", 700), ("Prokleta avlija", "Ivo Andrić", 450), ("Travnička hronika", "Ivo Andrić", 600),
    ("Gospođica", "Ivo Andrić", 400), ("Derviš i smrt", "Meša Selimović", 650), ("Tvrđava", "Meša Selimović", 550),
    ("Seobe", "Miloš Crnjanski", 800), ("Seobe 2", "Miloš Crnjanski", 900), ("Roman o Londonu", "Miloš Crnjanski", 850),
    ("Dnevnik o Čarnojeviću", "Miloš Crnjanski", 500), ("Grobnica za Borisa Davidoviča", "Danilo Kiš", 600),
    ("Bašta, pepeo", "Danilo Kiš", 550), ("Enciklopedija mrtvih", "Danilo Kiš", 650), ("Peščanik", "Danilo Kiš", 700),
    ("Hazarski rečnik", "Milorad Pavić", 900), ("Predeo slikan čajem", "Milorad Pavić", 750),
    ("Kad su cvetale tikve", "Dragoslav Mihailović", 500), ("Bašta sljezove boje", "Branko Ćopić", 450),
    ("Orlovi rano lete", "Branko Ćopić", 350), ("Gorski vijenac", "Petar Petrović Njegoš", 400),
    ("Nečista krv", "Borisav Stanković", 350), ("Koštana", "Borisav Stanković", 300), ("Pop Ćira i pop Spira", "Stevan Sremac", 350),
    ("Zona Zamfirova", "Stevan Sremac", 300), ("Sumnjivo lice", "Branislav Nušić", 300), ("Gospođa ministarka", "Branislav Nušić", 300),
    ("Autobiografija", "Branislav Nušić", 450), ("Pesme", "Desanka Maksimović", 400), ("Pesme", "Branko Radičević", 350),
    ("Knjiga za Marka", "Svetlana Velmar-Janković", 600), ("Dorćol", "Svetlana Velmar-Janković", 550),
    ("Kako upokojiti vampira", "Borislav Pekić", 700), ("Besnilo", "Borislav Pekić", 750), ("Vreme čuda", "Borislav Pekić", 650),
    ("Mamac", "David Albahari", 500), ("Kiklop", "Ranko Marinković", 600), ("Zločin i kazna", "Fjodor Dostojevski", 600),
    ("Braća Karamazovi", "Fjodor Dostojevski", 900), ("Ana Karenjina", "Lav Tolstoj", 800), ("Majstor i Margarita", "Mihail Bulgakov", 700),
    ("Sto godina samoće", "Gabrijel Garsija Markes", 750), ("Mali princ", "Antoan de Sent Egziperi", 350),
    ("Hiljadu čudesnih sunaca", "Haled Hoseini", 650), ("Lovac na zmajeve", "Haled Hoseini", 600), ("Alhemičar", "Paulo Koeljo", 450),
]

LAT_TO_CYR = [("dž", "џ"), ("Dž", "Џ"), ("lj", "љ"), ("Lj", "Љ"), ("nj", "њ"), ("Nj", "Њ")] + [
    (l, c) for l, c in zip("abvgdđežzijklmnoprstćufhcčš", "абвгдђежзијклмнопрстћуфхцчш")] + [
    (l, c) for l, c in zip("ABVGDĐEŽZIJKLMNOPRSTĆUFHCČŠ", "АБВГДЂЕЖЗИЈКЛМНОПРСТЋУФХЦЧШ")]
ASCII = str.maketrans({"č": "c", "ć": "c", "š": "s", "ž": "z", "đ": "dj", "Č": "C", "Ć": "C", "Š": "S", "Ž": "Z", "Đ": "Dj"})
NOISE = [" - kao nova", " (tvrd povez)", " odlično očuvana", ", 1977.", " NOVO", " - meki povez", " !!!"]
CONDITIONS = ["Odlično", "Vrlo dobro", "Dobro", None]


def to_cyrillic(text: str) -> str:
    for lat, cyr in LAT_TO_CYR:
        text = text.replace(lat, cyr)
    return text


def surname_first(author: str) -> str:
    parts = author.split()
    return " ".join(parts[-1:] + parts[:-1])


def initials(author: str) -> str:
    parts = author.split()
    return f"{parts[0][0]}. {' '.join(parts[1:])}"


def variant(rng: random.Random, title: str, author: str) -> tuple[str, str | None]:
    """One of the messy ways a real ad spells a book (spec §6.1 test material)."""
    kind = rng.choice(["exact", "ascii", "cyrillic", "upper", "noise", "author_in_title", "swapped", "initials", "no_author", "typo"])
    if kind == "ascii":
        return title.translate(ASCII), author.translate(ASCII)
    if kind == "cyrillic":
        return to_cyrillic(title), to_cyrillic(author)
    if kind == "upper":
        return title.upper(), author
    if kind == "noise":
        return title + rng.choice(NOISE), author
    if kind == "author_in_title":
        return f"{author} - {title}", None
    if kind == "swapped":
        return title, surname_first(author)
    if kind == "initials":
        return title, initials(author)
    if kind == "no_author":
        return title, None
    if kind == "typo" and len(title) > 6:
        i = rng.randrange(1, len(title) - 1)
        return title[:i] + title[i + 1:], author
    return title, author


def rsd_price(rng: random.Random, base: int) -> str:
    amount = int(round(base * rng.uniform(0.6, 1.6) / 10) * 10)
    fmt = rng.choice(["{a} din", "{a} din", "{a},00 RSD", "{a} RSD", "Cena: {a} dinara"])
    grouped = f"{amount:,}".replace(",", ".")
    return fmt.format(a=grouped)


def kp_price(rng: random.Random, base: int) -> str:
    roll = rng.random()
    if roll < 0.08:
        return "Dogovor"
    if roll < 0.35:
        eur = max(2, round(base * rng.uniform(0.6, 1.5) / 117.2))
        return rng.choice([f"{eur} €", f"{eur},00 €", f"{eur} EUR"])
    amount = int(round(base * rng.uniform(0.6, 1.5) / 50) * 50)
    return f"{amount:,} din".replace(",", ".")


def fixture_runs() -> None:
    rng = random.Random(2026)
    # L001..L130: every listing gets a fixed book, spelling, condition and starting price.
    listings = {}
    for n in range(1, 131):
        title, author, base = BOOKS[(n * 7) % len(BOOKS)] if n % 5 else rng.choice(BOOKS)
        t, a = variant(rng, title, author)
        listings[n] = {
            "externalId": f"L{n:03d}",
            "title": t,
            "author": a,
            "price": rsd_price(rng, base),
            "condition": rng.choice(CONDITIONS),
            "url": f"https://fixture.priceradar.local/listing/L{n:03d}",
            "postedAtUtc": (datetime(2026, 9, 1, tzinfo=timezone.utc) + timedelta(hours=n * 7)).isoformat().replace("+00:00", "Z"),
        }
    # Junk rows that MatchJob must reject (FR-5).
    listings[37]["price"] = "Dogovor"
    listings[64]["price"] = ""
    listings[88]["price"] = "1 din"

    scenario = {
        "run-001": list(range(1, 121)),
        "run-002": list(range(1, 113)) + list(range(121, 127)),
        "run-003": list(range(1, 111)) + [113, 114] + list(range(121, 131)),
    }
    for i, (name, ids) in enumerate(scenario.items()):
        captured = datetime(2026, 10, 18, 9, 0, tzinfo=timezone.utc) + timedelta(days=7 * i)
        rows = []
        for n in ids:
            row = dict(listings[n])
            if i > 0 and n % 11 == 0 and row["price"].endswith(" din"):   # occasional price drop
                amount = int(row["price"].split()[0].replace(".", "")) * 9 // 10
                row["price"] = f"{amount:,} din".replace(",", ".")
                listings[n] = row
            rows.append(row)
        write(OUT / "fixture" / f"{name}.json", "fixture", "fixture:category:knjige", captured, 4, rows)


def kp_runs() -> None:
    rng = random.Random(1580)
    rows = []
    for n in range(40):
        title, author, base = BOOKS[(n * 3) % len(BOOKS)]
        t, a = variant(rng, title, author)
        ad_id = 158_400_000 + n * 1_337
        rows.append({
            "externalId": str(ad_id),
            "title": t,
            "author": a,
            "price": kp_price(rng, base),
            "condition": rng.choice(["Kao novo (polovno)", "Korišćeno", "Novo - neotpakovano", "Oštećeno"]),
            "url": f"https://kp.priceradar.local/knjige/oglas/{ad_id}",
            "postedAtUtc": (datetime(2026, 10, 1, tzinfo=timezone.utc) + timedelta(hours=n * 11)).isoformat().replace("+00:00", "Z"),
        })
    write(OUT / "kp" / "run-001.json", "kp", "kp:category:knjige", datetime(2026, 10, 25, 10, 0, tzinfo=timezone.utc), 2, rows)


def write(path: Path, source: str, scope: str, captured: datetime, pages: int, rows: list[dict]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    doc = {
        "source": source,
        "scopeKey": scope,
        "capturedAtUtc": captured.isoformat().replace("+00:00", "Z"),
        "pages": pages,
        "note": "Invented listings for tests and demos. Not scraped from any marketplace.",
        "listings": rows,
    }
    path.write_text(json.dumps(doc, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(f"{path.relative_to(OUT)}: {len(rows)} listings")


if __name__ == "__main__":
    fixture_runs()
    kp_runs()
