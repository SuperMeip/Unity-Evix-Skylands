using UnityEngine;
using Unity.Collections;

namespace Evix.Terrain.Collections {

  /// <summary>
  /// A mesh of tris and verts
  /// </summary>
  public struct VoxelMeshData {

    /// <summary>
    /// The id of the chunk this mesh was made for
    /// </summary>
    public Chunk.ID chunkID {
      get;
    }

    /// <summary>
    /// the vertices
    /// </summary>
    public Vector3[] vertices;

    /// <summary>
    ///  the triangles
    /// </summary>
    public int[] triangles;

    /// <summary>
    /// the vertices
    /// </summary>
    public Color[] colors;

    /// <summary>
    /// if this mesh is empty
    /// </summary>
    public bool isEmpty
      => triangles == null && vertices == null;

    /// <summary>
    /// Get the # of triangles in this mesh
    /// </summary>
    public int triangleCount 
      => triangles.Length / 3;

    /// <summary>
    /// Make a mesh
    /// </summary>
    public VoxelMeshData(
      Chunk.ID forChunk,
      bool meshDataIsEmpty,
      NativeArray<Vector3> vertices,
      NativeArray<int> triangles,
      NativeArray<Color> colors
    ) {
      chunkID = forChunk;
      if (meshDataIsEmpty) {
        this.vertices = null;
        this.triangles = null;
        this.colors = null;
      } else {
        this.vertices = new Vector3[vertices.Length];
        vertices.CopyTo(this.vertices);
        this.triangles = new int[triangles.Length];
        triangles.CopyTo(this.triangles);
        this.colors = new Color[colors.Length];
        colors.CopyTo(this.colors);
      }
    }
  }
}