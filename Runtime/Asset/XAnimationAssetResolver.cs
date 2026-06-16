using System;
using System.Collections.Generic;
using UnityEngine;

using UObject = UnityEngine.Object;

namespace XAnimationEngine
{
    public interface IXAnimationAssetResolver
    {
        TextAsset LoadTextAsset(string assetPath);
        AnimationClip LoadAnimationClip(string assetPath);
        AvatarMask LoadAvatarMask(string assetPath);
        void Release(UObject asset);
    }

    public sealed class XAnimationRuntimeAssetResolver : IXAnimationAssetResolver
    {
        public TextAsset LoadTextAsset(string assetPath)
        {
            return XAnimation.Load<TextAsset>(assetPath);
        }

        public AnimationClip LoadAnimationClip(string assetPath)
        {
            XAnimationClipPathUtility.Split(assetPath, out string mainAssetPath, out string clipName);
            if (string.IsNullOrWhiteSpace(clipName))
            {
                return XAnimation.Load<AnimationClip>(mainAssetPath);
            }

            return XAnimation.LoadSubAsset<AnimationClip>(mainAssetPath, clipName);
        }

        public AvatarMask LoadAvatarMask(string assetPath)
        {
            return XAnimation.Load<AvatarMask>(assetPath);
        }

        public void Release(UObject asset)
        {
            XAnimation.Release(asset);
        }
    }

    internal sealed class XAnimationLoadedAssetRegistry : IDisposable
    {
        private readonly IXAnimationAssetResolver m_Resolver;
        private readonly List<UObject> m_Assets = new();
        private bool m_Disposed;

        internal XAnimationLoadedAssetRegistry(IXAnimationAssetResolver resolver)
        {
            m_Resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        }

        internal T Track<T>(T asset) where T : UObject
        {
            if (asset == null)
            {
                return null;
            }

            if (m_Disposed)
            {
                m_Resolver.Release(asset);
                return asset;
            }

            m_Assets.Add(asset);
            return asset;
        }

        public void Dispose()
        {
            if (m_Disposed)
            {
                return;
            }

            m_Disposed = true;
            for (int i = m_Assets.Count - 1; i >= 0; i--)
            {
                m_Resolver.Release(m_Assets[i]);
            }

            m_Assets.Clear();
        }
    }
}
