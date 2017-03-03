using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;
using UnityEngine.UI;

/// <summary>
/// 
/// </summary>
public class Terrain
{
    static Vector3[] directions = new Vector3[] {
            Vector3.left,
            Vector3.forward,
            Vector3.right,
            Vector3.back
        };

    public const float heightScalar = 50;
    Plane[] frustum;
    public GameObject terrain;
    TerrainPatch face;
    Texture2D heightmap;
    public float detailLevel = 5.0f;
    public float minResolution = 2.0f;
    int[] quadMatrix;
    public float scale;
    public int quadSize;
    private LibNoise.Generator.Perlin planes = new LibNoise.Generator.Perlin(0.03, 2.7, 0.5, 6, 1337, LibNoise.QualityMode.Low);
    private LibNoise.Generator.RidgedMultifractal mountains = new LibNoise.Generator.RidgedMultifractal(0.003, 6.5, 2, 1337, LibNoise.QualityMode.Medium);

    private float noise(Vector3 point, int octaves,
        float lucanarity = 2.0f, float gain = 0.5f, float warp = 0.25f)
    {
        float sum = 0.0f, freq = 1.0f, amp = 1.0f;

        for (int o = 0; o < octaves; o++)
        {
            sum += amp * (float)planes.GetValue(point);
            freq *= lucanarity;
            amp *= gain;
        }

        sum *= (float)mountains.GetValue(point * freq) * warp * octaves;

        return sum;
    }

    /// <summary>
    /// Creates a terrain
    ///     using 6 planes normalized around the origin
    /// </summary>
    /// <param name="name">The terrain name (duh)</param>
    /// <param name="scale">The scale of the terrain (radius)</param>
    public Terrain(string name, float scale, Texture2D heightmap, Transform parent, float detailLevel, float minResolution)
    {
        this.scale = scale;
        this.heightmap = heightmap;
        this.detailLevel = detailLevel;
        this.minResolution = minResolution;
        var hs = scale / 2;
        quadSize = (int)scale + 1;
        quadMatrix = new int[quadSize * quadSize];
        for (int x = 0; x < quadSize; ++x)
        {
            for (int z = 0; z < quadSize; ++z)
            {
                quadMatrix[z * quadSize + x] = 1;
            }
        }

        // Chuck it into a single game object for neatness
        terrain = new GameObject(name);
        terrain.transform.parent = parent.transform;

        face = new TerrainPatch(new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 0, 1), scale, this);
        face.gameObject.transform.parent = terrain.transform;

        PropagateRoughness();
    }

    public int GetQuadMatrixData(int x, int z)
    {
        return quadMatrix[(z * quadSize) + x];
    }

    public float GetHeightAt(float x, float z)
    {
        float u = (x) / scale;
        float v = (z) / scale;
        return heightmap.GetPixelBilinear(u, v).grayscale;
    }

    /// <summary>
    /// Turns a plane's vertices into a segment of a sphere
    ///  - Processes neighbour faces in order to remove gaps/tears
    /// </summary>
    /// <param name="face">The face in question (aint it an ugly one?)</param>
    private void FinalizeFace(TerrainPatch face)
    {
        if (face.finalized)
            return;

        var verts = face.mesh.vertices;

        for (int i = 0; i < verts.Length; i++)
        {
            //    //verts[i] = verts[i].normalized * (scale + noise(verts[i], 2, 1.7f, 0.1f, scale / size));
            //verts[i].y += noise(verts[i], 2, 1.7f, 0.1f, 10);
            float hs = scale / 2;
            //float u = (verts[i].x + hs) / scale;
            //float v = (verts[i].z + hs) / scale;
            //verts[i].y = heightmap.GetPixelBilinear(u, v).grayscale * 20;
            verts[i].y = GetHeightAt(verts[i].x + hs, verts[i].z + hs) * heightScalar;
        } 

        face.mesh.vertices = verts;

        face.mesh.RecalculateNormals();
        face.mesh.RecalculateBounds();

        face.Texturize();

        face.filter.sharedMesh = face.mesh;
        //face.collider.sharedMesh = face.mesh;

        face.finalized = true;
    }

    public int GetQuadX(TerrainPatch patch)
    {
        return patch.PosX + quadSize / 2;
    }

    public int GetQuadZ(TerrainPatch patch)
    {
        return patch.PosZ + quadSize / 2;
    }

    void DisableRenderer(TerrainPatch patch)
    {
        patch.renderer.enabled = false;
        if (patch.tree != null)
        {
            for (int i = 0; i < patch.tree.Length; i++)
            {
                DisableRenderer(patch.tree[i]);
            }
        }
    }

    private void RefineNode(TerrainPatch patch, Transform playerTransform, ref List<TerrainPatch> active)
    {
        int x = GetQuadX(patch);
        int z = GetQuadZ(patch);

        // 视景体剔除
        if (!GeometryUtility.TestPlanesAABB(frustum, patch.bound))
        {
            quadMatrix[z * quadSize + x] = 0;
            DisableRenderer(patch);
            return;
        }

        Vector3 cameraPos = playerTransform.position;
        Vector3 facePos = patch.center;
        //calculate the distance from the current point (L1 NORM, which, essentially, is a faster version of the 
        //normal distance equation you may be used to... yet again, thanks to Chris Cookson)
        int v1 = GetQuadMatrixData(x + 1, z);
        float viewDistance = (float)(Mathf.Abs(cameraPos.x - (facePos.x)) +
                                  Mathf.Abs(cameraPos.y - v1) +
                                  Mathf.Abs(cameraPos.z - (facePos.z)));


        //compute the 'f' value (as stated in Roettger's whitepaper of this algorithm)
        int v2 = GetQuadMatrixData(x - 1, z);
        float f = viewDistance / (patch.scale * minResolution *
                           Mathf.Max(detailLevel * v2 / 4.0f, 1.0f));
        
        int blend;
        if (f >= 1)
        {
            blend = 0;
        } else
        {
            blend = 255;
        }
        quadMatrix[z * quadSize + x] = blend;

        // 不需要拆分
        if (blend == 0)
        {
            active.Add(patch);

            if (patch.tree != null)
            {
                // 合并前恢复原始值
                for (int i = 0; i < patch.tree.Length; i++)
                {
                    int childX = GetQuadX(patch.tree[i]);
                    int childZ = GetQuadZ(patch.tree[i]);
                    quadMatrix[childZ * quadSize + childX] = quadMatrix[childZ * quadSize + childX - 1];
                }
                quadMatrix[z * quadSize + x] = quadMatrix[z * quadSize + x - 1];
                patch.Merge();
            }
        }
        // 需要拆分
        else if (patch.scale >= 4) 
        {
            if (patch.tree == null)
            {
                patch.SubDivide();
            }
        }

        // 计算子节点递归
        if (patch.tree != null)
        {
            for (int i = 0; i < patch.tree.Length; i++)
            {
                RefineNode(patch.tree[i], playerTransform, ref active);
            }
        }
    }

    public void PropagateRoughness()
    {
        int edgeLen = 4;
        while (edgeLen <= scale)
        {
            int edgeOffset = edgeLen / 2;
            int childOffset = edgeLen / 4;
            for (int z = edgeOffset; z < scale; z += edgeLen)
            {
                for (int x = edgeOffset; x < scale; x += edgeLen)
                {
                    float d2 = (Math.Abs((GetHeightAt(x - edgeOffset, z + edgeOffset) + GetHeightAt(x + edgeOffset, z + edgeOffset)) / 2.0f -
                              GetHeightAt(x, z + edgeOffset)));
                    d2 = Math.Max((Math.Abs((GetHeightAt(x + edgeOffset, z + edgeOffset) + GetHeightAt(x + edgeOffset, z - edgeOffset)) / 2.0f -
                              GetHeightAt(x + edgeOffset, z))), d2);
                    d2 = Math.Max((Math.Abs((GetHeightAt(x - edgeOffset, z - edgeOffset) + GetHeightAt(x + edgeOffset, z - edgeOffset)) / 2.0f -
                              GetHeightAt(x, z - edgeOffset))), d2);
                    d2 = Math.Max((Math.Abs((GetHeightAt(x - edgeOffset, z + edgeOffset) + GetHeightAt(x - edgeOffset, z - edgeOffset)) / 2.0f -
                              GetHeightAt(x - edgeOffset, z))), d2);

                    // 对角
                    d2 = Math.Max((Math.Abs((GetHeightAt(x - edgeOffset, z - edgeOffset) + GetHeightAt(x + edgeOffset, z + edgeOffset)) / 2.0f -
                              GetHeightAt(x, z))), d2);
                    d2 = Math.Max((Math.Abs((GetHeightAt(x + edgeOffset, z - edgeOffset) + GetHeightAt(x - edgeOffset, z + edgeOffset)) / 2.0f -
                              GetHeightAt(x, z))), d2);

                    int d2IntValue = Mathf.CeilToInt(d2 * 255.0f * 4.0f / edgeLen);

                    // 至少保证是1，否则与叶节点的含义冲突
                    d2IntValue = Math.Max(1, d2IntValue);

                    if (edgeLen == 4)
                    {
                        float maxHeight = (GetHeightAt(x + edgeOffset, z + edgeOffset));
                        maxHeight = Math.Max((GetHeightAt(x + edgeOffset, z)), maxHeight);
                        maxHeight = Math.Max((GetHeightAt(x + edgeOffset, z - edgeOffset)), maxHeight);
                        maxHeight = Math.Max((GetHeightAt(x, z - edgeOffset)), maxHeight);
                        maxHeight = Math.Max((GetHeightAt(x - edgeOffset, z - edgeOffset)), maxHeight);
                        maxHeight = Math.Max((GetHeightAt(x - edgeOffset, z)), maxHeight);
                        maxHeight = Math.Max((GetHeightAt(x - edgeOffset, z + edgeOffset)), maxHeight);
                        maxHeight = Math.Max((GetHeightAt(x, z + edgeOffset)), maxHeight);
                        maxHeight = Math.Max((GetHeightAt(x, z)), maxHeight);

                        quadMatrix[z * quadSize + x + 1] = Mathf.CeilToInt(maxHeight * 255.0f);
                    } else
                    {
                        float upperBound = 1.0f * minResolution / (2.0f * (minResolution - 1.0f));
                        d2IntValue = (Math.Max((int)(upperBound * GetQuadMatrixData(x, z)), d2IntValue));
                        d2IntValue = (Math.Max((int)(upperBound * GetQuadMatrixData(x - edgeOffset, z)), d2IntValue));
                        d2IntValue = (Math.Max((int)(upperBound * GetQuadMatrixData(x + edgeOffset, z)), d2IntValue));
                        d2IntValue = (Math.Max((int)(upperBound * GetQuadMatrixData(x, z - edgeOffset)), d2IntValue));
                        d2IntValue = (Math.Max((int)(upperBound * GetQuadMatrixData(x, z + edgeOffset)), d2IntValue));

                        float maxHeight = (GetHeightAt(x + childOffset, z + childOffset));
                        maxHeight = Math.Max((GetHeightAt(x + childOffset, z - childOffset)), maxHeight);
                        maxHeight = Math.Max((GetHeightAt(x - childOffset, z - childOffset)), maxHeight);
                        maxHeight = Math.Max((GetHeightAt(x - childOffset, z + childOffset)), maxHeight);

                        quadMatrix[z * quadSize + x + 1] = Mathf.CeilToInt(maxHeight * 255.0f);
                    }

                    quadMatrix[z * quadSize + x] = d2IntValue;
                    quadMatrix[z * quadSize + x - 1] = d2IntValue;

                    quadMatrix[(z - edgeOffset) * quadSize + x - edgeOffset] = Math.Max(quadMatrix[(z - edgeOffset) * quadSize + x - edgeOffset], d2IntValue);
                    quadMatrix[(z + edgeOffset) * quadSize + x - edgeOffset] = Math.Max(quadMatrix[(z + edgeOffset) * quadSize + x - edgeOffset], d2IntValue);
                    quadMatrix[(z + edgeOffset) * quadSize + x + edgeOffset] = Math.Max(quadMatrix[(z + edgeOffset) * quadSize + x + edgeOffset], d2IntValue);
                    quadMatrix[(z - edgeOffset) * quadSize + x + edgeOffset] = Math.Max(quadMatrix[(z - edgeOffset) * quadSize + x + edgeOffset], d2IntValue);
                }
            }
            edgeLen = edgeLen * 2;
        }
    }

    /// <summary>
    /// The coroutine terrain update process
    /// Keeps track of LOD according to player distance to each CubeFace
    /// Processes a single face per call, subdivision delays coroutine for smoothing.
    /// </summary>
    /// <param name="f">The face index of the cube [0, 5]</param>
    /// <returns></returns>
    //public IEnumerator Update()
    public void Update()
    {
        var player = Camera.main;
        //if (player == null)
        //    yield break;
        if (player == null)
            return;

        frustum = GeometryUtility.CalculateFrustumPlanes(Camera.main);
        var activeTree = new List<TerrainPatch>();
        RefineNode(face, player.transform, ref activeTree);

        for (int a = 0; a < activeTree.Count; a++)
        {
            activeTree[a].Generate();
            FinalizeFace(activeTree[a]);
        }
    }
}