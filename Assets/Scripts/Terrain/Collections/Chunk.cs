using Evix.Terrain.DataGeneration;
using Evix.Voxels;
using System;
using System.Linq;
using Unity.Collections;
using UnityEngine;

namespace Evix.Terrain.Collections {

  /// <summary>
  /// An area of world terrain made of voxels
  /// </summary>
  public class Chunk {

    /// <summary>
    /// The chunk of terrain's diameter in voxels. Used for x y and z
    /// </summary>
    public const int Diameter = 16;

    /// <summary>
    /// If this chunk is loaded yet
    /// Set true after all chunk voxels are loaded from file or generated from a biome or noise.
    /// @TODO: can i maybe remove this in favor of a check to the level's dictionary?
    /// </summary>
    public bool isLoaded = false;

    /// <summary>
    /// If this chunks mesh is currently generated
    /// </summary>
    public bool meshIsGenerated = false;

    /// <summary>
    /// If the mesh for this chunk is empty/ has no verts/tris
    /// </summary>
    public bool meshIsEmpty = true;

    /// <summary>
    /// get if this chunk is empty
    /// </summary>
    public bool isEmpty {
      get => voxels == null;
    }

    /// <summary>
    /// The chunk is solid if the solid voxel count equals the max voxel count
    /// </summary>
    public bool isSolid {
      get => solidVoxelCount == Diameter * Diameter * Diameter;
    }

    /// <summary>
    /// The number of solid (non 0) voxels in the chunk
    /// </summary>
    public int solidVoxelCount {
      get;
      private set;
    } = 0;

    /// <summary>
    /// The voxels
    /// </summary>
    byte[] voxels = null;

    /// <summary>
    /// Get the voxel value stored at
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <param name="z"></param>
    /// <returns></returns>
    public byte this[int x, int y, int z] {
      // uses same forula as in Coordinate.flatten
      get { 
        return voxels != null 
          ? voxels[Coordinate.Flatten(x, y, z, Diameter)] 
          : (byte)0;
      }
      set {
        if (value != Voxel.Types.Empty.Id) {
          if (voxels == null) {
            voxels = new byte[Diameter * Diameter * Diameter];
          }
          if (voxels[Coordinate.Flatten(x, y, z, Diameter)] == Voxel.Types.Empty.Id) {
            solidVoxelCount++;
          }
          voxels[Coordinate.Flatten(x, y, z, Diameter)] = value;
        } else {
          if (voxels != null && voxels[Coordinate.Flatten(x, y, z, Diameter)] != Voxel.Types.Empty.Id) {
            voxels[Coordinate.Flatten(x, y, z, Diameter)] = value;
            solidVoxelCount--;
          }
        }
      }
    } // OLD: x * Diameter * Diameter + y * Diameter + z

    /// <summary>
    /// Set the voxels in this chunk from a native array
    /// </summary>
    /// <param name="voxels"></param>
    public void setVoxels(NativeArray<byte> voxels, int? solidVoxelCount = null) {
      this.voxels = new byte[Diameter * Diameter * Diameter];
      voxels.CopyTo(this.voxels);
      this.solidVoxelCount = solidVoxelCount == null
        ? voxels.Count(value => value != Voxel.Types.Empty.Id)
        : (int)solidVoxelCount;
      isLoaded = true;
    }

    /// <summary>
    /// Get all the voxels as a native array
    /// </summary>
    /// <returns></returns>
    public NativeArray<byte> getVoxels() {
      return new NativeArray<byte>(voxels, Allocator.Persistent); 
    }

    public override string ToString() {
      return $"[={solidVoxelCount}::{(isLoaded ? "%" : "")}{(meshIsGenerated ? "#" : "")}]";
    }

    /// <summary>
    /// A chunk's hashable ID based on it's location in world.
    /// </summary>
    public struct ID : IEquatable<ID> {

      /// <summary>
      /// The X (East West) location in world of the chunk
      /// </summary>
      public readonly int X;

      /// <summary>
      /// The Y (Above Below) location in world of the chunk
      /// </summary>
      public readonly int Y;

      /// <summary>
      /// The Z (North South) location in world of the chunk
      /// </summary>
      public readonly int Z;

      /// <summary>
      /// Get the ID as a coordinate
      /// </summary>
      public Coordinate Coordinate {
        get => new Coordinate(X, Y, Z);
      }

      /// <summary>
      /// Make a new chunk id based on it's location
      /// </summary>
      /// <param name="x"></param>
      /// <param name="y"></param>
      /// <param name="z"></param>
      public ID(int x, int y, int z) {
        X = x;
        Y = y;
        Z = z;
      }

      /// <summary>
      /// 
      /// </summary>
      /// <param name="x"></param>
      /// <param name="y"></param>
      /// <param name="z"></param>
      /// <returns></returns>
      public static ID FromWorldPos(int x, int y, int z) {
        return new ID(x >> 4, y >> 4, z >> 4);
      }

      /// <summary>
      /// Get the world location of the 0,0,0 of the chunk with this id
      /// </summary>
      /// <returns></returns>
      public Coordinate toWorldLocation() {
        return new Coordinate(X * Diameter, Y * Diameter, Z * Diameter);
      }

      /// <summary>
      /// Turn a coord into a chunk id
      /// </summary>
      public static implicit operator ID(Coordinate coordinate) {
        return new ID(coordinate.x, coordinate.y, coordinate.z);
      }

      /// <summary>
      /// Turn a vector3 into a chunk id.
      /// </summary>
      public static implicit operator ID(Vector3 coordinate) {
        return new ID((int)coordinate.x, (int)coordinate.y, (int)coordinate.z);
      }

      /// <summary>
      /// Equality stuff
      /// </summary>
      /// <returns></returns>
      #region Equality members

      public bool Equals(ID other) {
        return X == other.X && Y == other.Y && Z == other.Z;
      }

      public override bool Equals(object obj) {
        if (obj is null) return false;
        return obj is ID other && Equals(other);
      }

      public override int GetHashCode() {
        unchecked {
          var hashCode = X;
          hashCode = (hashCode * 397) ^ Y;
          hashCode = (hashCode * 397) ^ Z;
          return hashCode;
        }
      }

      public static bool operator ==(ID left, ID right) {
        return left.Equals(right);
      }

      public static bool operator !=(ID left, ID right) {
        return !left.Equals(right);
      }

      #endregion

      public override string ToString() {
        return $"[{Coordinate}]";
      }
    }
  }
}