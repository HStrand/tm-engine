"""
Extract card metadata from TM card list HTML file into structured JSON.
Usage: python scripts/extract_cards.py
"""

import json
import re
from pathlib import Path

TAG_CLASS_MAP = {
    "buildingTag": "building",
    "spaceTag": "space",
    "scienceTag": "science",
    "plantTag": "plant",
    "microbeTag": "microbe",
    "animalTag": "animal",
    "powerTag": "power",
    "jovianTag": "jovian",
    "earthTag": "earth",
    "cityTag": "city",
    "eventTag": "event",
    "venusTag": "venus",
    "wildTag": "wild",
}

EXPANSION_MAP = {
    "standard": "base",
    "corporate": "corporate_era",
    "venusNext": "venus_next",
    "prelude": "prelude",
    "prelude2": "prelude2",
    "colonies": "colonies",
    "coloniess": "colonies",  # typo in source HTML
    "turmoil": "turmoil",
    "promo": "promo",
}

# Corporation names are embedded in wildly varied HTML structures (styled spans,
# divs, split across elements, even stored as CSS class names). A definitive map
# is more reliable than trying to parse them all.
CORPORATION_NAMES = {
    "CORP01": "Credicor",
    "CORP02": "Ecoline",
    "CORP03": "Helion",
    "CORP04": "Mining Guild",
    "CORP05": "Interplanetary Cinematics",
    "CORP06": "Inventrix",
    "CORP07": "Phobolog",
    "CORP08": "Tharsis Republic",
    "CORP09": "Thorgate",
    "CORP10": "United Nations Mars Initiative",
    "CORP11": "Teractor",
    "CORP12": "Saturn Systems",
    "CORP13": "Aphrodite",
    "CORP14": "Celestic",
    "CORP15": "Manutech",
    "CORP16": "Morning Star Inc.",
    "CORP17": "Viron",
    "CORP18": "Cheung Shing Mars",
    "CORP19": "Point Luna",
    "CORP20": "Robinson Industries",
    "CORP21": "Valley Trust",
    "CORP22": "Vitor",
    "CORP23": "Aridor",
    "CORP24": "Arklight",
    "CORP25": "Polyphemos",
    "CORP26": "Poseidon",
    "CORP27": "Stormcraft Incorporated",
    "CORP28": "Lakefront Resorts",
    "CORP29": "Pristar",
    "CORP30": "Septem Tribus",
    "CORP31": "Terralabs Research",
    "CORP32": "Utopia Invest",
    "CORP33": "Factorum",
    "CORP34": "Mons Insurance",
    "CORP35": "Philares",
    "CORP36": "Arcadian Communities",
    "CORP37": "Recyclon",
    "CORP38": "Splice",
    "CORP39": "Astrodrill",
    "CORP40": "Pharmacy Union",
    "CORP41": "Ecotec",
    "CORP42": "Tycho Magnetics",
    "CORP43": "Kuiper Cooperative",
    "CORP44": "Spire",
    "CORP45": "Sagitta",
    "CORP46": "Palladin Shipping",
    "CORP47": "Nirgal Enterprises",
}


def clean_text(text):
    """Strip HTML tags and normalize whitespace."""
    text = re.sub(r"<[^>]+>", " ", text)
    text = re.sub(r"&nbsp;", " ", text)
    text = re.sub(r"&amp;", "&", text)
    text = re.sub(r"&#?\w+;", "", text)
    text = re.sub(r"\s+", " ", text).strip()
    return text


def clean_description(desc):
    """Clean up description text: strip parens, normalize."""
    if not desc:
        return desc
    # Strip wrapping parens from each parenthesized segment
    desc = re.sub(r"\(([^)]+)\)", r"\1", desc)
    desc = re.sub(r"\s+", " ", desc).strip()
    # Remove trailing period-space artifacts
    desc = desc.strip(". ")
    return desc


def extract_text(html, css_class):
    """Extract text content from the first div with the given class."""
    # Match div with the class (class may contain additional classes)
    pattern = rf'<div\s+class="[^"]*\b{re.escape(css_class)}\b[^"]*"[^>]*>(.*?)</div>'
    m = re.search(pattern, html, re.DOTALL)
    if m:
        return clean_text(m.group(1))
    return None


def extract_all_text(html, css_class):
    """Extract text from ALL divs with the given class, concatenated."""
    pattern = rf'<div\s+class="[^"]*\b{re.escape(css_class)}\b[^"]*"[^>]*>(.*?)</div>'
    matches = re.findall(pattern, html, re.DOTALL)
    return " ".join(clean_text(m) for m in matches if clean_text(m))


def extract_tags_from_classes(classes):
    """Extract tag names from CSS class list."""
    tags = []
    for css_class, tag_name in TAG_CLASS_MAP.items():
        if css_class in classes:
            tags.append(tag_name)
    return tags


def extract_expansion(classes):
    """Extract expansion from CSS class list."""
    for css_class, expansion in EXPANSION_MAP.items():
        if css_class in classes:
            return expansion
    return None


def extract_data_attrs(li_tag_html):
    """Extract data-* attributes from the opening <li> tag."""
    attrs = {}
    for m in re.finditer(r'data-(\w+)="([^"]*)"', li_tag_html):
        key, val = m.group(1), m.group(2)
        if key == "minintegerlen":
            continue
        try:
            attrs[key] = int(val)
        except ValueError:
            attrs[key] = val
    return attrs


def split_li_elements(html_section):
    """Split an HTML section into individual <li>...</li> blocks."""
    # Find all <li ...>...</li> pairs, handling nested elements
    items = []
    # Use a simple approach: find each <li and match to its closing </li>
    parts = re.split(r"(<li\b)", html_section)
    for i, part in enumerate(parts):
        if part == "<li":
            # Reconstruct from <li onwards until </li>
            rest = parts[i + 1] if i + 1 < len(parts) else ""
            li_html = "<li" + rest
            # Find the closing </li>
            end = li_html.find("</li>")
            if end != -1:
                li_html = li_html[: end + 5]
            items.append(li_html)
    return items


def parse_project_card(li_html):
    """Parse a project card <li> element."""
    card = {}

    # Extract opening tag for classes and data attrs
    opening_match = re.match(r"<li\b([^>]*)>", li_html, re.DOTALL)
    if not opening_match:
        return None
    opening_attrs = opening_match.group(1)

    classes = re.findall(r'class="([^"]*)"', opening_attrs)
    class_str = classes[0] if classes else ""
    class_list = class_str.split()

    if "filterDiv" not in class_list:
        return None

    # Card type
    if "automated" in class_list:
        card["type"] = "automated"
    elif "active" in class_list:
        card["type"] = "active"
    elif "events" in class_list:
        card["type"] = "event"
    else:
        return None

    # Name from title div
    name = extract_text(li_html, "title")
    if name:
        card["name"] = name

    # Cost from price div
    price = extract_text(li_html, "price")
    if price:
        try:
            card["cost"] = int(price)
        except ValueError:
            card["cost_text"] = price

    # Card number
    number = extract_text(li_html, "number")
    if number:
        card["number"] = number.strip("#")

    # Tags
    tags = extract_tags_from_classes(class_list)
    # Also check for tag divs to get proper ordering
    tag_divs = re.findall(r'class="tag\s+tag(\d)\s+tag-(\w+)"', li_html)
    if tag_divs:
        ordered_tags = [t[1] for t in sorted(tag_divs, key=lambda x: int(x[0]))]
        card["tags"] = ordered_tags
    elif tags:
        card["tags"] = tags

    # Expansion
    expansion = extract_expansion(class_list)
    if expansion:
        card["expansion"] = expansion

    # Requirements
    req_text = extract_text(li_html, "requirements")
    if req_text:
        card["requirement"] = req_text
        # Check if it's a max requirement
        if re.search(r'class="[^"]*requirements-max[^"]*"', li_html):
            card["requirement_is_max"] = True

    # Data attributes (requirement thresholds)
    data_attrs = extract_data_attrs(opening_attrs)
    for key, val in data_attrs.items():
        card[f"requirement_{key}"] = val

    # Victory points
    vp = extract_text(li_html, "points")
    if vp is None:
        vp = extract_text(li_html, "points-big")
    if vp:
        try:
            card["victory_points"] = int(vp)
        except ValueError:
            card["victory_points"] = vp

    # Description
    desc = extract_all_text(li_html, "description")
    if desc:
        card["description"] = clean_description(desc)

    return card


def parse_corporation(li_html):
    """Parse a corporation <li> element."""
    card = {}

    opening_match = re.match(r"<li\b([^>]*)>", li_html, re.DOTALL)
    if not opening_match:
        return None
    opening_attrs = opening_match.group(1)

    classes = re.findall(r'class="([^"]*)"', opening_attrs)
    class_str = classes[0] if classes else ""
    class_list = class_str.split()

    if "corporation" not in class_list:
        return None

    card["type"] = "corporation"

    # ID as number
    id_match = re.search(r'id="([^"]*)"', opening_attrs)
    if id_match:
        corp_id = id_match.group(1).strip("#")
        card["number"] = corp_id
        # Use definitive name map
        if corp_id in CORPORATION_NAMES:
            card["name"] = CORPORATION_NAMES[corp_id]

    # Tags
    tag_divs = re.findall(r'class="tag\s+tag(\d)\s+tag-(\w+)"', li_html)
    if tag_divs:
        ordered_tags = [t[1] for t in sorted(tag_divs, key=lambda x: int(x[0]))]
        card["tags"] = ordered_tags

    # Expansion
    expansion = extract_expansion(class_list)
    if expansion:
        card["expansion"] = expansion

    # For corporations, extract descriptions from OUTSIDE the effect box only.
    # The effect box contains its own descriptions we don't want.
    content_match = re.search(
        r'<div\s+class="contentCorporation">(.*)',
        li_html, re.DOTALL
    )
    if content_match:
        content = content_match.group(1)
        # Find end of corporationEffectBox — it's a nested div, so find the
        # closing pattern after the effect box label
        effect_box_match = re.search(
            r'<div\s+class="corporationEffectBox"[^>]*>.*?</div>\s*</div>',
            content, re.DOTALL
        )
        if effect_box_match:
            after_effect = content[effect_box_match.end():]
        else:
            after_effect = content

        desc_matches = re.findall(
            r'<div\s+class="description"[^>]*>(.*?)</div>',
            after_effect, re.DOTALL
        )
        if desc_matches:
            desc = " ".join(clean_text(d) for d in desc_matches if clean_text(d))
            if desc:
                card["description"] = clean_description(desc)

    return card


def parse_prelude(li_html):
    """Parse a prelude <li> element."""
    card = {}

    opening_match = re.match(r"<li\b([^>]*)>", li_html, re.DOTALL)
    if not opening_match:
        return None
    opening_attrs = opening_match.group(1)

    classes = re.findall(r'class="([^"]*)"', opening_attrs)
    class_str = classes[0] if classes else ""
    class_list = class_str.split()

    if "prelude-card" not in class_list:
        return None

    card["type"] = "prelude"

    # Name
    name = extract_text(li_html, "title")
    if name:
        card["name"] = name

    # Number
    number = extract_text(li_html, "number")
    if number:
        card["number"] = number.strip("#")

    # Tags
    tag_divs = re.findall(r'class="tag\s+tag(\d)\s+tag-(\w+)"', li_html)
    if tag_divs:
        ordered_tags = [t[1] for t in sorted(tag_divs, key=lambda x: int(x[0]))]
        card["tags"] = ordered_tags

    # Expansion (preludes are always from prelude expansion)
    card["expansion"] = "prelude"

    # Description
    desc = extract_all_text(li_html, "description")
    if desc:
        desc = re.sub(r"^\((.+)\)$", r"\1", desc)
        card["description"] = desc

    return card


def main():
    html_path = Path(__file__).parent.parent / "project resources" / "card metadata" / "TM card list.html"
    output_path = Path(__file__).parent.parent / "project resources" / "card metadata" / "cards.json"

    html_content = html_path.read_text(encoding="utf-8")

    # Split HTML into sections
    sections = re.split(r"<!--(PROJECT CARDS|CORPORATIONS|PRELUDES|COLONIES|GLOBAL EVENTS)-->", html_content)

    project_html = ""
    corp_html = ""
    prelude_html = ""
    for i, section in enumerate(sections):
        if section == "PROJECT CARDS" and i + 1 < len(sections):
            project_html = sections[i + 1]
        elif section == "CORPORATIONS" and i + 1 < len(sections):
            corp_html = sections[i + 1]
        elif section == "PRELUDES" and i + 1 < len(sections):
            prelude_html = sections[i + 1]

    # Parse project cards
    project_cards = []
    for li_html in split_li_elements(project_html):
        card = parse_project_card(li_html)
        if card:
            project_cards.append(card)

    # Parse corporations
    corporations = []
    for li_html in split_li_elements(corp_html):
        card = parse_corporation(li_html)
        if card:
            corporations.append(card)

    # Parse preludes
    preludes = []
    for li_html in split_li_elements(prelude_html):
        card = parse_prelude(li_html)
        if card:
            preludes.append(card)

    result = {
        "project_cards": project_cards,
        "corporations": corporations,
        "preludes": preludes,
    }

    # Summary
    print(f"Extracted {len(project_cards)} project cards")
    print(f"Extracted {len(corporations)} corporations")
    print(f"Extracted {len(preludes)} preludes")
    print(f"Total: {len(project_cards) + len(corporations) + len(preludes)} cards")

    # Quality check
    for section_name, cards in [("project_cards", project_cards), ("corporations", corporations), ("preludes", preludes)]:
        with_name = sum(1 for c in cards if "name" in c)
        print(f"  {section_name}: {with_name}/{len(cards)} have names")

    output_path.write_text(json.dumps(result, indent=2, ensure_ascii=False), encoding="utf-8")
    print(f"\nWritten to {output_path}")


if __name__ == "__main__":
    main()
