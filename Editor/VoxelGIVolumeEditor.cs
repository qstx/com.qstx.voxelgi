using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using QSTX.VoxelGI;

namespace QSTX.VoxelGI.Editor
{
[CustomEditor(typeof(VoxelGIVolume))]
[CanEditMultipleObjects]
public sealed class VoxelGIVolumeEditor : UnityEditor.Editor
{
    SerializedProperty m_IsGlobal;
    SerializedProperty m_Priority;
    SerializedProperty m_BlendDistance;
    SerializedProperty m_Weight;
    SerializedProperty m_SharedProfile;
    SerializedProperty m_VoxelizationBounds;
    UnityEditor.Editor m_ProfileEditor;

    void OnEnable()
    {
        m_IsGlobal = serializedObject.FindProperty("m_IsGlobal");
        m_Priority = serializedObject.FindProperty("priority");
        m_BlendDistance = serializedObject.FindProperty("blendDistance");
        m_Weight = serializedObject.FindProperty("weight");
        m_SharedProfile = serializedObject.FindProperty("sharedProfile");
        m_VoxelizationBounds = serializedObject.FindProperty("m_VoxelizationBounds");
    }

    void OnDisable()
    {
        if (m_ProfileEditor != null)
            DestroyImmediate(m_ProfileEditor);
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(m_IsGlobal, new GUIContent("Is Global"));
        EditorGUILayout.PropertyField(m_Priority);
        if (!m_IsGlobal.boolValue)
            EditorGUILayout.PropertyField(m_BlendDistance);
        EditorGUILayout.PropertyField(m_Weight);
        EditorGUILayout.PropertyField(m_SharedProfile, new GUIContent("Profile"));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Regions", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Influence Region uses enabled Colliders on this GameObject. Voxelization Bounds uses the separate BoxCollider below.",
            MessageType.Info);
        EditorGUILayout.PropertyField(m_VoxelizationBounds, new GUIContent("Voxelization Bounds"));

        serializedObject.ApplyModifiedProperties();

        if (!m_IsGlobal.boolValue)
            DrawColliderStatus();

        DrawVoxelizationBoundsStatus();

        if (!serializedObject.isEditingMultipleObjects)
            DrawProfileSettings();
    }

    void DrawVoxelizationBoundsStatus()
    {
        var volume = (VoxelGIVolume)target;
        var voxelBounds = volume.VoxelizationBounds;
        bool sharesInfluenceObject = voxelBounds != null && voxelBounds.gameObject == volume.gameObject;
        if (voxelBounds != null && voxelBounds.enabled && !sharesInfluenceObject)
            return;

        string message = sharesInfluenceObject
            ? "Voxelization Bounds must be on a separate GameObject; otherwise Unity also treats it as part of the Volume influence region."
            : "Assign a dedicated BoxCollider for the voxelized world-space region.";
        EditorGUILayout.HelpBox(message, MessageType.Warning);

        if (GUILayout.Button("Create Separate Voxelization Bounds"))
            CreateVoxelizationBounds(volume);
    }

    void CreateVoxelizationBounds(VoxelGIVolume volume)
    {
        var boundsObject = new GameObject("Voxelization Bounds");
        Undo.RegisterCreatedObjectUndo(boundsObject, "Create Voxelization Bounds");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(boundsObject, volume.gameObject.scene);

        var influenceCollider = volume.GetComponent<Collider>();
        Bounds initialBounds = influenceCollider != null
            ? influenceCollider.bounds
            : new Bounds(volume.transform.position, Vector3.one);
        boundsObject.transform.position = initialBounds.center;
        boundsObject.transform.rotation = Quaternion.identity;
        boundsObject.transform.localScale = Vector3.one;

        var box = Undo.AddComponent<BoxCollider>(boundsObject);
        box.isTrigger = true;
        box.size = initialBounds.size;
        m_VoxelizationBounds.objectReferenceValue = box;
        serializedObject.ApplyModifiedProperties();
        Selection.activeGameObject = boundsObject;
    }

    void DrawColliderStatus()
    {
        var volume = (VoxelGIVolume)target;
        var volumeCollider = volume.GetComponent<Collider>();
        if (volumeCollider != null && volumeCollider.enabled)
            return;

        EditorGUILayout.HelpBox("Local Voxel GI Volumes require an enabled Collider.", MessageType.Warning);
        if (volumeCollider == null && GUILayout.Button("Add Box Collider"))
        {
            var box = Undo.AddComponent<BoxCollider>(volume.gameObject);
            box.isTrigger = true;
            volume.UpdateColliders();
        }
    }

    void DrawProfileSettings()
    {
        var volume = (VoxelGIVolume)target;
        var profile = volume.HasInstantiatedProfile() ? volume.profile : volume.sharedProfile;
        if (profile == null)
        {
            EditorGUILayout.HelpBox("Assign a Volume Profile containing Voxel GI Settings.", MessageType.Info);
            return;
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Voxel GI Profile", EditorStyles.boldLabel);
        CreateCachedEditor(profile, null, ref m_ProfileEditor);
        m_ProfileEditor.OnInspectorGUI();
    }
}
}
