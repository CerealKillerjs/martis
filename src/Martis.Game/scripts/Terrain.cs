using System;
using Godot;

namespace Martis.Game;

/// <summary>
/// Procedural relief for the simulation grid (ADR-017; 64, 128 or 256 cells per side since H3,
/// ADR-021). The relief is visual only:
/// the simulation keeps its own per-cell environment. Dunes, mesas and craters come from
/// the world seed, so the same save always shows the same landscape.
/// </summary>
public sealed class Terrain
{
    public const float CellSize = 2f;
    /// <summary>Vertices per side of one mesh chunk (H3): small enough for frustum culling.</summary>
    public const int ChunkVerts = 129;

    private readonly float[] _heights;

    /// <summary>Vertical exaggeration of real relief in the scaled-down region (M1, ADR-034).</summary>
    public const float ReliefExaggeration = 4f;

    public Terrain(int cellsX, int cellsY, ulong seed, Martis.Simulation.Planets.RegionRelief? relief = null)
    {
        CellsX = cellsX;
        CellsY = cellsY;

        // Vertices per cell edge: 4 up to 128×128 (513² vertices), 2 on 256×256 so the
        // terrain stays at the same vertex budget (H3).
        Resolution = Math.Max(cellsX, cellsY) <= 128 ? 4 : 2;
        SizeX = cellsX * CellSize;
        SizeZ = cellsY * CellSize;
        VertsX = (cellsX * Resolution) + 1;
        VertsZ = (cellsY * Resolution) + 1;
        _heights = new float[VertsX * VertsZ];
        Relief = relief;
        if (relief is null)
        {
            Generate(seed);
        }
        else
        {
            FromRelief(relief, seed);
        }
    }

    /// <summary>The real relief this terrain shows, if any (M1).</summary>
    public Martis.Simulation.Planets.RegionRelief? Relief { get; }

    /// <summary>Metres of the real region per world unit (1 without real relief).</summary>
    public float MetresPerUnit => Relief is null ? 1f : (float)(Relief.Definition.SizeM / SizeX);

    public int CellsX { get; }

    public int Resolution { get; }

    public int CellsY { get; }

    public float SizeX { get; }

    public float SizeZ { get; }

    public int VertsX { get; }

    public int VertsZ { get; }

    /// <summary>World position of the centre of a cell, on the ground.</summary>
    public Vector3 CellCenter(int x, int y)
    {
        var wx = ((x + 0.5f) * CellSize) - (SizeX / 2f);
        var wz = ((y + 0.5f) * CellSize) - (SizeZ / 2f);
        return new Vector3(wx, HeightAt(wx, wz), wz);
    }

    /// <summary>The cell under a world position, or false outside the map.</summary>
    public bool CellAt(Vector3 world, out int x, out int y)
    {
        x = (int)MathF.Floor((world.X + (SizeX / 2f)) / CellSize);
        y = (int)MathF.Floor((world.Z + (SizeZ / 2f)) / CellSize);
        return x >= 0 && y >= 0 && x < CellsX && y < CellsY;
    }

    /// <summary>Bilinear ground height at a world position (clamped to the map).</summary>
    public float HeightAt(float wx, float wz)
    {
        var step = CellSize / Resolution;
        var fx = Math.Clamp((wx + (SizeX / 2f)) / step, 0f, VertsX - 1.001f);
        var fz = Math.Clamp((wz + (SizeZ / 2f)) / step, 0f, VertsZ - 1.001f);
        var ix = (int)fx;
        var iz = (int)fz;
        var tx = fx - ix;
        var tz = fz - iz;
        var a = _heights[(iz * VertsX) + ix];
        var b = _heights[(iz * VertsX) + ix + 1];
        var c = _heights[((iz + 1) * VertsX) + ix];
        var d = _heights[((iz + 1) * VertsX) + ix + 1];
        return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), tz);
    }

    /// <summary>First hit of a ray with the ground (ray march, then bisection).</summary>
    public bool Raycast(Vector3 origin, Vector3 direction, out Vector3 hit)
    {
        hit = default;
        direction = direction.Normalized();
        var previous = origin;
        const float step = 0.4f;
        var reach = 900f + MathF.Max(SizeX, SizeZ);
        for (var t = step; t < reach; t += step)
        {
            var point = origin + (direction * t);
            if (point.Y < HeightAt(point.X, point.Z))
            {
                var low = previous;
                var high = point;
                for (var i = 0; i < 12; i++)
                {
                    var mid = (low + high) / 2f;
                    if (mid.Y < HeightAt(mid.X, mid.Z))
                    {
                        high = mid;
                    }
                    else
                    {
                        low = mid;
                    }
                }

                hit = (low + high) / 2f;
                return CellAt(hit, out _, out _);
            }

            previous = point;
            if (point.Y < -50f)
            {
                break;
            }
        }

        return false;
    }

    /// <summary>The terrain in chunks of at most <see cref="ChunkVerts"/>² vertices that share edges (H3).</summary>
    public System.Collections.Generic.IEnumerable<ArrayMesh> BuildChunks()
    {
        for (var z0 = 0; z0 < VertsZ - 1; z0 += ChunkVerts - 1)
        {
            for (var x0 = 0; x0 < VertsX - 1; x0 += ChunkVerts - 1)
            {
                yield return BuildMesh(x0, z0, Math.Min(x0 + ChunkVerts, VertsX), Math.Min(z0 + ChunkVerts, VertsZ));
            }
        }
    }

    public ArrayMesh BuildMesh() => BuildMesh(0, 0, VertsX, VertsZ);

    /// <summary>Mesh of the vertices [x0, x1) × [z0, z1); UVs are those of the whole map.</summary>
    public ArrayMesh BuildMesh(int x0, int z0, int x1, int z1)
    {
        var step = CellSize / Resolution;
        var width = x1 - x0;
        var depth = z1 - z0;
        var count = width * depth;
        var vertices = new Vector3[count];
        var normals = new Vector3[count];
        var uvs = new Vector2[count];
        var tangents = new float[count * 4];
        for (var z = z0; z < z1; z++)
        {
            for (var x = x0; x < x1; x++)
            {
                var i = ((z - z0) * width) + (x - x0);
                var h = (z * VertsX) + x;
                vertices[i] = new Vector3((x * step) - (SizeX / 2f), _heights[h], (z * step) - (SizeZ / 2f));
                uvs[i] = new Vector2(x / (float)(VertsX - 1), z / (float)(VertsZ - 1));
                var left = _heights[(z * VertsX) + Math.Max(x - 1, 0)];
                var right = _heights[(z * VertsX) + Math.Min(x + 1, VertsX - 1)];
                var down = _heights[(Math.Max(z - 1, 0) * VertsX) + x];
                var up = _heights[(Math.Min(z + 1, VertsZ - 1) * VertsX) + x];
                normals[i] = new Vector3(left - right, 2f * step, down - up).Normalized();
                var tangent = new Vector3(2f * step, right - left, 0f).Normalized();
                tangents[(i * 4) + 0] = tangent.X;
                tangents[(i * 4) + 1] = tangent.Y;
                tangents[(i * 4) + 2] = tangent.Z;
                tangents[(i * 4) + 3] = 1f;
            }
        }

        var indices = new int[(width - 1) * (depth - 1) * 6];
        var k = 0;
        for (var z = 0; z < depth - 1; z++)
        {
            for (var x = 0; x < width - 1; x++)
            {
                var a = (z * width) + x;
                var b = a + 1;
                var c = a + width;
                var d = c + 1;
                indices[k++] = a;
                indices[k++] = b;
                indices[k++] = c;
                indices[k++] = b;
                indices[k++] = d;
                indices[k++] = c;
            }
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Tangent] = tangents;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    /// <summary>A wide skirt of desert around the map so the horizon is not a cliff.</summary>
    public ArrayMesh BuildSkirt(float extent, int segments, ulong seed)
    {
        var noise = new FastNoiseLite { Seed = unchecked((int)(seed ^ 0x5A17)), Frequency = 0.012f, FractalOctaves = 4 };
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var stepSize = extent * 2f / segments;
        for (var z = 0; z < segments; z++)
        {
            for (var x = 0; x < segments; x++)
            {
                var corners = new Vector3[4];
                var outside = false;
                for (var c = 0; c < 4; c++)
                {
                    var px = -extent + ((x + (c & 1)) * stepSize);
                    var pz = -extent + ((z + (c >> 1)) * stepSize);
                    var inside = MathF.Max(MathF.Abs(px) - (SizeX / 2f), MathF.Abs(pz) - (SizeZ / 2f));
                    if (inside < 0f)
                    {
                        // Under the map: tuck the skirt below the terrain so it never shows through.
                        corners[c] = new Vector3(px, HeightAt(px, pz) - 6f, pz);
                        continue;
                    }

                    outside = true;
                    var blend = Math.Clamp(inside / 40f, 0f, 1f);
                    var edge = HeightAt(px, pz) - 0.3f;
                    var far = (noise.GetNoise2D(px, pz) * 18f) + (blend * blend * 14f);
                    corners[c] = new Vector3(px, Mathf.Lerp(edge, far, blend), pz);
                }

                if (!outside)
                {
                    continue;
                }

                st.AddVertex(corners[0]);
                st.AddVertex(corners[1]);
                st.AddVertex(corners[2]);
                st.AddVertex(corners[1]);
                st.AddVertex(corners[3]);
                st.AddVertex(corners[2]);
            }
        }

        st.GenerateNormals();
        return st.Commit();
    }

    /// <summary>SplitMix64 step mapped to [0, 1): crater placement only, never the simulation.</summary>
    private static float Next(ref ulong state)
    {
        state += 0x9E3779B97F4A7C15UL;
        var z = state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        z ^= z >> 31;
        return (z >> 40) / (float)(1UL << 24);
    }

    /// <summary>
    /// Real relief (M1, ADR-034): the region's height map scaled to the map, with a vertical
    /// exaggeration so a 96 km region still reads as relief, plus faint ripples below the data's
    /// resolution.
    /// </summary>
    private void FromRelief(Martis.Simulation.Planets.RegionRelief relief, ulong seed)
    {
        var ripples = new FastNoiseLite { Seed = unchecked((int)seed), NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 0.3f, FractalOctaves = 3 };
        var metres = relief.Definition.SizeM / SizeX;
        var mid = (relief.Definition.MinHeightM + relief.Definition.MaxHeightM) / 2.0;
        var step = CellSize / Resolution;
        for (var z = 0; z < VertsZ; z++)
        {
            for (var x = 0; x < VertsX; x++)
            {
                var h = (relief.HeightAt(x * step * metres, z * step * metres) - mid) / metres * ReliefExaggeration;
                var wx = (x * step) - (SizeX / 2f);
                var wz = (z * step) - (SizeZ / 2f);
                _heights[(z * VertsX) + x] = (float)h + (ripples.GetNoise2D(wx, wz) * 0.08f);
            }
        }
    }

    private void Generate(ulong seed)
    {
        var noise = new FastNoiseLite
        {
            Seed = unchecked((int)seed),
            NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth,
            Frequency = 0.018f,
            FractalType = FastNoiseLite.FractalTypeEnum.Fbm,
            FractalOctaves = 5,
        };
        var ridges = new FastNoiseLite
        {
            Seed = unchecked((int)(seed >> 7)) + 11,
            NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin,
            Frequency = 0.035f,
            FractalType = FastNoiseLite.FractalTypeEnum.Ridged,
            FractalOctaves = 3,
        };
        var state = seed ^ 0x3C6EUL;
        var craters = new (float X, float Z, float R, float D)[Math.Max(9, 9 * CellsX * CellsY / 4096)];
        for (var i = 0; i < craters.Length; i++)
        {
            craters[i] = (
                (Next(ref state) - 0.5f) * SizeX * 0.9f,
                (Next(ref state) - 0.5f) * SizeZ * 0.9f,
                4f + (Next(ref state) * (i == 0 ? 18f : 9f)),
                0.8f + (Next(ref state) * 1.4f));
        }

        var step = CellSize / Resolution;
        for (var z = 0; z < VertsZ; z++)
        {
            for (var x = 0; x < VertsX; x++)
            {
                var wx = (x * step) - (SizeX / 2f);
                var wz = (z * step) - (SizeZ / 2f);
                var h = noise.GetNoise2D(wx, wz) * 4.5f;
                var mesa = Mathf.SmoothStep(0.2f, 0.6f, ridges.GetNoise2D(wx, wz)) * 1.6f;
                h += mesa;
                var dunes = MathF.Sin((wx * 0.55f) + (noise.GetNoise2D(wz * 2f, wx * 2f) * 4f)) * 0.18f;
                h += dunes;
                foreach (var crater in craters)
                {
                    var d = new Vector2(wx - crater.X, wz - crater.Z).Length() / crater.R;
                    if (d < 1.6f)
                    {
                        var bowl = d < 1f ? (d * d) - 1f : 0f;
                        var rim = MathF.Exp(-MathF.Pow((d - 1f) * 3.2f, 2f)) * 0.55f;
                        h += (bowl + rim) * crater.D;
                    }
                }

                _heights[(z * VertsX) + x] = h;
            }
        }
    }
}
