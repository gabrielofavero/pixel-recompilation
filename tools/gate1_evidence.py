"""Verify supported-version evidence and selected original-IL patch anchors."""

from __future__ import annotations

import argparse
import json
from pathlib import Path
import sys


REPO = Path(__file__).resolve().parent.parent
ANCHORS = {
    "LowLevel.CLASS_GAMEPAD": (
        ".ctor", "Initialize", "SelectPlayer", "Update",
    ),
    "LowLevel.CLASS_SAVE": (
        "Ark_SavInit", "Ark_SavLoad", "StorageDeviceSelectorCallback",
        "Ark_SavSave", "NameFromPlayer", "DoSaveGame", "DoLoadGame", "DoCreate",
    ),
    "LowLevel.CLASS_SOUND": ("Initialize", "Update"),
    "LowLevel.CLASS_MOD_FADE": ("Initialize",),
    "LowLevel.CLASS_FNT": ("Ark_FntDisplay",),
    "LowLevel.CLASS_SPR": ("Ark_SprDisplay",),
    "Scale.GameScale": (
        ".ctor", "Initialize", "InitState", "EndState", "Update", "Draw",
    ),
    "Scale.INTRO.INTROMain": ("ProcessPage2", "Dispose"),
    "Scale.GAME.GAMEMain": ("LoadNextLevel", "InitLevel", "Update", "HandleInput"),
    "Scale.GAME.Level": (".ctor",),
    "Scale.GAME.GAME_Cat": (".ctor",),
    "Scale.TITLE.TITLEMain": (
        ".ctor", "Initialize", "ProcessStart", "ProcessLoadSave",
        "ProcessSelection", "ProcessConfirm", "ProcessSignIn",
        "ProcessPurchase", "Update",
    ),
    "Scale.ZoomMapContentReader": (".ctor", "Read"),
}

# Content.Load<T> call sites include overloaded constructors, selected by token.
CONTENT_LOAD_TOKENS = {"0x0600015d", "0x0600015f", "0x060001f4", "0x0600022b"}


def read_json(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def check() -> dict:
    version = read_json(REPO / "config" / "supported-versions.json")["packages"][0]
    inventory = read_json(REPO / "generated" / "pixel-inventory.json")
    main = read_json(REPO / "generated" / "pixel-assembly.json")
    companion = read_json(REPO / "generated" / "pixel-companion-assembly.json")
    content = read_json(REPO / "generated" / "pixel-content-readers.json")

    if (inventory["input"]["sha256"] != version["packageSha256"] or
            inventory["input"]["bytes"] != version["packageBytes"] or
            inventory["gameInfo"]["Name"] != version["title"] or
            int(inventory["gameInfo"]["TitleID"]) != int(version["titleId"], 16) or
            inventory["package"]["fileTableBlocks"] != version["fileTableBlocks"] or
            inventory["package"]["dataBlocksVerified"] != version["dataBlocks"] or
            inventory["counts"]["files"] != version["files"]):
        raise ValueError("package inventory differs from supported-version manifest")
    by_path = {row["path"]: row for row in inventory["files"]}
    reports = (
        (main, "assemblyPath", "assemblySha256", "assemblyMvid"),
        (companion, "contentAssemblyPath", "contentAssemblySha256", "contentAssemblyMvid"),
    )
    for report, path_key, hash_key, mvid_key in reports:
        if (report["schemaVersion"] != 2 or
                by_path[version[path_key]]["sha256"] != version[hash_key] or
                report["input"]["sha256"] != version[hash_key] or
                report["assembly"]["mvid"] != version[mvid_key] or
                report["assembly"]["name"] != Path(version[path_key]).stem or
                report["counts"]["methods"] != len(report["methods"]) or
                report["counts"]["typeRefs"] != len(report["typeRefs"]) or
                report["counts"]["memberRefs"] != len(report["memberRefs"])):
            raise ValueError(f"managed report differs from supported version: {path_key}")
    xnb_paths = {row["path"][len("584E07D1/Content/"):] for row in inventory["files"]
                 if row["path"].startswith("584E07D1/Content/") and
                 row["path"].lower().endswith(".xnb")}
    reported_paths = [row["path"] for row in content["files"]]
    reported_readers = {reader["name"] for row in content["files"] for reader in row["readers"]}
    if (content["schemaVersion"] != 1 or
            content["sourcePackageSha256"] != version["packageSha256"] or
            content["decodedXnbCount"] != len(xnb_paths) or
            len(reported_paths) != len(xnb_paths) or
            set(reported_paths) != xnb_paths or
            set(content["readerTypes"]) != reported_readers):
        raise ValueError("content-reader report differs from package inventory")

    result = {"schemaVersion": 1, "assemblies": []}
    for report in (main, companion):
        methods = []
        for type_name, names in ANCHORS.items():
            for name in names:
                matches = [m for m in report["methods"] if m["type"] == type_name and m["name"] == name]
                if not matches:
                    continue
                if len(matches) != 1 or not matches[0]["ilSha256"]:
                    raise ValueError(f"ambiguous or bodyless anchor: {type_name}.{name}")
                method = matches[0]
                methods.append({key: method[key] for key in ("type", "name", "token", "signature", "ilSha256")})
        if report["assembly"]["name"] == "Arkedo Series - 03 PIXEL!":
            for method in report["methods"]:
                if method["token"] in CONTENT_LOAD_TOKENS and not any(m["token"] == method["token"] for m in methods):
                    methods.append({key: method[key] for key in ("type", "name", "token", "signature", "ilSha256")})
        if not methods:
            raise ValueError(f"no anchors for {report['assembly']['name']}")
        result["assemblies"].append({
            "assemblyName": report["assembly"]["name"],
            "assemblySha256": report["input"]["sha256"],
            "assemblyMvid": report["assembly"]["mvid"],
            "methods": methods,
        })
    return result


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--write-anchors", action="store_true")
    args = parser.parse_args()
    try:
        actual = check()
        target = REPO / "config" / "patch-anchors.json"
        if args.write_anchors:
            target.write_text(json.dumps(actual, indent=2) + "\n", encoding="utf-8")
        elif read_json(target) != actual:
            raise ValueError("patch anchors differ from the original IL report")
    except (OSError, ValueError, KeyError, json.JSONDecodeError) as error:
        print(f"GATE1_EVIDENCE_FAIL: {error}", file=sys.stderr)
        return 1
    print(f"GATE1_EVIDENCE_OK anchors={sum(len(a['methods']) for a in actual['assemblies'])}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
