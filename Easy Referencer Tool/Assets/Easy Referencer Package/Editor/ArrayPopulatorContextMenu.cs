using UnityEditor;
using UnityEngine;

namespace CatzyFreshTools
{
    public static class ArrayPopulatorContextMenu
    {
        [MenuItem("CONTEXT/Component/Populate Arrays/Lists… %&p")] // Ctrl/Cmd+Alt+P
        private static void OpenForComponent(MenuCommand cmd)
        {
            var comp = cmd.context as Component;
            var win = EditorWindow.GetWindow<ArrayPopulatorWindow>("Array Populator");
            // Try to hand the target GO to the window (public method we add below)
            ArrayPopulatorWindow.TrySetTarget(comp?.gameObject);
        }
    }

}
