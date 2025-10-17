using UnityEngine;
using System.Collections.Generic;
using System.Linq;


#if UNITY_EDITOR
using UnityEditor;
#endif


public class generateRailing : MonoBehaviour
{
    [Header("Настройки склеивания граней")]
    [SerializeField] private GameObject stairOriginal;
    [SerializeField] private GameObject railingSegmentPrefab;
    [SerializeField] private float xOffsetrailing = 2f;
    [SerializeField] private int samplesPerStep = 3;
    [SerializeField] private float smoothing = 0.1f;
    [SerializeField] private int segmentCount = 10;

    [Header("Настройки оптимизации")]
    [Range(0.01f, 1f)]
    [SerializeField] private float simplifyRatio = 0.5f;


    [Header("Настройки следования")]
    private float weldDistance = 0.01f;
    private float faceDetectionAngle = 45f;
    private bool generateBothSides = true;
    private float overlap = 0.05f; 
    private float totalStairLength;
    private float segmentLength;

    private Mesh originalRailingMesh;
    private Vector3[] originalVertices;
    private List<GameObject> generatedSegments = new List<GameObject>();

    [System.Serializable]
    public class MeshFaceData
    {
        public int meshIndex;
        public Transform transform;
        public int vertexStartIndex;
        public int vertexCount;
        public List<int> vertexIndices;
        public List<Vector3> vertexPositions;
        public List<Vector3> vertexNormals;
        public List<int> frontFaceIndices = new List<int>();
        public List<Vector3> frontFacePositions = new List<Vector3>();
        public List<Vector3> frontFaceNormals = new List<Vector3>();

        public List<int> backFaceIndices = new List<int>();
        public List<Vector3> backFacePositions = new List<Vector3>();
        public List<Vector3> backFaceNormals = new List<Vector3>();
    }

    [ContextMenu("Генерировать перила")]
    public void GenerateRailings()
    {
        Debug.Log("=== Начало автоматической генерации перил ===");
        Debug.Log("Шаг 1: Генерация прямых перил...");
        GenerateStraightRailings();


#if UNITY_EDITOR
        EditorApplication.delayCall += () => {
            Debug.Log("Шаг 2: Склеивание соседних граней...");
            WeldAdjacentFacesAutomated();
            EditorApplication.delayCall += () => {
                Debug.Log("Шаг 3: Повтор формы лестницы...");
                FollowStairShapeAutomated();
                
                Debug.Log("=== Автоматическая генерация перил завершена ===");
            };
        };
#else
        WeldAdjacentFacesAutomated();
        FollowStairShapeAutomated();
        Debug.Log("=== Автоматическая генерация перил завершена ===");
#endif
    }

    private void WeldAdjacentFacesAutomated()
    {
        if (generatedSegments.Count == 0)
        {
            Debug.LogError("Нет сгенерированных сегментов для склеивания!");
            return;
        }
        Selection.objects = generatedSegments.ToArray();
        WeldAdjacentFaces();
        Debug.Log($"Автоматически склеены грани {generatedSegments.Count} сегментов");
    }

    private void FollowStairShapeAutomated()
    {
        GameObject weldedObject = GameObject.Find("Welded_Adjacent_Faces");
        if (weldedObject != null)
        {
            Selection.activeGameObject = weldedObject;
            if (stairOriginal != null)
            {
                railing_2 railingComponent = weldedObject.GetComponent<railing_2>();
                if (railingComponent == null)
                {
                    railingComponent = weldedObject.AddComponent<railing_2>();
                }
                railingComponent.stairOriginal = this.stairOriginal;
                railingComponent.FollowStairShape();
                Debug.Log("Форма лестницы применена к склеенным перилам");
            }
        }
        else
        {
            Debug.LogWarning("Не найден склеенный объект перил");
        }
    }

    public void WeldAdjacentFaces()
    {
        GameObject[] selectedObjects = Selection.gameObjects;
        if (selectedObjects.Length < 2)
        {
            Debug.LogError($"Выделено {selectedObjects.Length} объектов. Нужно хотя бы 2!");
            return;
        }
        var sortedObjects = selectedObjects
            .Where(obj => obj.GetComponent<MeshFilter>() != null)
            .OrderBy(obj => obj.transform.position.z)
            .ToArray();

        if (sortedObjects.Length < 2)
        {
            Debug.LogError("Не найдено мешей для склеивания!");
            return;
        }
        Debug.Log($"Склеиваем {sortedObjects.Length} мешей по оси Z");
        List<Vector3> allVertices = new List<Vector3>();
        List<Vector3> allNormals = new List<Vector3>();
        List<Vector2> allUV = new List<Vector2>();
        List<int> allTriangles = new List<int>();
        int vertexOffset = 0;

        for (int i = 0; i < sortedObjects.Length; i++)
        {
            MeshFilter meshFilter = sortedObjects[i].GetComponent<MeshFilter>();
            Mesh mesh = meshFilter.sharedMesh;
            Transform transform = meshFilter.transform;
            for (int j = 0; j < mesh.vertices.Length; j++)
            {
                Vector3 worldVertex = transform.TransformPoint(mesh.vertices[j]);
                allVertices.Add(worldVertex);

                if (j < mesh.normals.Length)
                    allNormals.Add(transform.TransformDirection(mesh.normals[j]));
                else
                    allNormals.Add(Vector3.up);

                if (j < mesh.uv.Length)
                    allUV.Add(mesh.uv[j]);
                else
                    allUV.Add(Vector2.zero);
            }
            for (int j = 0; j < mesh.triangles.Length; j++)
            {
                allTriangles.Add(mesh.triangles[j] + vertexOffset);
            }

            vertexOffset += mesh.vertices.Length;
        }
        Debug.Log($"Всего вершин до склеивания: {allVertices.Count}");
        WeldOnlyAdjacentFaces(allVertices, allNormals, allUV, allTriangles, sortedObjects);
        CreateCombinedMesh(allVertices, allNormals, allUV, allTriangles, "Welded_Adjacent_Faces");
    }

    private void WeldOnlyAdjacentFaces(List<Vector3> vertices, List<Vector3> normals, List<Vector2> uv,
                                      List<int> triangles, GameObject[] sortedObjects)
    {
        List<MeshFaceData> meshFaces = new List<MeshFaceData>();
        int vertexStartIndex = 0;
        for (int meshIndex = 0; meshIndex < sortedObjects.Length; meshIndex++)
        {
            MeshFilter meshFilter = sortedObjects[meshIndex].GetComponent<MeshFilter>();
            Mesh mesh = meshFilter.sharedMesh;
            Transform transform = meshFilter.transform;
            MeshFaceData faceData = new MeshFaceData
            {
                meshIndex = meshIndex,
                transform = transform,
                vertexStartIndex = vertexStartIndex,
                vertexCount = mesh.vertices.Length,
                vertexIndices = new List<int>(),
                vertexPositions = new List<Vector3>(),
                vertexNormals = new List<Vector3>()
            };
            FindMeshFaces(faceData, vertices, normals, vertexStartIndex, mesh.vertices.Length, transform);
            meshFaces.Add(faceData);
            vertexStartIndex += mesh.vertices.Length;
        }
        Dictionary<Vector3, int> vertexMap = new Dictionary<Vector3, int>();
        List<int> newVertexIndices = new List<int>();
        List<Vector3> newVertices = new List<Vector3>();
        List<Vector3> newNormals = new List<Vector3>();
        List<Vector2> newUV = new List<Vector2>();
        for (int i = 0; i < vertices.Count; i++)
        {
            newVertices.Add(vertices[i]);
            newNormals.Add(normals[i]);
            newUV.Add(uv[i]);
            newVertexIndices.Add(i);
            vertexMap[vertices[i]] = i;
        }
        for (int i = 0; i < meshFaces.Count - 1; i++)
        {
            MeshFaceData currentMesh = meshFaces[i];
            MeshFaceData nextMesh = meshFaces[i + 1];
            Debug.Log($"Склеиваем меш {i} (задняя грань) с мешем {i + 1} (передняя грань)");
            WeldFaces(currentMesh.backFaceIndices, currentMesh.backFacePositions, currentMesh.backFaceNormals,
                     nextMesh.frontFaceIndices, nextMesh.frontFacePositions, nextMesh.frontFaceNormals,
                     newVertexIndices, vertexMap);
        }
        List<int> newTriangles = new List<int>();
        for (int i = 0; i < triangles.Count; i++)
        {
            int oldIndex = triangles[i];
            int newIndex = newVertexIndices[oldIndex];
            newTriangles.Add(newIndex);
        }
        vertices.Clear();
        vertices.AddRange(newVertices);
        normals.Clear();
        normals.AddRange(newNormals);
        uv.Clear();
        uv.AddRange(newUV);
        triangles.Clear();
        triangles.AddRange(newTriangles);

        Debug.Log($"После склеивания граней: {vertices.Count} вершин");
    }

    private void FindMeshFaces(MeshFaceData faceData, List<Vector3> vertices, List<Vector3> normals,
                             int startIndex, int count, Transform transform)
    {
        for (int i = startIndex; i < startIndex + count; i++)
        {
            Vector3 worldNormal = normals[i];
            if (Vector3.Angle(worldNormal, Vector3.forward) < faceDetectionAngle)
            {
                faceData.frontFaceIndices.Add(i);
                faceData.frontFacePositions.Add(vertices[i]);
                faceData.frontFaceNormals.Add(normals[i]);
            }
            else if (Vector3.Angle(worldNormal, Vector3.back) < faceDetectionAngle)
            {
                faceData.backFaceIndices.Add(i);
                faceData.backFacePositions.Add(vertices[i]);
                faceData.backFaceNormals.Add(normals[i]);
            }
        }
        Debug.Log($"Меш {faceData.meshIndex}: передняя грань - {faceData.frontFaceIndices.Count} вершин, задняя грань - {faceData.backFaceIndices.Count} вершин");
    }

    private void WeldFaces(List<int> sourceIndices, List<Vector3> sourcePositions, List<Vector3> sourceNormals,
                          List<int> targetIndices, List<Vector3> targetPositions, List<Vector3> targetNormals,
                          List<int> vertexIndices, Dictionary<Vector3, int> vertexMap)
    {
        if (sourceIndices.Count == 0 || targetIndices.Count == 0)
        {
            Debug.LogWarning("Одна из граней пустая, пропускаем склеивание");
            return;
        }
        int weldedCount = 0;
        for (int i = 0; i < targetIndices.Count; i++)
        {
            int targetVertexIndex = targetIndices[i];
            Vector3 targetPosition = targetPositions[i];
            int closestSourceIndex = -1;
            float closestDistance = float.MaxValue;
            for (int j = 0; j < sourceIndices.Count; j++)
            {
                float distance = Vector3.Distance(sourcePositions[j], targetPosition);
                if (distance < closestDistance && distance <= weldDistance)
                {
                    closestDistance = distance;
                    closestSourceIndex = sourceIndices[j];
                }
            }
            if (closestSourceIndex != -1)
            {
                vertexIndices[targetVertexIndex] = vertexIndices[closestSourceIndex];
                weldedCount++;
            }
        }

        Debug.Log($"Склеено {weldedCount} вершин между гранями");
    }

    private void CreateCombinedMesh(List<Vector3> vertices, List<Vector3> normals, List<Vector2> uv,
                                   List<int> triangles, string name)
    {
        GameObject combinedObject = new GameObject(name);
        combinedObject.transform.position = Vector3.zero;

        MeshFilter meshFilter = combinedObject.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = combinedObject.AddComponent<MeshRenderer>();

        Mesh combinedMesh = new Mesh();

        combinedMesh.vertices = vertices.ToArray();
        combinedMesh.normals = normals.ToArray();
        combinedMesh.uv = uv.ToArray();
        combinedMesh.triangles = triangles.ToArray();
        combinedMesh.RecalculateBounds();
        combinedMesh.RecalculateTangents();
        meshFilter.sharedMesh = combinedMesh;
        MeshCollider collider = combinedObject.AddComponent<MeshCollider>();
        collider.sharedMesh = combinedMesh;
        GameObject firstSelected = Selection.activeGameObject;
        if (firstSelected != null)
        {
            MeshRenderer firstRenderer = firstSelected.GetComponent<MeshRenderer>();
            if (firstRenderer != null)
            {
                meshRenderer.sharedMaterial = firstRenderer.sharedMaterial;
            }
        }
        Debug.Log($"Создан объединенный меш: {combinedMesh.vertexCount} вершин, {combinedMesh.triangles.Length / 3} треугольников");
    }

    [ContextMenu("Показать информацию о гранях выделенных мешей")]
    public void ShowFacesInfo()
    {
        GameObject[] selectedObjects = Selection.gameObjects;
        foreach (GameObject obj in selectedObjects)
        {
            MeshFilter meshFilter = obj.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                Mesh mesh = meshFilter.sharedMesh;
                Debug.Log($"Меш: {obj.name}");
                Debug.Log($"  Вершин: {mesh.vertexCount}, Треугольников: {mesh.triangles.Length / 3}");
                Debug.Log($"  Позиция: {obj.transform.position}");
                Debug.Log($"  Границы: {mesh.bounds}");
                AnalyzeMeshFaces(mesh, obj.transform);
            }
        }
    }

    private void AnalyzeMeshFaces(Mesh mesh, Transform transform)
    {
        Vector3[] vertices = mesh.vertices;
        Vector3[] normals = mesh.normals;

        int frontFacing = 0;
        int backFacing = 0;
        int leftFacing = 0;
        int rightFacing = 0;
        int upFacing = 0;
        int downFacing = 0;

        for (int i = 0; i < normals.Length; i++)
        {
            Vector3 worldNormal = transform.TransformDirection(normals[i]);

            if (Vector3.Angle(worldNormal, Vector3.forward) < 45f) frontFacing++;
            else if (Vector3.Angle(worldNormal, Vector3.back) < 45f) backFacing++;
            else if (Vector3.Angle(worldNormal, Vector3.left) < 45f) leftFacing++;
            else if (Vector3.Angle(worldNormal, Vector3.right) < 45f) rightFacing++;
            else if (Vector3.Angle(worldNormal, Vector3.up) < 45f) upFacing++;
            else if (Vector3.Angle(worldNormal, Vector3.down) < 45f) downFacing++;
        }

        Debug.Log($"  Грани - Перед: {frontFacing}, Зад: {backFacing}, Лево: {leftFacing}, Право: {rightFacing}, Верх: {upFacing}, Низ: {downFacing}");
    }

    public void FollowStairShape()
    {
        if (stairOriginal == null)
        {
            Debug.LogError("Не назначен оригинал лестницы!");
            return;
        }
        MeshFilter railingFilter = GetComponent<MeshFilter>();
        if (railingFilter == null)
        {
            Debug.LogError("Нет MeshFilter на перилах!");
            return;
        }
        if (originalRailingMesh == null)
        {
            originalRailingMesh = railingFilter.sharedMesh;
            originalVertices = originalRailingMesh.vertices;
        }
        Vector3[] stairPath = GetStairPathPointsRailing();
        if (stairPath.Length < 2)
        {
            Debug.LogError("Не удалось получить путь лестницы!");
            return;
        }
        Mesh deformedMesh = DeformMeshToPath(originalRailingMesh, stairPath);
        railingFilter.sharedMesh = deformedMesh;
        Debug.Log($"Перила повторяют форму лестницы с {stairPath.Length} точками");
    }

    private Vector3[] GetStairPathPointsRailing()
    {
        List<Vector3> pathPoints = new List<Vector3>();
        GameObject[] stairs = FindAllStairDuplicates();
        if (stairs.Length < 2) return pathPoints.ToArray();
        stairs = stairs.OrderBy(s => GetStairIndex(s)).ToArray();

        for (int i = 0; i < stairs.Length; i++)
        {
            Vector3 stairPos = stairs[i].transform.position;
            pathPoints.Add(stairPos);
            if (i < stairs.Length - 1)
            {
                Vector3 nextStairPos = stairs[i + 1].transform.position;
                for (int j = 1; j <= samplesPerStep; j++)
                {
                    float t = (float)j / (samplesPerStep + 1);
                    Vector3 intermediatePoint = Vector3.Lerp(stairPos, nextStairPos, t);
                    if (smoothing > 0)
                    {
                        intermediatePoint = ApplySmoothing(intermediatePoint, pathPoints, smoothing);
                    }

                    pathPoints.Add(intermediatePoint);
                }
            }
        }
        return pathPoints.ToArray();
    }

    private GameObject[] FindAllStairDuplicates()
    {
        List<GameObject> duplicates = new List<GameObject>();
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        foreach (GameObject obj in allObjects)
        {
            if (obj != null && obj != stairOriginal &&
                obj.name.StartsWith(stairOriginal.name + "_Duplicate"))
            {
                duplicates.Add(obj);
            }
        }
        return duplicates.ToArray();
    }

    private int GetStairIndex(GameObject stair)
    {
        string name = stair.name;
        string indexStr = name.Replace(stairOriginal.name + "_Duplicate_", "");
        return int.TryParse(indexStr, out int index) ? index : 0;
    }

    private Vector3 ApplySmoothing(Vector3 point, List<Vector3> existingPoints, float smoothFactor)
    {
        if (existingPoints.Count < 2) return point;

        Vector3 lastPoint = existingPoints[existingPoints.Count - 1];
        Vector3 direction = (point - lastPoint).normalized;

        return Vector3.Lerp(point, lastPoint + direction * Vector3.Distance(lastPoint, point), smoothFactor);
    }

    private Mesh DeformMeshToPath(Mesh originalMesh, Vector3[] pathPoints)
    {
        Mesh deformedMesh = new Mesh();

        Vector3[] originalVerts = originalMesh.vertices;
        Vector3[] originalNormals = originalMesh.normals;
        Vector3[] deformedVerts = new Vector3[originalVerts.Length];
        Vector3[] deformedNormals = new Vector3[originalVerts.Length];
        Bounds originalBounds = originalMesh.bounds;
        float meshLength = originalBounds.size.z;
        float meshStartZ = originalBounds.min.z;

        for (int i = 0; i < originalVerts.Length; i++)
        {
            Vector3 localVertex = originalVerts[i];
            float t = Mathf.InverseLerp(meshStartZ, meshStartZ + meshLength, localVertex.z);
            t = Mathf.Clamp01(t);
            int pathIndex = Mathf.FloorToInt(t * (pathPoints.Length - 1));
            pathIndex = Mathf.Clamp(pathIndex, 0, pathPoints.Length - 2);

            float segmentT = t * (pathPoints.Length - 1) - pathIndex;
            segmentT = Mathf.Clamp01(segmentT);
            Vector3 pathPosition = Vector3.Lerp(pathPoints[pathIndex], pathPoints[pathIndex + 1], segmentT);
            Vector3 pathDirection = (pathPoints[Mathf.Min(pathIndex + 1, pathPoints.Length - 1)] -
                                   pathPoints[Mathf.Max(pathIndex - 1, 0)]).normalized;
            if (pathDirection.magnitude < 0.1f) pathDirection = Vector3.forward;
            Vector3 pathRight = Vector3.Cross(pathDirection, Vector3.up).normalized;
            Vector3 pathUp = Vector3.up;
            deformedVerts[i] = pathPosition +
                              pathRight * localVertex.x +
                              pathUp * localVertex.y;
            Quaternion rotation = Quaternion.FromToRotation(Vector3.forward, pathDirection);
            deformedNormals[i] = rotation * originalNormals[i];
            deformedNormals[i] = deformedNormals[i].normalized;
        }
        deformedMesh.vertices = deformedVerts;
        deformedMesh.normals = deformedNormals;
        deformedMesh.uv = originalMesh.uv;
        deformedMesh.triangles = originalMesh.triangles;

        deformedMesh.RecalculateBounds();
        deformedMesh.RecalculateTangents();

        return deformedMesh;
    }
    [ContextMenu("Вернуть оригинальную форму")]
    public void ResetToOriginalShape()
    {
        MeshFilter railingFilter = GetComponent<MeshFilter>();
        if (railingFilter != null && originalRailingMesh != null)
        {
            railingFilter.sharedMesh = originalRailingMesh;
            Debug.Log("Форма перил сброшена к оригиналу");
        }
    }
    public void GenerateStraightRailings()
    {
        if (stairOriginal == null || railingSegmentPrefab == null)
        {
            Debug.LogError("Не назначены оригинал лестницы или префаб перил!");
            return;
        }
        ClearGeneratedSegments();

        Debug.Log($"Лестница: {totalStairLength:F2}m, Сегмент: {segmentLength:F2}m, Сегментов: {segmentCount}");
        GenerateRailingSide(false);

        if (generateBothSides)
        {
            GenerateRailingSide(true);
        }
        Debug.Log($"Сгенерировано прямых перил: {segmentCount} сегментов на каждой стороне");
    }

    private void GenerateRailingSide(bool isLeftSide)
    {
        string sideName = isLeftSide ? "Left" : "Right";
        float sideMultiplier = isLeftSide ? -1f : 1f;
        GameObject railingsParent = new GameObject($"StraightRailings_{sideName}");
        railingsParent.transform.position = Vector3.zero;
        float segmentLength = GetRailingSegmentLength();
        float effectiveLength = segmentLength - overlap;
        Vector3 startPosition = new Vector3(xOffsetrailing * sideMultiplier, 0f, 0f);
        for (int i = 0; i < segmentCount; i++)
        {
            Vector3 segmentPosition = startPosition + new Vector3(0f, 0f, i * effectiveLength);
            GameObject segment = Instantiate(railingSegmentPrefab);
            segment.name = $"Railing_{sideName}_{i + 1}";
            segment.transform.position = segmentPosition;
            segment.transform.rotation = Quaternion.identity; 
            segment.transform.SetParent(railingsParent.transform);
            generatedSegments.Add(segment);
        }
    }

    private void ClearGeneratedSegments()
    {
        foreach (var segment in generatedSegments)
        {
            if (segment != null)
            {
                if (Application.isPlaying)
                    Destroy(segment);
                else
                    DestroyImmediate(segment);
            }
        }
        generatedSegments.Clear();
    }

    private float GetRailingSegmentLength()
    {
        if (railingSegmentPrefab == null) return 1f;
        MeshFilter meshFilter = railingSegmentPrefab.GetComponent<MeshFilter>();
        if (meshFilter != null && meshFilter.sharedMesh != null)
        {
            float length = meshFilter.sharedMesh.bounds.size.z * railingSegmentPrefab.transform.lossyScale.z;
            Debug.Log($"Длина сегмента: {length:F2}m");
            return length;
        }
        Renderer renderer = railingSegmentPrefab.GetComponent<Renderer>();
        if (renderer != null)
        {
            float length = renderer.bounds.size.z;
            Debug.Log($"Длина сегмента: {length:F2}m");
            return length;
        }
        Debug.LogWarning("Не удалось определить длину сегмента, используется 1m");
        return 1f;
    }


    [ContextMenu("Уменьшить полигоны этого меша")]
    public void SimplifyThisMesh()
    {
#if UNITY_EDITOR
    MeshFilter meshFilter = GetComponent<MeshFilter>();
    if (meshFilter == null || meshFilter.sharedMesh == null)
    {
        Debug.LogError("На этом объекте нет меша!");
        return;
    }

    SimplifyMesh(meshFilter);
#else
        Debug.LogWarning("Упрощение мешей доступно только в редакторе Unity");
#endif
    }


    private int SimplifyMesh(MeshFilter meshFilter)
    {
#if UNITY_EDITOR
    Mesh originalMesh = meshFilter.sharedMesh;
    int originalVertexCount = originalMesh.vertexCount;
    int originalTriangleCount = originalMesh.triangles.Length / 3;
    Mesh simplifiedMesh = ManualSimplifyMesh(originalMesh, simplifyRatio);

    int newVertexCount = simplifiedMesh.vertexCount;
    int newTriangleCount = simplifiedMesh.triangles.Length / 3;

    meshFilter.sharedMesh = simplifiedMesh;
    MeshCollider meshCollider = meshFilter.GetComponent<MeshCollider>();
    if (meshCollider != null)
    {
        meshCollider.sharedMesh = simplifiedMesh;
    }

    int verticesReduced = originalVertexCount - newVertexCount;
    int trianglesReduced = originalTriangleCount - newTriangleCount;

    Debug.Log($"Меш {meshFilter.gameObject.name} упрощен: " +
             $"Вершины {originalVertexCount} → {newVertexCount} (-{verticesReduced}), " +
             $"Треугольники {originalTriangleCount} → {newTriangleCount} (-{trianglesReduced})");

    return verticesReduced;
#else
        return 0;
#endif
    }

private Mesh ManualSimplifyMesh(Mesh originalMesh, float ratio = 0.5f)
{
    if (Mathf.Approximately(ratio, 1f))
    {
        Debug.Log("Коэффициент упрощения 1.0 - меш не изменен");
        return Instantiate(originalMesh);
    }

    Mesh simplifiedMesh = new Mesh();

    Vector3[] originalVertices = originalMesh.vertices;
    Vector3[] originalNormals = originalMesh.normals;
    Vector2[] originalUV = originalMesh.uv;
    int[] originalTriangles = originalMesh.triangles;
    int targetVertexCount = Mathf.Max(8, Mathf.RoundToInt(originalVertices.Length * ratio));
    
    List<Vector3> newVertices = new List<Vector3>();
    List<Vector3> newNormals = new List<Vector3>();
    List<Vector2> newUV = new List<Vector2>();
    List<int> newTriangles = new List<int>();

    Dictionary<int, int> vertexMap = new Dictionary<int, int>();
    float step = (float)originalVertices.Length / targetVertexCount;
    
    for (int i = 0; i < originalVertices.Length; i++)
    {
        if (i % Mathf.RoundToInt(step) == 0)
        {
            vertexMap[i] = newVertices.Count;
            newVertices.Add(originalVertices[i]);
            
            if (i < originalNormals.Length)
                newNormals.Add(originalNormals[i]);
            
            if (i < originalUV.Length)
                newUV.Add(originalUV[i]);
        }
    }
    int trianglesPreserved = 0;
    for (int i = 0; i < originalTriangles.Length; i += 3)
    {
        int v1 = originalTriangles[i];
        int v2 = originalTriangles[i + 1];
        int v3 = originalTriangles[i + 2];

        if (vertexMap.ContainsKey(v1) && vertexMap.ContainsKey(v2) && vertexMap.ContainsKey(v3))
        {
            newTriangles.Add(vertexMap[v1]);
            newTriangles.Add(vertexMap[v2]);
            newTriangles.Add(vertexMap[v3]);
            trianglesPreserved++;
        }
    }

        Debug.Log($"Сохранено треугольников: {trianglesPreserved} из {originalTriangles.Length / 3}");
    
    if (newTriangles.Count == 0)
    {
        Debug.LogWarning("Упрощение удалило все треугольники! Используется более мягкое упрощение.");
        return SoftSimplifyMesh(originalMesh, ratio * 2f);
    }
    simplifiedMesh.vertices = newVertices.ToArray();
    
    if (newNormals.Count == newVertices.Count)
        simplifiedMesh.normals = newNormals.ToArray();
    else
        simplifiedMesh.RecalculateNormals();
    
    if (newUV.Count == newVertices.Count)
        simplifiedMesh.uv = newUV.ToArray();
    
    simplifiedMesh.triangles = newTriangles.ToArray();

    simplifiedMesh.RecalculateBounds();
    simplifiedMesh.RecalculateTangents();

    return simplifiedMesh;
}

private Mesh SoftSimplifyMesh(Mesh originalMesh, float ratio = 0.5f)
{
    Mesh simplifiedMesh = new Mesh();

    Vector3[] originalVertices = originalMesh.vertices;
    int[] originalTriangles = originalMesh.triangles;

    float mergeDistance = 0.01f * (1f - ratio); 
    
    List<Vector3> newVertices = new List<Vector3>();
    List<int> newTriangles = new List<int>();

    Dictionary<int, int> vertexMap = new Dictionary<int, int>();

    for (int i = 0; i < originalVertices.Length; i++)
    {
        bool merged = false;
for (int j = 0; j < newVertices.Count; j++)
        {
            if (Vector3.Distance(originalVertices[i], newVertices[j]) < mergeDistance)
            {
                vertexMap[i] = j;
                merged = true;
                break;
            }
        }
        
        if (!merged)
        {
            vertexMap[i] = newVertices.Count;
            newVertices.Add(originalVertices[i]);
        }
    }
    for (int i = 0; i < originalTriangles.Length; i += 3)
    {
        int v1 = vertexMap[originalTriangles[i]];
        int v2 = vertexMap[originalTriangles[i + 1]];
        int v3 = vertexMap[originalTriangles[i + 2]];
        if (v1 != v2 && v2 != v3 && v3 != v1)
        {
            newTriangles.Add(v1);
            newTriangles.Add(v2);
            newTriangles.Add(v3);
        }
    }

    simplifiedMesh.vertices = newVertices.ToArray();
    simplifiedMesh.triangles = newTriangles.ToArray();

    simplifiedMesh.RecalculateNormals();
    simplifiedMesh.RecalculateBounds();
    simplifiedMesh.RecalculateTangents();

    return simplifiedMesh;
}

    #if UNITY_EDITOR
    public class Railing2Editor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            railing_2 script = (railing_2)target;

            GUILayout.Space(10);
            GUILayout.Label("Действия", EditorStyles.boldLabel);
              if (GUILayout.Button("Генерировать перила"))
            {
                script.GenerateRailings();
            }
             if (GUILayout.Button("Показать информацию о гранях"))
            {
                script.ShowFacesInfo();
            }
             if (GUILayout.Button("Уменьшить полигоны выделенных мешей"))

        if (GUILayout.Button("Уменьшить полигоны этого меша"))
        {
            script.SimplifyThisMesh();
        }
        
    }
}
#endif
        
}