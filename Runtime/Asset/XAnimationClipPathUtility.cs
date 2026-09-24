using System;

namespace XAnimationEngine
{
    public static class XAnimationClipPathUtility
    {
        private const string SubClipSeparator = "|";

        public static string Compose(string assetPath, string clipName = null)
        {
            if (string.IsNullOrWhiteSpace(assetPath) || string.IsNullOrWhiteSpace(clipName))
            {
                return assetPath ?? string.Empty;
            }

            return assetPath + SubClipSeparator + clipName;
        }

        public static void Split(string clipPath, out string assetPath, out string clipName)
        {
            assetPath = clipPath ?? string.Empty;
            clipName = string.Empty;
            if (string.IsNullOrWhiteSpace(clipPath))
            {
                return;
            }

            // FBX 子动画名可以包含 |（例如 root|slash01），第一个分隔符才是资源路径边界。
            int separatorIndex = clipPath.IndexOf(SubClipSeparator, StringComparison.Ordinal);
            if (separatorIndex <= 0 || separatorIndex >= clipPath.Length - 1)
            {
                return;
            }

            assetPath = clipPath[..separatorIndex];
            clipName = clipPath[(separatorIndex + SubClipSeparator.Length)..];
        }
    }
}
