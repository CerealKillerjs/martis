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
        "albedo_name": "CTX mosaic of the Mars human exploration zones",
        "ctx": True,
    },
    # The story-mode starting section (M3, ADR-036): Jezero, Nili Fossae, NE Syrtis and the
    # western plains of Isidis. At ~195 m per sample the base DEM is already the right detail, so
    # no CTX DTMs; the Jezero region above is blended in as a high-detail insert by the game.
    "isidis_west": {
        "name": "Isidis occidental",
        "lat": 19.0,
        "lon": 78.5,
        "size_km": 400.0,
        "height_px": 2049,
        "albedo_px": 4096,
        "albedo": "https://asc-pds-services.s3.us-west-2.amazonaws.com/mosaic/Mars/Mars_MRO_CTX_Equi_Mosaics_Robbins/100mpp/MC13_100mpp.16bit.tif",
        "albedo_name": "Global CTX mosaic, MC-13 Syrtis Major, 100 m (Robbins et al.)",
        "albedo_unsigned": True,  # stored as int16 but holds unsigned 16-bit brightness
        "ctx": False,
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


def warp_unsigned(source: str, transform, pixels: int, bounds, gap_below: float = 30000.0) -> np.ndarray:
    """Warps a mosaic whose int16 samples are really unsigned; gaps (dark stripes) are filled."""
    from rasterio.warp import transform_bounds
    from rasterio.windows import from_bounds

    out = np.full((pixels, pixels), np.nan, dtype="float32")
    with rasterio.open(source) as src:
        left, bottom, right, top = transform_bounds(GEOGRAPHIC, src.crs, *bounds, densify_pts=21)
        pad = 2000.0
        window = from_bounds(left - pad, bottom - pad, right + pad, top + pad, src.transform).round_offsets().round_lengths()
        data = src.read(1, window=window).view(np.uint16).astype("float32")
        data[(data < gap_below) | (data == 32768)] = np.nan  # int16 nodata (-32768) reads as 32768
        reproject(
            source=data,
            destination=out,
            src_transform=src.window_transform(window),
            src_crs=src.crs,
            src_nodata=np.nan,
            dst_transform=transform,
            dst_crs=GEOGRAPHIC,
            dst_nodata=np.nan,
            resampling=Resampling.average,
            num_threads=4,
        )
    return fill_gaps(out)


def fill_gaps(image: np.ndarray) -> np.ndarray:
    """Fills NaN holes from ever coarser averages of the valid pixels (a cheap pyramid fill)."""
    image = image.copy()
    level = image
    factor = 1
    while np.isnan(image).any() and level.shape[0] >= 2:
        n = level.shape[0] // 2 * 2
        blocks = level[:n, :n].reshape(n // 2, 2, n // 2, 2)
        with np.errstate(invalid="ignore"), __import__("warnings").catch_warnings():
            __import__("warnings").simplefilter("ignore", RuntimeWarning)
            level = np.nanmean(blocks, axis=(1, 3))
        factor *= 2
        up = np.repeat(np.repeat(level, factor, axis=0), factor, axis=1)
        up = np.pad(up, ((0, max(0, image.shape[0] - up.shape[0])), (0, max(0, image.shape[1] - up.shape[1]))), mode="edge")[: image.shape[0], : image.shape[1]]
        holes = np.isnan(image) & ~np.isnan(up)
        image[holes] = up[holes]
    return np.nan_to_num(image, nan=0.0)


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
            "source": [{"citation": f"{base['name']} ({base['by']}) {" + CTX DTMs (USGS, CC0)" if meta["ctx_strips"] else ""}", "url": base["url"], "accessed": meta["date"], "where": where}],
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
        strips = ctx_strips(bounds)[: args.max_strips] if region.get("ctx", True) else []
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
        if region.get("albedo_unsigned"):
            raw = warp_unsigned("/vsicurl/" + region["albedo"], albedo_transform, region["albedo_px"], bounds)
        else:
            raw = warp("/vsicurl/" + region["albedo"], albedo_transform, region["albedo_px"], 0, Resampling.average)
        valid = np.isfinite(raw) & (raw > 0)
        if valid.any() and raw[valid].max() > 255:  # a 16-bit mosaic: stretch to 8 bits
            low_a, high_a = np.percentile(raw[valid], [0.5, 99.5])
            raw = np.where(valid, 1 + (raw - low_a) / max(high_a - low_a, 1e-6) * 254, 0)
        albedo = np.where(valid, np.clip(raw, 1, 255), 0).astype(np.uint8)

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
        ] + ([{"name": f"MRO CTX controlled DTMs ({len(used)} strips)", "by": "USGS Astrogeology", "license": "CC0-1.0", "url": CTX_INDEX}] if used else []) + [
            {"name": region["albedo_name"], "by": "USGS Astrogeology / NASA-JPL-MSSS", "license": "public domain", "url": region["albedo"]},
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
