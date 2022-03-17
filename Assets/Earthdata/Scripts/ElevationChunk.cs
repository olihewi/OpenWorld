using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Earthdata;
using Maps.Features;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Rendering;

public class ElevationChunk : MonoBehaviour
{
  public Rect elevationRect;
  public int currentLOD = -1;
  [SerializeField] private MeshFilter meshFilter;
  [SerializeField] private MeshCollider meshCollider;
  [SerializeField] private MeshRenderer meshRenderer;

  public void UpdateLOD(int _newLOD)
  {
    if (currentLOD == _newLOD || !ElevationStreamer.IsElevationReady(elevationRect.min) || !ElevationStreamer.IsElevationReady(elevationRect.max) ) return;
    currentLOD = _newLOD;
    if (currentLOD >= 0) GenerateMesh();
  }

  [ContextMenu("Generate Mesh")]
  private void GenerateMesh()
  {
    int step = currentLOD + 1;
    int w = Mathf.FloorToInt(1.0F / step * elevationRect.width * 3601.0F);
    int h = Mathf.FloorToInt(1.0F / step * elevationRect.height * 3601.0F);
    Vector3[] vertices = new Vector3[w * h];
    for (int y = 0; y < h; y++)
    {
      for (int x = 0; x < w; x++)
      {
        Vector2 latLong = new Vector2(x / (float) (w-1) * elevationRect.width * 1.005F, y / (float) (h-1) * elevationRect.height * 1.005F);
        Vector3 vertex = new Vector3(latLong.x - elevationRect.width / 2.0F, ElevationStreamer.GetHeightAt(latLong + elevationRect.min), latLong.y - elevationRect.height / 2.0F);
        vertex.x *= 111319.444F;
        vertex.z *= 111319.444F;
        vertices[x + y * w] = vertex;
      }
    }
    int[] triangles = new int[(w - 1) * (h - 1) * 6];
    for (int ti = 0, vi = 0, y = 0; y < h - 1; y++, vi++)
    {
      for (int x = 0; x < w - 1; x++, ti += 6, vi++)
      {
        triangles[ti] = vi;
        triangles[ti + 3] = triangles[ti + 2] = vi + 1;
        triangles[ti + 4] = triangles[ti + 1] = vi + w;
        triangles[ti + 5] = vi + w + 1;
      }
    }
    Mesh mesh = new Mesh{vertices = vertices, triangles = triangles};
    mesh.RecalculateNormals();
    meshFilter.sharedMesh = mesh;
    meshCollider.sharedMesh = mesh;
    //meshRenderer.material.color = Color.white * (0.8F - currentLOD/ 16.0F);
  }
}