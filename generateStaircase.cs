using UnityEngine;
using System.Collections.Generic;
using System.Linq;


#if UNITY_EDITOR
using UnityEditor;
#endif

public class generateStaircase : MonoBehaviour
{
    [Header("Настройки дублирования")]
    [SerializeField] private GameObject originalObject;
    [SerializeField] private int duplicateCount = 11;
    [SerializeField] private Vector3 offset = new Vector3(0f, 1f, 1f);
    [SerializeField] public float offset1 = 0.1f;
    [SerializeField] public float offsethight = 0.1f;
    [SerializeField] private float finalRotationY = 90f;
    [SerializeField] private float positionMultiplier = 0.1f; 
    private int generationCounter = 1;




    [ContextMenu("Сгенерировать дубликаты")]
    public void GenerateDuplicates()
    {
        if (originalObject == null)
        {
            Debug.LogError("Назначьте оригинальный объект в инспекторе!");
            return;
        }

        GameObject generationContainer = new GameObject($"CollectionStep{generationCounter}");
        generationCounter++;
        generationContainer.transform.position = originalObject.transform.position;

        GameObject current = originalObject;

        for (int i = 0; i < duplicateCount; i++)
        {
            GameObject duplicate = Instantiate(current);
            duplicate.name = $"{originalObject.name}_Duplicate_{i + 1}";
            duplicate.transform.position = current.transform.position + offset;
            duplicate.transform.SetParent(generationContainer.transform);
            current = duplicate;
        }

        Debug.Log($"Создано {duplicateCount} дубликатов!");
    }





    [ContextMenu("Очистить дубликаты")]
    public void ClearDuplicates()
    {
        if (originalObject == null) return;

        GameObject[] allObjects = FindObjectsOfType<GameObject>();

        foreach (GameObject obj in allObjects)
        {
            if (obj != null && obj.name.StartsWith(originalObject.name + "_Duplicate"))
            {
                if (Application.isPlaying)
                    Destroy(obj);
                else
                    DestroyImmediate(obj);
            }
        }
    }





    [ContextMenu("Применить поворот вокруг оригинала")]
    public void ApplyRotationAroundOriginal()
    {
        List<GameObject> duplicates = FindAllDuplicates();

        if (duplicates.Count == 0)
        {
            Debug.LogWarning("Дубликаты не найдены! Сначала создайте дубликаты.");
            return;
        }
        duplicates = duplicates.OrderBy(go => GetDuplicateIndex(go)).ToList();
        Debug.Log($"Найдено {duplicates.Count} дубликатов");
        ApplyRotationAroundCenter(duplicates);

    }




    private List<GameObject> FindAllDuplicates()
    {
        List<GameObject> duplicates = new List<GameObject>();
        GameObject[] allObjects = FindObjectsOfType<GameObject>();

        foreach (GameObject obj in allObjects)
        {
            if (obj != null && obj != originalObject && obj != this.gameObject &&
                obj.name.StartsWith(originalObject.name + "_Duplicate"))
            {
                duplicates.Add(obj);
            }
        }

        return duplicates;
    }

    private int GetDuplicateIndex(GameObject duplicate)
    {
        string name = duplicate.name;
        string indexStr = name.Replace(originalObject.name + "_Duplicate_", "");

        if (int.TryParse(indexStr, out int index))
        {
            return index;
        }

        return 0;
    }

    private void ApplyRotationAroundCenter(List<GameObject> duplicates)
    {

        Quaternion originalRotation = originalObject.transform.rotation;
        Vector3 currentPosition = originalObject.transform.position;

        for (int i = 0; i < duplicateCount; i++)
        {
            GameObject duplicate = duplicates[i];
            float progress = (float)(i + 1) / duplicates.Count;
            float currentRotationY = finalRotationY * progress;
            Quaternion targetRotation = originalRotation * Quaternion.Euler(0f, currentRotationY, 0f);
            duplicate.transform.rotation = targetRotation;
            Vector3 basePosition = currentPosition + (offset * (i + 1));
            Vector3 localOffset = CalculatePositionOffset(currentRotationY, progress);
            duplicate.transform.position = basePosition + duplicate.transform.TransformDirection(localOffset);

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                Undo.RecordObject(duplicate.transform, "Apply Rotation and Position");
            }
#endif

            Debug.Log($"Дубликат {i + 1}: поворот Z = {currentRotationY:F1}°, смещение = {localOffset}");
        }
    }
    private void ApplyRotationBasedPosition(GameObject duplicate, int index)
    {
        float progress = (float)(index + 1) / duplicateCount;
        float currentRotationY = finalRotationY * progress;

        Vector3 rotationBasedOffset = CalculatePositionOffset(currentRotationY, progress);
        duplicate.transform.position += rotationBasedOffset;
    }

    private void ApplyRotation(GameObject duplicate, int index)
    {
        float progress = (float)(index + 1) / duplicateCount;
        float currentRotationY = finalRotationY * progress;

        Quaternion targetRotation = originalObject.transform.rotation * Quaternion.Euler(0f, 0f, currentRotationY);
        duplicate.transform.rotation = targetRotation;
    }

    private Vector3 CalculatePositionOffset(float rotationAngle, float progress)
    {
        float direction = Mathf.Sign(rotationAngle);
        float intensity = Mathf.Abs(rotationAngle) / 360f;
        float baseOffset = intensity * positionMultiplier * 10f;
        float baseOffsetz = intensity * positionMultiplier * 10f;
        float offsetValue = baseOffset * progress;
        float offsetValuez = baseOffsetz * progress;

        // Смещение по X и Y в зависимости от направления (если захочешь поменять rotation)
        Vector3 localOffset = new Vector3(
            offsetValue * direction,  // X 
            offsetValuez * direction,  // Y 
            0f   // Z    
        );

        return localOffset;
    }





[ContextMenu("Объединить выделенные объекты")]
public void CombineSelectedObjects()
{
    GameObject[] selectedObjects = Selection.gameObjects;

    if (selectedObjects.Length < 2)
    {
        Debug.LogWarning("Выберите хотя бы 2 объекта для объединения!");
        return;
    }

    CombineMeshes(selectedObjects);
}

private void CombineMeshes(GameObject[] objectsToCombine)
{
    GameObject combinedObject = new GameObject("Combined_Stairs");
    combinedObject.transform.position = Vector3.zero;

    List<CombineInstance> combineInstances = new List<CombineInstance>();
    List<Material> materials = new List<Material>();
    foreach (GameObject obj in objectsToCombine)
    {
        MeshFilter meshFilter = obj.GetComponent<MeshFilter>();
        if (meshFilter != null && meshFilter.sharedMesh != null)
        {
            CombineInstance combine = new CombineInstance();
            combine.mesh = meshFilter.sharedMesh;
            combine.transform = obj.transform.localToWorldMatrix;
            combineInstances.Add(combine);

            MeshRenderer renderer = obj.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                materials.AddRange(renderer.sharedMaterials);
            }
        }

        SkinnedMeshRenderer skinnedMesh = obj.GetComponent<SkinnedMeshRenderer>();
        if (skinnedMesh != null && skinnedMesh.sharedMesh != null)
        {
            Mesh bakedMesh = new Mesh();
            skinnedMesh.BakeMesh(bakedMesh);

            CombineInstance combine = new CombineInstance();
            combine.mesh = bakedMesh;
            combine.transform = obj.transform.localToWorldMatrix;
            combineInstances.Add(combine);

            if (skinnedMesh.sharedMaterials != null)
            {
                materials.AddRange(skinnedMesh.sharedMaterials);
            }
        }
    }

    if (combineInstances.Count == 0)
    {
        Debug.LogError("Нет мешей для объединения!");
        DestroyImmediate(combinedObject);
        return;
    }

    Debug.Log($"Найдено мешей для объединения: {combineInstances.Count}");

    MeshFilter combinedFilter = combinedObject.AddComponent<MeshFilter>();
    MeshRenderer combinedRenderer = combinedObject.AddComponent<MeshRenderer>();
    Mesh finalMesh = new Mesh();
    finalMesh.CombineMeshes(combineInstances.ToArray(), true);
    combinedFilter.sharedMesh = finalMesh;
    
    if (materials.Count > 0)
        {
            combinedRenderer.sharedMaterials = materials.Distinct().ToArray();
        }
        else
        {
            combinedRenderer.sharedMaterial = new Material(Shader.Find("Standard"));
        }

    finalMesh.RecalculateNormals();
    finalMesh.RecalculateBounds();

    Debug.Log($"Создан один объединенный меш! Вершин: {finalMesh.vertexCount}");
    foreach (GameObject obj in objectsToCombine)
    {
        if (obj != originalObject)
        {
            DestroyImmediate(obj);
        }
    }
    Debug.Log($"Удалено {objectsToCombine.Length - 1} старых дубликатов");
}



    [ContextMenu("Проверить компоненты выделенных объектов")]
    public void CheckSelectedComponents()
    {
        GameObject[] selectedObjects = Selection.gameObjects;

        foreach (GameObject obj in selectedObjects)
        {
            MeshFilter mf = obj.GetComponent<MeshFilter>();
        }
    }




    [ContextMenu("генерировать спираль")]
    public void SpiralObjects()
    {
        GameObject[] existingSteps = FindAllStepDuplicates();

        int N = existingSteps.Length;

        if (N == 0)
        {
            Debug.LogError("Не найдено дубликатов ступенек!");
            return;
        }
        float stepLength = GetStepLength(existingSteps[0]);
        float R = (N * (stepLength + offset1)) / (2f * Mathf.PI);
        Debug.Log($"Рассчитанный радиус: {R}, Ступенек: {N}, Длина ступеньки: {stepLength}");
        for (int i = 0; i < N; i++)
        {
            PlaceStepOnCircle(existingSteps[i], i, N, R);
        }
    }
    
    private GameObject[] FindAllStepDuplicates()
    {
        var allObjects = Resources.FindObjectsOfTypeAll<GameObject>()
        .Where(go => go.name.Contains("_") && !go.name.Contains("(Clone)"))
        .ToList();
        var sortedObjects = allObjects
            .OrderBy(go =>
            {
                string name = go.name;
                int underscoreIndex = name.LastIndexOf('_');
                if (underscoreIndex >= 0 && underscoreIndex < name.Length - 1)
                {
                    string numberStr = name.Substring(underscoreIndex + 1);
                    if (int.TryParse(numberStr, out int number))
                    {
                        return number;
                    }
                }
                return 0;
            })
            .ToArray();

        return sortedObjects;
    }

    private float GetStepLength(GameObject step)
    {
        float length = 0f;
        Renderer renderer = step.GetComponent<Renderer>();
        if (renderer != null)
        {
            length = renderer.bounds.size.z;
            Debug.Log($"Длина через Renderer: {length}");
            return length;
        }
        Collider collider = step.GetComponent<Collider>();
        if (collider != null)
        {
            length = collider.bounds.size.z;
            Debug.Log($"Длина через Collider: {length}");
            return length;
        }
        length = step.transform.lossyScale.z;
        Debug.Log($"Длина через Scale: {length}");
        return length;
    }

    private void PlaceStepOnCircle(GameObject step, int index, int totalSteps, float radius)
    {
        float angle = index * (2f * Mathf.PI / totalSteps);
        Vector3 position = new Vector3(
            radius * Mathf.Cos(angle),
            index * offsethight, 
            radius * Mathf.Sin(angle)
        );
        Vector3 tangentDirection = new Vector3(-Mathf.Sin(angle), 0f, Mathf.Cos(angle));
        Quaternion rotation = Quaternion.LookRotation(tangentDirection, Vector3.up);
        step.transform.position = position;
        step.transform.rotation = rotation;

        Debug.Log($"Ступенька {index}: позиция {position}, угол {angle * Mathf.Rad2Deg}°");
    }

    public void FindAndCountSteps()
    {
        var steps = FindAllStepDuplicates();
        Debug.Log($"Найдено ступенек: {steps.Length}");

        if (steps.Length > 0)
        {
            float length = GetStepLength(steps[0]);
            Debug.Log($"Длина первой ступеньки: {length}");
        }
    }




    [ContextMenu("Синхронизировать Blend Shapes")]
    public void SyncBlendShapes()
    {
        if (originalObject == null)
        {
            Debug.Log("Оригинальный объект не назначен");
            return;
        }
        SkinnedMeshRenderer originalSkinned = originalObject.GetComponent<SkinnedMeshRenderer>();
        if (originalSkinned == null)
        {
            Debug.Log("У оригинала нет SkinnedMeshRenderer");
            return;
        }

        int updatedCount = 0;
        GameObject[] allObjects = GameObject.FindObjectsOfType<GameObject>();

        foreach (GameObject obj in allObjects)
        {
            if (obj != originalObject && obj.name.Contains("_Duplicate"))
            {
                SyncBlendShapesForDuplicate(originalSkinned, obj);
                updatedCount++;
            }
        }

        Debug.Log($"Синхронизировано Blend Shapes для {updatedCount} дубликатов");
    }





    private void SyncBlendShapesForDuplicate(SkinnedMeshRenderer original, GameObject duplicate)
    {
        SkinnedMeshRenderer duplicateSkinned = duplicate.GetComponent<SkinnedMeshRenderer>();
        if (duplicateSkinned == null)
        {
            Debug.Log($"У дубликата {duplicate.name} нет SkinnedMeshRenderer");
            return;
        }
        for (int i = 0; i < original.sharedMesh.blendShapeCount; i++)
        {
            float weight = original.GetBlendShapeWeight(i);
            duplicateSkinned.SetBlendShapeWeight(i, weight);
        }
    }
}