using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GridGenerator2 : MonoBehaviour
{
    const int threadGroupSize = 8;

    public ComputeShader densityShader;
    List<Vector4> Grid = new List<Vector4>();
    public int Size = 10;
    public float IsoLevel = 0.5f;
    public int Seed = 1;
    public float radius = 4;
    public ComputeShader CubeMarcher;
    public float boundsSize = 1;
    public GameObject Cursor;

    float[] points;
    Dictionary<int, float> offsets = new Dictionary<int, float>();

    ComputeBuffer triangleBuffer;
    ComputeBuffer pointsBuffer;
    ComputeBuffer triCountBuffer;
    void Start()
    {

    }

    private void OnValidate()
    {
        Run();
    }

    // Update is called once per frame
    void Update()
    {
        Run();
    }

    Vector3 CentreFromCoord(Vector3Int coord)
    {
        return new Vector3(coord.x, coord.y, coord.z) * boundsSize;
    }

    void CreateBuffers()
    {
        int numPointsPerAxis = Size;
        int numPoints = numPointsPerAxis * numPointsPerAxis * numPointsPerAxis;
        int numVoxelsPerAxis = numPointsPerAxis - 1;
        int numVoxels = numVoxelsPerAxis * numVoxelsPerAxis * numVoxelsPerAxis;
        int maxTriangleCount = numVoxels * 5;

        // Always create buffers in editor (since buffers are released immediately to prevent memory leak)
        // Otherwise, only create if null or if size has changed
        if (!Application.isPlaying || (pointsBuffer == null || numPoints != pointsBuffer.count))
        {
            if (Application.isPlaying)
            {
                ReleaseBuffers();
            }
            triangleBuffer = new ComputeBuffer(maxTriangleCount, sizeof(float) * 3 * 3, ComputeBufferType.Append);
            pointsBuffer = new ComputeBuffer(numPoints, sizeof(float) * 4);
            triCountBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.Raw);

        }
    }

    void ReleaseBuffers()
    {
        if (triangleBuffer != null)
        {
            triangleBuffer.Release();
            pointsBuffer.Release();
            triCountBuffer.Release();
        }
    }

    private void Run()
    {
        CreateBuffers();
        int numPointsPerAxis = Size;
        int numVoxelsPerAxis = numPointsPerAxis - 1;
        int numPoints = numPointsPerAxis * numPointsPerAxis * numPointsPerAxis;
        int numThreadsPerAxis = Mathf.CeilToInt(numVoxelsPerAxis / (float)threadGroupSize);
        float pointSpacing = boundsSize / (numPointsPerAxis - 1);

        densityShader.SetBuffer(0, "points", pointsBuffer);
        densityShader.SetInt("numPointsPerAxis", numPointsPerAxis);
        densityShader.SetFloat("boundsSize", boundsSize);
        densityShader.SetFloat("radius", radius);
        densityShader.SetFloat("spacing", pointSpacing);
        densityShader.Dispatch(0, numThreadsPerAxis, numThreadsPerAxis, numThreadsPerAxis);

        points = new float[numPoints * 4];
        pointsBuffer.GetData(points);

        var clicked = Input.GetButtonDown("Fire1") || Input.GetButton("Fire2");
        var direction = -1;
        if (Input.GetButtonDown("Fire1"))
        {
            direction *= -1;
        }

        if (clicked)
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;
            bool didHit = GetComponent<MeshCollider>().Raycast(ray, out hit, 100.0f);
            if (didHit)
            {
                var floatPoints = hit.point + (hit.normal * -direction) * boundsSize / 2 / Size;

                //Cursor.transform.position = floatPoints;

                floatPoints.x += boundsSize / 2;
                floatPoints.y += boundsSize / 2;
                floatPoints.z += boundsSize / 2;
                floatPoints *= numPointsPerAxis;

                Vector3Int id = new Vector3Int((int)floatPoints.x, (int)floatPoints.y, (int)floatPoints.z);
                int index = id.z * numPointsPerAxis * numPointsPerAxis + id.y * numPointsPerAxis + id.x;
                index *= 4; //vector4 so every 4th is one element
                index += 3; //every 3th entry is the w value of the vector
                if (!offsets.ContainsKey(index))
                {
                    offsets[index] = .01f * direction;
                }
                else
                {
                    offsets[index] += .01f * direction;
                }
            }
        }

        foreach (var key in offsets.Keys)
        {
            points[key] -= offsets[key];
        }

        pointsBuffer.SetData(points);


        triangleBuffer.SetCounterValue(0);
        CubeMarcher.SetBuffer(0, "points", pointsBuffer);
        CubeMarcher.SetBuffer(0, "triangles", triangleBuffer);
        CubeMarcher.SetInt("numPointsPerAxis", numPointsPerAxis);
        CubeMarcher.SetFloat("isoLevel", IsoLevel);

        CubeMarcher.Dispatch(0, numThreadsPerAxis, numThreadsPerAxis, numThreadsPerAxis);

        // Get number of triangles in the triangle buffer
        ComputeBuffer.CopyCount(triangleBuffer, triCountBuffer, 0);
        int[] triCountArray = { 0 };
        triCountBuffer.GetData(triCountArray);
        int numTris = triCountArray[0];

        // Get triangle data from shader
        Triangle[] tris = new Triangle[numTris];
        triangleBuffer.GetData(tris, 0, 0, numTris);

        Mesh mesh = new Mesh();
        mesh.Clear();

        var vertices = new Vector3[numTris * 3];
        var meshTriangles = new int[numTris * 3];

        for (int i = 0; i < numTris; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                meshTriangles[i * 3 + j] = i * 3 + j;
                vertices[i * 3 + j] = tris[i][j];
            }
        }
        mesh.vertices = vertices;
        mesh.triangles = meshTriangles;

        mesh.RecalculateNormals();


        GetComponent<MeshFilter>().sharedMesh = mesh;
        GetComponent<MeshCollider>().sharedMesh = mesh;

        // Release buffers immediately in editor
        if (!Application.isPlaying)
        {
            ReleaseBuffers();
        }
    }

    struct Triangle
    {
#pragma warning disable 649 // disable unassigned variable warning
        public Vector3 a;
        public Vector3 b;
        public Vector3 c;

        public Vector3 this[int i]
        {
            get
            {
                switch (i)
                {
                    case 0:
                        return a;
                    case 1:
                        return b;
                    default:
                        return c;
                }
            }
        }
    }
}
