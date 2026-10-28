"""Generates diagrams/er.dot from the table definitions below (kept in sync with section 5 of the spec)."""
from pathlib import Path

TABLES = {
    "ingest.Source": ("#3a6ea5", [
        ("PK", "SourceId", "tinyint"), ("UQ", "Code", "varchar(32)"), ("", "Name", "nvarchar(100)"),
        ("", "Kind", "tinyint"), ("", "BaseUrl", "varchar(200)"), ("", "IsEnabled", "bit"),
        ("", "MinDelayMs", "int")]),
    "ingest.ScrapeRun": ("#3a6ea5", [
        ("PK", "ScrapeRunId", "bigint"), ("FK", "SourceId", "tinyint"), ("", "ScopeKey", "varchar(200)"),
        ("", "Status / Stage", "tinyint"), ("", "IsComplete", "bit"), ("UQ", "IdempotencyKey", "uniqueidentifier"),
        ("", "Listings* / AiCalls", "int"), ("", "EurRsdRate", "decimal(9,4)"), ("", "Created/Started/FinishedAtUtc", "datetime2(3)")]),
    "ingest.StagingListing": ("#3a6ea5", [
        ("PK", "StagingListingId", "bigint"), ("FK", "ScrapeRunId", "bigint"), ("", "ExternalId", "varchar(64)"),
        ("", "RawTitle / RawAuthor", "nvarchar"), ("", "RawPrice / RawCondition", "nvarchar"), ("", "PriceRsd", "decimal(12,2)"),
        ("", "NormTitle / NormAuthor", "varchar"), ("", "BookId", "int"), ("", "MatchStatus / MatchScore", ""),
        ("", "ProcessStatus", "tinyint"), ("", "PayloadHash", "binary(32)")]),
    "dw.Book": ("#4a8a3a", [
        ("PK", "BookId", "int"), ("", "Title", "nvarchar(300)"), ("", "Author", "nvarchar(200)"),
        ("", "NormTitle", "varchar(300)"), ("", "NormAuthor", "varchar(200)"), ("UQ", "NormKey (computed)", "varchar(501)"),
        ("", "Origin", "tinyint"), ("", "NeedsReview", "bit")]),
    "dw.Listing": ("#4a8a3a", [
        ("PK", "ListingId", "bigint"), ("FK", "SourceId", "tinyint"), ("UQ", "ExternalId", "varchar(64)"),
        ("FK", "BookId", "int NULL"), ("", "MatchStatus / MatchScore", ""), ("", "ScopeKey", "varchar(200)"),
        ("", "Status", "tinyint"), ("", "CurrentPriceRsd", "decimal(12,2)"), ("", "First/LastSeenAtUtc", "datetime2(3)"),
        ("FK", "LastSeenRunId", "bigint"), ("", "MissedRunCount", "tinyint"), ("", "DisappearedAtUtc", "datetime2(3)")]),
    "dw.PriceSnapshot": ("#2f6d22", [
        ("PK", "PriceSnapshotId", "bigint"), ("FK", "ListingId", "bigint"), ("FK", "BookId", "int NULL"),
        ("FK", "SourceId", "tinyint"), ("FK", "ScrapeRunId", "bigint"), ("FK", "DateKey", "int"),
        ("", "PriceRsd", "decimal(12,2)"), ("", "CapturedAtUtc", "datetime2(3)")]),
    "dw.DimDate": ("#4a8a3a", [
        ("PK", "DateKey", "int"), ("UQ", "Date", "date"), ("", "Year / Month / Day", "smallint, tinyint"),
        ("", "IsoWeek / DayOfWeek", "tinyint"), ("", "IsWeekend", "bit")]),
    "match.MatchCache": ("#6a4fb3", [
        ("PK", "MatchCacheId", "bigint"), ("UQ", "InputHash", "binary(32)"), ("", "PromptVersion", "varchar(16)"),
        ("", "NormTitle / NormAuthor", "varchar"), ("", "CandidateBookIds", "varchar(200)"), ("", "Decision", "tinyint"),
        ("FK", "ResolvedBookId", "int NULL"), ("", "Confidence", "decimal(4,3)"), ("", "Model", "varchar(64)"),
        ("", "Input/OutputTokens", "int"), ("", "HitCount", "int")]),
}

EDGES = [
    ("ingest.Source", "ingest.ScrapeRun"), ("ingest.ScrapeRun", "ingest.StagingListing"),
    ("ingest.Source", "dw.Listing"), ("dw.Book", "dw.Listing"), ("dw.Listing", "dw.PriceSnapshot"),
    ("ingest.ScrapeRun", "dw.PriceSnapshot"), ("dw.DimDate", "dw.PriceSnapshot"),
    ("dw.Book", "dw.PriceSnapshot"), ("dw.Book", "match.MatchCache"),
]

def node_id(name): return name.replace(".", "_")

def label(name, color, cols):
    rows = [f'<TR><TD COLSPAN="3" BGCOLOR="{color}" ALIGN="LEFT"><FONT COLOR="white"><B>{name}</B></FONT></TD></TR>']
    for key, col, typ in cols:
        kcolor = {"PK": "#bb3333", "FK": "#3366cc", "UQ": "#998855"}.get(key, "#999999")
        kcell = f'<FONT COLOR="{kcolor}" POINT-SIZE="8"><B>{key}</B></FONT>' if key else " "
        tcell = f'<FONT COLOR="#666666" POINT-SIZE="8">{typ}</FONT>' if typ else " "
        rows.append(f'<TR><TD ALIGN="LEFT">{kcell}</TD><TD ALIGN="LEFT">{col}</TD><TD ALIGN="LEFT">{tcell}</TD></TR>')
    return '<<TABLE BORDER="1" CELLBORDER="0" CELLSPACING="0" CELLPADDING="3" COLOR="#8895a3">' + "".join(rows) + "</TABLE>>"

out = ['digraph er {',
       '  graph [rankdir=LR, fontname="Helvetica", nodesep=0.25, ranksep=0.7, pad=0.15, bgcolor="white", splines=true];',
       '  node [shape=plain, fontname="Helvetica", fontsize=9];',
       '  edge [color="#5b6b7f", arrowhead=crow, arrowtail=tee, dir=both, arrowsize=0.7];']
for name, (color, cols) in TABLES.items():
    out.append(f'  {node_id(name)} [label={label(name, color, cols)}];')
out.append('  { rank=same; ingest_Source; dw_Book; dw_DimDate; }')
out.append('  { rank=same; ingest_ScrapeRun; dw_Listing; match_MatchCache; }')
out.append('  { rank=same; ingest_StagingListing; dw_PriceSnapshot; }')
for a, b in EDGES:
    out.append(f'  {node_id(a)} -> {node_id(b)};')
out.append('}')
Path(__file__).resolve().parent.parent.joinpath("diagrams", "er.dot").write_text("\n".join(out), encoding="utf-8")
print("wrote er.dot")
