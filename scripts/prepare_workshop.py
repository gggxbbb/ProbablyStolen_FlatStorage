import argparse
import hashlib
from pathlib import Path

from PIL import Image, ImageOps
import vdf


def prepare(root: Path, content: Path, output: Path, version: str) -> None:
    source = root / "preview.png"
    source_hash = hashlib.sha256(source.read_bytes()).hexdigest()
    output.mkdir(parents=True, exist_ok=True)
    preview = output / "preview.jpg"
    with Image.open(source) as original:
        original.load()
        picture = ImageOps.exif_transpose(original).convert("RGBA")
        picture.thumbnail((1024, 1024), Image.Resampling.LANCZOS)
        background = Image.new("RGB", picture.size, "white")
        background.paste(picture, mask=picture.getchannel("A"))
        background.save(preview, format="JPEG", quality=90, optimize=True)
    if preview.stat().st_size >= 1_000_000:
        raise ValueError("Workshop preview must be smaller than 1,000,000 bytes")
    with Image.open(preview) as image:
        image.verify()
    if hashlib.sha256(source.read_bytes()).hexdigest() != source_hash:
        raise ValueError("The original preview image changed during packaging")

    with (root / "Workshop" / "upload.vdf").open(encoding="utf-8") as handle:
        document = vdf.load(handle, escaped=True)
    item = document["workshopitem"]
    if item["appid"] != "4348910" or item["publishedfileid"] != "3833606510":
        raise ValueError("Workshop application or item ID is invalid")
    descriptions = [
        (root / "Workshop" / f"description.{language}.txt").read_text(encoding="utf-8").strip()
        for language in ("zh", "en")
    ]
    if any(not description or version not in description for description in descriptions):
        raise ValueError("Workshop descriptions must include the release version")
    item["contentfolder"] = content.resolve().as_posix()
    item["previewfile"] = preview.resolve().as_posix()
    item["description"] = "\n\n".join(descriptions)
    item["changenote"] = f"Flat Storage {version}"
    if not content.is_dir() or not (content / "ProbablyStolen_FlatStorage.dll").is_file():
        raise ValueError("The release content directory is incomplete")
    target = output / "upload.vdf"
    with target.open("w", encoding="utf-8", newline="\n") as handle:
        vdf.dump(document, handle, pretty=True, escaped=True)
    with target.open(encoding="utf-8") as handle:
        if vdf.load(handle, escaped=True) != document:
            raise ValueError("Workshop VDF round-trip verification failed")
    print(f"Preview verified: {preview.stat().st_size} bytes; original SHA256: {source_hash}")
    print(f"Workshop VDF verified: {target}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", required=True, type=Path)
    parser.add_argument("--content", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--version", required=True)
    arguments = parser.parse_args()
    prepare(arguments.root.resolve(), arguments.content.resolve(), arguments.output.resolve(), arguments.version)
