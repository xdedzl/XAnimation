using System;
using System.Linq;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

using UObject = UnityEngine.Object;

namespace XAnimationEngine
{
    public interface IXAnimationResLoader
    {
        T Load<T>(string assetPath) where T : UObject;
        T LoadSubAsset<T>(string assetPath, string subAssetName) where T : UObject;
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
            
            return LoadAsset<T>(assetPath);
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

            return EnsureResLoader().LoadSubAsset<T>(assetPath, subAssetName);
        }

        private static T LoadAsset<T>(string assetPath) where T : UObject
        {
            return EnsureResLoader().Load<T>(assetPath);
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
        public T Load<T>(string assetPath) where T : UObject
        {
            return AssetDatabase.LoadAssetAtPath<T>(assetPath);
        }

        public T LoadSubAsset<T>(string assetPath, string subAssetName) where T : UObject
        {
            if (string.IsNullOrWhiteSpace(assetPath) || string.IsNullOrWhiteSpace(subAssetName))
            {
                return null;
            }

            return AssetDatabase.LoadAllAssetsAtPath(assetPath)
                .OfType<T>()
                .FirstOrDefault(asset => string.Equals(asset.name, subAssetName, StringComparison.Ordinal));
        }
    }
#endif
}
