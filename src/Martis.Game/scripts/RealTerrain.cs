using System;
using System.Collections.Generic;
using Godot;
using Martis.Simulation.Planets;

namespace Martis.Game;

/// <summary>
/// The region at real scale (M2, ADR-035): one world unit is one metre. The ground is split
/// into square chunks that are built around a focus point (the rover) with fewer vertices the
/// further they are, and freed when they fall out of range. Heights come from the real relief
/// (≈47 m between samples) plus a few metres of procedural roughness below that resolution, so
/// the ground close to the rover is not smooth. Chunks are built a few per frame on the main
/// thread; nothing here touches the simulation.
/// </summary>
public partial class RealTerrain : Node3D
{
    /// <summary>Side of a chunk in metres.</summary>
    public const float ChunkM = 512f;

    /// <summary>Chunks kept around the focus in each direction.</summary>
    public const int Radius = 7;

    private const int BuildsPerFrame = 3;

    private readonly Dictionary<(int X, int Z), (MeshInstance3D Mesh, int Lod)> _chunks = [];
    private readonly FastNoiseLite _rough = new() { NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 0.02f, FractalOctaves = 4, Seed = 7 };
    private readonly FastNoiseLite _rocks = new() { NoiseType = FastNoiseLite.NoiseTypeEnum.Cellular, Frequency = 0.08f, Seed = 11 };
    private RegionRelief _relief = null!;
    private ShaderMaterial _material = null!;
    private Vector3 _focus;
    private float _half;

    /// <summary>Chunks currently built (for checks and the HUD).</summary>
    public int ChunkCount => _chunks.Count;

    public RegionRelief Relief => _relief;

    public float SizeM => (float)_relief.Definition.SizeM;

    public void Setup(RegionRelief relief, Texture2D? albedo)
    {
        _relief = relief ?? throw new ArgumentNullException(nameof(relief));
        _half = SizeM / 2f;
        _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/real_terrain.gdshader") };
        _material.SetShaderParameter("region_size", SizeM);
        if (albedo is not null)
        {
            _material.SetShaderParameter("albedo_map", albedo);
        }

        BuildFar();
    }

    /// <summary>A sharper orbital image over a detail insert (M3, ADR-036).</summary>
    public void SetDetail(Insert insert, Texture2D albedo)
    {
        ArgumentNullException.ThrowIfNull(insert);
        _material.SetShaderParameter("detail_map", albedo);
        _material.SetShaderParameter("detail_rect", new Vector3((float)(insert.X / _relief.Definition.SizeM), (float)(insert.Y / _relief.Definition.SizeM), (float)(insert.Detail.Definition.SizeM / _relief.Definition.SizeM)));
        _material.SetShaderParameter("detail_on", 1f);
    }

    /// <summary>
    /// The whole region at 375 m per vertex, a few metres below the detailed chunks: it fills the
    /// horizon (crater rims tens of kilometres away) where no chunk is built.
    /// </summary>
    private void BuildFar()
    {
        const int verts = 257;
        var step = SizeM / (verts - 1);
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        float H(int i, int j) => (float)_relief.HeightAt(Math.Clamp(i, 0, verts - 1) * step, Math.Clamp(j, 0, verts - 1) * step) - 4f;
        for (var j = 0; j < verts; j++)
        {
            for (var i = 0; i < verts; i++)
            {
                st.SetNormal(new Vector3(H(i - 1, j) - H(i + 1, j), 2f * step, H(i, j - 1) - H(i, j + 1)).Normalized());
                st.AddVertex(new Vector3((i * step) - _half, H(i, j), (j * step) - _half));
            }
        }

        for (var j = 0; j < verts - 1; j++)
        {
            for (var i = 0; i < verts - 1; i++)
            {
                var a = (j * verts) + i;
                st.AddIndex(a);
                st.AddIndex(a + 1);
                st.AddIndex(a + verts);
                st.AddIndex(a + 1);
                st.AddIndex(a + verts + 1);
                st.AddIndex(a + verts);
            }
        }

        AddChild(new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = _material, Name = "Far", CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
    }

    /// <summary>Local position (metres, origin at the region centre, +X east, +Z south) of a point in region metres.</summary>
    public Vector3 FromRegion(double xM, double yM) => new((float)xM - _half, HeightAt((float)xM - _half, (float)yM - _half), (float)yM - _half);

    /// <summary>Ground height in metres at a local position, real relief plus roughness.</summary>
    public float HeightAt(float x, float z)
    {
        var real = (float)_relief.HeightAt(x + _half, z + _half);
        return real + Detail(x, z);
    }

    /// <summary>Moves the area that is kept built; call every frame with the rover or camera position.</summary>
    public void Focus(Vector3 position)
    {
        _focus = position;
        var cx = (int)MathF.Floor(position.X / ChunkM);
        var cz = (int)MathF.Floor(position.Z / ChunkM);
        var built = 0;
        // Nearest chunks first, so the ground under the rover is never missing.
        for (var ring = 0; ring <= Radius && built < BuildsPerFrame; ring++)
        {
            for (var dz = -ring; dz <= ring && built < BuildsPerFrame; dz++)
            {
                for (var dx = -ring; dx <= ring && built < BuildsPerFrame; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != ring)
                    {
                        continue;
                    }

                    var key = (cx + dx, cz + dz);
                    if (!Inside(key))
                    {
                        continue;
                    }

                    var lod = Lod(ring);
                    if (_chunks.TryGetValue(key, out var chunk) && chunk.Lod == lod)
                    {
                        continue;
                    }

                    chunk.Mesh?.QueueFree();
                    var mesh = new MeshInstance3D { Mesh = Build(key, lod), MaterialOverride = _material, Name = $"Chunk_{key.Item1}_{key.Item2}" };
                    AddChild(mesh);
                    _chunks[key] = (mesh, lod);
                    built++;
                }
            }
        }

        var stale = new List<(int, int)>();
        foreach (var key in _chunks.Keys)
        {
            if (Math.Max(Math.Abs(key.X - cx), Math.Abs(key.Z - cz)) > Radius + 1)
            {
                stale.Add(key);
            }
        }

        foreach (var key in stale)
        {
            _chunks[key].Mesh.QueueFree();
            _chunks.Remove(key);
        }
    }

    /// <summary>Builds every chunk around the focus at once (screenshots and checks).</summary>
    public void BuildAll(Vector3 position)
    {
        for (var i = 0; i < 400; i++)
        {
            var before = _chunks.Count;
            Focus(position);
            if (_chunks.Count == before && i > 0 && Settled(position))
            {
                break;
            }
        }
    }

    private bool Settled(Vector3 position)
    {
        var cx = (int)MathF.Floor(position.X / ChunkM);
        var cz = (int)MathF.Floor(position.Z / ChunkM);
        for (var dz = -Radius; dz <= Radius; dz++)
        {
            for (var dx = -Radius; dx <= Radius; dx++)
            {
                var key = (cx + dx, cz + dz);
                if (Inside(key) && (!_chunks.TryGetValue(key, out var chunk) || chunk.Lod != Lod(Math.Max(Math.Abs(dx), Math.Abs(dz)))))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Vertices per chunk side: 129 next to the rover (4 m), down to 9 far away (64 m).</summary>
    private static int Lod(int ring) => ring switch
    {
        0 or 1 => 129,
        2 => 65,
        <= 4 => 33,
        _ => 17,
    };

    private bool Inside((int X, int Z) key)
    {
        var x0 = key.X * ChunkM;
        var z0 = key.Z * ChunkM;
        return x0 + ChunkM > -_half && x0 < _half && z0 + ChunkM > -_half && z0 < _half;
    }

    private float Detail(float x, float z)
    {
        // A few metres of undulation plus small rocky bumps; fades with nothing: it is below
        // the 47 m resolution of the data, so it never contradicts it at the scale it can show.
        var undulation = _rough.GetNoise2D(x, z) * 2.2f;
        var rocks = MathF.Max(0f, 0.35f - _rocks.GetNoise2D(x, z)) * 0.9f;
        return undulation + rocks;
    }

    private ArrayMesh Build((int X, int Z) key, int verts)
    {
        var step = ChunkM / (verts - 1);
        var x0 = key.X * ChunkM;
        var z0 = key.Z * ChunkM;
        var count = verts * verts;
        var positions = new Vector3[count];
        var normals = new Vector3[count];
        var heights = new float[(verts + 2) * (verts + 2)];
        for (var j = -1; j <= verts; j++)
        {
            for (var i = -1; i <= verts; i++)
            {
                var x = Math.Clamp(x0 + (i * step), -_half, _half);
                var z = Math.Clamp(z0 + (j * step), -_half, _half);
                heights[((j + 1) * (verts + 2)) + i + 1] = HeightAt(x, z);
            }
        }

        for (var j = 0; j < verts; j++)
        {
            for (var i = 0; i < verts; i++)
            {
                float H(int a, int b) => heights[((b + 1) * (verts + 2)) + a + 1];
                var x = Math.Clamp(x0 + (i * step), -_half, _half);
                var z = Math.Clamp(z0 + (j * step), -_half, _half);
                positions[(j * verts) + i] = new Vector3(x, H(i, j), z);
                normals[(j * verts) + i] = new Vector3(H(i - 1, j) - H(i + 1, j), 2f * step, H(i, j - 1) - H(i, j + 1)).Normalized();
            }
        }

        var indices = new int[(verts - 1) * (verts - 1) * 6];
        var k = 0;
        for (var j = 0; j < verts - 1; j++)
        {
            for (var i = 0; i < verts - 1; i++)
            {
                var a = (j * verts) + i;
                indices[k++] = a;
                indices[k++] = a + 1;
                indices[k++] = a + verts;
                indices[k++] = a + 1;
                indices[k++] = a + verts + 1;
                indices[k++] = a + verts;
            }
        }

        // A short skirt hides the cracks between chunks of different detail.
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        var skirt = Skirt(positions, normals, verts, out var skirtIndices);
        var allPositions = new Vector3[positions.Length + skirt.Positions.Length];
        positions.CopyTo(allPositions, 0);
        skirt.Positions.CopyTo(allPositions, positions.Length);
        var allNormals = new Vector3[normals.Length + skirt.Normals.Length];
        normals.CopyTo(allNormals, 0);
        skirt.Normals.CopyTo(allNormals, normals.Length);
        var allIndices = new int[indices.Length + skirtIndices.Length];
        indices.CopyTo(allIndices, 0);
        for (var i = 0; i < skirtIndices.Length; i++)
        {
            allIndices[indices.Length + i] = skirtIndices[i] + positions.Length;
        }

        arrays[(int)Mesh.ArrayType.Vertex] = allPositions;
        arrays[(int)Mesh.ArrayType.Normal] = allNormals;
        arrays[(int)Mesh.ArrayType.Index] = allIndices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    private static (Vector3[] Positions, Vector3[] Normals) Skirt(Vector3[] positions, Vector3[] normals, int verts, out int[] indices)
    {
        var edge = new List<int>();
        for (var i = 0; i < verts; i++) edge.Add(i);
        for (var j = 1; j < verts; j++) edge.Add((j * verts) + verts - 1);
        for (var i = verts - 2; i >= 0; i--) edge.Add(((verts - 1) * verts) + i);
        for (var j = verts - 2; j > 0; j--) edge.Add(j * verts);
        var n = edge.Count;
        var p = new Vector3[n * 2];
        var nn = new Vector3[n * 2];
        for (var e = 0; e < n; e++)
        {
            p[e] = positions[edge[e]];
            p[n + e] = positions[edge[e]] - new Vector3(0, 6f, 0);
            nn[e] = normals[edge[e]];
            nn[n + e] = normals[edge[e]];
        }

        var list = new List<int>();
        for (var e = 0; e < n; e++)
        {
            var f = (e + 1) % n;
            list.AddRange([e, f, n + e, f, n + f, n + e]);
            list.AddRange([e, n + e, f, f, n + e, n + f]); // both sides: the skirt is seen from any chunk
        }

        indices = [.. list];
        return (p, nn);
    }
}
