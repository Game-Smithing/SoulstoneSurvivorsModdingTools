using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

public class BoneRenamerEditorWindow : EditorWindow
{
    private GameObject _originalModel;
    private string _currentError;
    private GameObject _modelTempInstance;

    [MenuItem("Game Smithing/Bone Renamer")]
    public static void ShowWindow()
    {
        GetWindow<BoneRenamerEditorWindow>("Bone Renamer");
    }

    private void OnGUI()
    {
        var guide = "1 - Bring the model into the project.\n" 
            + "2 - Drag the model into the \"Model\" field.\n" 
            + "3 - Click Rename Bones.";
        EditorGUILayout.HelpBox(guide, MessageType.None);

        var originalModel = _originalModel;
        _originalModel = (GameObject)EditorGUILayout.ObjectField("Model", originalModel, typeof(GameObject), allowSceneObjects: false);
        if (originalModel != _originalModel)
        {
            _currentError = string.Empty;
        }

        if (!string.IsNullOrEmpty(_currentError))
        {
            EditorGUILayout.HelpBox(_currentError, MessageType.Error);
        }

        GUILayout.Space(10);

        if (_originalModel == null)
        {
            EditorGUILayout.HelpBox("Please assign a model before renaming.", MessageType.Warning);
        }

        EditorGUI.BeginDisabledGroup(_originalModel == null);

        if (GUILayout.Button("Rename Bones"))
        {
            try
            {
                var newModelName = Rename();
                _currentError = string.Empty;
                var message = $"Rename operation completed. Use the newly created model. \r\n {newModelName}";
                EditorUtility.DisplayDialog("Rename Model", message, "Ok");
            }
            catch (Exception e)
            {
                _currentError = e.Message;
                GameObject.DestroyImmediate(_modelTempInstance);
                EditorUtility.DisplayDialog("Rename Model", "Rename operation failed.", "Ok");
            }
        }

        EditorGUI.EndDisabledGroup();
    }

    public string Rename()
    {
        _modelTempInstance = Instantiate(_originalModel, Vector3.zero, Quaternion.identity);
        if (!_modelTempInstance.TryGetComponent(out Animator modelAnimator))
        {
            throw new Exception("Model does not have an Animator component");
        }

        if (modelAnimator.avatar == null)
        {
            throw new Exception("Animator does not have an avatar");
        }

        if (!modelAnimator.avatar.isValid || !modelAnimator.avatar.isHuman)
        {
            throw new Exception("Animator's avatar is not a valid human");
        }

        foreach (HumanBodyBones item in Enum.GetValues(typeof(HumanBodyBones)))
        {
            if (item != HumanBodyBones.LastBone)
            {
                var targetTransform = modelAnimator.GetBoneTransform(item);
                if (targetTransform != null)
                {
                    targetTransform.name = item.ToString();
                }
            }
        }

        var originalPath = AssetDatabase.GetAssetPath(_originalModel);
        var pathDirectory = Path.GetDirectoryName(originalPath);
        var finalName = $"{Path.GetFileNameWithoutExtension(originalPath)}_Renamed.fbx";
        var destinationPath = EditorUtility.SaveFilePanel("Export renamed model", pathDirectory, finalName, "fbx");
        if (string.IsNullOrEmpty(destinationPath))
        {
            throw new Exception("Save operation canceled");
        }

        ExportModel(_modelTempInstance, destinationPath);

        AssetDatabase.Refresh();
        GameObject.DestroyImmediate(_modelTempInstance);

        var newModel = AssetDatabase.LoadAssetAtPath<GameObject>(destinationPath);
        if (newModel != null)
        {
            EditorGUIUtility.PingObject(newModel);
        }

        return finalName;
    }

    public static void ExportModel(GameObject model, string destinationPath)
    {
        var exportModelSettingsType = Type.GetType("UnityEditor.Formats.Fbx.Exporter.ExportModelSettingsSerialize, Unity.Formats.Fbx.Editor");
        var exportSettingsType = Type.GetType("UnityEditor.Formats.Fbx.Exporter.ExportSettings, Unity.Formats.Fbx.Editor");
        var modelExporterType = Type.GetType("UnityEditor.Formats.Fbx.Exporter.ModelExporter, Unity.Formats.Fbx.Editor");
        var iExportDataType = Type.GetType("UnityEditor.Formats.Fbx.Exporter.IExportData, Unity.Formats.Fbx.Editor");
        var iExportOptionsType = Type.GetType("UnityEditor.Formats.Fbx.Exporter.IExportOptions, Unity.Formats.Fbx.Editor");

        if (exportModelSettingsType == null || exportSettingsType == null || modelExporterType == null || iExportDataType == null || iExportOptionsType == null)
        {
            throw new Exception("Reflection failed - Couldn't find required types");
        }

        var exportOptions = Activator.CreateInstance(exportModelSettingsType);
        var binaryFormat = Enum.Parse(exportSettingsType.GetNestedType("ExportFormat"), "Binary");
        var includeModel = Enum.Parse(exportSettingsType.GetNestedType("Include"), "Model");
        var lodHighest = Enum.Parse(exportSettingsType.GetNestedType("LODExportType"), "Highest");
        var localCentered = Enum.Parse(exportSettingsType.GetNestedType("ObjectPosition"), "LocalCentered");
        SetOption(exportOptions, "SetExportFormat", binaryFormat);
        SetOption(exportOptions, "SetModelAnimIncludeOption", includeModel);
        SetOption(exportOptions, "SetLODExportType", lodHighest);
        SetOption(exportOptions, "SetObjectPosition", localCentered);
        SetOption(exportOptions, "SetAnimatedSkinnedMesh", false);
        SetOption(exportOptions, "SetUseMayaCompatibleNames", true);
        SetOption(exportOptions, "SetPreserveImportSettings", false);
        SetOption(exportOptions, "SetKeepInstances", true);
        SetOption(exportOptions, "SetEmbedTextures", true);

        var flags = BindingFlags.NonPublic | BindingFlags.Static;
        var targetOverload = new Type[]
        {
            typeof(GameObject), iExportOptionsType
        };
        var getExportDataMethod = modelExporterType.GetMethod("GetExportData", flags, binder: null, targetOverload, modifiers: null);
        var exportData = getExportDataMethod.Invoke(null, new object[] { model, exportOptions });
        var exportDataMap = Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(GameObject), iExportDataType));
        exportDataMap.GetType().GetMethod("Add").Invoke(exportDataMap, new object[] { model, exportData });

        var exportObjectsMethod = modelExporterType.GetMethod("ExportObjects", flags);
        exportObjectsMethod.Invoke(null, new object[] { destinationPath, new UnityEngine.Object[] { model }, exportOptions, exportDataMap });
    }

    private static void SetOption(object instance, string methodName, object value)
    {
        var method = instance.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
        method?.Invoke(instance, new object[] { value });
    }
}