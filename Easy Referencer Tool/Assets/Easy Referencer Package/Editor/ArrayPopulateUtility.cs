using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using System.Reflection;

namespace CatzyFreshTools
{
    public static partial class ArrayPopulateUtility
    {
        public interface ICommitAdapter
        {
            void Commit();
        }

        public static List<(FieldInfo field, Type elementType, string niceName)> GetSerializedArrayOrListFields(Component comp)
        {
            var result = new List<(FieldInfo, Type, string)>();
            if (!comp) return result;

            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var fi in comp.GetType().GetFields(flags))
            {
                if (!IsSerializedField(fi)) continue;

                Type elem = null;
                if (fi.FieldType.IsArray)
                {
                    elem = fi.FieldType.GetElementType();
                }
                else if (typeof(IList).IsAssignableFrom(fi.FieldType) && fi.FieldType.IsGenericType)
                {
                    elem = fi.FieldType.GetGenericArguments()[0];
                }
                else continue;

                if (elem == null) continue;

                var readable = $"{fi.Name} : {PrettyFieldType(fi.FieldType)}";
                result.Add((fi, elem, readable));
            }
            return result;
        }

        public static bool IsSerializedField(FieldInfo fi)
        {
            if (fi == null) return false;
            if (fi.IsPublic && fi.GetCustomAttribute<NonSerializedAttribute>() == null) return true;
            if (fi.GetCustomAttribute<SerializeField>() != null) return true;
            return false;
        }

        public static string PrettyFieldType(Type t)
        {
            if (t.IsArray) return $"{t.GetElementType().Name}[]";
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
                return $"List<{t.GetGenericArguments()[0].Name}>";
            return t.Name;
        }

        // Is a source object compatible with a target element type?
        public static bool IsAssignableToElement(UnityEngine.Object o, Type elementType)
        {
            if (!o || elementType == null) return false;

            if (typeof(Component).IsAssignableFrom(elementType))
                return elementType.IsInstanceOfType(o);

            if (elementType == typeof(GameObject))
                return o is GameObject;

            return elementType.IsInstanceOfType(o);
        }

        // Extract a chosen component type from a GameObject/Prefab. If null, fall back to ExpandObjectForType
        public static IEnumerable<UnityEngine.Object> ExtractForElementOrComponentOverride(
            UnityEngine.Object picked,
            Type elementType,
            Type componentOverrideOrNull)
        {
            if (!picked || elementType == null) yield break;

            // If override is specified, pull exactly that component type from GOs/prefabs.
            if (componentOverrideOrNull != null && typeof(Component).IsAssignableFrom(componentOverrideOrNull))
            {
                if (picked is GameObject g1)
                {
                    foreach (var c in g1.GetComponents(componentOverrideOrNull))
                        if (IsAssignableToElement(c, elementType)) yield return c;
                    yield break;
                }
                if (picked is Component c1)
                {
                    if (componentOverrideOrNull.IsInstanceOfType(c1) && IsAssignableToElement(c1, elementType))
                        yield return c1;
                    else
                    {
                        // Also try the GameObject to fetch *that* component type if the picked comp wasn't of the type
                        foreach (var c in c1.gameObject.GetComponents(componentOverrideOrNull))
                            if (IsAssignableToElement(c, elementType)) yield return c;
                    }
                    yield break;
                }

                // For assets: if it’s a prefab GameObject, load and extract
                var go = picked as GameObject;
                if (go != null)
                {
                    foreach (var c in go.GetComponents(componentOverrideOrNull))
                        if (IsAssignableToElement(c, elementType)) yield return c;
                    yield break;
                }
            }

            // No override → use general logic (adds GO, or extracts matching components, or accepts direct assets)
            foreach (var o in ExpandObjectForType(picked, elementType))
                yield return o;
        }

        // Resolve the IList from a SerializedProperty that is array/list-like.
        public static IList GetBackingList(SerializedProperty arrayProp, UnityEngine.Object host, out Type elementType)
        {
            if (arrayProp == null || !arrayProp.isArray)
            {
                elementType = null;
                return null;
            }

            // Try resolve FieldInfo via reflection (works for non-[SerializeReference] object references)
            var targetType = host.GetType();
            var fi = targetType
                .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                .FirstOrDefault(f => f.Name == arrayProp.name);

            if (fi != null)
            {
                var fieldType = fi.FieldType;
                if (fieldType.IsArray)
                {
                    elementType = fieldType.GetElementType();
                    // Convert array to list for manipulation, we’ll write back on apply.
                    var arr = (Array)fi.GetValue(host);
                    var list = new List<object>();
                    if (arr != null) list.AddRange(arr.Cast<object>());
                    return new BackedArrayList(host, fi, elementType, list);
                }

                if (typeof(IList).IsAssignableFrom(fieldType))
                {
                    // Generic List<T> etc.
                    if (fieldType.IsGenericType) elementType = fieldType.GetGenericArguments()[0];
                    else elementType = typeof(UnityEngine.Object); // fallback

                    var list = (IList)fi.GetValue(host);
                    return new BackedList(host, fi, elementType, list);
                }
            }

            // Fallback: SerializedProperty-only manipulation (slower). ElementType best-effort.
            elementType = GuessElementTypeFromProperty(arrayProp);
            return new SerializedPropertyBackedList(arrayProp, elementType);
        }

        public static Type GuessElementTypeFromProperty(SerializedProperty arrayProp)
        {
            // Be defensive: only arrays/lists are valid (non-string)
            if (arrayProp == null || !arrayProp.isArray || arrayProp.propertyType == SerializedPropertyType.String)
                return typeof(UnityEngine.Object);

            // Try to infer from first non-null element
            for (int i = 0; i < arrayProp.arraySize; i++)
            {
                var el = arrayProp.GetArrayElementAtIndex(i);
                if (el.propertyType == SerializedPropertyType.ObjectReference)
                {
                    var obj = el.objectReferenceValue;
                    if (obj != null) return obj.GetType();
                }
            }

            // Fallback
            return typeof(UnityEngine.Object);
        }

        public static IEnumerable<UnityEngine.Object> FromCurrentSelection(Type elementType, bool includeSceneObjects = true, bool includeProjectAssets = true)
        {
            var list = new List<UnityEngine.Object>();
            foreach (var obj in Selection.objects)
            {
                if (obj == null) continue;

                if (!includeProjectAssets && AssetDatabase.Contains(obj)) continue;
                if (!includeSceneObjects && !AssetDatabase.Contains(obj)) continue;

                foreach (var picked in ExpandObjectForType(obj, elementType))
                    list.Add(picked);
            }
            return list;
        }

        public static IEnumerable<UnityEngine.Object> FromParentAndChildren(GameObject parent, Type elementType, bool includeInactive, bool includeSelf = true)
        {
            if (parent == null) yield break;

            if (includeSelf)
            {
                foreach (var o in ExpandObjectForType(parent, elementType))
                    yield return o;
            }

            foreach (var t in parent.GetComponentsInChildren<Transform>(includeInactive))
            {
                if (t.gameObject == parent && !includeSelf) continue;
                foreach (var o in ExpandObjectForType(t.gameObject, elementType))
                    yield return o;
            }
        }

        public static IEnumerable<UnityEngine.Object> FromFolder(string folderPath, Type elementType, bool recursive)
        {
            if (string.IsNullOrEmpty(folderPath)) yield break;

            var searchFolders = new[] { folderPath };
            var filter = TypeToFindFilter(elementType);
            var guids = AssetDatabase.FindAssets(filter, searchFolders);

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!recursive && System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/') != folderPath.TrimEnd('/'))
                    continue;

                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                if (asset == null) continue;

                foreach (var o in ExpandObjectForType(asset, elementType))
                    yield return o;
            }
        }

        // Expand a single picked object into assignables matching elementType:
        // - If elementType is GameObject and picked is GameObject → itself
        // - If elementType is Component subtype and picked is GameObject → matching components
        // - If elementType is UnityEngine.Object supertype → object if compatible
        public static IEnumerable<UnityEngine.Object> ExpandObjectForType(UnityEngine.Object picked, Type elementType)
        {
            if (picked == null || elementType == null) yield break;

            if (typeof(Component).IsAssignableFrom(elementType))
            {
                if (picked is GameObject go)
                {
                    var comps = go.GetComponents(elementType);
                    foreach (var c in comps) yield return c;
                    yield break;
                }
                if (elementType.IsInstanceOfType(picked))
                {
                    yield return picked;
                    yield break;
                }
            }
            else if (elementType == typeof(GameObject))
            {
                if (picked is GameObject go) yield return go;
                else if (picked is Component c) yield return c.gameObject;
            }
            else
            {
                // Any other UnityEngine.Object subtype (Material, Texture2D, ScriptableObject, AudioClip, etc.)
                if (elementType.IsInstanceOfType(picked))
                    yield return picked;
            }
        }

        public static string TypeToFindFilter(Type elementType)
        {
            // Use Unity’s type filter for AssetDatabase.FindAssets whenever possible.
            // For Component types, search prefabs (t:GameObject) and then extract components.
            if (elementType == typeof(GameObject) || typeof(Component).IsAssignableFrom(elementType))
                return "t:GameObject";

            if (typeof(ScriptableObject).IsAssignableFrom(elementType)) return "t:ScriptableObject";
            if (typeof(Texture).IsAssignableFrom(elementType)) return "t:Texture";
            if (typeof(Material).IsAssignableFrom(elementType)) return "t:Material";
            if (typeof(AudioClip).IsAssignableFrom(elementType)) return "t:AudioClip";
            if (typeof(AnimationClip).IsAssignableFrom(elementType)) return "t:AnimationClip";

            // Fallback
            return $"t:{elementType.Name}";
        }

        public static IList ApplySet(
            IList existing,
            IEnumerable<UnityEngine.Object> incoming,
            bool append,
            bool enforceUnique,
            Func<IEnumerable<UnityEngine.Object>, IEnumerable<UnityEngine.Object>> postProcess = null)
        {
            var work = append && existing != null
                ? new List<UnityEngine.Object>(existing.Cast<UnityEngine.Object>())
                : new List<UnityEngine.Object>();

            if (incoming != null) work.AddRange(incoming);

            if (enforceUnique)
                work = work.Where(x => x != null).Distinct().ToList();

            if (postProcess != null)
                work = postProcess(work).ToList();

            return work;
        }

        public static IList SortByName(IList list)
        {
            var arr = list.Cast<UnityEngine.Object>().OrderBy(o => o ? o.name : string.Empty).ToList();
            return arr;
        }
        public static IList Shuffle(IList list)
        {
            var rng = new System.Random();
            var arr = list.Cast<object>().ToList();
            int n = arr.Count;
            while (n > 1)
            {
                n--;
                int k = rng.Next(n + 1);
                (arr[k], arr[n]) = (arr[n], arr[k]);
            }
            return arr;
        }

        // ————— Internal adapters —————

        // Writes changes back to an *array* field
        private class BackedArrayList : IList, ICommitAdapter
        {
            private readonly UnityEngine.Object _host;
            private readonly System.Reflection.FieldInfo _fi;
            private readonly Type _elementType;
            private readonly List<object> _backing;

            public BackedArrayList(UnityEngine.Object host, System.Reflection.FieldInfo fi, Type elementType, List<object> backing)
            {
                _host = host; _fi = fi; _elementType = elementType; _backing = backing;
            }

            public void Commit()
            {
                var arr = Array.CreateInstance(_elementType, _backing.Count);
                for (int i = 0; i < _backing.Count; i++) arr.SetValue(_backing[i], i);
                _fi.SetValue(_host, arr);
            }

            // IList implementation (proxy to _backing)
            public int Add(object value) { _backing.Add(value); return _backing.Count - 1; }
            public void Clear() { _backing.Clear(); }
            public bool Contains(object value) { return _backing.Contains(value); }
            public int IndexOf(object value) { return _backing.IndexOf(value); }
            public void Insert(int index, object value) { _backing.Insert(index, value); }
            public bool IsFixedSize => false;
            public bool IsReadOnly => false;
            public void Remove(object value) { _backing.Remove(value); }
            public void RemoveAt(int index) { _backing.RemoveAt(index); }
            public object this[int index] { get => _backing[index]; set => _backing[index] = value; }
            public void CopyTo(Array array, int index) { _backing.ToArray().CopyTo(array, index); }
            public int Count => _backing.Count;
            public bool IsSynchronized => false;
            public object SyncRoot => this;
            public IEnumerator GetEnumerator() => _backing.GetEnumerator();
        }

        // Writes to an existing List<T> field
        private class BackedList : IList, ICommitAdapter
        {
            private readonly UnityEngine.Object _host;
            private readonly System.Reflection.FieldInfo _fi;
            private readonly Type _elementType;
            private readonly IList _list;

            public BackedList(UnityEngine.Object host, System.Reflection.FieldInfo fi, Type elementType, IList list)
            {
                _host = host; _fi = fi; _elementType = elementType; _list = list ?? (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(_elementType));
            }

            public void Commit() { _fi.SetValue(_host, _list); }
            public int Add(object value) { return _list.Add(value); }
            public void Clear() { _list.Clear(); }
            public bool Contains(object value) { return _list.Contains(value); }
            public int IndexOf(object value) { return _list.IndexOf(value); }
            public void Insert(int index, object value) { _list.Insert(index, value); }
            public bool IsFixedSize => _list.IsFixedSize;
            public bool IsReadOnly => _list.IsReadOnly;
            public void Remove(object value) { _list.Remove(value); }
            public void RemoveAt(int index) { _list.RemoveAt(index); }
            public object this[int index] { get => _list[index]; set => _list[index] = value; }
            public void CopyTo(Array array, int index) { _list.CopyTo(array, index); }
            public int Count => _list.Count;
            public bool IsSynchronized => _list.IsSynchronized;
            public object SyncRoot => _list.SyncRoot;
            public IEnumerator GetEnumerator() => _list.GetEnumerator();
        }

        // Slow path: write via SerializedProperty (only if reflection fails)
        private class SerializedPropertyBackedList : IList, ICommitAdapter
        {
            private readonly SerializedProperty _prop;
            private readonly Type _elementType;

            public SerializedPropertyBackedList(SerializedProperty prop, Type elementType)
            {
                _prop = prop; _elementType = elementType;
            }

            public void Commit()
            {
                _prop.serializedObject.ApplyModifiedProperties();
            }

            public int Add(object value)
            {
                int idx = _prop.arraySize;
                _prop.InsertArrayElementAtIndex(idx);
                var el = _prop.GetArrayElementAtIndex(idx);
                if (el.propertyType == SerializedPropertyType.ObjectReference)
                    el.objectReferenceValue = value as UnityEngine.Object;
                return idx;
            }

            public void Clear() { _prop.arraySize = 0; }
            public bool Contains(object value) { return IndexOf(value) >= 0; }
            public int IndexOf(object value)
            {
                for (int i = 0; i < _prop.arraySize; i++)
                {
                    var el = _prop.GetArrayElementAtIndex(i);
                    if (el.propertyType == SerializedPropertyType.ObjectReference &&
                        el.objectReferenceValue == (UnityEngine.Object)value) return i;
                }
                return -1;
            }
            public void Insert(int index, object value)
            {
                _prop.InsertArrayElementAtIndex(index);
                var el = _prop.GetArrayElementAtIndex(index);
                if (el.propertyType == SerializedPropertyType.ObjectReference)
                    el.objectReferenceValue = value as UnityEngine.Object;
            }
            public bool IsFixedSize => false;
            public bool IsReadOnly => false;
            public void Remove(object value)
            {
                int i = IndexOf(value);
                if (i >= 0) RemoveAt(i);
            }
            public void RemoveAt(int index) { _prop.DeleteArrayElementAtIndex(index); }

            public object this[int index]
            {
                get
                {
                    var el = _prop.GetArrayElementAtIndex(index);
                    return el.propertyType == SerializedPropertyType.ObjectReference ? el.objectReferenceValue : null;
                }
                set
                {
                    var el = _prop.GetArrayElementAtIndex(index);
                    if (el.propertyType == SerializedPropertyType.ObjectReference)
                        el.objectReferenceValue = value as UnityEngine.Object;
                }
            }

            public void CopyTo(Array array, int index) { /* not used */ }
            public int Count => _prop.arraySize;
            public bool IsSynchronized => false;
            public object SyncRoot => this;
            public IEnumerator GetEnumerator()
            {
                for (int i = 0; i < _prop.arraySize; i++) yield return this[i];
            }
        }
    }
}

