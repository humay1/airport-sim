#!/usr/bin/env python3
"""Validate every content file against its schema.

Content is data (spec/04-data-schemas.md). Invalid content fails the build; it
never silently falls back to a default.

Schema -> content mapping is by directory name:
    data/schemas/aircraft.schema.json  validates  data/aircraft/*.json

Exits 0 when everything valid or when there is simply no content yet.
Exits 1 on any violation.
"""
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SCHEMA_DIR = ROOT / "data" / "schemas"

try:
    import jsonschema
except ImportError:
    jsonschema = None


def content_dir_for(schema_path: Path) -> Path:
    # aircraft.schema.json -> data/aircraft/
    return ROOT / "data" / schema_path.name.replace(".schema.json", "")


def main() -> int:
    if not SCHEMA_DIR.is_dir():
        print("  no data/schemas directory — nothing to validate")
        return 0

    schemas = sorted(SCHEMA_DIR.glob("*.schema.json"))
    if not schemas:
        print("  no schemas yet — nothing to validate")
        return 0

    errors = []
    checked = 0

    for schema_path in schemas:
        # The schema itself must be valid JSON.
        try:
            schema = json.loads(schema_path.read_text())
        except json.JSONDecodeError as e:
            errors.append(f"{schema_path.relative_to(ROOT)}: invalid JSON: {e}")
            continue

        cdir = content_dir_for(schema_path)
        if not cdir.is_dir():
            continue  # no content of this kind yet; fine

        for f in sorted(cdir.rglob("*.json")):
            checked += 1
            try:
                doc = json.loads(f.read_text())
            except json.JSONDecodeError as e:
                errors.append(f"{f.relative_to(ROOT)}: invalid JSON: {e}")
                continue

            if "schema_version" not in doc:
                errors.append(f"{f.relative_to(ROOT)}: missing schema_version")

            # Identifiers must be stable strings, never positional.
            if "id" in doc and not isinstance(doc["id"], str):
                errors.append(f"{f.relative_to(ROOT)}: id must be a string")

            if jsonschema is not None:
                try:
                    jsonschema.validate(doc, schema)
                except jsonschema.ValidationError as e:
                    loc = "/".join(str(p) for p in e.absolute_path) or "(root)"
                    errors.append(f"{f.relative_to(ROOT)}: {loc}: {e.message}")

    if jsonschema is None:
        print("  WARNING: jsonschema not installed — structural checks only.")
        print("  Install with: pip install jsonschema")

    if errors:
        print(f"  {len(errors)} content error(s):")
        for e in errors:
            print(f"    {e}")
        return 1

    print(f"  {checked} content file(s) valid against {len(schemas)} schema(s)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
