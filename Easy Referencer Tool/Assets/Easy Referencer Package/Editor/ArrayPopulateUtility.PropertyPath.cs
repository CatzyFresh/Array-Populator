using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;

namespace CatzyFreshTools
{
    public static partial class ArrayPopulateUtility
    {
        /// Resolve the concrete owner object and field via SerializedProperty.propertyPath,
        /// then return an IList adapter for arrays and List<T>. Works for nested/serialized fields.
        public static IList GetBackingListFromPropertyPath(SerializedProperty prop, out Type elementType)
        {
            elementType = null;
            if (prop == null || prop.serializedObject == null || prop.serializedObject.targetObject == null)
                return null;

            object owner = prop.serializedObject.targetObject;
            Type ownerType = owner.GetType();
            string path = prop.propertyPath; // e.g., "data.sub.items.Array.data[3]" or "items.Array.data[0]"
                                             // Walk the path:
            var segments = path.Split('.');
            object parent = owner;
            Type parentType = ownerType;
            FieldInfo targetField = null;

            for (int i = 0; i < segments.Length; i++)
            {
                string seg = segments[i];

                if (seg == "Array")
                {
                    // Next must be data[index]
                    i++;
                    if (i >= segments.Length) break;
                    string data = segments[i]; // "data[x]"
                    int start = data.IndexOf('[');
                    int end = data.IndexOf(']');
                    if (start >= 0 && end > start && int.TryParse(data.Substring(start + 1, end - start - 1), out int index))
                    {
                        if (parent is IList listLike && index >= 0 && index < listLike.Count)
                        {
                            parent = listLike[index];
                            parentType = parent?.GetType();
                            continue;
                        }
                        // If index is out of range or parent null, stop
                        return null;
                    }
                    return null;
                }

                // Regular field step
                var fi = GetFieldHierarchical(parentType, seg);
                if (fi == null)
                {
                    // Could be a property drawer using displayName path, ignore
                    return null;
                }

                if (i == segments.Length - 1)
                {
                    // This is the final field (should be our array/list)
                    targetField = fi;
                    break;
                }
                else
                {
                    parent = fi.GetValue(parent);
                    parentType = parent?.GetType();
                    if (parent == null) return null;
                }
            }

            if (targetField == null) return null;
            var fieldType = targetField.FieldType;

            if (fieldType.IsArray)
            {
                elementType = fieldType.GetElementType();
                var arr = (Array)targetField.GetValue(parent);
                var list = new List<object>();
                if (arr != null) foreach (var x in arr) list.Add(x);
                return new PathBackedArrayList(parent, targetField, elementType, list);
            }

            if (typeof(IList).IsAssignableFrom(fieldType))
            {
                if (fieldType.IsGenericType) elementType = fieldType.GetGenericArguments()[0];
                else elementType = typeof(UnityEngine.Object);

                var list = (IList)targetField.GetValue(parent);
                if (list == null)
                {
                    // Create a new List<T> if null
                    var listType = typeof(List<>).MakeGenericType(elementType);
                    list = (IList)Activator.CreateInstance(listType);
                    targetField.SetValue(parent, list);
                }
                return new PathBackedList(parent, targetField, elementType, list);
            }

            return null;
        }

        private static FieldInfo GetFieldHierarchical(Type t, string name)
        {
            if (t == null) return null;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var fi = t.GetField(name, flags);
            if (fi != null) return fi;
            return GetFieldHierarchical(t.BaseType, name);
        }

        // Adapters for propertyPath-backed fields
        private class PathBackedArrayList : IList, ICommitAdapter
        {
            private readonly object _owner;
            private readonly FieldInfo _field;
            private readonly Type _elemType;
            private readonly List<object> _list;

            public PathBackedArrayList(object owner, FieldInfo field, Type elemType, List<object> backing)
            { _owner = owner; _field = field; _elemType = elemType; _list = backing; }

            public void Commit()
            {
                var arr = Array.CreateInstance(_elemType, _list.Count);
                for (int i = 0; i < _list.Count; i++) arr.SetValue(_list[i], i);
                _field.SetValue(_owner, arr);
            }

            public int Add(object value) { _list.Add(value); return _list.Count - 1; }
            public void Clear() { _list.Clear(); }
            public bool Contains(object value) => _list.Contains(value);
            public int IndexOf(object value) => _list.IndexOf(value);
            public void Insert(int index, object value) => _list.Insert(index, value);
            public bool IsFixedSize => false;
            public bool IsReadOnly => false;
            public void Remove(object value) => _list.Remove(value);
            public void RemoveAt(int index) => _list.RemoveAt(index);
            public object this[int index] { get => _list[index]; set => _list[index] = value; }
            public void CopyTo(Array array, int index) => _list.ToArray().CopyTo(array, index);
            public int Count => _list.Count;
            public bool IsSynchronized => false;
            public object SyncRoot => this;
            public IEnumerator GetEnumerator() => _list.GetEnumerator();
        }

        private class PathBackedList : IList, ICommitAdapter
        {
            private readonly object _owner;
            private readonly FieldInfo _field;
            private readonly Type _elemType;
            private readonly IList _list;

            public PathBackedList(object owner, FieldInfo field, Type elemType, IList list)
            { _owner = owner; _field = field; _elemType = elemType; _list = list; }

            public void Commit() => _field.SetValue(_owner, _list);

            public int Add(object value) => _list.Add(value);
            public void Clear() => _list.Clear();
            public bool Contains(object value) => _list.Contains(value);
            public int IndexOf(object value) => _list.IndexOf(value);
            public void Insert(int index, object value) => _list.Insert(index, value);
            public bool IsFixedSize => _list.IsFixedSize;
            public bool IsReadOnly => _list.IsReadOnly;
            public void Remove(object value) => _list.Remove(value);
            public void RemoveAt(int index) => _list.RemoveAt(index);
            public object this[int index] { get => _list[index]; set => _list[index] = value; }
            public void CopyTo(Array array, int index) => _list.CopyTo(array, index);
            public int Count => _list.Count;
            public bool IsSynchronized => _list.IsSynchronized;
            public object SyncRoot => _list.SyncRoot;
            public IEnumerator GetEnumerator() => _list.GetEnumerator();
        }
    }
}