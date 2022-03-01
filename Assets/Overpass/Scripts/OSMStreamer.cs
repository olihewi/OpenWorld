using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Xml.Linq;
using Maps.Features;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Maps
{
  public class OSMStreamer : MonoBehaviour
  {
    [Header("Overpass API Query")]
    public string filePath;
    public Rect boundingBox;

    [Header("Mesh Generation")] public HighwayNetwork highwayNetwork;

    [HideInInspector] public Node[] nodes;
    [HideInInspector] public Way[] ways;
    [HideInInspector] public Relation[] relations;
    
    [Header("Debug")] [SerializeField] private bool logExecutionTime = false;

    private void Awake()
    {
      GenerateEverything();
    }

    [ContextMenu("Generate Everything")]
    public void GenerateEverything()
    {
      GenerateData();
      GenerateMeshes();
    }

    [ContextMenu("Generate Data")]
    public void GenerateData()
    {
      double time = EditorApplication.timeSinceStartup;
      XElement response = XDocument.Load(filePath).Element("osm");
      if (logExecutionTime)
      {
        Debug.Log($"{gameObject.name}: Loaded {filePath} in {EditorApplication.timeSinceStartup - time:F}s");
        time = EditorApplication.timeSinceStartup;
      }
      Vector2 centrePoint = new Vector2(boundingBox.x + boundingBox.width / 2.0F, boundingBox.y - boundingBox.height / 2.0F);
      // Nodes
      Dictionary<long, Node> nodeDict = new Dictionary<long, Node>();
      foreach (XElement nodeElement in response.Elements("node"))
      {
        Node node = new Node(nodeElement, centrePoint);
        if (!nodeDict.ContainsKey(node.id)) nodeDict.Add(node.id, node);
      }
      // Ways
      Dictionary<long, Way> wayDict = new Dictionary<long, Way>();
      foreach (XElement wayElement in response.Elements("way"))
      {
        Way way = new Way(wayElement, nodeDict);
        if (!wayDict.ContainsKey(way.id)) wayDict.Add(way.id, way);
      }
      // Relations
      List<Relation> relationList = new List<Relation>();
      foreach (XElement relationElement in response.Elements("relation"))
      {
        relationList.Add(new Relation(relationElement, nodeDict, wayDict));
      }
      // To Arrays
      int i = 0;
      nodes = new Node[nodeDict.Count];
      foreach (KeyValuePair<long, Node> nodePair in nodeDict)
      {
        nodes[i++] = nodePair.Value;
      }
      i = 0;
      ways = new Way[wayDict.Count];
      foreach (KeyValuePair<long, Way> wayPair in wayDict)
      {
        ways[i++] = wayPair.Value;
      }
      relations = relationList.ToArray();
      if (logExecutionTime)
        Debug.Log($"{gameObject.name}: Generated Data in {EditorApplication.timeSinceStartup - time:F}s ({nodes.Length} nodes, {ways.Length} ways, {relations.Length} relations)");
      
      highwayNetwork.GenerateNetwork(ways);
    }
    
    [ContextMenu("Generate Meshes")]
    public void GenerateMeshes()
    {
      double time = EditorApplication.timeSinceStartup;
      MapFeature.RegisterFeatureGenerators();
      // Clear children
      while (transform.childCount != 0)
      {
        DestroyImmediate(transform.GetChild(0).gameObject);
      }
      // Mesh generation for each way
      Dictionary<MapFeature, MapFeature.FeatureMeshData> featureTypes = new Dictionary<MapFeature, MapFeature.FeatureMeshData>();
      foreach (Way way in ways)
      {
        MapFeature generator = MapFeature.GetFeatureGenerator(way);
        if (generator == null || generator.elementType != MapFeature.MapElement.Way) continue;
        if (!featureTypes.ContainsKey(generator)) featureTypes.Add(generator, new MapFeature.FeatureMeshData());
        MapFeature.FeatureMeshData meshData = featureTypes[generator];
        MapFeature.FeatureMeshData newData = generator.GetMesh(way, meshData.triOffset);
        meshData.vertices.AddRange(newData.vertices);
        meshData.triangles.AddRange(newData.triangles);
        meshData.uvs.AddRange(newData.uvs);
        meshData.triOffset = meshData.vertices.Count;
      }
      foreach (Node node in nodes)
      {
        MapFeature generator = MapFeature.GetFeatureGenerator(node);
        if (generator == null || generator.elementType != MapFeature.MapElement.Node) continue;
        if (!featureTypes.ContainsKey(generator)) featureTypes.Add(generator, new MapFeature.FeatureMeshData());
        MapFeature.FeatureMeshData meshData = featureTypes[generator];
        MapFeature.FeatureMeshData newData = generator.GetMesh(node, meshData.triOffset);
        meshData.vertices.AddRange(newData.vertices);
        meshData.triangles.AddRange(newData.triangles);
        meshData.uvs.AddRange(newData.uvs);
        meshData.triOffset = meshData.vertices.Count;
      }
      foreach (Relation relation in relations)
      {
        MapFeature generator = MapFeature.GetFeatureGenerator(relation);
        if (generator == null || generator.elementType != MapFeature.MapElement.Relation) continue;
        if (!featureTypes.ContainsKey(generator)) featureTypes.Add(generator, new MapFeature.FeatureMeshData());
        MapFeature.FeatureMeshData meshData = featureTypes[generator];
        MapFeature.FeatureMeshData newData = generator.GetMesh(relation, meshData.triOffset);
        meshData.vertices.AddRange(newData.vertices);
        meshData.triangles.AddRange(newData.triangles);
        meshData.uvs.AddRange(newData.uvs);
        meshData.triOffset = meshData.vertices.Count;
      }
      // Create a new mesh GameObject for each feature generator type
      foreach (KeyValuePair<MapFeature, MapFeature.FeatureMeshData> featurePair in featureTypes)
      {
        // TODO: See if using prefabs for this is more efficient.
        MeshFilter meshFilter = new GameObject(featurePair.Key.name,new [] {typeof(MeshFilter), typeof(MeshRenderer)}).GetComponent<MeshFilter>();
        MeshRenderer meshRenderer = meshFilter.GetComponent<MeshRenderer>();
        meshRenderer.sharedMaterials = featurePair.Key.materials;
        meshFilter.transform.parent = transform;
        // Create the new mesh
        Mesh mesh = new Mesh();
        mesh.indexFormat = IndexFormat.UInt32;
        mesh.vertices = featurePair.Value.vertices.ToArray();
        mesh.triangles = featurePair.Value.triangles.ToArray();
        mesh.uv = featurePair.Value.uvs.ToArray();
        mesh.RecalculateNormals();
        meshFilter.sharedMesh = mesh;
      }
      highwayNetwork.GenerateMeshes();
      
      if (logExecutionTime)
        Debug.Log($"{gameObject.name}: Generated Meshes in {EditorApplication.timeSinceStartup - time:F}s");
    }

    [ContextMenu("Request New Data from Overpass")]
    public async void RequestNewData()
    {
      HttpWebRequest request = WebRequest.CreateHttp($"http://overpass-api.de/api/interpreter?data=[out:xml];(node({boundingBox.y - boundingBox.height},{boundingBox.x},{boundingBox.y},{boundingBox.x + boundingBox.width});way({boundingBox.y - boundingBox.height},{boundingBox.x},{boundingBox.y},{boundingBox.x + boundingBox.width});relation({boundingBox.y - boundingBox.height},{boundingBox.x},{boundingBox.y},{boundingBox.x + boundingBox.width}););out body;>;out skel qt;");
      request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
      Debug.Log($"{gameObject.name}: Requesting data from Overpass API...");
      HttpWebResponse response = (HttpWebResponse) await request.GetResponseAsync();
      Debug.Log($"{gameObject.name}: Received data from Overpass!");
      Stream stream = response.GetResponseStream();
      StreamReader reader = new StreamReader(stream);
      XDocument doc = XDocument.Parse(await reader.ReadToEndAsync());
      doc.Save(filePath);
      Debug.Log($"{gameObject.name}: Successfully saved new data to {filePath}");
    }
  }
}