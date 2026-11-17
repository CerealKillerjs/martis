#!/usr/bin/env python3
"""Builds the real relief of a MARTIS region from public Mars data (M1, ADR-034).

Sources (all public, read over HTTP range requests, nothing is redistributed raw):
  * Mars HRSC-MOLA blended DEM, global, 200 m/px (USGS Astrogeology, public domain).
  * MRO CTX DTMs, ~20 m/px (USGS Astrogeology analysis-ready data, CC0-1.0), blended over
    the base where they exist.
  * MRO CTX image mosaic of the Mars human exploration zones, ~5 m/px (USGS, public domain),
    as the ground albedo.

Output (in --out): height.png (16-bit grey, metres between the region's min and max),
albedo.jpg (8-bit grey) and region.json (grid, bounds, sources). The game reads them through
data/planets/mars.v1.json.

Usage:
  pip install rasterio pandas pyarrow pillow
  python3 tools/planet/build_region.py --region jezero --out data/planets/mars/jezero
"""

from __future__ import annotations

import argparse
import io
import json
import math
import os
import sys
import urllib.request

import numpy as np
import rasterio
from rasterio.crs import CRS
from rasterio.transform import from_origin
from rasterio.warp import Resampling, reproject

MARS_RADIUS_M = 3396190.0
BASE_DEM = "https://asc-pds-services.s3.us-west-2.amazonaws.com/mosaic/Mars/HRSC_MOLA_Blend/Mars_HRSC_MOLA_BlendDEM_Global_200mp_v2.tif"
CTX_INDEX = "https://astrogeo-ard.s3.us-west-2.amazonaws.com/collectionindices/mro_ctx_controlled_usgs_dtms.parquet"

REGIONS = {
    # Jezero crater: the river delta Perseverance explores. Centre of the crater floor.
    "jezero": {
        "name": "Cráter Jezero",
        "lat": 18.40,
        "lon": 77.55,
        "size_km": 96.0,
        "height_px": 2049,
        "albedo_px": 4096,
        "albedo": "https://asc-pds-services.s3.us-west-2.amazonaws.com/mosaic/Mars/CTX_EZs/Jezero_CTX_BlockAdj_dd.tif",
    },
}

GEOGRAPHIC = CRS.from_wkt(
    'GEOGCS["GCS_Mars_2000_Sphere",DATUM["Mars_2000_(Sphere)",SPHEROID["Mars_2000_Sphere_IAU_IAG",3396190,0]],'
    'PRIMEM["Reference_Meridian",0],UNIT["degree",0.0174532925199433]]'
)


def grid(region: dict, pixels: int):
    """A lat/lon grid whose pixels are square on the ground at the region's latitude."""
    half_lat = math.degrees((region["size_km"] * 500.0) / MARS_RADIUS_M)
    half_lon = half_lat / math.cos(math.radians(region["lat"]))
    west, north = region["lon"] - half_lon, region["lat"] + half_lat
    transform = from_origin(west, north, (2 * half_lon) / pixels, (2 * half_lat) / pixels)
    bounds = (west, region["lat"] - half_lat, region["lon"] + half_lon, north)
    return transform, bounds


def warp(source: str, transform, pixels: int, nodata: float, resampling=Resampling.bilinear, dtype="float32") -> np.ndarray:
    out = np.full((pixels, pixels), np.nan if dtype == "float32" else 0, dtype=dtype)
    with rasterio.open(source) as src:
        reproject(
            source=rasterio.band(src, 1),
            destination=out,
            src_nodata=src.nodata if src.nodata is not None else nodata,
            dst_transform=transform,
            dst_crs=GEOGRAPHIC,
            dst_nodata=np.nan if dtype == "float32" else 0,
            resampling=resampling,
            num_threads=4,
        )
    return out


def ctx_strips(bounds) -> list[tuple[str, str]]:
    import pandas as pd

    cache = os.path.join(os.environ.get("TMPDIR", "/tmp"), "martis_ctx_index.parquet")
    if not os.path.exists(cache):
        print("downloading the CTX DTM index…", file=sys.stderr)
        urllib.request.urlretrieve(CTX_INDEX, cache)
    frame = pd.read_parquet(cache, columns=["id", "bbox", "assets"])
    west, south, east, north = bounds
    strips = []
    for _, row in frame.iterrows():
        b = row["bbox"]
        if b["xmax"] - b["xmin"] > 90:  # strips across the antimeridian carry a useless box
            continue
        if b["xmin"] < east and b["xmax"] > west and b["ymin"] < north and b["ymax"] > south:
            strips.append((row["id"], row["assets"]["dtm"]["href"].replace("s3-us-west-2", "s3.us-west-2")))
    return strips


def feather(valid: np.ndarray, width: int) -> np.ndarray:
    """0 at the edge of the valid area, rising to 1 within `width` pixels (cheap chamfer)."""
    weight = valid.astype(np.float32)
    current = weight.copy()
    for _ in range(width):
        shrunk = current.copy()
        shrunk[1:, :] = np.minimum(shrunk[1:, :], current[:-1, :])
        shrunk[:-1, :] = np.minimum(shrunk[:-1, :], current[1:, :])
        shrunk[:, 1:] = np.minimum(shrunk[:, 1:], current[:, :-1])
        shrunk[:, :-1] = np.minimum(shrunk[:, :-1], current[:, 1:])
        weight += shrunk
        current = shrunk
    return weight / (width + 1)


def parameters(meta: dict) -> list[dict]:
    """The region's numbers as a classified parameter catalogue (every data file is one)."""
    base = meta["sources"][0]

    def measured(pid: str, field: str, value: float, unit: str, where: str) -> dict:
        return {
            "id": f"{meta['id']}.{pid}", "field": field, "value": value, "unit": unit,
            "classification": "SCIENTIFIC_MEASURED", "scope": f"regional:{meta['id']}", "version": "1", "status": "OK",
            "source": [{"citation": f"{base['name']} ({base['by']}) + CTX DTMs (USGS, CC0)", "url": base["url"], "accessed": meta["date"], "where": where}],
        }

    return [
        measured("height_min", "min_m", meta["height"]["min_m"], "m", "lowest sample of the blended relief over the region"),
        measured("height_max", "max_m", meta["height"]["max_m"], "m", "highest sample of the blended relief over the region"),
        {
            "id": f"{meta['id']}.size", "field": "size_m", "value": meta["size_m"], "unit": "m",
            "classification": "GAME_BALANCE", "scope": "model", "version": "1", "status": "OK",
            "note": "Lado del cuadrado de la región jugable, centrado en el punto elegido.",
        },
    ]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--region", required=True, choices=sorted(REGIONS))
    parser.add_argument("--out", required=True)
    parser.add_argument("--max-strips", type=int, default=80)
    args = parser.parse_args()

    region = REGIONS[args.region]
    os.makedirs(args.out, exist_ok=True)
    n = region["height_px"]
    transform, bounds = grid(region, n)

    with rasterio.Env(GDAL_DISABLE_READDIR_ON_OPEN="EMPTY_DIR", GDAL_HTTP_MULTIRANGE="YES", GDAL_HTTP_MERGE_CONSECUTIVE_RANGES="YES", VSI_CACHE="TRUE"):
        print("base relief (HRSC-MOLA 200 m)…", file=sys.stderr)
        base = warp("/vsicurl/" + BASE_DEM, transform, n, -32768, Resampling.cubic)
        if np.isnan(base).any():
            raise SystemExit("the base DEM has holes in this region")

        total = np.zeros_like(base)
        weights = np.zeros_like(base)
        strips = ctx_strips(bounds)[: args.max_strips]
        used = []
        for i, (strip_id, href) in enumerate(strips, 1):
            print(f"CTX {i}/{len(strips)} {strip_id}", file=sys.stderr)
            try:
                dtm = warp("/vsicurl/" + href, transform, n, -32767)
            except rasterio.errors.RasterioIOError as error:
                print(f"  skipped: {error}", file=sys.stderr)
                continue
            valid = ~np.isnan(dtm)
            if valid.sum() < 1000:
                continue
            # Each strip is aligned to MOLA, but a residual vertical offset remains: remove it.
            offset = np.median(dtm[valid] - base[valid])
            if abs(offset) > 150:
                print(f"  skipped: offset {offset:.0f} m", file=sys.stderr)
                continue
            w = feather(valid, 24)
            total[valid] += (dtm[valid] - offset) * w[valid]
            weights += w
            used.append(strip_id)

        detail = np.where(weights > 0, total / np.maximum(weights, 1e-6), base)
        blend = np.clip(weights, 0.0, 1.0)
        height = base * (1 - blend) + detail * blend

        print("albedo (CTX mosaic)…", file=sys.stderr)
        albedo_transform, _ = grid(region, region["albedo_px"])
        albedo = warp("/vsicurl/" + region["albedo"], albedo_transform, region["albedo_px"], 0, Resampling.average, "uint8")

    low, high = float(height.min()), float(height.max())
    scaled = np.round((height - low) / (high - low) * 65535.0).astype(np.uint16)
    from PIL import Image

    Image.fromarray(scaled).save(os.path.join(args.out, "height.png"), optimize=True)
    filled = albedo.copy()
    if (filled == 0).any():  # outside the mosaic: the mean albedo
        filled[filled == 0] = int(albedo[albedo > 0].mean()) if (albedo > 0).any() else 120
    Image.fromarray(filled).save(os.path.join(args.out, "albedo.jpg"), quality=86, optimize=True)

    spacing = region["size_km"] * 1000.0 / (n - 1)
    meta = {
        "schema": "martis.region",
        "id": args.region,
        "name": region["name"],
        "date": __import__("datetime").date.today().isoformat(),
        "center": {"lat": region["lat"], "lon": region["lon"]},
        "size_m": region["size_km"] * 1000.0,
        "height": {"file": "height.png", "pixels": n, "spacing_m": spacing, "min_m": low, "max_m": high},
        "albedo": {"file": "albedo.jpg", "pixels": region["albedo_px"]},
        "bounds_deg": {"west": bounds[0], "south": bounds[1], "east": bounds[2], "north": bounds[3]},
        "ctx_coverage": float((weights > 0).mean()),
        "sources": [
            {"name": "Mars HRSC-MOLA Blended DEM Global 200 m v2", "by": "USGS Astrogeology / ESA-DLR-FU Berlin / NASA", "license": "public domain", "url": BASE_DEM},
            {"name": f"MRO CTX controlled DTMs ({len(used)} strips)", "by": "USGS Astrogeology", "license": "CC0-1.0", "url": CTX_INDEX},
            {"name": "CTX mosaic of the Mars human exploration zones", "by": "USGS Astrogeology / NASA-JPL-MSSS", "license": "public domain", "url": region["albedo"]},
        ],
        "ctx_strips": used,
    }
    meta["parameters"] = parameters(meta)
    with open(os.path.join(args.out, "region.json"), "w", encoding="utf-8") as handle:
        json.dump(meta, handle, ensure_ascii=False, indent=2)
        handle.write("\n")
    print(f"done: {low:.0f}…{high:.0f} m, CTX coverage {meta['ctx_coverage']:.0%}, {len(used)} strips", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
