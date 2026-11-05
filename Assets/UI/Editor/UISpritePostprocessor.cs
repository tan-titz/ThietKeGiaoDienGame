using System.IO;
using UnityEditor;
using UnityEngine;

namespace GearUI.EditorTools
{
    /// <summary>
    /// Import rules for sprites exported from Figma at @3x (bg_environment at @2x).
    /// PPU 300 makes 1 canvas unit == 1 Figma px on a 402x874 reference canvas.
    /// </summary>
    public class UISpritePostprocessor : AssetPostprocessor
    {
        public const string SpriteFolder = "Assets/UI/Sprites/";

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(SpriteFolder)) return;

            var ti = (TextureImporter)assetImporter;
            string name = Path.GetFileNameWithoutExtension(assetPath);

            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.spritePixelsPerUnit = name == "bg_environment" ? 200f : 300f;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Bilinear;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.maxTextureSize = 2048;

            var settings = new TextureImporterSettings();
            ti.ReadTextureSettings(settings);
            Vector4 border = BorderFor(name);
            settings.spriteBorder = border;
            settings.spriteMeshType = border == Vector4.zero ? SpriteMeshType.Tight : SpriteMeshType.FullRect;
            ti.SetTextureSettings(settings);
        }

        /// <summary>9-slice borders (left, bottom, right, top) in source pixels for stretchable sprites.</summary>
        static Vector4 BorderFor(string name)
        {
            switch (name)
            {
                case "slider_bg": return new Vector4(14, 14, 14, 14);
                case "slider_fill_cyan":
                case "slider_fill_red":
                case "slider_fill_green": return new Vector4(11, 11, 11, 11);
                case "bar_bottom": return new Vector4(36, 36, 36, 36);
                default: return Vector4.zero;
            }
        }
    }
}
