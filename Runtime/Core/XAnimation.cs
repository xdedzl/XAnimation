using System;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

using UObject = UnityEngine.Object;

namespace XAnimationEngine
{
    public interface IXAnimationResLoader
    {
        UObject Load(string assetPath, Type assetType);
        UObject LoadSubAsset(string assetPath, string subAssetName, Type assetType);
    }

    public static class XAnimation
    {
        private static IXAnimationResLoader s_ResLoader = CreateDefaultResLoader();

        public static void SetResLoader(IXAnimationResLoader resLoader)
        {
            s_ResLoader = resLoader ?? throw new ArgumentNullException(nameof(resLoader));
        }

        public static T Load<T>(string assetPath) where T : UObject
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return null;
            }
            
            return LoadAsset(assetPath, typeof(T)) as T;
        }

        public static T LoadSubAsset<T>(string assetPath, string subAssetName) where T : UObject
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(subAssetName))
            {
                return Load<T>(assetPath);
            }

            return LoadSubAsset(assetPath, subAssetName, typeof(T)) as T;
        }

        private static UObject LoadAsset(string assetPath, Type assetType)
        {
            return EnsureResLoader().Load(assetPath, assetType);
        }

        private static UObject LoadSubAsset(string assetPath, string subAssetName, Type assetType)
        {
            return EnsureResLoader().LoadSubAsset(assetPath, subAssetName, assetType);
        }

        private static IXAnimationResLoader EnsureResLoader()
        {
            if (s_ResLoader == null)
            {
                throw new XAnimationException("XAnimation resource loader is not set. Call XAnimation.SetResLoader before loading assets by path.");
            }

            return s_ResLoader;
        }

        private static IXAnimationResLoader CreateDefaultResLoader()
        {
#if UNITY_EDITOR
            return new XAnimationEditorResLoader();
#else
            return null;
#endif
        }
    }

#if UNITY_EDITOR
    internal sealed class XAnimationEditorResLoader : IXAnimationResLoader
    {
        public UObject Load(string assetPath, Type assetType)
        {
            return AssetDatabase.LoadAssetAtPath(assetPath, assetType);
        }

        public UObject LoadSubAsset(string assetPath, string subAssetName, Type assetType)
        {
            UObject[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            foreach (UObject asset in assets)
            {
                if (asset != null &&
                    assetType.IsInstanceOfType(asset) &&
                    string.Equals(asset.name, subAssetName, StringComparison.Ordinal))
                {
                    return asset;
                }
            }

            return null;
        }
    }
#endif
}
