#!/usr/bin/env python3
"""Generate a minimal, release-pinned Xray geodata snapshot for Dirac's RU split.

Input: unmodified runetfreedom/russia-v2ray-rules-dat release/geo{ip,site}.dat
Outputs are never committed as binaries; their checksums are embedded in Dirac.
No Python packages or network access are required.
"""
import argparse
import hashlib
import json
from pathlib import Path

SOURCE_SHA256 = {
    "geoip.dat": "5a403626d9fad0465dcd05932830444245679ade8a6c3874f2ce2752185e78b6",
    "geosite.dat": "76fdbe01687a6cc7683b50c38ceea84941458e8371d215918daf555665a537cd",
}
CATEGORIES = {
    "geoip.dat": {
        "private", "ru", "ru-blocked", "telegram",
    },
    "geosite.dat": {
        "category-ru", "ru-blocked", "ru-available-only-inside",
        "youtube", "discord", "openai", "telegram",
        "google", "twitter", "meta",
    },
}

def sha256(data):
    return hashlib.sha256(data).hexdigest()

def decode_varint(data, offset):
    result = 0
    for i in range(10):
        if offset >= len(data):
            raise ValueError("Truncated varint")
        digit = data[offset]
        offset += 1
        result |= (digit & 0x7f) << (7 * i)
        if not digit & 0x80:
            return result, offset
    raise ValueError("Varint too long")

def protobuf_fields(data):
    pos = 0
    while pos < len(data):
        start = pos
        key, pos = decode_varint(data, pos)
        number, wire = key >> 3, key & 7
        if not number:
            raise ValueError("Invalid field number zero")
        if wire == 2:
            length, pos = decode_varint(data, pos)
            stop = pos + length
            if stop > len(data):
                raise ValueError("Truncated protobuf bytes")
            payload = data[pos:stop]
            pos = stop
        elif wire == 0:
            _, pos = decode_varint(data, pos)
            payload = None
        elif wire in (1, 5):
            stop = pos + (8 if wire == 1 else 4)
            if stop > len(data):
                raise ValueError("Truncated protobuf fixed-width field")
            payload = None
            pos = stop
        else:
            raise ValueError("Unsupported protobuf wire type")
        yield number, wire, payload, data[start:pos]

def category_name(entry):
    names = [payload.decode("ascii").casefold() for number, wire, payload, _ in protobuf_fields(entry)
             if number == 1 and wire == 2]
    if len(names) != 1:
        raise ValueError("Expected exactly one country/category name in geodata entry")
    return names[0]

def trim_one(src_path, dst_path, wanted):
    data = src_path.read_bytes()
    file_name = src_path.name
    actual = sha256(data)
    if actual != SOURCE_SHA256[file_name]:
        raise ValueError("Source SHA256 mismatch for " + file_name)
    found = set()
    kept = []
    count = 0
    for field, wire, entry, encoded in protobuf_fields(data):
        if field != 1 or wire != 2:
            raise ValueError("Unexpected top-level protobuf field")
        count += 1
        name = category_name(entry)
        if name in wanted:
            if name in found:
                raise ValueError("Duplicate required category: " + name)
            kept.append(encoded)
            found.add(name)
    missing = wanted - found
    if missing:
        raise ValueError("Missing required " + file_name + " categories: " + ", ".join(sorted(missing)))
    result = b"".join(kept)
    if not result or len(result) > len(data):
        raise ValueError("Trimmed output size invalid")
    if dst_path.exists():
        raise FileExistsError("Refusing to overwrite existing trimmed geodata: " + str(dst_path))
    dst_path.write_bytes(result)
    print(f"{file_name}: source_entries={count} selected={len(found)} source_bytes={len(data)} trimmed_bytes={len(result)} sha256={sha256(result)}")
    return {"input_sha256": actual, "output_sha256": sha256(result),
            "source_bytes": len(data), "output_bytes": len(result),
            "categories": sorted(found)}

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-dir", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    results = {}
    for name in ("geoip.dat", "geosite.dat"):
        results[name] = trim_one(args.source_dir / name, args.output_dir / name, CATEGORIES[name])
    provenance = {
        "source": "https://github.com/runetfreedom/russia-v2ray-rules-dat",
        "source_branch": "release",
        "files": results,
    }
    (args.output_dir / "dirac-russia-geodata-manifest.json").write_text(
        json.dumps(provenance, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )

if __name__ == "__main__":
    main()