using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace CatzyFreshTools
{
    
    public class ArrayPopulatorWindow : EditorWindow
    {
        // TARGET
        private GameObject _targetGO;
        private Component[] _candidateComponents = Array.Empty<Component>();
        private int _selectedComponentIdx = -1;

        private List<(FieldInfo field, Type elementType, string niceName)> _candidateFields = new();
        private int _selectedFieldIdx = -1;

        // OPTIONS
        private bool _append = false;
        private bool _enforceUnique = true;
        private bool _sortAZ = true;
        private bool _shuffle = false;

        // SOURCES
        private bool _includeSceneObjects = true;
        private bool _includeProjectAssets = true;

        private GameObject _parentForChildren;
        private bool _includeInactiveChildren = true;
        private bool _includeSelf = true;

        private DefaultAsset _folderAsset;
        private bool _recursiveFolder = true;

        // Component override picker for extracting from sources (e.g., MeshRenderer)
        private string[] _componentTypeNames = Array.Empty<string>();
        private Type[] _componentTypes = Array.Empty<Type>();
        private int _selectedComponentTypeIdx = 0; // 0 = (Auto)

        // STATUS
        private string _status = "";
        private Rect _dropArea;

        [MenuItem("Tools/Array Populator")]
        public static void Open()
        {
            var w = GetWindow<ArrayPopulatorWindow>("Array Populator");
            w.minSize = new Vector2(560, 520);
            w.BuildComponentTypeList();
        }

        public static void TrySetTarget(GameObject go)
        {
            var w = GetWindow<ArrayPopulatorWindow>("Array Populator");
            w.SetInitialTarget(go);
        }

        private void SetInitialTarget(GameObject go)
        {
            _targetGO = go;
            RefreshComponents();
            Repaint();
        }


        private void OnSelectionChange()
        {
            Repaint();
        }

        private void BuildComponentTypeList()
        {
            var types = TypeCache.GetTypesDerivedFrom<Component>()
                .Where(t => !t.IsAbstract && t.IsPublic)
                .OrderBy(t => t.Name)
                .ToList();

            _componentTypes = new Type[types.Count + 1];
            _componentTypeNames = new string[types.Count + 1];
            _componentTypes[0] = null;
            _componentTypeNames[0] = "(Auto: match element type)";

            for (int i = 0; i < types.Count; i++)
            {
                _componentTypes[i + 1] = types[i];
                _componentTypeNames[i + 1] = types[i].Name;
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();
            DrawTargetPicker();
            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(!HasValidTargetField()))
            {
                DrawSourcePickers();
                EditorGUILayout.Space();
                DrawOptions();
                EditorGUILayout.Space(6);
                DrawActions();
                EditorGUILayout.Space(6);
                DrawDropZone();
            }

            if (!string.IsNullOrEmpty(_status))
            {
                EditorGUILayout.HelpBox(_status, MessageType.Info);
            }
        }

        // ---------- TARGET PICKER ----------

        private void DrawTargetPicker()
        {
            EditorGUILayout.LabelField("Target", EditorStyles.boldLabel);

            var newGO = (GameObject)EditorGUILayout.ObjectField("Target GameObject", _targetGO, typeof(GameObject), true);
            if (newGO != _targetGO)
            {
                _targetGO = newGO;
                RefreshComponents();
                _status = _targetGO ? $"✔ Selected GameObject '{_targetGO.name}'." : "Pick a GameObject that owns the list/array.";
            }

            using (new EditorGUI.DisabledScope(_candidateComponents.Length == 0))
            {
                var compNames = _candidateComponents.Select(c => c ? c.GetType().Name : "<null>").ToArray();
                _selectedComponentIdx = Mathf.Clamp(_selectedComponentIdx, 0, _candidateComponents.Length - 1);
                _selectedComponentIdx = EditorGUILayout.Popup("Target Component (script)", _selectedComponentIdx, compNames);

                if (GUI.changed && _candidateComponents.Length > 0)
                {
                    RefreshFields();
                }
            }

            using (new EditorGUI.DisabledScope(_candidateFields.Count == 0))
            {
                var fieldNames = _candidateFields.Select(f => f.niceName).ToArray();
                _selectedFieldIdx = Mathf.Clamp(_selectedFieldIdx, 0, _candidateFields.Count - 1);
                _selectedFieldIdx = EditorGUILayout.Popup("Target Field (array/list)", _selectedFieldIdx, fieldNames);

                if (_candidateFields.Count > 0)
                {
                    var sel = _candidateFields[_selectedFieldIdx];
                    EditorGUILayout.LabelField("Element Type", sel.elementType?.Name ?? "Unknown");
                    _status = $"✔ Found {_candidateFields.Count} serialized arrays/lists on '{_candidateComponents[_selectedComponentIdx].GetType().Name}'. Selected: {sel.niceName}.";
                }
            }
        }

        private void RefreshComponents()
        {
            _candidateComponents = Array.Empty<Component>();
            _selectedComponentIdx = -1;
            _candidateFields.Clear();
            _selectedFieldIdx = -1;

            if (!_targetGO) return;

            // Only components that HAVE at least one serialized array/list are shown.
            var all = _targetGO.GetComponents<Component>();
            var filtered = new List<Component>();
            foreach (var c in all)
            {
                if (!c) continue;
                var fields = ArrayPopulateUtility.GetSerializedArrayOrListFields(c);
                if (fields.Count > 0) filtered.Add(c);
            }
            _candidateComponents = filtered.ToArray();
            if (_candidateComponents.Length > 0)
            {
                _selectedComponentIdx = 0;
                RefreshFields();
            }
        }

        private void RefreshFields()
        {
            _candidateFields.Clear();
            _selectedFieldIdx = -1;

            if (_selectedComponentIdx < 0 || _selectedComponentIdx >= _candidateComponents.Length) return;
            var comp = _candidateComponents[_selectedComponentIdx];
            if (!comp) return;

            _candidateFields = ArrayPopulateUtility.GetSerializedArrayOrListFields(comp);
            if (_candidateFields.Count > 0) _selectedFieldIdx = 0;
        }

        private bool HasValidTargetField()
        {
            return _targetGO &&
                   _selectedComponentIdx >= 0 && _selectedComponentIdx < _candidateComponents.Length &&
                   _selectedFieldIdx >= 0 && _selectedFieldIdx < _candidateFields.Count;
        }

        private (Component comp, FieldInfo field, Type elementType) CurrentTarget()
        {
            if (!HasValidTargetField()) return (null, null, null);
            var comp = _candidateComponents[_selectedComponentIdx];
            var tup = _candidateFields[_selectedFieldIdx];
            return (comp, tup.field, tup.elementType);
        }

        // ---------- SOURCE PICKERS ----------

        private void DrawSourcePickers()
        {
            EditorGUILayout.LabelField("Sources", EditorStyles.boldLabel);

            // Component override (only meaningful if target expects Component type)
            var (_, _, elementType) = CurrentTarget();
            bool elementIsComponent = elementType != null && typeof(Component).IsAssignableFrom(elementType);
            using (new EditorGUI.DisabledScope(!elementIsComponent))
            {
                if (elementIsComponent)
                    EditorGUILayout.LabelField("Component Extraction (for sources)", EditorStyles.miniBoldLabel);

                _selectedComponentTypeIdx = EditorGUILayout.Popup("Component Type", _selectedComponentTypeIdx, _componentTypeNames);
                if (_selectedComponentTypeIdx == 0)
                {
                    // Auto: if element type is a Component, we’ll extract that exact type.
                }
            }

            // Selection
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("From Selection (Project + Scene)");
            using (new EditorGUILayout.HorizontalScope())
            {
                _includeSceneObjects = EditorGUILayout.ToggleLeft("Scene Objects", _includeSceneObjects);
                _includeProjectAssets = EditorGUILayout.ToggleLeft("Project Assets", _includeProjectAssets);
            }
            if (GUILayout.Button("Add From Current Selection"))
                TryApplyFromSelection();
            EditorGUILayout.EndVertical();

            // Parent + Children
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("From Parent & Children (Scene)");
            _parentForChildren = (GameObject)EditorGUILayout.ObjectField("Parent", _parentForChildren, typeof(GameObject), true);
            using (new EditorGUILayout.HorizontalScope())
            {
                _includeSelf = EditorGUILayout.ToggleLeft("Include Self", _includeSelf);
                _includeInactiveChildren = EditorGUILayout.ToggleLeft("Include Inactive", _includeInactiveChildren);
            }
            using (new EditorGUI.DisabledScope(_parentForChildren == null))
            {
                if (GUILayout.Button("Add From Parent & Children"))
                    TryApplyFromParent();
            }
            EditorGUILayout.EndVertical();

            // Folder
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("From Folder (Project)");
            _folderAsset = (DefaultAsset)EditorGUILayout.ObjectField("Folder", _folderAsset, typeof(DefaultAsset), false);
            _recursiveFolder = EditorGUILayout.ToggleLeft("Recursive", _recursiveFolder);
            using (new EditorGUI.DisabledScope(_folderAsset == null || !AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(_folderAsset))))
            {
                if (GUILayout.Button("Add From Folder"))
                    TryApplyFromFolder();
            }
            EditorGUILayout.EndVertical();
        }

        // ---------- OPTIONS / ACTIONS ----------

        private void DrawOptions()
        {
            EditorGUILayout.LabelField("Options", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                _append = EditorGUILayout.ToggleLeft("Append (otherwise Replace)", _append);
                _enforceUnique = EditorGUILayout.ToggleLeft("Unique", _enforceUnique);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                _sortAZ = EditorGUILayout.ToggleLeft("Sort A→Z", _sortAZ);
                _shuffle = EditorGUILayout.ToggleLeft("Shuffle", _shuffle);
            }
            if (_shuffle && _sortAZ)
                EditorGUILayout.HelpBox("Both Sort and Shuffle are enabled. Shuffle applies after Sort.", MessageType.None);
        }

        private void DrawActions()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Clear Field")) ClearTargetField();
                if (GUILayout.Button("Ping Target")) EditorGUIUtility.PingObject(_targetGO);
            }
        }

        private void DrawDropZone()
        {
            GUILayout.Space(4);
            EditorGUILayout.LabelField("Drag & Drop assets or scene objects here to add", EditorStyles.miniBoldLabel);

            _dropArea = GUILayoutUtility.GetRect(0, 44, GUILayout.ExpandWidth(true));
            GUI.Box(_dropArea, "Drop here", EditorStyles.helpBox);

            var evt = Event.current;
            if ((evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform) && _dropArea.Contains(evt.mousePosition))
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                if (evt.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    HandleDropped(DragAndDrop.objectReferences);
                }
                Event.current.Use();
            }
        }

        // ---------- APPLY LOGIC ----------

        private Type CurrentComponentOverrideOrAuto()
        {
            var (_, _, elementType) = CurrentTarget();
            if (elementType != null && typeof(Component).IsAssignableFrom(elementType))
            {
                // Auto → element type itself
                if (_selectedComponentTypeIdx == 0) return elementType;
                return _componentTypes[_selectedComponentTypeIdx];
            }
            return null; // Not applicable
        }

        private void HandleDropped(UnityEngine.Object[] dropped)
        {
            if (dropped == null || dropped.Length == 0 || !HasValidTargetField()) return;
            var overrideType = CurrentComponentOverrideOrAuto();
            ApplyIncoming(dropped, overrideType, nameof(HandleDropped));
        }

        private void TryApplyFromSelection()
        {
            if (!HasValidTargetField()) { _status = "Select a valid target (GO → Component → Field) first."; return; }

            var picked = new List<UnityEngine.Object>();
            foreach (var obj in Selection.objects)
            {
                if (!obj) continue;
                bool isAsset = AssetDatabase.Contains(obj);
                if (isAsset && !_includeProjectAssets) continue;
                if (!isAsset && !_includeSceneObjects) continue;
                picked.Add(obj);
            }
            var overrideType = CurrentComponentOverrideOrAuto();
            ApplyIncoming(picked, overrideType, "From Selection");
        }

        private void TryApplyFromParent()
        {
            if (!HasValidTargetField()) { _status = "Select a valid target (GO → Component → Field) first."; return; }
            if (!_parentForChildren) { _status = "Pick a parent GameObject."; return; }

            var objs = new List<UnityEngine.Object>();
            if (_includeSelf) objs.Add(_parentForChildren);
            foreach (var t in _parentForChildren.GetComponentsInChildren<Transform>(_includeInactiveChildren))
            {
                if (!_includeSelf && t.gameObject == _parentForChildren) continue;
                objs.Add(t.gameObject);
            }

            var overrideType = CurrentComponentOverrideOrAuto();
            ApplyIncoming(objs, overrideType, "From Parent & Children");
        }

        private void TryApplyFromFolder()
        {
            if (!HasValidTargetField()) { _status = "Select a valid target (GO → Component → Field) first."; return; }
            var folderPath = _folderAsset ? AssetDatabase.GetAssetPath(_folderAsset) : null;
            if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
            {
                _status = "Pick a valid Assets/ folder.";
                return;
            }

            var (_, _, elementType) = CurrentTarget();
            var incoming = ArrayPopulateUtility.FromFolder(folderPath, elementType, _recursiveFolder);
            var overrideType = CurrentComponentOverrideOrAuto();
            ApplyIncoming(incoming, overrideType, "From Folder");
        }

        private void ApplyIncoming(IEnumerable<UnityEngine.Object> raw, Type componentOverride, string origin)
        {
            var (comp, field, elementType) = CurrentTarget();
            if (!comp || field == null || elementType == null)
            {
                _status = "Invalid target.";
                return;
            }

            // Expand raw → assignables
            var expanded = new List<UnityEngine.Object>();
            foreach (var r in raw)
                expanded.AddRange(ArrayPopulateUtility.ExtractForElementOrComponentOverride(r, elementType, componentOverride));

            // Type safety: count compatible vs skipped
            int compatible = 0, skipped = 0;
            foreach (var e in expanded)
            {
                if (ArrayPopulateUtility.IsAssignableToElement(e, elementType)) compatible++;
                else skipped++;
            }

            // Build a working IList for the field
            var so = new SerializedObject(comp);
            var prop = so.FindProperty(field.Name);
            if (prop == null || !prop.isArray || prop.propertyType == SerializedPropertyType.String)
            {
                _status = "Target field is not an array/list.";
                return;
            }

            Undo.RecordObject(comp, "Populate Array/List");

            // Adapt list/array via reflection-aware adapter
            var list = ArrayPopulateUtility.GetBackingList(prop, comp, out var _resolvedElement);
            if (list == null)
            {
                _status = "Could not access backing list/array.";
                return;
            }

            // Apply options
            IList result = ArrayPopulateUtility.ApplySet(list, expanded, _append, _enforceUnique, post =>
            {
                IEnumerable<UnityEngine.Object> seq = post;
                if (_sortAZ) seq = seq.OrderBy(o => o ? o.name : string.Empty);
                if (_shuffle) seq = ArrayPopulateUtility.Shuffle(seq.Cast<object>().ToList()).Cast<UnityEngine.Object>();
                return seq;
            });

            int before = list.Count;
            list.Clear();
            foreach (var v in result) list.Add(v);

            if (list is ArrayPopulateUtility.ICommitAdapter adapter) adapter.Commit();

            so.Update();
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(comp);

            int after = list.Count;
            int added = Math.Max(0, after - (_append ? before : 0));
            _status = $"✔ {origin}: Added {added}, skipped {skipped} (unique={_enforceUnique}). Size: {before} → {after}.";
        }

        private void ClearTargetField()
        {
            var (comp, field, _) = CurrentTarget();
            if (!comp || field == null) { _status = "No valid target to clear."; return; }

            var so = new SerializedObject(comp);
            var prop = so.FindProperty(field.Name);
            if (prop == null || !prop.isArray || prop.propertyType == SerializedPropertyType.String)
            {
                _status = "Target field is not an array/list.";
                return;
            }

            Undo.RecordObject(comp, "Clear Array/List");
            int before = prop.arraySize;
            prop.arraySize = 0;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(comp);
            _status = $"✔ Cleared. Size: {before} → 0.";
        }
    }
}