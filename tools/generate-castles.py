#!/usr/bin/env python3
"""Generate castles.json for the aalg-trips "Castle Bingo" page.

Author-side, offline tooling (in the spirit of the searoute/OSRM route helpers):
it queries the public Wikidata SPARQL endpoint once for every UK castle, cleans
and flattens the result, maps the operator to an access tier, merges any manual
overrides, and writes a committed JSON catalogue the site reads at startup. The
running site never talks to Wikidata — this is run by hand when the list needs
refreshing.

Usage:  python generate-castles.py --out ../src/AalgTrips/Data/castles.json
"""

import argparse
import json
import re
import sys
import time
import urllib.parse
import urllib.request

ENDPOINT = "https://query.wikidata.org/sparql"
UA = "aalg-trips-castle-bingo/1.0 (https://aalg.co.uk; gep13@gep13.co.uk)"

# Home nations, as Wikidata items, reached from a castle via located-in (P131*).
NATIONS = {
    "Scotland": "Q22",
    "England": "Q21",
    "Wales": "Q25",
    "Northern Ireland": "Q26",
}

# Operator (P137) label -> (short badge, access tier). Membership bodies are the
# ones where a member gets in free; everything else with a known operator is
# treated as managed/pay, and no operator at all is "unknown". Overrides can
# correct individual castles (e.g. a free open ruin).
MEMBERSHIP_OPERATORS = [
    ("National Trust for Scotland", "NTS"),
    ("Historic Environment Scotland", "HES"),
    ("Historic Scotland", "HES"),
    ("English Heritage", "EH"),
    ("Cadw", "Cadw"),
    ("National Trust", "NT"),  # keep AFTER "National Trust for Scotland"
]


def sparql(query, retries=3):
    url = ENDPOINT + "?" + urllib.parse.urlencode({"query": query, "format": "json"})
    req = urllib.request.Request(url, headers={"User-Agent": UA, "Accept": "application/sparql-results+json"})
    for attempt in range(retries):
        try:
            with urllib.request.urlopen(req, timeout=180) as resp:
                body = resp.read()
            text = body.decode("utf-8", "replace").lstrip()
            if not text.startswith("{"):
                # WDQS answers a server-side timeout with an HTTP 200 whose body is
                # a Java stack trace, not JSON — treat that as a retryable failure.
                raise ValueError("non-JSON response (likely a query timeout)")
            return json.loads(text)["results"]["bindings"]
        except Exception as exc:  # noqa: BLE001 - author tool, surface and retry
            if attempt == retries - 1:
                raise
            print(f"  query failed ({exc}); retrying...", file=sys.stderr)
            time.sleep(5)


def qid(uri):
    return uri.rsplit("/", 1)[-1]


def parse_point(wkt):
    # Wikidata coordinates come as "Point(<lon> <lat>)".
    m = re.match(r"Point\(([-0-9.]+) ([-0-9.]+)\)", wkt)
    if not m:
        return None, None
    return round(float(m.group(2)), 5), round(float(m.group(1)), 5)  # lat, lon


def classify(operator_labels):
    for label in operator_labels:
        for needle, badge in MEMBERSHIP_OPERATORS:
            if needle.lower() in label.lower():
                return badge, "MembersFree"
    if operator_labels:
        # A known operator that is not a membership body: assume managed / pay.
        return operator_labels[0], "Paid"
    return None, "Unknown"


# Castles proper (Q23413) plus tower houses (Q91312) — a huge category of Scottish
# castles (Old Slains, and much of Aberdeenshire) that the strict castle type alone
# leaves out.
BASE = "?item wdt:P31 ?ctype ; wdt:P17 wd:Q145 . VALUES ?ctype { wd:Q23413 wd:Q91312 }"


def fetch_items():
    # The spine: every UK castle with a coordinate and an English label. One flat
    # query, no OPTIONAL joins or aggregation, so it stays well under the WDQS
    # 60-second limit where the all-in-one grouped query timed out.
    query = f"""
    SELECT ?item ?itemLabel ?coord WHERE {{
      {BASE} ?item wdt:P625 ?coord .
      ?item rdfs:label ?itemLabel . FILTER(lang(?itemLabel) = "en")
    }}
    """
    rows = sparql(query)
    items = {}
    for r in rows:
        item = qid(r["item"]["value"])
        if item in items:
            continue
        lat, lon = parse_point(r["coord"]["value"])
        if lat is None:
            continue
        items[item] = {"id": item, "name": r["itemLabel"]["value"], "lat": lat, "lon": lon}
    return items


def fetch_single(select_var, where):
    # A thin flat query returning (item -> first value of select_var). Used for the
    # sparse side properties (website, admin, ...), each cheap on its own.
    rows = sparql(f"SELECT ?item ?{select_var} WHERE {{ {BASE} {where} }}")
    out = {}
    for r in rows:
        item = qid(r["item"]["value"])
        if item not in out and select_var in r:
            out[item] = r[select_var]["value"]
    return out


def fetch_operators():
    rows = sparql(
        f'SELECT ?item ?opLabel WHERE {{ {BASE} ?item wdt:P137 ?op. '
        f'?op rdfs:label ?opLabel. FILTER(lang(?opLabel) = "en") }}')
    out = {}
    for r in rows:
        out.setdefault(qid(r["item"]["value"]), []).append(r["opLabel"]["value"])
    return out


def fetch_heritage():
    rows = sparql(f"SELECT DISTINCT ?item WHERE {{ {BASE} ?item wdt:P1435 ?h. }}")
    return {qid(r["item"]["value"]) for r in rows}


def fetch_parents(nodes):
    # One-hop "located in" (P131) parents for a set of items. Flat and cheap — no
    # property-path closure, which is what times out on WDQS.
    out = {}
    nodes = sorted(nodes)
    for i in range(0, len(nodes), 200):
        chunk = nodes[i:i + 200]
        values = " ".join(f"wd:{a}" for a in chunk)
        rows = sparql(f"SELECT ?a ?p WHERE {{ VALUES ?a {{ {values} }} ?a wdt:P131 ?p. }}")
        for r in rows:
            out.setdefault(qid(r["a"]["value"]), set()).add(qid(r["p"]["value"]))
    return out


def resolve_nations(admin_qids):
    """Map each admin area to its home nation by walking one-hop parent edges up to
    a nation item, instead of a server-side transitive closure."""
    nation_by_qid = {q: name for name, q in NATIONS.items()}
    parents = {}
    frontier = set(admin_qids)
    for _ in range(6):  # UK admin nesting is only a few levels deep
        to_fetch = {n for n in frontier if n not in parents and n not in nation_by_qid}
        if not to_fetch:
            break
        got = fetch_parents(to_fetch)
        parents.update(got)
        frontier = {p for ps in got.values() for p in ps} - parents.keys()

    def climb(node, seen):
        if node in nation_by_qid:
            return nation_by_qid[node]
        if node in seen:
            return None
        seen.add(node)
        for parent in parents.get(node, ()):  # noqa: B007
            found = climb(parent, seen)
            if found:
                return found
        return None

    return {a: (climb(a, set()) or "") for a in admin_qids}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--overrides", default=None, help="optional overrides JSON (id -> field patch)")
    args = ap.parse_args()

    print("Fetching castles (spine)...", file=sys.stderr)
    items = fetch_items()
    print(f"  {len(items)} castles with coordinates", file=sys.stderr)

    print("Fetching websites, admin areas, operators, heritage...", file=sys.stderr)
    websites = fetch_single("website", "?item wdt:P856 ?website.")
    admin_items = fetch_single("admin", "?item wdt:P131 ?admin.")
    admin_labels = fetch_single(
        "adminLabel", '?item wdt:P131 ?a. ?a rdfs:label ?adminLabel. FILTER(lang(?adminLabel) = "en")')
    operators = fetch_operators()
    heritage_ids = fetch_heritage()

    admin_id_of = {item: qid(uri) for item, uri in admin_items.items()}
    admin_qids = set(admin_id_of.values())
    print(f"Resolving {len(admin_qids)} admin areas to nations...", file=sys.stderr)
    admin_nation = resolve_nations(admin_qids)

    overrides = {}
    if args.overrides:
        try:
            overrides = json.load(open(args.overrides, encoding="utf-8"))
        except FileNotFoundError:
            pass

    castles = []
    seen_coords = {}
    for item, base in items.items():
        name = base["name"]
        lat, lon = base["lat"], base["lon"]
        ops = operators.get(item, [])
        badge, tier = classify(ops)
        nation = admin_nation.get(admin_id_of.get(item), "")

        record = {
            "id": item,
            "name": name,
            "lat": lat,
            "lon": lon,
            "nation": nation,
            "admin": admin_labels.get(item),
            "operator": badge,
            "access": tier,
            "website": websites.get(item),
            "heritage": item in heritage_ids,
        }

        # Near-duplicate coordinate guard (e.g. "Gight Castle" + "Gight"): keep one,
        # preferring the entry whose name reads like the castle proper.
        key = (round(lat, 3), round(lon, 3))
        if key in seen_coords:
            prev = seen_coords[key]
            if "castle" in name.lower() and "castle" not in prev["name"].lower():
                castles.remove(prev)
                seen_coords[key] = record
                castles.append(record)
            continue
        seen_coords[key] = record
        castles.append(record)

    # Merge manual overrides. An override keyed by an id already in the set PATCHES
    # it (access corrections, name fixes, free-ruin flags); an override keyed by an
    # id that is NOT in the set is a full record to INJECT — this is how castles the
    # strict type filter excludes (e.g. Fyvie, Balmoral, typed "stately home") are
    # added by hand.
    existing_ids = {c["id"] for c in castles}
    for c in castles:
        patch = overrides.get(c["id"])
        if patch:
            c.update(patch)

    for oid, rec in overrides.items():
        if oid in existing_ids:
            continue
        if not rec.get("name") or rec.get("lat") is None or rec.get("lon") is None:
            print(f"  override {oid} skipped: needs at least name, lat, lon", file=sys.stderr)
            continue
        castles.append({
            "id": oid,
            "name": rec["name"],
            "lat": rec["lat"],
            "lon": rec["lon"],
            "nation": rec.get("nation", ""),
            "admin": rec.get("admin"),
            "operator": rec.get("operator"),
            "access": rec.get("access", "Unknown"),
            "website": rec.get("website"),
            "heritage": rec.get("heritage", False),
        })

    # Nearest to Ellon first is the site's default order, but store alphabetically
    # by name so diffs are stable across refreshes; the app sorts by distance.
    castles.sort(key=lambda c: c["name"])

    with open(args.out, "w", encoding="utf-8", newline="\n") as f:
        json.dump(castles, f, ensure_ascii=False, indent=1)
        f.write("\n")

    # Summary for the operator running the refresh.
    total = len(castles)
    by_nation = {}
    for c in castles:
        by_nation[c["nation"] or "(unknown)"] = by_nation.get(c["nation"] or "(unknown)", 0) + 1
    tiers = {}
    for c in castles:
        tiers[c["access"]] = tiers.get(c["access"], 0) + 1
    web = sum(1 for c in castles if c["website"])
    print(f"\nWrote {total} castles to {args.out}", file=sys.stderr)
    print(f"  nation: {by_nation}", file=sys.stderr)
    print(f"  access: {tiers}", file=sys.stderr)
    print(f"  with website: {web}", file=sys.stderr)


if __name__ == "__main__":
    main()
