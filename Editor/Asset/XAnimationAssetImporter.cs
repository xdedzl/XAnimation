using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using XAnimationEngine;

namespace XAnimationEditor
{
    public abstract class XAnimationAssetImporterBase : ScriptedImporter
    {
        private const string AnimationIconName = "xasset-aniamtion-icon.png";
        private const string AnimationOverrideIconName = "xasset-aniamtion-override-icon.png";

        public override void OnImportAsset(AssetImportContext ctx)
        {
            string text = File.ReadAllText(ctx.assetPath);
            TextAsset textAsset = new(text);
            Texture2D icon = ResolveIcon(text);
            if (icon != null)
            {
                ctx.AddObjectToAsset("main obj", textAsset, icon);
            }
            else
            {
                ctx.AddObjectToAsset("main obj", textAsset);
            }

            ctx.SetMainObject(textAsset);
        }

        private static Texture2D ResolveIcon(string text)
        {
            if (!XAnimationAssetUtility.TryReadMetaInfo(text, out XAnimationMetaInfo metaInfo))
            {
                return null;
            }

            string iconName;
            if (string.Equals(metaInfo.typeAlias, XAnimationAssetUtility.AnimationAssetAlias, System.StringComparison.Ordinal))
            {
                iconName = AnimationIconName;
            }
            else if (string.Equals(metaInfo.typeAlias, XAnimationAssetUtility.AnimationOverrideAlias, System.StringComparison.Ordinal))
            {
                iconName = AnimationOverrideIconName;
            }
            else
            {
                return null;
            }

            string iconPath = ResolveIconPath(iconName);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(iconPath);
        }

        private static string ResolveIconPath(string iconName)
        {
            UnityEditor.PackageManager.PackageInfo packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(XAnimationAssetImporterBase).Assembly);
            if (packageInfo == null || string.IsNullOrEmpty(packageInfo.assetPath))
            {
                return string.Empty;
            }

            return Path.Combine(packageInfo.assetPath, "Editor", "Assets", iconName).Replace('\\', '/');
        }
    }

    [ScriptedImporter(1, "xanimation")]
    public class XAnimationAssetImporter : XAnimationAssetImporterBase
    {
    }
}
