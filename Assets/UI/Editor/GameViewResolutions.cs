using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace GearUI.EditorTools
{
    /// <summary>
    /// Adds the target phone/tablet resolutions to the Game view size dropdown
    /// and lets you switch between them from the Tools menu.
    /// Uses internal editor APIs through reflection (GameViewSizes).
    /// </summary>
    public static class GameViewResolutions
    {
        [MenuItem("Tools/Gear UI/Game View/9:21  (1080x2520)", priority = 40)]
        static void Phone921() => Select("9:21 Phone", 1080, 2520);

        [MenuItem("Tools/Gear UI/Game View/3:4  (1536x2048)", priority = 41)]
        static void Tablet34() => Select("3:4 Tablet", 1536, 2048);

        [MenuItem("Tools/Gear UI/Game View/Figma 402x874 (1206x2622)", priority = 42)]
        static void FigmaRef() => Select("Figma ref", 1206, 2622);

        public static bool Select(string label, int width, int height)
        {
            try
            {
                var asm = typeof(Editor).Assembly;
                var sizesType = asm.GetType("UnityEditor.GameViewSizes");
                var singleton = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
                var instance = singleton.GetProperty("instance").GetValue(null);
                var groupType = (GameViewSizeGroupType)sizesType.GetProperty("currentGroupType").GetValue(instance);
                var group = sizesType.GetMethod("GetGroup").Invoke(instance, new object[] { groupType });
                var groupClass = group.GetType();

                int index = FindSize(group, groupClass, width, height);
                if (index < 0)
                {
                    var sizeType = asm.GetType("UnityEditor.GameViewSize");
                    var sizeKind = asm.GetType("UnityEditor.GameViewSizeType");
                    var ctor = sizeType.GetConstructor(new[] { sizeKind, typeof(int), typeof(int), typeof(string) });
                    var size = ctor.Invoke(new object[] { Enum.ToObject(sizeKind, 1), width, height, label });
                    groupClass.GetMethod("AddCustomSize").Invoke(group, new[] { size });
                    sizesType.GetMethod("SaveToHDD").Invoke(instance, null);
                    index = FindSize(group, groupClass, width, height);
                }

                var gameViewType = asm.GetType("UnityEditor.GameView");
                var gameView = EditorWindow.GetWindow(gameViewType);
                var select = gameViewType.GetMethod("SizeSelectionCallback", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                select.Invoke(gameView, new object[] { index, null });
                gameView.Repaint();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[GearUI] Could not set Game view size: " + e.Message);
                return false;
            }
        }

        static int FindSize(object group, Type groupClass, int w, int h)
        {
            int total = (int)groupClass.GetMethod("GetTotalCount").Invoke(group, null);
            var getSize = groupClass.GetMethod("GetGameViewSize");
            for (int i = 0; i < total; i++)
            {
                var s = getSize.Invoke(group, new object[] { i });
                var t = s.GetType();
                if ((int)t.GetProperty("width").GetValue(s) == w && (int)t.GetProperty("height").GetValue(s) == h
                    && (int)t.GetProperty("sizeType").GetValue(s) == 1)
                    return i;
            }
            return -1;
        }
    }
}
