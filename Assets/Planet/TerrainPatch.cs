using UnityEngine;
using System.Collections;

enum Neighbor {
    forward = 1,
    back = 2,
    left = 4,
    right = 8 
}


/// <summary>
/// A single plane mesh, rendered with accompanying collider and renderer
/// Contains references to a parent (if any), and 0 or 4 children (Quadtree nodes)
/// </summary>
public class TerrainPatch
{
    public TerrainPatch parent;
    public TerrainPatch[] tree;
    public Terrain terrain;
    public Bounds bound;
    int neighborState = 0;

    public Material material { get; private set; }
    public Mesh mesh { get; private set; }

    public MeshFilter filter { get; private set; }
    public MeshRenderer renderer { get; private set; }
    //public MeshCollider collider { get; private set; }
    public GameObject gameObject { get; private set; }

    private Vector3 position, left, forward;
    public Vector3 center;
    public float scale { get; private set; }
    public bool generated = false;
    public bool finalized = false;
    public bool textured = false;

    public int PosX
    {
        get
        {
            return (int)center.x;
        }
    }
    public int PosZ
    {
        get
        {
            return (int)center.z;
        }
    }

    /// Initialize a sub-Cubeface with a parent referece
    public TerrainPatch(TerrainPatch parent, Vector3 pos, Vector3 left, Vector3 forward, float scale, Terrain t)
    {
        this.terrain = t;
        Initialize(parent, pos, left, forward, scale);
    }

    // Initialize a parent Cubeface with nulled parent reference
    public TerrainPatch(Vector3 pos, Vector3 left, Vector3 forward, float scale, Terrain t)
    {
        this.terrain = t;
        Initialize(null, pos, left, forward, scale);
    }

    /// <summary> Creates a CubeFace </summary>
    /// <param name="parent">Associated parent reference as a QuadTree sibling</param>
    /// <param name="pos">Position vector to render the cubeface at</param>
    /// <param name="left">The x axis of the plane</param>
    /// <param name="forward">The z axis of the plane</param>
    /// <param name="scale">The scale of the plane</param>
    /// <param name="size">The number of vertices in length/width to create (segments)</param>
    private void Initialize(TerrainPatch parent, Vector3 pos, Vector3 left, Vector3 forward, float scale)
    {
        pos.y = terrain.GetHeightAt(pos.x + terrain.scale / 2, pos.z + terrain.scale / 2) * Terrain.heightScalar;
        this.bound = new Bounds(pos, new Vector3(scale, scale, scale));
        this.parent = parent;
        this.scale = scale;
        this.center = pos;
        this.position = pos;
        this.left = left;
        this.forward = forward;

        // Centre the plane!
        position -= left * (scale / 2);
        position -= forward * (scale / 2);

        gameObject = new GameObject("TerrainPatch_" + scale + "_" + 2 + "_" + pos.ToString());
        gameObject.isStatic = true;
        gameObject.layer = 8; // To Raycast neighbours

        material = new Material(Shader.Find("Standard"));
        filter = gameObject.AddComponent<MeshFilter>();
        renderer = gameObject.AddComponent<MeshRenderer>();
        //collider = gameObject.AddComponent<MeshCollider>();


        // Ensure hierarchy
        if (parent != null)
            gameObject.transform.parent = parent.gameObject.transform;

        //Generate();
    }

    /// <summary>
    /// Used to first generate the plane's verts, uvs and tris
    /// If already generated, turns on this CubeFace's renderer and collider (makes it active on the planet)
    /// </summary>
    public void Generate()
    {
        int vertCount = 0;
        int cNeighborState = 0;

        int nowValue = terrain.GetQuadMatrixData(terrain.GetQuadX(this), terrain.GetQuadZ(this));

        // forward
        int forwardZ = terrain.GetQuadZ(this) + (int)this.scale;
        if (forwardZ < terrain.quadSize)
        {
            if (terrain.GetQuadMatrixData(terrain.GetQuadX(this), forwardZ) != 255 &&
                terrain.GetQuadMatrixData(terrain.GetQuadX(this), forwardZ) != 0)
            {
                cNeighborState |= (int)Neighbor.forward;
                vertCount++;
            }
        }
        int backZ = terrain.GetQuadZ(this) - (int)this.scale;
        if (backZ >= 0)
        {
            if (terrain.GetQuadMatrixData(terrain.GetQuadX(this), backZ) != 255 &&
                terrain.GetQuadMatrixData(terrain.GetQuadX(this), backZ) != 0)
            {
                cNeighborState |= (int)Neighbor.back;
                vertCount++;
            }
        }

        // left
        int leftX = terrain.GetQuadX(this) + (int)this.scale;
        if (leftX < terrain.quadSize)
        {
            if (terrain.GetQuadMatrixData(leftX, terrain.GetQuadZ(this)) != 255 &&
                terrain.GetQuadMatrixData(leftX, terrain.GetQuadZ(this)) != 0)
            {
                cNeighborState |= (int)Neighbor.left;
                vertCount++;
            }
        }
        int rightX = terrain.GetQuadX(this) - (int)this.scale;
        if (rightX >= 0)
        {
            if (terrain.GetQuadMatrixData(rightX, terrain.GetQuadZ(this)) != 255 &&
                terrain.GetQuadMatrixData(rightX, terrain.GetQuadZ(this)) != 0)
            {
                cNeighborState |= (int)Neighbor.right;
                vertCount++;
            }
        }

        renderer.enabled = true;

        if (generated && cNeighborState == neighborState)
        {
            //if (parent != null)
            //    parent.collider.enabled = false;
            //collider.enabled = true;
            return;
        }

        neighborState = cNeighborState;

        finalized = false;

        // 如果是lod最大值，即scale == 2，那么如果旁边是更高的scale，则跳过这个点即可
        // 如果不是，获得，上右，上左，下左，下右是否有子树，如果都没有子树，那么与lod最大值同理

        mesh = new Mesh();
        mesh.name = "Mesh_" + gameObject.name;

        int size = 2;


        var verts = new Vector3[(size + 1) * (size + 1) - vertCount];
        var tris = new int[size * size * 6 - vertCount * 3];

        var uvs = new Vector2[(size + 1) * (size + 1) - vertCount];
        var uvFactor = 1.0f / size;

        int cx = 1;
        int cz = 1;
        var px = (float)cx / size;
        var pz = (float)cz / size;
        var vx = left * px * scale;
        var vz = forward * pz * scale;
        uvs[0] = new Vector2(cx * uvFactor, cz * uvFactor);
        verts[0] = position + vx + vz;

        int i = 1;

        int x = 0;
        int z = 0;
        px = (float)(x) / size;
        pz = (float)z / size;
        vx = left * px * scale;
        vz = forward * pz * scale;
        uvs[i] = new Vector2(x * uvFactor, z * uvFactor);
        verts[i] = position + vx + vz;

        i++;
        if ((cNeighborState & ((int)Neighbor.back)) == 0)
        {
            x = 1;
            z = 0;

            px = (float)(x) / size;
            pz = (float)z / size;
            vx = left * px * scale;
            vz = forward * pz * scale;
            uvs[i] = new Vector2(x * uvFactor, z * uvFactor);
            verts[i] = position + vx + vz;

            i++;
        }

        x = 2;
        z = 0;

        px = (float)(x) / size;
        pz = (float)z / size;
        vx = left * px * scale;
        vz = forward * pz * scale;
        uvs[i] = new Vector2(x * uvFactor, z * uvFactor);
        verts[i] = position + vx + vz;

        i++;

        if ((cNeighborState & ((int)Neighbor.left)) == 0)
        {
            x = 2;
            z = 1;

            px = (float)(x) / size;
            pz = (float)z / size;
            vx = left * px * scale;
            vz = forward * pz * scale;
            uvs[i] = new Vector2(x * uvFactor, z * uvFactor);
            verts[i] = position + vx + vz;

            i++;
        }

        x = 2;
        z = 2;

        px = (float)(x) / size;
        pz = (float)z / size;
        vx = left * px * scale;
        vz = forward * pz * scale;
        uvs[i] = new Vector2(x * uvFactor, z * uvFactor);
        verts[i] = position + vx + vz;

        i++;

        if ((cNeighborState & ((int)Neighbor.forward)) == 0)
        {
            x = 1;
            z = 2;

            px = (float)(x) / size;
            pz = (float)z / size;
            vx = left * px * scale;
            vz = forward * pz * scale;
            uvs[i] = new Vector2(x * uvFactor, z * uvFactor);
            verts[i] = position + vx + vz;

            i++;
        }

        x = 0;
        z = 2;

        px = (float)(x) / size;
        pz = (float)z / size;
        vx = left * px * scale;
        vz = forward * pz * scale;
        uvs[i] = new Vector2(x * uvFactor, z * uvFactor);
        verts[i] = position + vx + vz;

        i++;

        if ((cNeighborState & ((int)Neighbor.right)) == 0)
        {
            x = 0;
            z = 1;

            px = (float)(x) / size;
            pz = (float)z / size;
            vx = left * px * scale;
            vz = forward * pz * scale;
            uvs[i] = new Vector2(x * uvFactor, z * uvFactor);
            verts[i] = position + vx + vz;
        }


        // Calculate tris
        int ti = 0;
        for (int vi = 1; vi < verts.Length - 1; ti += 3, vi += 1)
        {
            tris[ti] = 0;
            tris[ti + 1] = vi + 1;
            tris[ti + 2] = vi;
        }

        tris[ti] = 0;
        tris[ti + 1] = 1;
        tris[ti + 2] = verts.Length - 1;


        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.triangles = tris;

        if (parent != null)
            parent.Dispose();

        generated = true;
    }

    /// <summary>
    /// Must be called after mesh.RecalculateNormals (needs normal values)
    /// Generates normal map and applies grayscale texture to CubeFace's material
    /// </summary>
    public void Texturize()
    {
        if (textured)
            return;

        material.SetFloat("_Glossiness", 0.1f);
        material.color = Color.gray;

        // Mesh complete, set render/physics objects
        renderer.material = material;   // If we keep the same material use sharedMaterial for batching

        textured = true;
    }

    /// <summary>
    /// Quadtree merge of any children
    /// </summary>
    public void Merge()
    {
        if (tree == null)
            return;

        for (int t = 0; t < tree.Length; t++)
        {
            tree[t].Merge();
            tree[t].Clear();
        }

        tree = null;
    }

    public void Dispose()
    {
        renderer.enabled = false;
        generated = false;
        //collider.enabled = false;
    }

    public void Clear()
    {
        Dispose();
        Object.Destroy(gameObject);
    }

    /// <summary>
    /// Quadtree subdivide
    /// Creates 4 sub-CubeFaces taking same space as current mesh, doubling the detail.
    /// </summary>
    public void SubDivide()
    {
        var subPos = position;
        subPos += left * (scale / 2);
        subPos += forward * (scale / 2);

        var stepLeft = (left * scale / 4);
        var stepForward = (forward * scale / 4);
        var hs = scale / 2;

        tree = new TerrainPatch[] {
                new TerrainPatch(this, subPos - stepLeft + stepForward, left, forward, hs, terrain),
                new TerrainPatch(this, subPos + stepLeft + stepForward, left, forward, hs, terrain),
                new TerrainPatch(this, subPos - stepLeft - stepForward, left, forward, hs, terrain),
                new TerrainPatch(this, subPos + stepLeft - stepForward, left, forward, hs, terrain)
        };

        Dispose();
    }
}
