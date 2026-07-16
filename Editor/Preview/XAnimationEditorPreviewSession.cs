#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using XAnimationEngine;

namespace XAnimationEditor
{
    internal sealed class XAnimationEditorPreviewSession : IDisposable
    {
        public event Action AssetChanged;

        public readonly struct PreviewLogEntry
        {
            public PreviewLogEntry(int id, string message)
            {
                Id = id;
                Message = message ?? string.Empty;
            }

            public int Id { get; }
            public string Message { get; }
        }

        private const float CloseGridSpacing = 1f;
        private const float FarGridSpacing = 10f;
        private const float SwitchToFarGridCellPixels = 8f;
        private const float SwitchToCloseGridCellPixels = 12f;
        private const float MinGridHalfSize = 250f;
        private const float PreviewFarClipPlane = 500f;

        private readonly XAnimationAssetLoader m_AssetLoader = new(new XAnimationEditorAssetResolver());
        private readonly XAnimationEditorActor m_EditorActor = new();
        private readonly List<PreviewLogEntry> m_CueLogs = new();

        private PreviewRenderUtility m_PreviewUtility;
        private RenderTexture m_RenderTexture;
        private GameObject m_Instance;
        private Animator m_Animator;
        private GameObject m_KeyLight;
        private GameObject m_FillLight;
        private GameObject m_RimLight;
        private XAnimationCompiledAsset m_CompiledAsset;
        private Vector2Int m_RenderTextureSize;
        private readonly Dictionary<string, string> m_OriginalClipPathByKey = new(StringComparer.Ordinal);
        private string m_AssetPath;
        private bool m_IsOverrideAsset;
        private XAnimationOverrideAsset m_OverrideAsset;

        private Vector3 m_InitialPosition;
        private Quaternion m_InitialRotation;
        private Bounds m_InitialBounds;
        private Vector3 m_CameraPivot;
        private float m_CameraDistance;
        private float m_CameraYaw = 140f;
        private float m_CameraPitch = 18f;
        private Vector3 m_CameraPosition;
        private bool m_CameraInitialized;
        private bool m_RootMotionEnabled;

        private bool m_GridVisible = true;
        private GameObject m_GridPlane;
        private Material m_GridMaterial;
        private float m_GridSpacing = CloseGridSpacing;
        private int m_NextLogId = 1;
        private int m_LogVersion;

        public IReadOnlyList<PreviewLogEntry> CueLogs => m_CueLogs;
        public XAnimationCompiledAsset CompiledAsset => m_CompiledAsset;
        public Texture PreviewTexture => m_RenderTexture;
        public bool IsLoaded => m_EditorActor.IsLoaded && m_Animator != null;
        public bool IsOverrideAsset => m_IsOverrideAsset;
        public int LogVersion => m_LogVersion;
        public bool IsPaused => m_EditorActor.IsPaused;
        public float GlobalSpeed => m_EditorActor.GlobalSpeed;
        private XAnimationEditorActor LoadedEditorActor { get { EnsureLoaded(); return m_EditorActor; } }

        public XAnimationEditorPreviewSession()
        {
            m_EditorActor.CueTriggered += OnCueTriggered;
            m_EditorActor.OnStateEnter += OnStateEnter;
            m_EditorActor.OnStateExit += OnStateExit;
        }

        public void Load(GameObject prefabAsset, string assetPath)
        {
            if (prefabAsset == null)
            {
                throw new XAnimationException("XAnimation preview prefab cannot be null.");
            }

            if (string.IsNullOrWhiteSpace(assetPath))
            {
                throw new XAnimationException("XAnimation preview assetPath cannot be empty.");
            }

            DisposePreview();

            m_AssetPath = assetPath;
            CacheOriginalClipPaths(assetPath);
            m_CompiledAsset = m_AssetLoader.Load(assetPath);
            m_RootMotionEnabled = m_CompiledAsset.RootMotionEnabled;

            m_PreviewUtility = new PreviewRenderUtility();
            ConfigurePreviewCamera();
            ConfigurePreviewLights();

            m_Instance = UnityEngine.Object.Instantiate(prefabAsset);
            m_Instance.transform.position = Vector3.zero;
            ApplyHideFlags(m_Instance);

            m_Animator = m_Instance.GetComponent<Animator>();
            if (m_Animator == null)
            {
                m_Animator = m_Instance.GetComponentInChildren<Animator>(true);
            }

            if (m_Animator == null)
            {
                m_Animator = m_Instance.AddComponent<Animator>();
            }

            SanitizePreviewInstance();
            m_Animator.runtimeAnimatorController = null;
            m_Animator.enabled = true;
            m_Animator.applyRootMotion = false;
            m_Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            ConfigurePreviewSkinnedMeshRenderers();

            m_PreviewUtility.AddSingleGO(m_Instance);

            CacheInitialTransform();
            CacheInitialBounds();
            PrepareGrid();

            m_EditorActor.Initialize(m_CompiledAsset, m_Animator, m_RootMotionEnabled);
        }

        public void Render(Vector2 size)
        {
            if (!IsLoaded)
            {
                return;
            }

            int width = Mathf.Max(1, Mathf.RoundToInt(size.x));
            int height = Mathf.Max(1, Mathf.RoundToInt(size.y));
            EnsureRenderTexture(width, height);
            UpdateCameraTransform();

            Camera camera = m_PreviewUtility.camera;
            camera.targetTexture = m_RenderTexture;
            camera.Render();
            camera.targetTexture = null;
        }

        public void Pause()
        {
            if (!IsLoaded)
            {
                return;
            }

            m_EditorActor.Pause();
        }

        public void Resume()
        {
            if (!IsLoaded)
            {
                return;
            }

            m_EditorActor.Resume();
        }

        public void SetPaused(bool paused)
        {
            if (!IsLoaded)
            {
                return;
            }

            m_EditorActor.SetPaused(paused);
        }

        public void SetGlobalSpeed(float speed)
        {
            if (!IsLoaded)
            {
                return;
            }

            m_EditorActor.SetGlobalSpeed(speed);
        }

        public void Step(float deltaTime)
        {
            EnsureLoaded();
            float clampedDeltaTime = Mathf.Clamp(deltaTime, 0f, 0.1f);
            if (clampedDeltaTime <= 0f)
            {
                throw new XAnimationException("XAnimation preview step deltaTime must be greater than 0.");
            }

            m_EditorActor.Step(clampedDeltaTime);
        }

        public void SyncPreviewFrame()
        {
            if (!IsLoaded)
            {
                return;
            }

            m_EditorActor.SyncFrame();
        }

        public void PlayClip(
            string clipKey,
            string channelName,
            XAnimationTransitionOptions transition = default)
            => LoadedEditorActor.PlayClip(clipKey, channelName, transition);

        public void PlayState(
            string stateKey,
            XAnimationTransitionOptions transition = default)
            => LoadedEditorActor.PlayState(stateKey, transition);

        public void PlayState(
            string channelName,
            string stateKey,
            XAnimationTransitionOptions transition = default)
            => LoadedEditorActor.PlayState(channelName, stateKey, transition);

        public XAnimationActionHandle PlayAction(
            string stateKey,
            XAnimationActionOptions options = default)
            => LoadedEditorActor.PlayAction(stateKey, options);

        public XAnimationActionHandle PlayAction(
            string channelName,
            string stateKey,
            XAnimationActionOptions options = default)
            => LoadedEditorActor.PlayAction(channelName, stateKey, options);

        public void PreloadAll() => LoadedEditorActor.PreloadAll();

        public void StopAll()
        {
            if (!IsLoaded)
            {
                return;
            }

            m_EditorActor.StopAll();
        }

        public void Stop(string channelName) => LoadedEditorActor.Stop(channelName);

        public void SetChannelWeight(string channelName, float weight) => LoadedEditorActor.SetChannelWeight(channelName, weight);

        public float GetChannelWeight(string channelName) => LoadedEditorActor.GetChannelWeight(channelName);

        public void PauseChannel(string channelName) => LoadedEditorActor.PauseChannel(channelName);

        public void ResumeChannel(string channelName) => LoadedEditorActor.ResumeChannel(channelName);

        public void SetChannelPaused(string channelName, bool paused) => LoadedEditorActor.SetChannelPaused(channelName, paused);

        public bool IsChannelPaused(string channelName) => LoadedEditorActor.IsChannelPaused(channelName);

        public bool SeekChannel(string channelName, float normalizedTime) => LoadedEditorActor.SeekChannel(channelName, normalizedTime);

        public void SetPreviewParameter(string key, float value) => LoadedEditorActor.SetParameter(key, value);

        public void SetPreviewParameter(string key, bool value) => LoadedEditorActor.SetParameter(key, value);

        public void SetPreviewParameter(string key, int value) => LoadedEditorActor.SetParameter(key, value);

        public bool TryGetPreviewParameter(string key, out float value) => m_EditorActor.TryGetParameter(key, out value);

        public bool TryGetPreviewParameter(string key, out bool value) => m_EditorActor.TryGetParameter(key, out value);

        public bool TryGetPreviewParameter(string key, out int value) => m_EditorActor.TryGetParameter(key, out value);

        public void ClearCueLogs()
        {
            m_CueLogs.Clear();
            m_LogVersion++;
        }

        public string AddParameter()
        {
            EnsureBaseAssetEditable();
            XAnimationAsset asset = m_CompiledAsset.Asset;
            string parameterName = CreateUniqueParameterName("NewParameter");
            asset.parameters = AppendItem(asset.parameters, new XAnimationParameterConfig
            {
                name = parameterName,
                type = XAnimationParameterType.Float,
                defaultValue = 0f,
            });
            RebuildDriverAndSave();
            return parameterName;
        }

        public void DeleteParameter(string parameterName)
        {
            EnsureBaseAssetEditable();
            parameterName = parameterName?.Trim();
            if (string.IsNullOrWhiteSpace(parameterName))
            {
                return;
            }

            XAnimationAsset asset = m_CompiledAsset.Asset;
            XAnimationParameterConfig[] parameters = asset.parameters ?? Array.Empty<XAnimationParameterConfig>();
            bool hasReference = HasStateParameterReference(asset, parameterName);
            bool removed = false;
            List<XAnimationParameterConfig> orderedParameters = new(parameters.Length);
            for (int i = 0; i < parameters.Length; i++)
            {
                XAnimationParameterConfig parameter = parameters[i];
                if (parameter != null && string.Equals(parameter.name, parameterName, StringComparison.Ordinal))
                {
                    removed = true;
                    continue;
                }

                orderedParameters.Add(parameter);
            }

            if (!removed)
            {
                return;
            }

            asset.parameters = orderedParameters.ToArray();
            string fallbackParameterName = hasReference ? EnsureFloatParameter() : null;
            RemoveStateParameterReferences(asset, parameterName, fallbackParameterName);
            RebuildDriverAndSave();
        }

        public void RenameParameter(string oldName, string newName)
        {
            EnsureLoaded();
            newName = newName?.Trim();
            if (string.Equals(oldName, newName, StringComparison.Ordinal))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(newName))
            {
                throw new XAnimationException("XAnimation parameter name cannot be empty.");
            }

            if (m_CompiledAsset.TryGetParameterIndex(newName, out _))
            {
                throw new XAnimationException($"XAnimation parameter '{newName}' is duplicated.");
            }

            XAnimationParameterConfig config = m_CompiledAsset.GetParameter(oldName).Config;
            config.name = newName;
            RenameStateParameterReferences(m_CompiledAsset.Asset, oldName, newName);
            RebuildDriverAndSave();
        }

        public void SetParameterType(string parameterName, XAnimationParameterType type)
        {
            EnsureLoaded();
            XAnimationParameterConfig config = m_CompiledAsset.GetParameter(parameterName).Config;
            config.type = type;
            config.defaultValue = type switch
            {
                XAnimationParameterType.Float => ConvertParameterDefaultToFloat(config.defaultValue),
                XAnimationParameterType.Int => ConvertParameterDefaultToInt(config.defaultValue),
                XAnimationParameterType.Bool => ConvertParameterDefaultToBool(config.defaultValue),
                XAnimationParameterType.Trigger => null,
                _ => config.defaultValue,
            };

            if (type != XAnimationParameterType.Float)
            {
                XAnimationAsset asset = m_CompiledAsset.Asset;
                string fallbackParameterName = HasStateParameterReference(asset, parameterName)
                    ? EnsureFloatParameter()
                    : null;
                RemoveStateParameterReferences(asset, parameterName, fallbackParameterName);
            }

            RebuildDriverAndSave();
        }

        public void SetParameterDefaultValue(string parameterName, object defaultValue)
        {
            EnsureLoaded();
            XAnimationParameterConfig config = m_CompiledAsset.GetParameter(parameterName).Config;
            config.defaultValue = config.type switch
            {
                XAnimationParameterType.Float => Convert.ToSingle(defaultValue, CultureInfo.InvariantCulture),
                XAnimationParameterType.Int => Convert.ToInt32(defaultValue, CultureInfo.InvariantCulture),
                XAnimationParameterType.Bool => Convert.ToBoolean(defaultValue, CultureInfo.InvariantCulture),
                XAnimationParameterType.Trigger => null,
                _ => defaultValue,
            };
            RebuildDriverAndSave();
        }

        public void RenameChannel(string oldName, string newName)
        {
            EnsureLoaded();
            newName = newName?.Trim();
            if (string.Equals(oldName, newName, StringComparison.Ordinal))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(newName))
            {
                throw new XAnimationException("XAnimation channel name cannot be empty.");
            }

            if (m_CompiledAsset.TryGetChannelIndex(newName, out _))
            {
                throw new XAnimationException($"XAnimation channel '{newName}' is duplicated.");
            }

            XAnimationAsset asset = m_CompiledAsset.Asset;
            XAnimationChannelConfig channel = m_CompiledAsset.GetChannel(oldName).Config;
            channel.name = newName;

            RenameStateChannelReferences(asset, oldName, newName);

            RebuildDriverAndSave();
        }

        public void SetChannelLayerType(string channelName, XAnimationChannelLayerType layerType)
        {
            EnsureLoaded();
            XAnimationChannelConfig config = m_CompiledAsset.GetChannel(channelName).Config;
            config.layerType = layerType;

            RebuildDriverAndSave();
        }

        public void SetChannelMaskPath(string channelName, string maskPath)
        {
            EnsureLoaded();
            m_CompiledAsset.GetChannel(channelName).Config.maskPath = maskPath ?? string.Empty;
            RebuildDriverAndSave();
        }

        public void SetChannelAllowInterrupt(string channelName, bool allowInterrupt)
        {
            EnsureLoaded();
            m_CompiledAsset.GetChannel(channelName).Config.allowInterrupt = allowInterrupt;
            SaveCompiledAssetIfNeeded(true);
        }

        public void SetChannelDefaultWeight(string channelName, float weight, bool save = true)
        {
            EnsureLoaded();
            float defaultWeight = Mathf.Max(0f, weight);
            m_CompiledAsset.GetChannel(channelName).Config.defaultWeight = defaultWeight;
            m_EditorActor.SetChannelWeight(channelName, defaultWeight);
            SaveCompiledAssetIfNeeded(save);
        }

        public void SetChannelFade(string channelName, float fadeIn, float fadeOut, bool save = true)
        {
            EnsureLoaded();
            XAnimationChannelConfig config = m_CompiledAsset.GetChannel(channelName).Config;
            config.defaultFadeIn = Mathf.Max(0f, fadeIn);
            config.defaultFadeOut = Mathf.Max(0f, fadeOut);
            SaveCompiledAssetIfNeeded(save);
        }

        public void SetRootMotionEnabled(bool enabled)
        {
            EnsureLoaded();
            m_RootMotionEnabled = enabled;
            m_EditorActor.SetRootMotionEnabled(enabled);
            if (!enabled)
            {
                ResetTransform();
            }
        }

        public bool GetRootMotionEnabled()
        {
            return m_EditorActor.GetRootMotionEnabled();
        }

        public void SetGridVisible(bool visible)
        {
            m_GridVisible = visible;
            if (m_GridPlane != null)
            {
                m_GridPlane.SetActive(visible);
            }
            else if (visible && IsLoaded)
            {
                PrepareGrid();
            }
        }

        public void ResetTransform()
        {
            if (m_Instance == null)
            {
                return;
            }

            m_Instance.transform.SetPositionAndRotation(m_InitialPosition, m_InitialRotation);
            CacheInitialBounds();
            UpdateGridTransform();
        }

        public void ResetCamera()
        {
            m_CameraYaw = 140f;
            m_CameraPitch = 18f;
            CacheInitialBounds();
            RecalculateCameraPosition();
        }

        public void Orbit(Vector2 delta)
        {
            m_CameraYaw += delta.x * 0.12f;
            m_CameraPitch = Mathf.Clamp(m_CameraPitch + delta.y * 0.08f, -80f, 80f);
        }

        public void Zoom(float delta)
        {
            Quaternion rotation = Quaternion.Euler(m_CameraPitch, m_CameraYaw, 0f);
            float distance = Mathf.Max(m_CameraDistance * 0.08f, 0.05f);
            m_CameraPosition -= rotation * Vector3.forward * delta * distance;
            m_CameraDistance = Mathf.Max(Vector3.Distance(m_CameraPosition, m_CameraPivot), 0.05f);
        }

        /// <summary>
        /// Move camera in its local space. x=right, y=up, z=forward.
        /// </summary>
        public void MoveCamera(Vector3 localDelta)
        {
            Quaternion rotation = Quaternion.Euler(m_CameraPitch, m_CameraYaw, 0f);
            m_CameraPosition += rotation * localDelta;
            // Keep pivot in sync so orbit still works around the look-at point
            m_CameraPivot = m_CameraPosition + rotation * Vector3.forward * m_CameraDistance;
        }

        private void RecalculateCameraPosition()
        {
            Quaternion rotation = Quaternion.Euler(m_CameraPitch, m_CameraYaw, 0f);
            m_CameraPosition = m_CameraPivot - rotation * Vector3.forward * m_CameraDistance;
        }

        public XAnimationChannelState GetChannelState(string channelName) => m_EditorActor.GetChannelState(channelName);

        public XAnimationDebugGraphSnapshot GetDebugGraphSnapshot()
        {
            return IsLoaded
                ? m_EditorActor.GetDebugGraphSnapshot()
                : XAnimationDebugGraphSnapshot.Invalid("XAnimation preview session is not loaded.");
        }

        public string AddChannel()
        {
            EnsureBaseAssetEditable();
            XAnimationAsset asset = m_CompiledAsset.Asset;
            string channelName = CreateUniqueChannelName("NewChannel");
            asset.channels = AppendItem(asset.channels, new XAnimationChannelConfig
            {
                name = channelName,
                layerType = XAnimationChannelLayerType.Override,
                defaultWeight = 1f,
                allowInterrupt = true,
                defaultFadeIn = 0.15f,
                defaultFadeOut = 0.15f,
            });
            RebuildDriverAndSave();
            return channelName;
        }

        public void DeleteChannel(string channelName)
        {
            EnsureBaseAssetEditable();
            XAnimationAsset asset = m_CompiledAsset.Asset;
            XAnimationChannelConfig[] channels = asset.channels ?? Array.Empty<XAnimationChannelConfig>();
            if (channels.Length <= 1)
            {
                throw new XAnimationException("XAnimation asset must contain at least one channel.");
            }

            XAnimationChannelConfig channel = m_CompiledAsset.GetChannel(channelName).Config;
            bool hasOtherBaseChannel = false;
            for (int i = 0; i < channels.Length; i++)
            {
                XAnimationChannelConfig item = channels[i];
                if (!ReferenceEquals(item, channel) && item != null && item.layerType == XAnimationChannelLayerType.Base)
                {
                    hasOtherBaseChannel = true;
                    break;
                }
            }

            if (channel.layerType == XAnimationChannelLayerType.Base && !hasOtherBaseChannel)
            {
                throw new XAnimationException("XAnimation asset must contain at least one Base channel.");
            }

            List<XAnimationChannelConfig> orderedChannels = new(channels.Length - 1);
            for (int i = 0; i < channels.Length; i++)
            {
                if (!ReferenceEquals(channels[i], channel))
                {
                    orderedChannels.Add(channels[i]);
                }
            }

            asset.channels = orderedChannels.ToArray();
            RemoveStatesInChannel(asset, channelName);

            RebuildDriverAndSave();
        }

        public string AddClip(string parentPath = null)
        {
            EnsureBaseAssetEditable();
            XAnimationAsset asset = m_CompiledAsset.Asset;
            XAnimationClipConfig[] clips = asset.clips ?? Array.Empty<XAnimationClipConfig>();
            string clipPath = FindTemplateClipPath(clips);
            if (string.IsNullOrWhiteSpace(clipPath))
            {
                throw new XAnimationException("Cannot add clip because no template AnimationClip exists.");
            }

            string clipKey = CreateUniqueClipKey(BuildClipPathKey(parentPath, "NewClip"));
            asset.clips = AppendItem(clips, new XAnimationClipConfig
            {
                key = clipKey,
                clipPath = clipPath,
            });
            m_OriginalClipPathByKey[clipKey] = clipPath;
            RebuildDriverAndSave();
            return clipKey;
        }

        public void RenameClipPath(string oldPath, string newPath)
        {
            EnsureBaseAssetEditable();
            oldPath = NormalizeClipPathKey(oldPath);
            newPath = NormalizeClipPathKey(newPath);
            if (string.Equals(oldPath, newPath, StringComparison.Ordinal))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(oldPath) || string.IsNullOrWhiteSpace(newPath))
            {
                throw new XAnimationException("XAnimation clip folder path cannot be empty.");
            }

            XAnimationAsset asset = m_CompiledAsset.Asset;
            XAnimationClipConfig[] clips = asset.clips ?? Array.Empty<XAnimationClipConfig>();
            Dictionary<string, string> renamedKeys = BuildClipPathRenameMap(clips, oldPath, newPath);
            if (renamedKeys.Count == 0)
            {
                throw new XAnimationException($"XAnimation clip folder '{oldPath}' does not exist.");
            }

            ApplyClipKeyRenameMap(asset, renamedKeys);
            RebuildDriverAndSave();
        }

        public void ClearClipPath(string path)
        {
            EnsureBaseAssetEditable();
            path = NormalizeClipPathKey(path);
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            string parentPath = GetClipPathParent(path);
            XAnimationAsset asset = m_CompiledAsset.Asset;
            XAnimationClipConfig[] clips = asset.clips ?? Array.Empty<XAnimationClipConfig>();
            Dictionary<string, string> renamedKeys = new(StringComparer.Ordinal);
            HashSet<string> resultingKeys = new(StringComparer.Ordinal);
            for (int i = 0; i < clips.Length; i++)
            {
                XAnimationClipConfig clip = clips[i];
                if (clip == null || string.IsNullOrWhiteSpace(clip.key))
                {
                    continue;
                }

                string clipKey = NormalizeClipPathKey(clip.key);
                string resultKey = clipKey;
                if (IsClipInPath(clipKey, path))
                {
                    string suffix = GetClipPathSuffix(clipKey, path);
                    resultKey = string.IsNullOrWhiteSpace(parentPath)
                        ? suffix
                        : BuildClipPathKey(parentPath, suffix);
                    renamedKeys[clip.key] = resultKey;
                }

                if (!resultingKeys.Add(resultKey))
                {
                    throw new XAnimationException($"XAnimation clip '{resultKey}' is duplicated.");
                }
            }

            if (renamedKeys.Count == 0)
            {
                return;
            }

            ApplyClipKeyRenameMap(asset, renamedKeys);
            RebuildDriverAndSave();
        }

        public void MoveClip(string clipKey, string targetParentPath, string insertBeforeClipKey = null)
        {
            EnsureBaseAssetEditable();
            clipKey = NormalizeRequiredClipKey(clipKey);
            targetParentPath = NormalizeClipPathKey(targetParentPath);
            XAnimationAsset asset = m_CompiledAsset.Asset;
            XAnimationClipConfig[] clips = asset.clips ?? Array.Empty<XAnimationClipConfig>();
            XAnimationClipConfig movedClip = null;
            List<XAnimationClipConfig> orderedClips = new(clips.Length);
            for (int i = 0; i < clips.Length; i++)
            {
                XAnimationClipConfig clip = clips[i];
                if (clip != null && string.Equals(clip.key, clipKey, StringComparison.Ordinal))
                {
                    movedClip = clip;
                    continue;
                }

                orderedClips.Add(clip);
            }

            if (movedClip == null)
            {
                throw new XAnimationException($"XAnimation clip '{clipKey}' does not exist.");
            }

            string oldKey = movedClip.key;
            string targetKey = BuildClipPathKey(targetParentPath, GetClipPathLeafName(oldKey));
            if (!string.Equals(oldKey, targetKey, StringComparison.Ordinal) &&
                m_CompiledAsset.TryGetClipIndex(targetKey, out _))
            {
                targetKey = CreateUniqueClipKey(targetKey);
            }

            if (!string.Equals(oldKey, targetKey, StringComparison.Ordinal))
            {
                movedClip.key = targetKey;
                RenameCueClipReferences(asset, oldKey, targetKey);
                RenameStateClipReferences(asset, oldKey, targetKey);
                if (m_OriginalClipPathByKey.Remove(oldKey, out string originalClipPath))
                {
                    m_OriginalClipPathByKey[targetKey] = originalClipPath;
                }
            }

            int insertIndex = orderedClips.Count;
            if (!string.IsNullOrWhiteSpace(insertBeforeClipKey))
            {
                for (int i = 0; i < orderedClips.Count; i++)
                {
                    XAnimationClipConfig clip = orderedClips[i];
                    if (clip != null && string.Equals(clip.key, insertBeforeClipKey, StringComparison.Ordinal))
                    {
                        insertIndex = i;
                        break;
                    }
                }
            }

            orderedClips.Insert(insertIndex, movedClip);
            asset.clips = orderedClips.ToArray();
            RebuildDriverAndSave();
        }

        public void DeleteClip(string clipKey)
        {
            EnsureBaseAssetEditable();
            XAnimationAsset asset = m_CompiledAsset.Asset;
            XAnimationClipConfig[] clips = asset.clips ?? Array.Empty<XAnimationClipConfig>();
            if (clips.Length <= 1)
            {
                throw new XAnimationException("XAnimation asset must contain at least one clip.");
            }

            m_CompiledAsset.GetClip(clipKey);
            List<XAnimationClipConfig> orderedClips = new(clips.Length - 1);
            for (int i = 0; i < clips.Length; i++)
            {
                XAnimationClipConfig clip = clips[i];
                if (clip != null && string.Equals(clip.key, clipKey, StringComparison.Ordinal))
                {
                    continue;
                }

                orderedClips.Add(clip);
            }

            asset.clips = orderedClips.ToArray();
            RemoveCueReferences(asset, new HashSet<string>(StringComparer.Ordinal) { clipKey });
            RemoveStateReferences(asset, new HashSet<string>(StringComparer.Ordinal) { clipKey });
            m_OriginalClipPathByKey.Remove(clipKey);
            RebuildDriverAndSave();
        }

        public void RenameClip(string oldKey, string newKey)
        {
            EnsureLoaded();
            newKey = newKey?.Trim();
            if (string.Equals(oldKey, newKey, StringComparison.Ordinal))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(newKey))
            {
                throw new XAnimationException("XAnimation clip key cannot be empty.");
            }

            if (m_CompiledAsset.TryGetClipIndex(newKey, out _))
            {
                throw new XAnimationException($"XAnimation clip '{newKey}' is duplicated.");
            }

            XAnimationAsset asset = m_CompiledAsset.Asset;
            XAnimationClipConfig clipConfig = m_CompiledAsset.GetClip(oldKey).Config;
            clipConfig.key = newKey;

            RenameCueClipReferences(asset, oldKey, newKey);
            RenameStateClipReferences(asset, oldKey, newKey);

            if (m_OriginalClipPathByKey.Remove(oldKey, out string originalClipPath))
            {
                m_OriginalClipPathByKey[newKey] = originalClipPath;
            }

            RebuildDriverAndSave();
        }

        public void SetClipPath(string clipKey, string clipPath)
        {
            EnsureLoaded();
            if (string.IsNullOrWhiteSpace(clipPath))
            {
                throw new XAnimationException("XAnimation clipPath cannot be empty.");
            }

            m_CompiledAsset.GetClip(clipKey);
            if (m_IsOverrideAsset)
            {
                SetOverrideClipPath(clipKey, clipPath);
                return;
            }

            XAnimationClipConfig config = m_CompiledAsset.GetClip(clipKey).Config;
            if (string.Equals(config.clipPath, clipPath, StringComparison.Ordinal))
            {
                return;
            }

            config.clipPath = clipPath;
            m_OriginalClipPathByKey[clipKey] = clipPath;
            RebuildDriverAndSave();
        }

        public string AddState(string channelName, string parentPath = null)
        {
            EnsureBaseAssetEditable();
            m_CompiledAsset.GetChannel(channelName);
            XAnimationAsset asset = m_CompiledAsset.Asset;
            string clipKey = FindTemplateClipKey(asset.clips ?? Array.Empty<XAnimationClipConfig>());
            if (string.IsNullOrWhiteSpace(clipKey))
            {
                throw new XAnimationException("Cannot add state because no template clip exists.");
            }

            string stateKey = CreateUniqueStateKey(BuildStatePathKey(parentPath, "NewState"));
            asset.states = AppendItem(asset.states, new XAnimationStateConfig
            {
                key = stateKey,
                stateType = XAnimationStateType.Single,
                clipKey = clipKey,
                channelName = channelName,
                speed = 1f,
                loop = true,
                parameterName = string.Empty,
                parameterXName = string.Empty,
                parameterYName = string.Empty,
                samples = Array.Empty<XAnimationBlend1DSampleConfig>(),
                directionalSamples = Array.Empty<XAnimationBlend2DSimpleDirectionalSampleConfig>(),
                behaviors = Array.Empty<XAnimationStateBehavior>(),
            });
            RebuildDriverAndSave();
            return stateKey;
        }

        public void RenameStatePath(string channelName, string oldPath, string newPath)
        {
            EnsureBaseAssetEditable();
            channelName = channelName?.Trim();
            string normalizedOld = NormalizeStatePath(oldPath);
            string normalizedNew = NormalizeStatePath(newPath);
            if (string.IsNullOrWhiteSpace(channelName))
            {
                throw new XAnimationException("XAnimation state folder channelName cannot be empty.");
            }

            if (string.IsNullOrWhiteSpace(normalizedOld))
            {
                throw new XAnimationException("XAnimation state folder oldPath cannot be empty.");
            }

            if (string.IsNullOrWhiteSpace(normalizedNew))
            {
                throw new XAnimationException("XAnimation state folder newPath cannot be empty.");
            }

            if (string.Equals(normalizedOld, normalizedNew, StringComparison.Ordinal))
            {
                return;
            }

            XAnimationAsset asset = m_CompiledAsset.Asset;
            Dictionary<string, string> renamedKeys = BuildStatePathRenameMap(asset.states, channelName, normalizedOld, normalizedNew);
            if (renamedKeys.Count == 0)
            {
                throw new XAnimationException($"XAnimation state folder '{normalizedOld}' does not exist in channel '{channelName}'.");
            }

            ApplyStateKeyRenameMap(asset, channelName, renamedKeys);
            RebuildDriverAndSave();
        }

        public void ClearStatePath(string channelName, string path)
        {
            EnsureBaseAssetEditable();
            channelName = channelName?.Trim();
            string normalizedPath = NormalizeStatePath(path);
            if (string.IsNullOrWhiteSpace(channelName))
            {
                throw new XAnimationException("XAnimation state folder channelName cannot be empty.");
            }

            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                return;
            }

            string parentPath = GetStatePathParent(normalizedPath);
            XAnimationAsset asset = m_CompiledAsset.Asset;
            XAnimationStateConfig[] states = asset.states ?? Array.Empty<XAnimationStateConfig>();
            Dictionary<string, string> renamedKeys = new(StringComparer.Ordinal);
            HashSet<string> resultingKeys = new(StringComparer.Ordinal);
            for (int i = 0; i < states.Length; i++)
            {
                XAnimationStateConfig state = states[i];
                if (state == null ||
                    string.IsNullOrWhiteSpace(state.key) ||
                    !string.Equals(state.channelName, channelName, StringComparison.Ordinal))
                {
                    continue;
                }

                string stateKey = NormalizeStatePath(state.key);
                string resultKey = stateKey;
                if (IsStateInPath(stateKey, normalizedPath))
                {
                    string suffix = GetStatePathSuffix(stateKey, normalizedPath);
                    resultKey = string.IsNullOrWhiteSpace(parentPath)
                        ? suffix
                        : BuildStatePathKey(parentPath, suffix);
                    renamedKeys[state.key] = resultKey;
                }

                if (!resultingKeys.Add(resultKey))
                {
                    throw new XAnimationException($"XAnimation state '{resultKey}' is duplicated in channel '{channelName}'.");
                }
            }

            if (renamedKeys.Count > 0)
            {
                ApplyStateKeyRenameMap(asset, channelName, renamedKeys);
                RebuildDriverAndSave();
            }
        }

        public void DeleteState(string stateKey)
        {
            XAnimationStateConfig config = ResolveUnambiguousStateConfig(stateKey);
            DeleteState(config.channelName, stateKey);
        }

        public void DeleteState(string channelName, string stateKey)
        {
            EnsureBaseAssetEditable();
            channelName = NormalizeRequiredChannelName(channelName);
            stateKey = NormalizeRequiredStateKey(stateKey);
            XAnimationAsset asset = m_CompiledAsset.Asset;
            XAnimationStateConfig[] states = asset.states ?? Array.Empty<XAnimationStateConfig>();
            if (states.Length <= 1)
            {
                throw new XAnimationException("XAnimation asset must contain at least one state.");
            }

            m_CompiledAsset.GetState(channelName, stateKey);
            List<XAnimationStateConfig> orderedStates = new(states.Length - 1);
            bool removed = false;
            for (int i = 0; i < states.Length; i++)
            {
                XAnimationStateConfig state = states[i];
                if (state != null &&
                    string.Equals(state.channelName, channelName, StringComparison.Ordinal) &&
                    string.Equals(state.key, stateKey, StringComparison.Ordinal))
                {
                    removed = true;
                    continue;
                }

                orderedStates.Add(state);
            }

            if (!removed)
            {
                throw new XAnimationException($"XAnimation state '{stateKey}' in channel '{channelName}' does not exist.");
            }

            asset.states = orderedStates.ToArray();
            ClearAutoTransitionReferences(asset, channelName, stateKey);
            ClearDefaultTransitionReferences(asset, channelName, stateKey);
            ClearStateGateReferences(asset, channelName, stateKey);
            RebuildDriverAndSave();
        }

        public void RenameState(string oldKey, string newKey)
        {
            XAnimationStateConfig config = ResolveUnambiguousStateConfig(oldKey);
            RenameState(config.channelName, oldKey, newKey);
        }

        public void RenameState(string channelName, string oldKey, string newKey)
        {
            EnsureLoaded();
            channelName = NormalizeRequiredChannelName(channelName);
            oldKey = NormalizeRequiredStateKey(oldKey);
            newKey = NormalizeStatePath(newKey);
            if (string.Equals(oldKey, newKey, StringComparison.Ordinal))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(newKey))
            {
                throw new XAnimationException("XAnimation state key cannot be empty.");
            }

            if (m_CompiledAsset.TryGetStateIndex(channelName, newKey, out _))
            {
                throw new XAnimationException($"XAnimation state '{newKey}' is duplicated in channel '{channelName}'.");
            }

            XAnimationAsset asset = m_CompiledAsset.Asset;
            RenameAutoTransitionReferences(asset, channelName, oldKey, newKey);
            RenameDefaultTransitionReferences(asset, channelName, oldKey, newKey);
            RenameStateGateReferences(asset, channelName, oldKey, newKey);
            m_CompiledAsset.GetState(channelName, oldKey).Config.key = newKey;
            RebuildDriverAndSave();
        }

        public void MoveState(string stateKey, string channelName, string insertBeforeStateKey = null, string groupName = null)
        {
            XAnimationStateConfig config = ResolveUnambiguousStateConfig(stateKey);
            MoveState(config.channelName, stateKey, channelName, insertBeforeStateKey, groupName);
        }

        public void MoveState(
            string sourceChannelName,
            string stateKey,
            string targetChannelName,
            string insertBeforeStateKey = null,
            string parentPath = null)
        {
            EnsureLoaded();
            sourceChannelName = NormalizeRequiredChannelName(sourceChannelName);
            targetChannelName = NormalizeRequiredChannelName(targetChannelName);
            stateKey = NormalizeRequiredStateKey(stateKey);
            parentPath = NormalizeStatePath(parentPath);
            m_CompiledAsset.GetChannel(targetChannelName);
            m_CompiledAsset.GetState(sourceChannelName, stateKey);
            string targetKey = BuildStatePathKey(parentPath, GetStatePathLeafName(stateKey));
            if ((!string.Equals(sourceChannelName, targetChannelName, StringComparison.Ordinal) ||
                    !string.Equals(stateKey, targetKey, StringComparison.Ordinal)) &&
                m_CompiledAsset.TryGetStateIndex(targetChannelName, targetKey, out _))
            {
                targetKey = CreateUniqueStateKey(targetKey);
            }

            XAnimationAsset asset = m_CompiledAsset.Asset;
            XAnimationStateConfig[] states = asset.states ?? Array.Empty<XAnimationStateConfig>();
            XAnimationStateConfig movedState = null;
            List<XAnimationStateConfig> orderedStates = new(states.Length);
            for (int i = 0; i < states.Length; i++)
            {
                XAnimationStateConfig state = states[i];
                if (state != null &&
                    string.Equals(state.channelName, sourceChannelName, StringComparison.Ordinal) &&
                    string.Equals(state.key, stateKey, StringComparison.Ordinal))
                {
                    movedState = state;
                    continue;
                }

                orderedStates.Add(state);
            }

            if (movedState == null)
            {
                throw new XAnimationException($"XAnimation state '{stateKey}' does not exist.");
            }

            if (!string.Equals(movedState.key, targetKey, StringComparison.Ordinal))
            {
                RenameAutoTransitionReferences(asset, sourceChannelName, movedState.key, targetKey);
                RenameDefaultTransitionReferences(asset, sourceChannelName, movedState.key, targetKey);
                RenameStateGateReferences(asset, sourceChannelName, movedState.key, targetKey);
                movedState.key = targetKey;
            }

            movedState.channelName = targetChannelName;
            int insertIndex = orderedStates.Count;
            if (!string.IsNullOrWhiteSpace(insertBeforeStateKey))
            {
                for (int i = 0; i < orderedStates.Count; i++)
                {
                    XAnimationStateConfig state = orderedStates[i];
                    if (state != null &&
                        string.Equals(state.channelName, targetChannelName, StringComparison.Ordinal) &&
                        string.Equals(state.key, insertBeforeStateKey, StringComparison.Ordinal))
                    {
                        insertIndex = i;
                        break;
                    }
                }
            }

            orderedStates.Insert(insertIndex, movedState);
            asset.states = orderedStates.ToArray();
            RebuildDriverAndSave();
        }

        public void SetStateType(string stateKey, XAnimationStateType stateType)
        {
            XAnimationStateConfig config = ResolveUnambiguousStateConfig(stateKey);
            SetStateType(config.channelName, stateKey, stateType);
        }

        public void SetStateType(string channelName, string stateKey, XAnimationStateType stateType)
        {
            EnsureLoaded();
            XAnimationStateConfig config = GetStateConfig(channelName, stateKey);
            if (config.stateType == stateType)
            {
                return;
            }

            ApplyMigratedStateType(config, stateType);

            RebuildDriverAndSave();
        }

        private void ApplyMigratedStateType(XAnimationStateConfig config, XAnimationStateType stateType)
        {
            if (config == null)
            {
                throw new XAnimationException("XAnimation state config cannot be null.");
            }

            XAnimationStateType sourceType = config.stateType;
            string nextClipKey;
            string nextParameterName;
            string nextParameterXName;
            string nextParameterYName;
            XAnimationBlend1DSampleConfig[] nextSamples;
            XAnimationBlend2DSimpleDirectionalSampleConfig[] nextDirectionalSamples;

            if (stateType == XAnimationStateType.Single)
            {
                nextClipKey = ResolvePreferredSingleClipKey(config);
                nextParameterName = string.Empty;
                nextParameterXName = string.Empty;
                nextParameterYName = string.Empty;
                nextSamples = Array.Empty<XAnimationBlend1DSampleConfig>();
                nextDirectionalSamples = Array.Empty<XAnimationBlend2DSimpleDirectionalSampleConfig>();
            }
            else if (stateType == XAnimationStateType.Blend1D)
            {
                nextClipKey = string.Empty;
                nextParameterName = sourceType switch
                {
                    XAnimationStateType.Blend1D when !string.IsNullOrWhiteSpace(config.parameterName) => config.parameterName,
                    XAnimationStateType.Blend2DSimpleDirectional or XAnimationStateType.Blend2DFreeformDirectional
                        when !string.IsNullOrWhiteSpace(config.parameterXName) => config.parameterXName,
                    _ => EnsureFloatParameter(),
                };
                nextParameterXName = string.Empty;
                nextParameterYName = string.Empty;
                nextSamples = BuildMigratedBlendSamples(config);
                nextDirectionalSamples = Array.Empty<XAnimationBlend2DSimpleDirectionalSampleConfig>();
            }
            else if (IsDirectionalBlendStateType(stateType))
            {
                nextClipKey = string.Empty;
                nextParameterName = string.Empty;
                bool sourceDirectional = IsDirectionalBlendStateType(sourceType);
                bool sourceBlend1D = sourceType == XAnimationStateType.Blend1D;
                nextParameterXName = sourceDirectional && !string.IsNullOrWhiteSpace(config.parameterXName)
                    ? config.parameterXName
                    : sourceBlend1D && !string.IsNullOrWhiteSpace(config.parameterName)
                        ? config.parameterName
                        : EnsureFloatParameter("blendX");
                nextParameterYName = sourceDirectional && !string.IsNullOrWhiteSpace(config.parameterYName)
                    ? config.parameterYName
                    : sourceBlend1D && !string.IsNullOrWhiteSpace(config.parameterName)
                        ? config.parameterName
                        : EnsureFloatParameter("blendY");
                nextSamples = Array.Empty<XAnimationBlend1DSampleConfig>();
                nextDirectionalSamples = BuildMigratedDirectionalSamples(config);
            }
            else
            {
                throw new XAnimationException($"XAnimation stateType '{stateType}' is not supported.");
            }

            config.stateType = stateType;
            config.clipKey = nextClipKey;
            config.parameterName = nextParameterName;
            config.parameterXName = nextParameterXName;
            config.parameterYName = nextParameterYName;
            config.samples = nextSamples;
            config.directionalSamples = nextDirectionalSamples;
        }

        public void SetStateChannel(string stateKey, string channelName)
        {
            XAnimationStateConfig config = ResolveUnambiguousStateConfig(stateKey);
            MoveState(config.channelName, stateKey, channelName, insertBeforeStateKey: null, GetStatePathParent(config.key));
        }

        public void SetStateChannel(string sourceChannelName, string stateKey, string targetChannelName)
        {
            EnsureLoaded();
            XAnimationStateConfig config = GetStateConfig(sourceChannelName, stateKey);
            MoveState(sourceChannelName, stateKey, targetChannelName, insertBeforeStateKey: null, GetStatePathParent(config.key));
        }

        public void SetStateClipKey(string stateKey, string clipKey)
        {
            XAnimationStateConfig config = ResolveUnambiguousStateConfig(stateKey);
            SetStateClipKey(config.channelName, stateKey, clipKey);
        }

        public void SetStateClipKey(string channelName, string stateKey, string clipKey)
        {
            EnsureLoaded();
            clipKey = clipKey?.Trim();
            if (string.IsNullOrWhiteSpace(clipKey))
            {
                throw new XAnimationException("XAnimation state clipKey cannot be empty.");
            }

            m_CompiledAsset.GetClip(clipKey);
            XAnimationStateConfig config = GetStateConfig(channelName, stateKey);
            config.clipKey = clipKey;
            RebuildDriverAndSave();
        }

        public void SetStateBlendParameter(string stateKey, string parameterName)
        {
            XAnimationStateConfig config = ResolveUnambiguousStateConfig(stateKey);
            SetStateBlendParameter(config.channelName, stateKey, parameterName);
        }

        public void SetStateBlendParameter(string channelName, string stateKey, string parameterName)
        {
            EnsureLoaded();
            parameterName = parameterName?.Trim();
            XAnimationCompiledParameter parameter = m_CompiledAsset.GetParameter(parameterName);
            if (parameter.Type != XAnimationParameterType.Float)
            {
                throw new XAnimationException($"XAnimation parameter '{parameterName}' must be Float for Blend1D.");
            }

            GetStateConfig(channelName, stateKey).parameterName = parameterName;
            RebuildDriverAndSave();
        }

        public void SetStateDirectionalBlendParameters(string stateKey, string parameterXName, string parameterYName)
        {
            XAnimationStateConfig config = ResolveUnambiguousStateConfig(stateKey);
            SetStateDirectionalBlendParameters(config.channelName, stateKey, parameterXName, parameterYName);
        }

        public void SetStateDirectionalBlendParameters(string channelName, string stateKey, string parameterXName, string parameterYName)
        {
            EnsureLoaded();
            parameterXName = parameterXName?.Trim();
            parameterYName = parameterYName?.Trim();
            XAnimationCompiledParameter parameterX = m_CompiledAsset.GetParameter(parameterXName);
            if (parameterX.Type != XAnimationParameterType.Float)
            {
                throw new XAnimationException($"XAnimation parameter '{parameterXName}' must be Float for 2D directional blend states.");
            }

            XAnimationCompiledParameter parameterY = m_CompiledAsset.GetParameter(parameterYName);
            if (parameterY.Type != XAnimationParameterType.Float)
            {
                throw new XAnimationException($"XAnimation parameter '{parameterYName}' must be Float for 2D directional blend states.");
            }

            XAnimationStateConfig config = GetStateConfig(channelName, stateKey);
            config.parameterXName = parameterXName;
            config.parameterYName = parameterYName;
            RebuildDriverAndSave();
        }

        public void SetStateLoop(string stateKey, bool loop, bool save = true)
        {
            XAnimationStateConfig config = ResolveUnambiguousStateConfig(stateKey);
            SetStateLoop(config.channelName, stateKey, loop, save);
        }

        public void SetStateLoop(string channelName, string stateKey, bool loop, bool save = true)
        {
            EnsureLoaded();
            GetStateConfig(channelName, stateKey).loop = loop;
            SaveCompiledAssetIfNeeded(save);
        }

        public void SetStateSpeed(string stateKey, float speed, bool save = true)
        {
            XAnimationStateConfig config = ResolveUnambiguousStateConfig(stateKey);
            SetStateSpeed(config.channelName, stateKey, speed, save);
        }

        public void SetStateSpeed(string channelName, string stateKey, float speed, bool save = true)
        {
            EnsureLoaded();
            GetStateConfig(channelName, stateKey).speed = Mathf.Approximately(speed, 0f) ? 1f : speed;
            SaveCompiledAssetIfNeeded(save);
        }

        public void AddStateBehavior(string channelName, string stateKey, Type behaviorType)
        {
            EnsureBaseAssetEditable();
            if (behaviorType == null ||
                behaviorType.IsAbstract ||
                behaviorType.ContainsGenericParameters ||
                !typeof(XAnimationStateBehavior).IsAssignableFrom(behaviorType))
            {
                throw new XAnimationException("XAnimationStateBehavior type is invalid.");
            }

            if (behaviorType.GetConstructor(Type.EmptyTypes) == null)
            {
                throw new XAnimationException($"XAnimationStateBehavior '{behaviorType.FullName}' must have a public parameterless constructor.");
            }

            if (Activator.CreateInstance(behaviorType) is not XAnimationStateBehavior behavior)
            {
                throw new XAnimationException($"Failed to create XAnimationStateBehavior '{behaviorType.FullName}'.");
            }

            XAnimationStateConfig config = GetStateConfig(channelName, stateKey);
            config.behaviors = AppendItem(config.behaviors ?? Array.Empty<XAnimationStateBehavior>(), behavior);
            SaveCompiledAssetIfNeeded(true);
        }

        public void DeleteStateBehavior(string channelName, string stateKey, int behaviorIndex)
        {
            EnsureBaseAssetEditable();
            XAnimationStateConfig config = GetStateConfig(channelName, stateKey);
            XAnimationStateBehavior[] behaviors = config.behaviors ?? Array.Empty<XAnimationStateBehavior>();
            if (behaviorIndex < 0 || behaviorIndex >= behaviors.Length)
            {
                throw new XAnimationException($"XAnimation state behavior index '{behaviorIndex}' does not exist.");
            }

            config.behaviors = RemoveAt(behaviors, behaviorIndex);
            SaveCompiledAssetIfNeeded(true);
        }

        public void SetStateBehaviorFieldValue(
            string channelName,
            string stateKey,
            int behaviorIndex,
            string fieldName,
            object value,
            bool save = true)
        {
            EnsureBaseAssetEditable();
            XAnimationStateBehavior behavior = GetStateBehavior(channelName, stateKey, behaviorIndex);
            FieldInfo fieldInfo = behavior.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
            if (fieldInfo == null)
            {
                throw new XAnimationException($"XAnimationStateBehavior field '{fieldName}' does not exist.");
            }

            fieldInfo.SetValue(behavior, ConvertStateBehaviorFieldValue(fieldInfo.FieldType, value));
            SaveCompiledAssetIfNeeded(save);
        }

        public void AddStateAllowedNextState(string stateKey)
        {
            XAnimationStateConfig config = ResolveUnambiguousStateConfig(stateKey);
            AddStateAllowedNextState(config.channelName, stateKey);
        }

        public void AddStateAllowedNextState(string channelName, string stateKey)
        {
            EnsureBaseAssetEditable();
            AddStateGateValue(channelName, stateKey, allowedNext: true);
        }

        public void AddStateAllowedPreviousState(string stateKey)
        {
            XAnimationStateConfig config = ResolveUnambiguousStateConfig(stateKey);
            AddStateAllowedPreviousState(config.channelName, stateKey);
        }

        public void AddStateAllowedPreviousState(string channelName, string stateKey)
        {
            EnsureBaseAssetEditable();
            AddStateGateValue(channelName, stateKey, allowedNext: false);
        }

        public void DeleteStateAllowedNextState(string stateKey, int index)
        {
            XAnimationStateConfig config = ResolveUnambiguousStateConfig(stateKey);
            DeleteStateAllowedNextState(config.channelName, stateKey, index);
        }

        public void DeleteStateAllowedNextState(string channelName, string stateKey, int index)
        {
            EnsureBaseAssetEditable();
            DeleteStateGateValue(channelName, stateKey, index, allowedNext: true);
        }

        public void DeleteStateAllowedPreviousState(string stateKey, int index)
        {
            XAnimationStateConfig config = ResolveUnambiguousStateConfig(stateKey);
            DeleteStateAllowedPreviousState(config.channelName, stateKey, index);
        }

        public void DeleteStateAllowedPreviousState(string channelName, string stateKey, int index)
        {
            EnsureBaseAssetEditable();
            DeleteStateGateValue(channelName, stateKey, index, allowedNext: false);
        }

        public void SetStateAllowedNextState(string stateKey, int index, string targetStateKey)
        {
            XAnimationStateConfig config = ResolveUnambiguousStateConfig(stateKey);
            SetStateAllowedNextState(config.channelName, stateKey, index, targetStateKey);
        }

        public void SetStateAllowedNextState(string channelName, string stateKey, int index, string targetStateKey)
        {
            EnsureBaseAssetEditable();
            SetStateGateValue(channelName, stateKey, index, targetStateKey, allowedNext: true);
        }

        public void SetStateAllowedPreviousState(string stateKey, int index, string sourceStateKey)
        {
            XAnimationStateConfig config = ResolveUnambiguousStateConfig(stateKey);
            SetStateAllowedPreviousState(config.channelName, stateKey, index, sourceStateKey);
        }

        public void SetStateAllowedPreviousState(string channelName, string stateKey, int index, string sourceStateKey)
        {
            EnsureBaseAssetEditable();
            SetStateGateValue(channelName, stateKey, index, sourceStateKey, allowedNext: false);
        }

        public void AddBlendSample(string stateKey)
        {
            XAnimationStateConfig config = ResolveUnambiguousStateConfig(stateKey);
            AddBlendSample(config.channelName, stateKey);
        }

        public void AddBlendSample(string channelName, string stateKey)
        {
            EnsureLoaded();
            XAnimationStateConfig config = GetStateConfig(channelName, stateKey);
            if (config.stateType != XAnimationStateType.Blend1D)
            {
                throw new XAnimationException($"XAnimation state '{stateKey}' is not Blend1D.");
            }

            List<XAnimationBlend1DSampleConfig> samples = new(config.samples ?? Array.Empty<XAnimationBlend1DSampleConfig>());
            string clipKey = FindTemplateClipKey(m_CompiledAsset.Asset.clips ?? Array.Empty<XAnimationClipConfig>());
            float threshold = samples.Count == 0 ? 0f : samples[^1].threshold + 1f;
            config.samples = AppendItem(config.samples, new XAnimationBlend1DSampleConfig
            {
                clipKey = clipKey,
                threshold = threshold,
            });
            RebuildDriverAndSave();
        }

        public void AddDirectionalBlendSample(string stateKey)
        {
            XAnimationStateConfig config = ResolveUnambiguousStateConfig(stateKey);
            AddDirectionalBlendSample(config.channelName, stateKey);
        }

        public void AddDirectionalBlendSample(string channelName, string stateKey)
        {
            EnsureLoaded();
            XAnimationStateConfig config = GetStateConfig(channelName, stateKey);
            if (!IsDirectionalBlendStateType(config.stateType))
            {
                throw new XAnimationException($"XAnimation state '{stateKey}' is not a 2D directional blend state.");
            }

            List<XAnimationBlend2DSimpleDirectionalSampleConfig> samples = new(
                config.directionalSamples ?? Array.Empty<XAnimationBlend2DSimpleDirectionalSampleConfig>());
            string clipKey = FindTemplateClipKey(m_CompiledAsset.Asset.clips ?? Array.Empty<XAnimationClipConfig>());
            Vector2 samplePosition = FindNextDirectionalBlendSamplePosition(samples);
            config.directionalSamples = AppendItem(config.directionalSamples, new XAnimationBlend2DSimpleDirectionalSampleConfig
            {
                clipKey = clipKey,
                positionX = samplePosition.x,
                positionY = samplePosition.y,
            });
            RebuildDriverAndSave();
        }

        public void DeleteBlendSample(string stateKey, int sampleIndex)
        {
            XAnimationStateConfig config = ResolveUnambiguousStateConfig(stateKey);
            DeleteBlendSample(config.channelName, stateKey, sampleIndex);
        }

        public void DeleteBlendSample(string channelName, string stateKey, int sampleIndex)
        {
            EnsureLoaded();
            XAnimationStateConfig config = GetStateConfig(channelName, stateKey);
            XAnimationBlend1DSampleConfig[] samples = config.samples ?? Array.Empty<XAnimationBlend1DSampleConfig>();
            if (sampleIndex < 0 || sampleIndex >= samples.Length)
            {
                throw new XAnimationException($"XAnimation Blend1D sample index '{sampleIndex}' does not exist.");
            }

            if (samples.Length <= 2)
            {
                throw new XAnimationException("XAnimation Blend1D state must contain at least two samples.");
            }

            config.samples = RemoveAt(samples, sampleIndex);
            RebuildDriverAndSave();
        }

        public void DeleteDirectionalBlendSample(string stateKey, int sampleIndex)
        {
            XAnimationStateConfig config = ResolveUnambiguousStateConfig(stateKey);
            DeleteDirectionalBlendSample(config.channelName, stateKey, sampleIndex);
        }

        public void DeleteDirectionalBlendSample(string channelName, string stateKey, int sampleIndex)
        {
            EnsureLoaded();
            XAnimationStateConfig config = GetStateConfig(channelName, stateKey);
            XAnimationBlend2DSimpleDirectionalSampleConfig[] samples =
                config.directionalSamples ?? Array.Empty<XAnimationBlend2DSimpleDirectionalSampleConfig>();
            if (sampleIndex < 0 || sampleIndex >= samples.Length)
            {
                throw new XAnimationException($"XAnimation 2D directional blend sample index '{sampleIndex}' does not exist.");
            }

            if (samples.Length <= 2)
            {
                throw new XAnimationException("XAnimation 2D directional blend state must contain at least two samples.");
            }

            if (config.stateType == XAnimationStateType.Blend2DFreeformDirectional && IsIdleDirectionalSample(samples[sampleIndex]))
            {
                throw new XAnimationException("XAnimation Blend2DFreeformDirectional state must keep exactly one idle sample at (0, 0).");
            }

            config.directionalSamples = RemoveAt(samples, sampleIndex);
            RebuildDriverAndSave();
        }

        public void SetBlendSampleClipKey(string stateKey, int sampleIndex, string clipKey)
        {
            XAnimationStateConfig config = ResolveUnambiguousStateConfig(stateKey);
            SetBlendSampleClipKey(config.channelName, stateKey, sampleIndex, clipKey);
        }

        public void SetBlendSampleClipKey(string channelName, string stateKey, int sampleIndex, string clipKey)
        {
            EnsureLoaded();
            clipKey = clipKey?.Trim();
            m_CompiledAsset.GetClip(clipKey);
            XAnimationBlend1DSampleConfig sample = GetBlendSampleConfig(channelName, stateKey, sampleIndex);
            sample.clipKey = clipKey;
            RebuildDriverAndSave();
        }

        public void SetBlendSampleThreshold(string stateKey, int sampleIndex, float threshold)
        {
            XAnimationStateConfig config = ResolveUnambiguousStateConfig(stateKey);
            SetBlendSampleThreshold(config.channelName, stateKey, sampleIndex, threshold);
        }

        public void SetBlendSampleThreshold(string channelName, string stateKey, int sampleIndex, float threshold)
        {
            EnsureLoaded();
            XAnimationBlend1DSampleConfig sample = GetBlendSampleConfig(channelName, stateKey, sampleIndex);
            sample.threshold = threshold;
            RebuildDriverAndSave();
        }

        public void SetDirectionalBlendSampleClipKey(string stateKey, int sampleIndex, string clipKey)
        {
            XAnimationStateConfig config = ResolveUnambiguousStateConfig(stateKey);
            SetDirectionalBlendSampleClipKey(config.channelName, stateKey, sampleIndex, clipKey);
        }

        public void SetDirectionalBlendSampleClipKey(string channelName, string stateKey, int sampleIndex, string clipKey)
        {
            EnsureLoaded();
            clipKey = clipKey?.Trim();
            m_CompiledAsset.GetClip(clipKey);
            XAnimationBlend2DSimpleDirectionalSampleConfig sample = GetDirectionalBlendSampleConfig(channelName, stateKey, sampleIndex);
            sample.clipKey = clipKey;
            RebuildDriverAndSave();
        }

        public void SetDirectionalBlendSamplePosition(string stateKey, int sampleIndex, float positionX, float positionY)
        {
            XAnimationStateConfig config = ResolveUnambiguousStateConfig(stateKey);
            SetDirectionalBlendSamplePosition(config.channelName, stateKey, sampleIndex, positionX, positionY);
        }

        public void SetDirectionalBlendSamplePosition(string channelName, string stateKey, int sampleIndex, float positionX, float positionY)
        {
            EnsureLoaded();
            XAnimationStateConfig state = GetStateConfig(channelName, stateKey);
            XAnimationBlend2DSimpleDirectionalSampleConfig[] samples =
                state.directionalSamples ?? Array.Empty<XAnimationBlend2DSimpleDirectionalSampleConfig>();
            if (sampleIndex < 0 || sampleIndex >= samples.Length)
            {
                throw new XAnimationException($"XAnimation 2D directional blend sample index '{sampleIndex}' does not exist.");
            }

            if (state.stateType == XAnimationStateType.Blend2DFreeformDirectional)
            {
                bool wasIdle = IsIdleDirectionalSample(samples[sampleIndex]);
                bool willBeIdle = Mathf.Approximately(positionX, 0f) && Mathf.Approximately(positionY, 0f);
                if (wasIdle != willBeIdle)
                {
                    throw new XAnimationException("XAnimation Blend2DFreeformDirectional state must keep exactly one idle sample at (0, 0).");
                }
            }

            XAnimationBlend2DSimpleDirectionalSampleConfig sample = GetDirectionalBlendSampleConfig(channelName, stateKey, sampleIndex);
            sample.positionX = positionX;
            sample.positionY = positionY;
            RebuildDriverAndSave();
        }

        public int AddCue(string clipKey, float normalizedTime = 0f)
        {
            EnsureBaseAssetEditable();
            if (string.IsNullOrWhiteSpace(clipKey))
            {
                if (m_CompiledAsset.Clips.Count == 0)
                {
                    throw new XAnimationException("Cannot add cue because no clip exists.");
                }

                clipKey = m_CompiledAsset.Clips[0].Key;
            }

            m_CompiledAsset.GetClip(clipKey);
            XAnimationAsset asset = m_CompiledAsset.Asset;
            XAnimationCueConfig[] cues = asset.cues ?? Array.Empty<XAnimationCueConfig>();
            asset.cues = AppendItem(cues, new XAnimationCueConfig
            {
                clipKey = clipKey,
                time = Mathf.Clamp01(normalizedTime),
                eventKey = CreateUniqueCueEventKey("NewCue"),
                payload = string.Empty,
            });
            RebuildDriverAndSave();
            return asset.cues.Length - 1;
        }

        public void DeleteCue(int cueIndex)
        {
            EnsureBaseAssetEditable();
            XAnimationAsset asset = m_CompiledAsset.Asset;
            XAnimationCueConfig[] cues = asset.cues ?? Array.Empty<XAnimationCueConfig>();
            if (cueIndex < 0 || cueIndex >= cues.Length)
            {
                throw new XAnimationException($"XAnimation cue index '{cueIndex}' does not exist.");
            }

            asset.cues = RemoveAt(cues, cueIndex);
            RebuildDriverAndSave();
        }

        public void SetCueClipKey(int cueIndex, string clipKey)
        {
            EnsureBaseAssetEditable();
            clipKey = clipKey?.Trim();
            if (string.IsNullOrWhiteSpace(clipKey))
            {
                throw new XAnimationException("XAnimation cue clipKey cannot be empty.");
            }

            m_CompiledAsset.GetClip(clipKey);
            XAnimationCueConfig cue = GetCueConfig(cueIndex);
            if (string.Equals(cue.clipKey, clipKey, StringComparison.Ordinal))
            {
                return;
            }

            cue.clipKey = clipKey;
            RebuildDriverAndSave();
        }

        public void SetCueTime(int cueIndex, float time, bool save = true)
        {
            EnsureBaseAssetEditable();
            XAnimationCueConfig cue = GetCueConfig(cueIndex);
            cue.time = Mathf.Clamp01(time);
            SaveCompiledAssetIfNeeded(save);
        }

        public void SetCueEventKey(int cueIndex, string eventKey)
        {
            EnsureBaseAssetEditable();
            eventKey = eventKey?.Trim();
            if (string.IsNullOrWhiteSpace(eventKey))
            {
                throw new XAnimationException("XAnimation cue eventKey cannot be empty.");
            }

            XAnimationCueConfig cue = GetCueConfig(cueIndex);
            cue.eventKey = eventKey;
            SaveCompiledAssetIfNeeded(true);
        }

        public void SetCuePayload(int cueIndex, string payload)
        {
            EnsureBaseAssetEditable();
            XAnimationCueConfig cue = GetCueConfig(cueIndex);
            cue.payload = payload ?? string.Empty;
            SaveCompiledAssetIfNeeded(true);
        }

        private void SetOverrideClipPath(string clipKey, string clipPath)
        {
            if (m_OverrideAsset == null)
            {
                throw new XAnimationException("XAnimation override asset is not loaded.");
            }

            string originalClipPath = GetOriginalClipPath(clipKey);
            List<XAnimationOverrideClipConfig> overrideClips = new(m_OverrideAsset.clips ?? Array.Empty<XAnimationOverrideClipConfig>());
            int index = overrideClips.FindIndex(item => item != null && string.Equals(item.key, clipKey, StringComparison.Ordinal));
            if (string.Equals(originalClipPath, clipPath, StringComparison.Ordinal))
            {
                if (index >= 0)
                {
                    overrideClips.RemoveAt(index);
                }
            }
            else if (index >= 0)
            {
                overrideClips[index].clipPath = clipPath;
            }
            else
            {
                overrideClips.Add(new XAnimationOverrideClipConfig
                {
                    key = clipKey,
                    clipPath = clipPath,
                });
            }

            m_OverrideAsset.clips = overrideClips.ToArray();
            m_CompiledAsset.GetClip(clipKey).Config.clipPath = clipPath;
            AssetChanged?.Invoke();
            RebuildDriver();
        }

        private void EnsureBaseAssetEditable()
        {
            EnsureLoaded();
            if (m_IsOverrideAsset)
            {
                throw new XAnimationException("XAnimation override asset cannot edit channels or clip structure.");
            }
        }

        private string CreateUniqueChannelName(string prefix)
        {
            return CreateUniqueName(prefix, name => m_CompiledAsset.TryGetChannelIndex(name, out _));
        }

        private string CreateUniqueClipKey(string prefix)
        {
            return CreateUniqueName(prefix, key => m_CompiledAsset.TryGetClipIndex(key, out _));
        }

        private string CreateUniqueStateKey(string prefix)
        {
            return CreateUniqueName(prefix, key => m_CompiledAsset.TryGetStateIndex(key, out _));
        }

        public XAnimationStateConfig GetStateConfig(string channelName, string stateKey)
        {
            EnsureLoaded();
            return m_CompiledAsset.GetState(NormalizeRequiredChannelName(channelName), NormalizeRequiredStateKey(stateKey)).Config;
        }

        public bool TryGetStatesGraphNodePosition(string channelName, string path, bool isFolder, out Vector2 position)
        {
            EnsureLoaded();
            channelName = NormalizeRequiredChannelName(channelName);
            path = NormalizeRequiredStateKey(path);
            position = default;

            XAnimationStatesGraphNodePosition[] positions =
                m_CompiledAsset.Asset.editData?.statesGraph?.nodePositions ??
                Array.Empty<XAnimationStatesGraphNodePosition>();
            for (int i = 0; i < positions.Length; i++)
            {
                XAnimationStatesGraphNodePosition item = positions[i];
                if (item != null &&
                    item.isFolder == isFolder &&
                    string.Equals(item.channelName, channelName, StringComparison.Ordinal) &&
                    string.Equals(NormalizeStatePath(item.path), path, StringComparison.Ordinal))
                {
                    position = new Vector2(item.x, item.y);
                    return true;
                }
            }

            return false;
        }

        public void SetStatesGraphNodePosition(string channelName, string path, bool isFolder, Vector2 position, bool save = true)
        {
            EnsureBaseAssetEditable();
            channelName = NormalizeRequiredChannelName(channelName);
            path = NormalizeRequiredStateKey(path);

            XAnimationStatesGraphEditData graphEditData = GetOrCreateStatesGraphEditData(m_CompiledAsset.Asset);
            XAnimationStatesGraphNodePosition[] positions =
                graphEditData.nodePositions ?? Array.Empty<XAnimationStatesGraphNodePosition>();
            for (int i = 0; i < positions.Length; i++)
            {
                XAnimationStatesGraphNodePosition item = positions[i];
                if (item != null &&
                    item.isFolder == isFolder &&
                    string.Equals(item.channelName, channelName, StringComparison.Ordinal) &&
                    string.Equals(NormalizeStatePath(item.path), path, StringComparison.Ordinal))
                {
                    item.x = RoundGraphPosition(position.x);
                    item.y = RoundGraphPosition(position.y);
                    SaveCompiledAssetIfNeeded(save);
                    return;
                }
            }

            List<XAnimationStatesGraphNodePosition> nextPositions = new(positions)
            {
                new XAnimationStatesGraphNodePosition
                {
                    channelName = channelName,
                    path = path,
                    isFolder = isFolder,
                    x = RoundGraphPosition(position.x),
                    y = RoundGraphPosition(position.y),
                }
            };
            graphEditData.nodePositions = nextPositions.ToArray();
            SaveCompiledAssetIfNeeded(save);
        }

        public bool TryGetStatesGraphViewPanOffset(string channelName, string path, out Vector2 panOffset)
        {
            EnsureLoaded();
            channelName = NormalizeRequiredChannelName(channelName);
            path = NormalizeStatePath(path);
            panOffset = default;

            XAnimationStatesGraphViewState[] viewStates =
                m_CompiledAsset.Asset.editData?.statesGraph?.viewStates ??
                Array.Empty<XAnimationStatesGraphViewState>();
            for (int i = 0; i < viewStates.Length; i++)
            {
                XAnimationStatesGraphViewState item = viewStates[i];
                if (item != null &&
                    string.Equals(item.channelName, channelName, StringComparison.Ordinal) &&
                    string.Equals(NormalizeStatePath(item.path), path, StringComparison.Ordinal))
                {
                    panOffset = new Vector2(item.panX, item.panY);
                    return true;
                }
            }

            return false;
        }

        public void SetStatesGraphViewPanOffset(string channelName, string path, Vector2 panOffset, bool save = true)
        {
            EnsureBaseAssetEditable();
            channelName = NormalizeRequiredChannelName(channelName);
            path = NormalizeStatePath(path);

            XAnimationStatesGraphEditData graphEditData = GetOrCreateStatesGraphEditData(m_CompiledAsset.Asset);
            XAnimationStatesGraphViewState[] viewStates =
                graphEditData.viewStates ?? Array.Empty<XAnimationStatesGraphViewState>();
            for (int i = 0; i < viewStates.Length; i++)
            {
                XAnimationStatesGraphViewState item = viewStates[i];
                if (item != null &&
                    string.Equals(item.channelName, channelName, StringComparison.Ordinal) &&
                    string.Equals(NormalizeStatePath(item.path), path, StringComparison.Ordinal))
                {
                    item.panX = RoundGraphPosition(panOffset.x);
                    item.panY = RoundGraphPosition(panOffset.y);
                    SaveCompiledAssetIfNeeded(save);
                    return;
                }
            }

            List<XAnimationStatesGraphViewState> nextViewStates = new(viewStates)
            {
                new XAnimationStatesGraphViewState
                {
                    channelName = channelName,
                    path = path,
                    panX = RoundGraphPosition(panOffset.x),
                    panY = RoundGraphPosition(panOffset.y),
                }
            };
            graphEditData.viewStates = nextViewStates.ToArray();
            SaveCompiledAssetIfNeeded(save);
        }

        private XAnimationStateConfig ResolveUnambiguousStateConfig(string stateKey)
        {
            EnsureLoaded();
            return m_CompiledAsset.GetState(NormalizeRequiredStateKey(stateKey)).Config;
        }

        private static string NormalizeRequiredChannelName(string channelName)
        {
            channelName = channelName?.Trim();
            if (string.IsNullOrWhiteSpace(channelName))
            {
                throw new XAnimationException("XAnimation state channelName cannot be empty.");
            }

            return channelName;
        }

        private static string NormalizeRequiredStateKey(string stateKey)
        {
            stateKey = NormalizeStatePath(stateKey);
            if (string.IsNullOrWhiteSpace(stateKey))
            {
                throw new XAnimationException("XAnimation state key cannot be empty.");
            }

            return stateKey;
        }

        private static XAnimationStatesGraphEditData GetOrCreateStatesGraphEditData(XAnimationAsset asset)
        {
            asset.editData ??= new XAnimationEditData();
            asset.editData.statesGraph ??= new XAnimationStatesGraphEditData();
            asset.editData.statesGraph.nodePositions ??= Array.Empty<XAnimationStatesGraphNodePosition>();
            asset.editData.statesGraph.viewStates ??= Array.Empty<XAnimationStatesGraphViewState>();
            return asset.editData.statesGraph;
        }

        private static float RoundGraphPosition(float value)
        {
            return Mathf.Round(value * 100f) / 100f;
        }

        private static string NormalizeRequiredClipKey(string clipKey)
        {
            clipKey = NormalizeClipPathKey(clipKey);
            if (string.IsNullOrWhiteSpace(clipKey))
            {
                throw new XAnimationException("XAnimation clip key cannot be empty.");
            }

            return clipKey;
        }

        private static string NormalizeClipPathKey(string path)
        {
            List<string> segments = new();
            if (!string.IsNullOrWhiteSpace(path))
            {
                string[] rawSegments = path.Split('/');
                for (int i = 0; i < rawSegments.Length; i++)
                {
                    string segment = rawSegments[i]?.Trim();
                    if (!string.IsNullOrWhiteSpace(segment))
                    {
                        segments.Add(segment);
                    }
                }
            }

            return segments.Count == 0 ? string.Empty : string.Join("/", segments);
        }

        private static string NormalizeStatePath(string path)
        {
            return XAnimationStatePathUtility.NormalizePath(path);
        }

        private static string BuildStatePathKey(string parentPath, string leafName)
        {
            return XAnimationStatePathUtility.BuildPath(parentPath, leafName);
        }

        private static string GetStatePathParent(string path)
        {
            return XAnimationStatePathUtility.GetParentPath(path);
        }

        private static string GetStatePathLeafName(string path)
        {
            return XAnimationStatePathUtility.GetLeafName(path);
        }

        private static bool IsStateInPath(string key, string path)
        {
            return XAnimationStatePathUtility.IsInPath(key, path);
        }

        private static string GetStatePathSuffix(string key, string path)
        {
            return XAnimationStatePathUtility.GetSuffixInPath(key, path);
        }

        private static string BuildClipPathKey(string parentPath, string leafName)
        {
            parentPath = NormalizeClipPathKey(parentPath);
            leafName = NormalizeClipPathKey(leafName);
            if (string.IsNullOrWhiteSpace(parentPath))
            {
                return leafName;
            }

            return string.IsNullOrWhiteSpace(leafName) ? parentPath : $"{parentPath}/{leafName}";
        }

        private static string GetClipPathParent(string path)
        {
            string normalizedPath = NormalizeClipPathKey(path);
            int slashIndex = normalizedPath.LastIndexOf('/');
            return slashIndex > 0 ? normalizedPath[..slashIndex] : string.Empty;
        }

        private static string GetClipPathLeafName(string path)
        {
            string normalizedPath = NormalizeClipPathKey(path);
            int slashIndex = normalizedPath.LastIndexOf('/');
            return slashIndex >= 0 && slashIndex + 1 < normalizedPath.Length
                ? normalizedPath[(slashIndex + 1)..]
                : normalizedPath;
        }

        private static bool IsClipInPath(string clipKey, string path)
        {
            clipKey = NormalizeClipPathKey(clipKey);
            path = NormalizeClipPathKey(path);
            return !string.IsNullOrWhiteSpace(path) &&
                   clipKey.StartsWith($"{path}/", StringComparison.Ordinal);
        }

        private static string GetClipPathSuffix(string clipKey, string path)
        {
            clipKey = NormalizeClipPathKey(clipKey);
            path = NormalizeClipPathKey(path);
            return IsClipInPath(clipKey, path) && clipKey.Length > path.Length + 1
                ? clipKey[(path.Length + 1)..]
                : clipKey;
        }

        private static T[] AppendItem<T>(T[] items, T item)
        {
            List<T> list = new(items ?? Array.Empty<T>()) { item };
            return list.ToArray();
        }

        private static T[] RemoveAt<T>(T[] items, int index)
        {
            List<T> list = new(items.Length - 1);
            for (int i = 0; i < items.Length; i++)
            {
                if (i != index)
                {
                    list.Add(items[i]);
                }
            }

            return list.ToArray();
        }

        private static void ReplaceIfEqual(ref string value, string oldValue, string newValue)
        {
            if (string.Equals(value, oldValue, StringComparison.Ordinal))
            {
                value = newValue;
            }
        }

        private string CreateUniqueCueEventKey(string prefix)
        {
            return CreateUniqueName(prefix, key =>
            {
                XAnimationCueConfig[] cues = m_CompiledAsset.Asset.cues ?? Array.Empty<XAnimationCueConfig>();
                for (int i = 0; i < cues.Length; i++)
                {
                    XAnimationCueConfig cue = cues[i];
                    if (cue != null && string.Equals(cue.eventKey, key, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }

                return false;
            });
        }

        private XAnimationCueConfig GetCueConfig(int cueIndex)
        {
            XAnimationCueConfig[] cues = m_CompiledAsset.Asset.cues ?? Array.Empty<XAnimationCueConfig>();
            if (cueIndex < 0 || cueIndex >= cues.Length || cues[cueIndex] == null)
            {
                throw new XAnimationException($"XAnimation cue index '{cueIndex}' does not exist.");
            }

            return cues[cueIndex];
        }

        private XAnimationBlend1DSampleConfig GetBlendSampleConfig(string channelName, string stateKey, int sampleIndex)
        {
            XAnimationStateConfig state = GetStateConfig(channelName, stateKey);
            XAnimationBlend1DSampleConfig[] samples = state.samples ?? Array.Empty<XAnimationBlend1DSampleConfig>();
            if (sampleIndex < 0 || sampleIndex >= samples.Length || samples[sampleIndex] == null)
            {
                throw new XAnimationException($"XAnimation Blend1D sample index '{sampleIndex}' does not exist.");
            }

            return samples[sampleIndex];
        }

        private XAnimationStateBehavior GetStateBehavior(string channelName, string stateKey, int behaviorIndex)
        {
            XAnimationStateConfig state = GetStateConfig(channelName, stateKey);
            XAnimationStateBehavior[] behaviors = state.behaviors ?? Array.Empty<XAnimationStateBehavior>();
            if (behaviorIndex < 0 || behaviorIndex >= behaviors.Length || behaviors[behaviorIndex] == null)
            {
                throw new XAnimationException($"XAnimation state behavior index '{behaviorIndex}' does not exist.");
            }

            return behaviors[behaviorIndex];
        }

        private void AddStateGateValue(string channelName, string stateKey, bool allowedNext)
        {
            XAnimationStateConfig state = GetStateConfig(channelName, stateKey);
            List<string> values = new(GetStateGateValues(state, allowedNext));
            string candidate = FindAvailableStateGateCandidate(channelName, stateKey, values);
            if (string.IsNullOrWhiteSpace(candidate))
            {
                throw new XAnimationException("没有更多可配置的 state。");
            }

            values.Add(candidate);
            SetStateGateValues(state, values, allowedNext);
            RebuildDriverAndSave();
        }

        private void DeleteStateGateValue(string channelName, string stateKey, int index, bool allowedNext)
        {
            XAnimationStateConfig state = GetStateConfig(channelName, stateKey);
            List<string> values = new(GetStateGateValues(state, allowedNext));
            if (index < 0 || index >= values.Count)
            {
                throw new XAnimationException($"XAnimation state gate index '{index}' does not exist.");
            }

            values.RemoveAt(index);
            SetStateGateValues(state, values, allowedNext);
            RebuildDriverAndSave();
        }

        private void SetStateGateValue(string channelName, string stateKey, int index, string targetStateKey, bool allowedNext)
        {
            targetStateKey = targetStateKey?.Trim();
            if (string.IsNullOrWhiteSpace(targetStateKey))
            {
                throw new XAnimationException("XAnimation state gate target cannot be empty.");
            }

            m_CompiledAsset.GetState(channelName, targetStateKey);
            XAnimationStateConfig state = GetStateConfig(channelName, stateKey);
            List<string> values = new(GetStateGateValues(state, allowedNext));
            if (index < 0 || index >= values.Count)
            {
                throw new XAnimationException($"XAnimation state gate index '{index}' does not exist.");
            }

            for (int i = 0; i < values.Count; i++)
            {
                if (i != index && string.Equals(values[i], targetStateKey, StringComparison.Ordinal))
                {
                    throw new XAnimationException($"XAnimation state '{stateKey}' gate target '{targetStateKey}' is duplicated.");
                }
            }

            values[index] = targetStateKey;
            SetStateGateValues(state, values, allowedNext);
            RebuildDriverAndSave();
        }

        private string FindAvailableStateGateCandidate(string channelName, string stateKey, IReadOnlyList<string> existing)
        {
            HashSet<string> used = new(existing ?? Array.Empty<string>(), StringComparer.Ordinal);
            IReadOnlyList<XAnimationCompiledState> states = m_CompiledAsset.States;
            for (int i = 0; i < states.Count; i++)
            {
                if (!string.Equals(states[i].Config.channelName, channelName, StringComparison.Ordinal))
                {
                    continue;
                }

                string candidate = states[i].Key;
                if (!string.Equals(candidate, stateKey, StringComparison.Ordinal) && !used.Contains(candidate))
                {
                    return candidate;
                }
            }

            return string.Empty;
        }

        private static string[] GetStateGateValues(XAnimationStateConfig state, bool allowedNext)
        {
            return allowedNext
                ? state.allowedNextStateKeys ?? Array.Empty<string>()
                : state.allowedPreviousStateKeys ?? Array.Empty<string>();
        }

        private static void SetStateGateValues(XAnimationStateConfig state, List<string> values, bool allowedNext)
        {
            string[] array = values == null || values.Count == 0 ? Array.Empty<string>() : values.ToArray();
            if (allowedNext)
            {
                state.allowedNextStateKeys = array;
            }
            else
            {
                state.allowedPreviousStateKeys = array;
            }
        }

        private XAnimationBlend2DSimpleDirectionalSampleConfig GetDirectionalBlendSampleConfig(string channelName, string stateKey, int sampleIndex)
        {
            XAnimationStateConfig state = GetStateConfig(channelName, stateKey);
            XAnimationBlend2DSimpleDirectionalSampleConfig[] samples =
                state.directionalSamples ?? Array.Empty<XAnimationBlend2DSimpleDirectionalSampleConfig>();
            if (sampleIndex < 0 || sampleIndex >= samples.Length || samples[sampleIndex] == null)
            {
                throw new XAnimationException($"XAnimation Blend2DSimpleDirectional sample index '{sampleIndex}' does not exist.");
            }

            return samples[sampleIndex];
        }

        private static string CreateUniqueName(string prefix, Predicate<string> exists)
        {
            if (!exists(prefix))
            {
                return prefix;
            }

            for (int i = 1; i < 10000; i++)
            {
                string name = $"{prefix}{i}";
                if (!exists(name))
                {
                    return name;
                }
            }

            throw new XAnimationException($"Unable to create unique name with prefix '{prefix}'.");
        }

        private static string FindTemplateClipPath(XAnimationClipConfig[] clips)
        {
            for (int i = 0; i < clips.Length; i++)
            {
                XAnimationClipConfig clip = clips[i];
                if (clip != null && !string.IsNullOrWhiteSpace(clip.clipPath))
                {
                    return clip.clipPath;
                }
            }

            return string.Empty;
        }

        private static Dictionary<string, string> BuildClipPathRenameMap(XAnimationClipConfig[] clips, string oldPath, string newPath)
        {
            Dictionary<string, string> renamedKeys = new(StringComparer.Ordinal);
            HashSet<string> resultingKeys = new(StringComparer.Ordinal);
            for (int i = 0; i < clips.Length; i++)
            {
                XAnimationClipConfig clip = clips[i];
                if (clip == null || string.IsNullOrWhiteSpace(clip.key))
                {
                    continue;
                }

                string clipKey = NormalizeClipPathKey(clip.key);
                string resultKey = clipKey;
                if (IsClipInPath(clipKey, oldPath))
                {
                    string suffix = GetClipPathSuffix(clipKey, oldPath);
                    resultKey = BuildClipPathKey(newPath, suffix);
                    renamedKeys[clip.key] = resultKey;
                }

                if (!resultingKeys.Add(resultKey))
                {
                    throw new XAnimationException($"XAnimation clip '{resultKey}' is duplicated.");
                }
            }

            return renamedKeys;
        }

        private static Dictionary<string, string> BuildStatePathRenameMap(
            XAnimationStateConfig[] states,
            string channelName,
            string oldPath,
            string newPath)
        {
            Dictionary<string, string> renamedKeys = new(StringComparer.Ordinal);
            HashSet<string> resultingKeys = new(StringComparer.Ordinal);
            for (int i = 0; i < states.Length; i++)
            {
                XAnimationStateConfig state = states[i];
                if (state == null ||
                    string.IsNullOrWhiteSpace(state.key) ||
                    !string.Equals(state.channelName, channelName, StringComparison.Ordinal))
                {
                    continue;
                }

                string stateKey = NormalizeStatePath(state.key);
                string resultKey = stateKey;
                if (IsStateInPath(stateKey, oldPath))
                {
                    string suffix = GetStatePathSuffix(stateKey, oldPath);
                    resultKey = BuildStatePathKey(newPath, suffix);
                    renamedKeys[state.key] = resultKey;
                }

                if (!resultingKeys.Add(resultKey))
                {
                    throw new XAnimationException($"XAnimation state '{resultKey}' is duplicated in channel '{channelName}'.");
                }
            }

            return renamedKeys;
        }

        private void ApplyClipKeyRenameMap(XAnimationAsset asset, Dictionary<string, string> renamedKeys)
        {
            XAnimationClipConfig[] clips = asset.clips ?? Array.Empty<XAnimationClipConfig>();
            for (int i = 0; i < clips.Length; i++)
            {
                XAnimationClipConfig clip = clips[i];
                if (clip != null && renamedKeys.TryGetValue(clip.key, out string newKey))
                {
                    clip.key = newKey;
                }
            }

            RenameCueClipReferences(asset, renamedKeys);
            RenameStateClipReferences(asset, renamedKeys);
            foreach (KeyValuePair<string, string> pair in renamedKeys)
            {
                if (m_OriginalClipPathByKey.Remove(pair.Key, out string originalClipPath))
                {
                    m_OriginalClipPathByKey[pair.Value] = originalClipPath;
                }
            }
        }

        private static void ApplyStateKeyRenameMap(
            XAnimationAsset asset,
            string channelName,
            Dictionary<string, string> renamedKeys)
        {
            foreach (KeyValuePair<string, string> pair in renamedKeys)
            {
                RenameAutoTransitionReferences(asset, channelName, pair.Key, pair.Value);
                RenameDefaultTransitionReferences(asset, channelName, pair.Key, pair.Value);
                RenameStateGateReferences(asset, channelName, pair.Key, pair.Value);
            }

            XAnimationStateConfig[] states = asset.states ?? Array.Empty<XAnimationStateConfig>();
            for (int i = 0; i < states.Length; i++)
            {
                XAnimationStateConfig state = states[i];
                if (state != null &&
                    string.Equals(state.channelName, channelName, StringComparison.Ordinal) &&
                    renamedKeys.TryGetValue(state.key, out string newKey))
                {
                    state.key = newKey;
                }
            }
        }

        private static string FindTemplateClipKey(XAnimationClipConfig[] clips)
        {
            for (int i = 0; i < clips.Length; i++)
            {
                XAnimationClipConfig clip = clips[i];
                if (clip != null && !string.IsNullOrWhiteSpace(clip.key))
                {
                    return clip.key;
                }
            }

            return string.Empty;
        }

        private string EnsureFloatParameter(string prefix = "blend")
        {
            XAnimationParameterConfig[] parameters = m_CompiledAsset.Asset.parameters ?? Array.Empty<XAnimationParameterConfig>();
            for (int i = 0; i < parameters.Length; i++)
            {
                XAnimationParameterConfig parameter = parameters[i];
                if (parameter != null && parameter.type == XAnimationParameterType.Float && !string.IsNullOrWhiteSpace(parameter.name))
                {
                    if (string.IsNullOrWhiteSpace(prefix) || parameter.name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        return parameter.name;
                    }
                }
            }

            string parameterName = CreateUniqueParameterName(prefix);
            List<XAnimationParameterConfig> orderedParameters = new(parameters)
            {
                new XAnimationParameterConfig
                {
                    name = parameterName,
                    type = XAnimationParameterType.Float,
                    defaultValue = 0f,
                }
            };
            m_CompiledAsset.Asset.parameters = orderedParameters.ToArray();
            return parameterName;
        }

        private string CreateUniqueParameterName(string prefix)
        {
            return CreateUniqueName(prefix, name =>
            {
                XAnimationParameterConfig[] parameters = m_CompiledAsset.Asset.parameters ?? Array.Empty<XAnimationParameterConfig>();
                for (int i = 0; i < parameters.Length; i++)
                {
                    XAnimationParameterConfig parameter = parameters[i];
                    if (parameter != null && string.Equals(parameter.name, name, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }

                return false;
            });
        }

        private XAnimationBlend1DSampleConfig[] CreateDefaultBlendSamples(string channelName)
        {
            XAnimationClipConfig[] clips = m_CompiledAsset.Asset.clips ?? Array.Empty<XAnimationClipConfig>();
            List<string> clipKeys = new(2);
            for (int i = 0; i < clips.Length && clipKeys.Count < 2; i++)
            {
                XAnimationClipConfig clip = clips[i];
                if (clip != null && !string.IsNullOrWhiteSpace(clip.key) && !clipKeys.Contains(clip.key))
                {
                    clipKeys.Add(clip.key);
                }
            }

            if (clipKeys.Count < 2)
            {
                throw new XAnimationException("Cannot create Blend1D state because at least two clips are required.");
            }

            return new[]
            {
                new XAnimationBlend1DSampleConfig
                {
                    clipKey = clipKeys[0],
                    threshold = 0f,
                },
                new XAnimationBlend1DSampleConfig
                {
                    clipKey = clipKeys[1],
                    threshold = 1f,
                }
            };
        }

        private XAnimationBlend2DSimpleDirectionalSampleConfig[] CreateDefaultDirectionalBlendSamples()
        {
            XAnimationClipConfig[] clips = m_CompiledAsset.Asset.clips ?? Array.Empty<XAnimationClipConfig>();
            if (clips.Length < 2)
            {
                throw new XAnimationException("Cannot create Blend2DSimpleDirectional state because at least two clips are required.");
            }

            string idleClipKey = FindTemplateClipKey(clips);
            string directionalClipKey = idleClipKey;
            for (int i = 0; i < clips.Length; i++)
            {
                XAnimationClipConfig clip = clips[i];
                if (clip == null || string.IsNullOrWhiteSpace(clip.key) || string.Equals(clip.key, idleClipKey, StringComparison.Ordinal))
                {
                    continue;
                }

                directionalClipKey = clip.key;
                break;
            }

            return new[]
            {
                new XAnimationBlend2DSimpleDirectionalSampleConfig
                {
                    clipKey = idleClipKey,
                    positionX = 0f,
                    positionY = 0f,
                },
                new XAnimationBlend2DSimpleDirectionalSampleConfig
                {
                    clipKey = directionalClipKey,
                    positionX = 0f,
                    positionY = 1f,
                }
            };
        }

        private string ResolvePreferredSingleClipKey(XAnimationStateConfig state)
        {
            if (state == null)
            {
                return FindTemplateClipKey(m_CompiledAsset.Asset.clips ?? Array.Empty<XAnimationClipConfig>());
            }

            if (!string.IsNullOrWhiteSpace(state.clipKey))
            {
                return state.clipKey;
            }

            if (state.stateType == XAnimationStateType.Blend1D)
            {
                return GetFirstBlendSampleClipKey(state) ??
                       FindTemplateClipKey(m_CompiledAsset.Asset.clips ?? Array.Empty<XAnimationClipConfig>());
            }

            if (IsDirectionalBlendStateType(state.stateType))
            {
                return GetIdleDirectionalClipKey(state) ??
                       GetFirstDirectionalClipKey(state) ??
                       FindTemplateClipKey(m_CompiledAsset.Asset.clips ?? Array.Empty<XAnimationClipConfig>());
            }

            return FindTemplateClipKey(m_CompiledAsset.Asset.clips ?? Array.Empty<XAnimationClipConfig>());
        }

        private XAnimationBlend1DSampleConfig[] BuildMigratedBlendSamples(XAnimationStateConfig state)
        {
            if (state == null)
            {
                return CreateDefaultBlendSamples(string.Empty);
            }

            if (state.stateType == XAnimationStateType.Blend1D && (state.samples?.Length ?? 0) >= 2)
            {
                return CloneBlendSamples(state.samples);
            }

            XAnimationBlend1DSampleConfig[] samples = CreateDefaultBlendSamples(state.channelName);
            List<string> seedClipKeys = GetBlendSeedClipKeys(state);
            for (int i = 0; i < samples.Length && i < seedClipKeys.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(seedClipKeys[i]))
                {
                    samples[i].clipKey = seedClipKeys[i];
                }
            }

            return samples;
        }

        private XAnimationBlend2DSimpleDirectionalSampleConfig[] BuildMigratedDirectionalSamples(XAnimationStateConfig state)
        {
            if (state == null)
            {
                return CreateDefaultDirectionalBlendSamples();
            }

            if (IsDirectionalBlendStateType(state.stateType) && (state.directionalSamples?.Length ?? 0) >= 2)
            {
                return CloneDirectionalSamples(state.directionalSamples);
            }

            XAnimationBlend2DSimpleDirectionalSampleConfig[] samples = CreateDefaultDirectionalBlendSamples();
            List<string> seedClipKeys = GetDirectionalSeedClipKeys(state);
            for (int i = 0; i < samples.Length && i < seedClipKeys.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(seedClipKeys[i]))
                {
                    samples[i].clipKey = seedClipKeys[i];
                }
            }

            return samples;
        }

        private static XAnimationBlend1DSampleConfig[] CloneBlendSamples(XAnimationBlend1DSampleConfig[] samples)
        {
            if (samples == null || samples.Length == 0)
            {
                return Array.Empty<XAnimationBlend1DSampleConfig>();
            }

            XAnimationBlend1DSampleConfig[] cloned = new XAnimationBlend1DSampleConfig[samples.Length];
            for (int i = 0; i < samples.Length; i++)
            {
                XAnimationBlend1DSampleConfig sample = samples[i];
                cloned[i] = sample == null
                    ? null
                    : new XAnimationBlend1DSampleConfig
                    {
                        clipKey = sample.clipKey,
                        threshold = sample.threshold,
                    };
            }

            return cloned;
        }

        private static XAnimationBlend2DSimpleDirectionalSampleConfig[] CloneDirectionalSamples(XAnimationBlend2DSimpleDirectionalSampleConfig[] samples)
        {
            if (samples == null || samples.Length == 0)
            {
                return Array.Empty<XAnimationBlend2DSimpleDirectionalSampleConfig>();
            }

            XAnimationBlend2DSimpleDirectionalSampleConfig[] cloned = new XAnimationBlend2DSimpleDirectionalSampleConfig[samples.Length];
            for (int i = 0; i < samples.Length; i++)
            {
                XAnimationBlend2DSimpleDirectionalSampleConfig sample = samples[i];
                cloned[i] = sample == null
                    ? null
                    : new XAnimationBlend2DSimpleDirectionalSampleConfig
                    {
                        clipKey = sample.clipKey,
                        positionX = sample.positionX,
                        positionY = sample.positionY,
                    };
            }

            return cloned;
        }

        private List<string> GetBlendSeedClipKeys(XAnimationStateConfig state)
        {
            List<string> seedClipKeys = new(2);
            if (state == null)
            {
                return seedClipKeys;
            }

            if (state.stateType == XAnimationStateType.Single)
            {
                AddOrderedClipKey(seedClipKeys, state.clipKey);
                return seedClipKeys;
            }

            if (IsDirectionalBlendStateType(state.stateType))
            {
                string idleClipKey = GetIdleDirectionalClipKey(state);
                AddOrderedClipKey(seedClipKeys, idleClipKey);
                XAnimationBlend2DSimpleDirectionalSampleConfig[] samples = state.directionalSamples ?? Array.Empty<XAnimationBlend2DSimpleDirectionalSampleConfig>();
                for (int i = 0; i < samples.Length && seedClipKeys.Count < 2; i++)
                {
                    XAnimationBlend2DSimpleDirectionalSampleConfig sample = samples[i];
                    if (sample == null)
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(idleClipKey) &&
                        Mathf.Approximately(sample.positionX, 0f) &&
                        Mathf.Approximately(sample.positionY, 0f))
                    {
                        continue;
                    }

                    AddOrderedClipKey(seedClipKeys, sample.clipKey);
                }

                return seedClipKeys;
            }

            return seedClipKeys;
        }

        private List<string> GetDirectionalSeedClipKeys(XAnimationStateConfig state)
        {
            List<string> seedClipKeys = new(2);
            if (state == null)
            {
                return seedClipKeys;
            }

            if (state.stateType == XAnimationStateType.Single)
            {
                AddOrderedClipKey(seedClipKeys, state.clipKey);
                return seedClipKeys;
            }

            if (state.stateType == XAnimationStateType.Blend1D)
            {
                XAnimationBlend1DSampleConfig[] samples = state.samples ?? Array.Empty<XAnimationBlend1DSampleConfig>();
                if (samples.Length > 0)
                {
                    AddOrderedClipKey(seedClipKeys, samples[0]?.clipKey);
                }

                if (samples.Length > 1)
                {
                    AddOrderedClipKey(seedClipKeys, samples[1]?.clipKey);
                }
            }

            return seedClipKeys;
        }

        private static string GetFirstBlendSampleClipKey(XAnimationStateConfig state)
        {
            XAnimationBlend1DSampleConfig[] samples = state?.samples ?? Array.Empty<XAnimationBlend1DSampleConfig>();
            for (int i = 0; i < samples.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(samples[i]?.clipKey))
                {
                    return samples[i].clipKey;
                }
            }

            return null;
        }

        private static string GetIdleDirectionalClipKey(XAnimationStateConfig state)
        {
            XAnimationBlend2DSimpleDirectionalSampleConfig[] samples = state?.directionalSamples ?? Array.Empty<XAnimationBlend2DSimpleDirectionalSampleConfig>();
            for (int i = 0; i < samples.Length; i++)
            {
                XAnimationBlend2DSimpleDirectionalSampleConfig sample = samples[i];
                if (sample != null &&
                    Mathf.Approximately(sample.positionX, 0f) &&
                    Mathf.Approximately(sample.positionY, 0f) &&
                    !string.IsNullOrWhiteSpace(sample.clipKey))
                {
                    return sample.clipKey;
                }
            }

            return null;
        }

        private static string GetFirstDirectionalClipKey(XAnimationStateConfig state)
        {
            XAnimationBlend2DSimpleDirectionalSampleConfig[] samples = state?.directionalSamples ?? Array.Empty<XAnimationBlend2DSimpleDirectionalSampleConfig>();
            for (int i = 0; i < samples.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(samples[i]?.clipKey))
                {
                    return samples[i].clipKey;
                }
            }

            return null;
        }

        private static void AddOrderedClipKey(List<string> clipKeys, string clipKey)
        {
            if (!string.IsNullOrWhiteSpace(clipKey))
            {
                clipKeys.Add(clipKey);
            }
        }

        private static Vector2 FindNextDirectionalBlendSamplePosition(
            IReadOnlyList<XAnimationBlend2DSimpleDirectionalSampleConfig> samples)
        {
            Vector2[] candidates =
            {
                new(0f, 1f),
                new(1f, 0f),
                new(0f, -1f),
                new(-1f, 0f),
                new(0.707f, 0.707f),
                new(0.707f, -0.707f),
                new(-0.707f, -0.707f),
                new(-0.707f, 0.707f),
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                if (!ContainsDirectionalSamplePosition(samples, candidates[i]))
                {
                    return candidates[i];
                }
            }

            float radius = 2f;
            while (radius < 1000f)
            {
                Vector2 candidate = new(0f, radius);
                if (!ContainsDirectionalSamplePosition(samples, candidate))
                {
                    return candidate;
                }

                radius += 1f;
            }

            return new(0f, 1f);
        }

        private static bool ContainsDirectionalSamplePosition(
            IReadOnlyList<XAnimationBlend2DSimpleDirectionalSampleConfig> samples,
            Vector2 position)
        {
            if (samples == null)
            {
                return false;
            }

            for (int i = 0; i < samples.Count; i++)
            {
                XAnimationBlend2DSimpleDirectionalSampleConfig sample = samples[i];
                if (sample == null)
                {
                    continue;
                }

                if (Mathf.Approximately(sample.positionX, position.x) &&
                    Mathf.Approximately(sample.positionY, position.y))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsIdleDirectionalSample(XAnimationBlend2DSimpleDirectionalSampleConfig sample)
        {
            return sample != null &&
                   Mathf.Approximately(sample.positionX, 0f) &&
                   Mathf.Approximately(sample.positionY, 0f);
        }

        private static bool IsDirectionalBlendStateType(XAnimationStateType stateType)
        {
            return stateType == XAnimationStateType.Blend2DSimpleDirectional ||
                   stateType == XAnimationStateType.Blend2DFreeformDirectional;
        }

        private static void RemoveCueReferences(XAnimationAsset asset, HashSet<string> removedClipKeys)
        {
            if (asset.cues == null || removedClipKeys == null || removedClipKeys.Count == 0)
            {
                return;
            }

            List<XAnimationCueConfig> cues = new(asset.cues.Length);
            for (int i = 0; i < asset.cues.Length; i++)
            {
                XAnimationCueConfig cue = asset.cues[i];
                if (cue == null || !removedClipKeys.Contains(cue.clipKey))
                {
                    cues.Add(cue);
                }
            }

            asset.cues = cues.ToArray();
        }

        private static void RemoveStateReferences(XAnimationAsset asset, HashSet<string> removedClipKeys)
        {
            if (asset.states == null || removedClipKeys == null || removedClipKeys.Count == 0)
            {
                return;
            }

            List<XAnimationStateConfig> states = new(asset.states.Length);
            for (int i = 0; i < asset.states.Length; i++)
            {
                XAnimationStateConfig state = asset.states[i];
                if (state == null)
                {
                    continue;
                }

                if (state.stateType == XAnimationStateType.Single && removedClipKeys.Contains(state.clipKey))
                {
                    continue;
                }

                if (state.stateType == XAnimationStateType.Blend1D && HasRemovedBlendSample(state, removedClipKeys))
                {
                    continue;
                }

                if (IsDirectionalBlendStateType(state.stateType) && HasRemovedDirectionalBlendSample(state, removedClipKeys))
                {
                    continue;
                }

                states.Add(state);
            }

            asset.states = states.ToArray();
        }

        private static void RemoveStatesInChannel(XAnimationAsset asset, string channelName)
        {
            if (asset.states == null)
            {
                return;
            }

            List<XAnimationStateConfig> states = new(asset.states.Length);
            for (int i = 0; i < asset.states.Length; i++)
            {
                XAnimationStateConfig state = asset.states[i];
                if (state == null || string.Equals(state.channelName, channelName, StringComparison.Ordinal))
                {
                    continue;
                }

                states.Add(state);
            }

            asset.states = states.ToArray();
        }

        private static bool HasRemovedBlendSample(XAnimationStateConfig state, HashSet<string> removedClipKeys)
        {
            XAnimationBlend1DSampleConfig[] samples = state.samples ?? Array.Empty<XAnimationBlend1DSampleConfig>();
            for (int i = 0; i < samples.Length; i++)
            {
                XAnimationBlend1DSampleConfig sample = samples[i];
                if (sample != null && removedClipKeys.Contains(sample.clipKey))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasRemovedDirectionalBlendSample(XAnimationStateConfig state, HashSet<string> removedClipKeys)
        {
            XAnimationBlend2DSimpleDirectionalSampleConfig[] samples =
                state.directionalSamples ?? Array.Empty<XAnimationBlend2DSimpleDirectionalSampleConfig>();
            for (int i = 0; i < samples.Length; i++)
            {
                XAnimationBlend2DSimpleDirectionalSampleConfig sample = samples[i];
                if (sample != null && removedClipKeys.Contains(sample.clipKey))
                {
                    return true;
                }
            }

            return false;
        }

        private static void RenameStateChannelReferences(XAnimationAsset asset, string oldName, string newName)
        {
            if (asset.states == null)
            {
                return;
            }

            for (int i = 0; i < asset.states.Length; i++)
            {
                XAnimationStateConfig state = asset.states[i];
                if (state != null)
                {
                    ReplaceIfEqual(ref state.channelName, oldName, newName);
                }
            }
        }

        private static void RenameStateClipReferences(XAnimationAsset asset, string oldKey, string newKey)
        {
            RenameStateClipReferences(asset, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [oldKey] = newKey,
            });
        }

        private static void RenameStateClipReferences(XAnimationAsset asset, Dictionary<string, string> renamedKeys)
        {
            if (asset.states == null)
            {
                return;
            }

            for (int i = 0; i < asset.states.Length; i++)
            {
                XAnimationStateConfig state = asset.states[i];
                if (state == null)
                {
                    continue;
                }

                ReplaceIfMapped(ref state.clipKey, renamedKeys);

                XAnimationBlend1DSampleConfig[] samples = state.samples ?? Array.Empty<XAnimationBlend1DSampleConfig>();
                for (int sampleIndex = 0; sampleIndex < samples.Length; sampleIndex++)
                {
                    XAnimationBlend1DSampleConfig sample = samples[sampleIndex];
                    if (sample != null)
                    {
                        ReplaceIfMapped(ref sample.clipKey, renamedKeys);
                    }
                }

                XAnimationBlend2DSimpleDirectionalSampleConfig[] directionalSamples =
                    state.directionalSamples ?? Array.Empty<XAnimationBlend2DSimpleDirectionalSampleConfig>();
                for (int sampleIndex = 0; sampleIndex < directionalSamples.Length; sampleIndex++)
                {
                    XAnimationBlend2DSimpleDirectionalSampleConfig sample = directionalSamples[sampleIndex];
                    if (sample != null)
                    {
                        ReplaceIfMapped(ref sample.clipKey, renamedKeys);
                    }
                }
            }
        }

        private static void RenameCueClipReferences(XAnimationAsset asset, string oldKey, string newKey)
        {
            RenameCueClipReferences(asset, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [oldKey] = newKey,
            });
        }

        private static void RenameCueClipReferences(XAnimationAsset asset, Dictionary<string, string> renamedKeys)
        {
            if (asset.cues == null)
            {
                return;
            }

            for (int i = 0; i < asset.cues.Length; i++)
            {
                XAnimationCueConfig cue = asset.cues[i];
                if (cue != null)
                {
                    ReplaceIfMapped(ref cue.clipKey, renamedKeys);
                }
            }
        }

        private static void ReplaceIfMapped(ref string value, Dictionary<string, string> renamedKeys)
        {
            if (renamedKeys != null && value != null && renamedKeys.TryGetValue(value, out string newValue))
            {
                value = newValue;
            }
        }

        private static void RenameStateParameterReferences(XAnimationAsset asset, string oldName, string newName)
        {
            if (asset.states == null)
            {
                return;
            }

            for (int i = 0; i < asset.states.Length; i++)
            {
                XAnimationStateConfig state = asset.states[i];
                if (state == null)
                {
                    continue;
                }

                ReplaceIfEqual(ref state.parameterName, oldName, newName);
                ReplaceIfEqual(ref state.parameterXName, oldName, newName);
                ReplaceIfEqual(ref state.parameterYName, oldName, newName);
            }
        }

        private static bool HasStateParameterReference(XAnimationAsset asset, string parameterName)
        {
            if (asset.states == null)
            {
                return false;
            }

            for (int i = 0; i < asset.states.Length; i++)
            {
                XAnimationStateConfig state = asset.states[i];
                if (state == null)
                {
                    continue;
                }

                if (string.Equals(state.parameterName, parameterName, StringComparison.Ordinal) ||
                    string.Equals(state.parameterXName, parameterName, StringComparison.Ordinal) ||
                    string.Equals(state.parameterYName, parameterName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static void RemoveStateParameterReferences(XAnimationAsset asset, string parameterName, string fallbackParameterName)
        {
            if (asset.states == null)
            {
                return;
            }

            for (int i = 0; i < asset.states.Length; i++)
            {
                XAnimationStateConfig state = asset.states[i];
                if (state == null)
                {
                    continue;
                }

                if (string.Equals(state.parameterName, parameterName, StringComparison.Ordinal))
                {
                    state.parameterName = fallbackParameterName ?? string.Empty;
                }

                if (string.Equals(state.parameterXName, parameterName, StringComparison.Ordinal))
                {
                    state.parameterXName = fallbackParameterName ?? string.Empty;
                }

                if (string.Equals(state.parameterYName, parameterName, StringComparison.Ordinal))
                {
                    state.parameterYName = fallbackParameterName ?? string.Empty;
                }
            }
        }

        private static float ConvertParameterDefaultToFloat(object value)
        {
            if (value == null)
            {
                return 0f;
            }

            return Convert.ToSingle(value, CultureInfo.InvariantCulture);
        }

        private static bool ConvertParameterDefaultToBool(object value)
        {
            if (value == null)
            {
                return false;
            }

            return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
        }

        private static object ConvertStateBehaviorFieldValue(Type fieldType, object value)
        {
            if (fieldType == null)
            {
                return value;
            }

            if (value == null)
            {
                return fieldType.IsValueType ? Activator.CreateInstance(fieldType) : null;
            }

            if (fieldType.IsInstanceOfType(value))
            {
                return value;
            }

            if (fieldType == typeof(string))
            {
                return value.ToString();
            }

            if (fieldType.IsEnum)
            {
                if (value is Enum enumValue)
                {
                    return Enum.ToObject(fieldType, enumValue);
                }

                string enumText = value.ToString();
                return string.IsNullOrWhiteSpace(enumText)
                    ? Activator.CreateInstance(fieldType)
                    : Enum.Parse(fieldType, enumText);
            }

            return Convert.ChangeType(value, fieldType, CultureInfo.InvariantCulture);
        }

        private static int ConvertParameterDefaultToInt(object value)
        {
            if (value == null)
            {
                return 0;
            }

            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }

        private void SaveCompiledAsset()
        {
            if (m_IsOverrideAsset)
            {
                m_OverrideAsset?.SaveAsset();
                return;
            }

            if (m_CompiledAsset?.Asset == null)
            {
                return;
            }

            m_CompiledAsset.Asset.SaveAsset();
        }

        public void SaveCurrentAsset()
        {
            EnsureLoaded();
            SaveCompiledAsset();
        }

        public void SetAssetPreload(bool preload, bool save = true)
        {
            EnsureLoaded();
            m_CompiledAsset.Asset.preload = preload;
            if (preload)
            {
                m_EditorActor.PreloadAll();
            }

            SaveCompiledAssetIfNeeded(save);
        }

        public void SetAssetRootMotion(bool rootMotion, bool save = true)
        {
            EnsureLoaded();
            m_CompiledAsset.Asset.rootMotion = rootMotion;
            m_RootMotionEnabled = rootMotion;
            m_EditorActor.SetRootMotionEnabled(rootMotion);
            if (!rootMotion)
            {
                ResetTransform();
            }

            SaveCompiledAssetIfNeeded(save);
        }

        private void SaveCompiledAssetIfNeeded(bool save)
        {
            if (save)
            {
                AssetChanged?.Invoke();
            }
        }

        private void RebuildDriverAndSave(bool save = true)
        {
            RebuildCompiledAsset();
            RebuildDriver();
            SaveCompiledAssetIfNeeded(save);
        }

        private void RebuildCompiledAsset()
        {
            m_CompiledAsset = m_AssetLoader.Compile(m_CompiledAsset.Asset);
        }

        private void RebuildDriver()
        {
            if (m_Animator != null)
            {
                m_EditorActor.Rebuild(m_CompiledAsset, m_Animator);
            }
        }

        public void SetAutoTransitionNextState(string channelName, string stateKey, string nextStateKey, bool save = true)
        {
            EnsureLoaded();
            channelName = NormalizeRequiredChannelName(channelName);
            stateKey = NormalizeRequiredStateKey(stateKey);
            m_CompiledAsset.GetState(channelName, stateKey);
            if (!string.IsNullOrWhiteSpace(nextStateKey))
            {
                m_CompiledAsset.GetState(channelName, nextStateKey.Trim());
            }

            XAnimationAutoTransitionConfig config = GetOrCreateAutoTransition(m_CompiledAsset.Asset, channelName, stateKey);
            config.nextStateKey = string.IsNullOrWhiteSpace(nextStateKey) ? string.Empty : nextStateKey.Trim();
            RebuildDriverAndSave(save);
        }

        public void SetAutoTransitionTiming(
            string channelName,
            string stateKey,
            float exitTime,
            float transitionDuration,
            float enterTime,
            bool save = true)
        {
            EnsureLoaded();
            channelName = NormalizeRequiredChannelName(channelName);
            stateKey = NormalizeRequiredStateKey(stateKey);
            m_CompiledAsset.GetState(channelName, stateKey);
            XAnimationAutoTransitionConfig config = GetOrCreateAutoTransition(m_CompiledAsset.Asset, channelName, stateKey);
            config.exitTime = Mathf.Clamp01(exitTime);
            config.transitionDuration = Mathf.Max(0f, transitionDuration);
            config.enterTime = Mathf.Clamp01(enterTime);
            RebuildDriverAndSave(save);
        }

        public XAnimationCompiledAutoTransition AddAutoTransition(string preferredChannelName = null, string preferredPreStateKey = null)
        {
            EnsureBaseAssetEditable();
            XAnimationCompiledState preState = ResolveAutoTransitionPreState(preferredChannelName, preferredPreStateKey);
            string channelName = preState.Config.channelName;
            string preStateKey = preState.Key;
            XAnimationAsset asset = m_CompiledAsset.Asset;
            if (FindAutoTransition(asset, channelName, preStateKey) == null)
            {
                asset.autoTransitions = AppendItem(asset.autoTransitions, new XAnimationAutoTransitionConfig
                {
                    channelName = channelName,
                    preStateKey = preStateKey,
                    nextStateKey = string.Empty,
                    exitTime = 1f,
                    transitionDuration = 0f,
                    enterTime = 0f,
                });
            }

            RebuildDriverAndSave();
            return m_CompiledAsset.GetAutoTransition(channelName, preStateKey);
        }

        public void DeleteAutoTransition(string channelName, string preStateKey)
        {
            EnsureBaseAssetEditable();
            channelName = NormalizeRequiredChannelName(channelName);
            preStateKey = NormalizeRequiredStateKey(preStateKey);
            XAnimationAutoTransitionConfig[] transitions = m_CompiledAsset.Asset.autoTransitions ?? Array.Empty<XAnimationAutoTransitionConfig>();
            List<XAnimationAutoTransitionConfig> remaining = new(transitions.Length);
            bool removed = false;
            for (int i = 0; i < transitions.Length; i++)
            {
                XAnimationAutoTransitionConfig transition = transitions[i];
                if (transition == null)
                {
                    continue;
                }

                if (IsAutoTransitionForState(m_CompiledAsset.Asset, transition, channelName, preStateKey))
                {
                    removed = true;
                    continue;
                }

                remaining.Add(transition);
            }

            if (!removed)
            {
                throw new XAnimationException($"XAnimation auto transition '{preStateKey}' in channel '{channelName}' does not exist.");
            }

            m_CompiledAsset.Asset.autoTransitions = remaining.ToArray();
            RebuildDriverAndSave();
        }

        public void SetAutoTransitionPreState(string channelName, string currentPreStateKey, string newPreStateKey, bool save = true)
        {
            EnsureBaseAssetEditable();
            channelName = NormalizeRequiredChannelName(channelName);
            currentPreStateKey = NormalizeRequiredStateKey(currentPreStateKey);
            newPreStateKey = newPreStateKey?.Trim();
            if (string.Equals(currentPreStateKey, newPreStateKey, StringComparison.Ordinal))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(newPreStateKey))
            {
                throw new XAnimationException("XAnimation auto transition preStateKey cannot be empty.");
            }

            XAnimationCompiledState newPreState = m_CompiledAsset.GetState(channelName, newPreStateKey);
            if (newPreState.Config.loop)
            {
                throw new XAnimationException($"XAnimation state '{newPreStateKey}' in channel '{channelName}' is looping and cannot configure auto transition.");
            }

            XAnimationAutoTransitionConfig transition = FindAutoTransition(m_CompiledAsset.Asset, channelName, currentPreStateKey);
            if (transition == null)
            {
                throw new XAnimationException($"XAnimation auto transition '{currentPreStateKey}' in channel '{channelName}' does not exist.");
            }

            if (FindAutoTransition(m_CompiledAsset.Asset, channelName, newPreStateKey) != null)
            {
                throw new XAnimationException($"XAnimation auto transition preState '{newPreStateKey}' in channel '{channelName}' is duplicated.");
            }

            transition.channelName = channelName;
            transition.preStateKey = newPreStateKey;
            RebuildDriverAndSave(save);
        }

        public bool TryGetAutoTransition(string channelName, string preStateKey, out XAnimationAutoTransitionConfig config)
        {
            EnsureLoaded();
            config = FindAutoTransition(m_CompiledAsset.Asset, channelName, preStateKey);
            return config != null;
        }

        public int AddDefaultTransition()
        {
            EnsureBaseAssetEditable();
            XAnimationAsset asset = m_CompiledAsset.Asset;
            XAnimationDefaultTransitionConfig transition = CreateDefaultTransitionConfig(-1);
            asset.defaultTransitions = AppendItem(asset.defaultTransitions, new XAnimationDefaultTransitionConfig
            {
                channelName = transition.channelName,
                preStateKey = transition.preStateKey,
                nextStateKey = transition.nextStateKey,
                fadeIn = transition.fadeIn,
                fadeOut = transition.fadeOut,
                enterTime = transition.enterTime,
                priority = transition.priority,
                interruptible = transition.interruptible,
            });
            RebuildDriverAndSave();
            return asset.defaultTransitions.Length - 1;
        }

        public int AddDefaultTransition(string preStateKey, string nextStateKey, bool save = true)
            => AddDefaultTransition(null, preStateKey, nextStateKey, save);

        public int AddDefaultTransition(string channelName, string preStateKey, string nextStateKey, bool save = true)
        {
            EnsureBaseAssetEditable();
            channelName = channelName?.Trim();
            preStateKey = preStateKey?.Trim();
            nextStateKey = nextStateKey?.Trim();

            XAnimationAsset asset = m_CompiledAsset.Asset;
            XAnimationDefaultTransitionConfig[] transitions = asset.defaultTransitions ?? Array.Empty<XAnimationDefaultTransitionConfig>();
            int transitionIndex = transitions.Length;
            channelName = ResolveDefaultTransitionChannelName(channelName, preStateKey, nextStateKey);
            ValidateDefaultTransitionPairChange(transitionIndex, channelName, preStateKey, nextStateKey);

            asset.defaultTransitions = AppendItem(transitions, new XAnimationDefaultTransitionConfig
            {
                channelName = channelName,
                preStateKey = preStateKey,
                nextStateKey = nextStateKey,
                fadeIn = 0.15f,
                fadeOut = 0.15f,
                enterTime = 0f,
                priority = 0,
                interruptible = true,
            });
            RebuildDriverAndSave(save);
            return asset.defaultTransitions.Length - 1;
        }

        public void DeleteDefaultTransition(int transitionIndex)
        {
            EnsureBaseAssetEditable();
            XAnimationDefaultTransitionConfig[] transitions = m_CompiledAsset.Asset.defaultTransitions ?? Array.Empty<XAnimationDefaultTransitionConfig>();
            if (transitionIndex < 0 || transitionIndex >= transitions.Length)
            {
                throw new XAnimationException($"XAnimation default transition index '{transitionIndex}' does not exist.");
            }

            m_CompiledAsset.Asset.defaultTransitions = RemoveAt(transitions, transitionIndex);
            RebuildDriverAndSave();
        }

        public void SetDefaultTransitionChannel(int transitionIndex, string channelName, bool save = true)
        {
            EnsureBaseAssetEditable();
            XAnimationDefaultTransitionConfig transition = GetDefaultTransitionConfig(transitionIndex);
            channelName = ResolveDefaultTransitionChannelName(channelName, transition.preStateKey, transition.nextStateKey);
            ValidateDefaultTransitionPairChange(transitionIndex, channelName, transition.preStateKey, transition.nextStateKey);
            transition.channelName = channelName;
            RebuildDriverAndSave(save);
        }

        public void SetDefaultTransitionOptions(
            int transitionIndex,
            float fadeIn,
            float fadeOut,
            float enterTime,
            int priority,
            bool interruptible,
            bool save = true)
        {
            EnsureBaseAssetEditable();
            XAnimationDefaultTransitionConfig transition = GetDefaultTransitionConfig(transitionIndex);
            transition.fadeIn = Mathf.Max(0f, fadeIn);
            transition.fadeOut = Mathf.Max(0f, fadeOut);
            transition.enterTime = Mathf.Clamp01(enterTime);
            transition.priority = priority;
            transition.interruptible = interruptible;
            RebuildDriverAndSave(save);
        }

        public int AddDefaultTransitionPair(int transitionIndex)
        {
            EnsureBaseAssetEditable();
            XAnimationAsset asset = m_CompiledAsset.Asset;
            XAnimationDefaultTransitionConfig transition = CreateDefaultTransitionConfig(transitionIndex);
            asset.defaultTransitions = AppendItem(asset.defaultTransitions, transition);
            RebuildDriverAndSave();
            return 0;
        }

        public int AddDefaultTransitionPair(int transitionIndex, string preStateKey, string nextStateKey, bool save = true)
        {
            EnsureBaseAssetEditable();
            preStateKey = preStateKey?.Trim();
            nextStateKey = nextStateKey?.Trim();

            XAnimationDefaultTransitionConfig source = transitionIndex >= 0 &&
                transitionIndex < (m_CompiledAsset.Asset.defaultTransitions?.Length ?? 0)
                    ? GetDefaultTransitionConfig(transitionIndex)
                    : null;
            string channelName = ResolveDefaultTransitionChannelName(source?.channelName, preStateKey, nextStateKey);
            ValidateDefaultTransitionPairChange(-1, channelName, preStateKey, nextStateKey);
            XAnimationDefaultTransitionConfig transition = new()
            {
                channelName = channelName,
                preStateKey = preStateKey,
                nextStateKey = nextStateKey,
                fadeIn = source?.fadeIn ?? 0.15f,
                fadeOut = source?.fadeOut ?? 0.15f,
                enterTime = source?.enterTime ?? 0f,
                priority = source?.priority ?? 0,
                interruptible = source?.interruptible ?? true,
            };
            m_CompiledAsset.Asset.defaultTransitions = AppendItem(
                m_CompiledAsset.Asset.defaultTransitions ?? Array.Empty<XAnimationDefaultTransitionConfig>(),
                transition);
            RebuildDriverAndSave(save);
            return 0;
        }

        public void DeleteDefaultTransitionPair(int transitionIndex, int pairIndex)
        {
            DeleteDefaultTransition(transitionIndex);
        }

        public void SetDefaultTransitionPair(
            int transitionIndex,
            int pairIndex,
            string preStateKey,
            string nextStateKey,
            bool save = true)
        {
            EnsureBaseAssetEditable();
            preStateKey = preStateKey?.Trim();
            nextStateKey = nextStateKey?.Trim();
            XAnimationDefaultTransitionConfig transition = GetDefaultTransitionConfig(transitionIndex);
            string channelName = ResolveDefaultTransitionChannelName(transition.channelName, preStateKey, nextStateKey);
            ValidateDefaultTransitionPairChange(transitionIndex, channelName, preStateKey, nextStateKey);
            transition.channelName = channelName;
            transition.preStateKey = preStateKey;
            transition.nextStateKey = nextStateKey;
            RebuildDriverAndSave(save);
        }

        public string GetOriginalClipPath(string clipKey)
        {
            if (string.IsNullOrWhiteSpace(clipKey))
            {
                return string.Empty;
            }

            return m_OriginalClipPathByKey.TryGetValue(clipKey, out string clipPath) ? clipPath : string.Empty;
        }

        public void Dispose()
        {
            DisposePreview();
        }

        private void DisposePreview()
        {
            m_EditorActor.Dispose();

            DestroyGrid();
            DestroyLight(ref m_KeyLight);
            DestroyLight(ref m_FillLight);
            DestroyLight(ref m_RimLight);

            if (m_PreviewUtility != null)
            {
                m_PreviewUtility.Cleanup();
                m_PreviewUtility = null;
            }

            if (m_Instance != null)
            {
                UnityEngine.Object.DestroyImmediate(m_Instance);
                m_Instance = null;
            }

            if (m_RenderTexture != null)
            {
                m_RenderTexture.Release();
                UnityEngine.Object.DestroyImmediate(m_RenderTexture);
                m_RenderTexture = null;
            }

            m_Animator = null;
            m_CompiledAsset = null;
            m_AssetPath = null;
            m_IsOverrideAsset = false;
            m_OverrideAsset = null;
            m_CueLogs.Clear();
            m_OriginalClipPathByKey.Clear();
            m_RenderTextureSize = Vector2Int.zero;
        }

        private void CacheOriginalClipPaths(string assetPath)
        {
            m_OriginalClipPathByKey.Clear();
            m_IsOverrideAsset = false;
            m_OverrideAsset = null;

            TextAsset textAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
            if (textAsset == null)
            {
                return;
            }

            XAnimationOverrideAsset overrideAsset = textAsset.ToXAnimationAsset<XAnimationOverrideAsset>();
            if (overrideAsset != null && !string.IsNullOrWhiteSpace(overrideAsset.baseAssetPath))
            {
                m_IsOverrideAsset = true;
                m_OverrideAsset = overrideAsset;
                TextAsset baseTextAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(overrideAsset.baseAssetPath);
                if (baseTextAsset == null)
                {
                    return;
                }

                CacheOriginalClipPaths(baseTextAsset.ToXAnimationAsset<XAnimationAsset>());
                return;
            }

            CacheOriginalClipPaths(textAsset.ToXAnimationAsset<XAnimationAsset>());
        }

        private void CacheOriginalClipPaths(XAnimationAsset asset)
        {
            if (asset?.clips == null)
            {
                return;
            }

            for (int i = 0; i < asset.clips.Length; i++)
            {
                XAnimationClipConfig clip = asset.clips[i];
                if (clip == null || string.IsNullOrWhiteSpace(clip.key))
                {
                    continue;
                }

                m_OriginalClipPathByKey[clip.key] = clip.clipPath;
            }
        }

        private void EnsureLoaded()
        {
            if (!IsLoaded)
            {
                throw new XAnimationException("XAnimation preview session is not loaded.");
            }
        }

        private void ConfigurePreviewCamera()
        {
            Camera camera = m_PreviewUtility.camera;
            camera.fieldOfView = 30f;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = PreviewFarClipPlane;
            camera.allowMSAA = false;
            camera.allowHDR = false;

            // Use the editor default skybox, matching prefab preview appearance
            Material skybox = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
            if (skybox != null)
            {
                camera.clearFlags = CameraClearFlags.Skybox;
                Skybox skyboxComponent = camera.gameObject.GetComponent<Skybox>();
                if (skyboxComponent == null)
                {
                    skyboxComponent = camera.gameObject.AddComponent<Skybox>();
                }
                skyboxComponent.material = skybox;
            }
            else
            {
                camera.clearFlags = CameraClearFlags.Color;
                camera.backgroundColor = new Color(0.22f, 0.22f, 0.24f, 1f);
            }
        }

        private void ConfigurePreviewLights()
        {
            // Disable built-in PreviewRenderUtility lights (not used by SRP)
            Light[] builtinLights = m_PreviewUtility.lights;
            if (builtinLights != null)
            {
                for (int i = 0; i < builtinLights.Length; i++)
                {
                    if (builtinLights[i] != null)
                    {
                        builtinLights[i].intensity = 0f;
                        builtinLights[i].enabled = false;
                    }
                }
            }

            // Create real directional lights as GameObjects so URP/SRP renders them
            m_KeyLight = CreateDirectionalLight("__PreviewKeyLight__",
                Quaternion.Euler(50f, 120f, 0f), new Color(1f, 0.97f, 0.92f), 1.5f);
            m_PreviewUtility.AddSingleGO(m_KeyLight);

            m_FillLight = CreateDirectionalLight("__PreviewFillLight__",
                Quaternion.Euler(340f, 300f, 0f), new Color(0.82f, 0.87f, 1f), 0.8f);
            m_PreviewUtility.AddSingleGO(m_FillLight);

            m_RimLight = CreateDirectionalLight("__PreviewRimLight__",
                Quaternion.Euler(10f, 220f, 0f), new Color(0.9f, 0.9f, 0.95f), 0.5f);
            m_PreviewUtility.AddSingleGO(m_RimLight);

            // Set ambient for the preview scene
            m_PreviewUtility.ambientColor = new Color(0.45f, 0.45f, 0.50f, 1f);
        }

        private static GameObject CreateDirectionalLight(string name, Quaternion rotation, Color color, float intensity)
        {
            GameObject go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            go.transform.rotation = rotation;
            Light light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = color;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
            return go;
        }

        private void CacheInitialTransform()
        {
            m_InitialPosition = m_Instance.transform.position;
            m_InitialRotation = m_Instance.transform.rotation;
        }

        private void CacheInitialBounds()
        {
            Renderer[] renderers = m_Instance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                m_InitialBounds = new Bounds(m_Instance.transform.position, Vector3.one);
            }
            else
            {
                m_InitialBounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                {
                    m_InitialBounds.Encapsulate(renderers[i].bounds);
                }
            }

            m_CameraPivot = m_InitialBounds.center;
            float extentsMagnitude = Mathf.Max(m_InitialBounds.extents.magnitude, 0.5f);
            m_CameraDistance = extentsMagnitude * 2.8f;
        }

        private void EnsureRenderTexture(int width, int height)
        {
            if (m_RenderTexture != null && m_RenderTextureSize.x == width && m_RenderTextureSize.y == height)
            {
                return;
            }

            if (m_RenderTexture != null)
            {
                m_RenderTexture.Release();
                UnityEngine.Object.DestroyImmediate(m_RenderTexture);
            }

            RenderTextureDescriptor descriptor = new(width, height, RenderTextureFormat.ARGB32, 24)
            {
                msaaSamples = 1,
                sRGB = QualitySettings.activeColorSpace == ColorSpace.Linear,
                useMipMap = false,
                autoGenerateMips = false,
            };
            m_RenderTexture = new RenderTexture(descriptor)
            {
                hideFlags = HideFlags.HideAndDontSave,
                antiAliasing = 1,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            m_RenderTexture.Create();
            m_RenderTextureSize = new Vector2Int(width, height);
        }

        private void UpdateCameraTransform()
        {
            if (!m_CameraInitialized)
            {
                RecalculateCameraPosition();
                m_CameraInitialized = true;
            }

            Camera camera = m_PreviewUtility.camera;
            Quaternion rotation = Quaternion.Euler(m_CameraPitch, m_CameraYaw, 0f);
            camera.transform.position = m_CameraPosition;
            camera.transform.rotation = rotation;
            UpdateGridMaterialForCamera();
        }

        private void ApplyHideFlags(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                transforms[i].gameObject.hideFlags = HideFlags.HideAndDontSave;
            }
        }

        private void SanitizePreviewInstance()
        {
            if (m_Instance == null)
            {
                return;
            }

            AudioSource[] audioSources = m_Instance.GetComponentsInChildren<AudioSource>(true);
            for (int i = 0; i < audioSources.Length; i++)
            {
                AudioSource audioSource = audioSources[i];
                audioSource.playOnAwake = false;
                audioSource.Stop();
                audioSource.enabled = false;
            }

            Behaviour[] behaviours = m_Instance.GetComponentsInChildren<Behaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                Behaviour behaviour = behaviours[i];
                if (behaviour == null || behaviour == m_Animator)
                {
                    continue;
                }

                behaviour.enabled = false;
            }

            Rigidbody[] rigidbodies = m_Instance.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < rigidbodies.Length; i++)
            {
                Rigidbody rigidbody = rigidbodies[i];
                rigidbody.isKinematic = true;
                rigidbody.detectCollisions = false;
            }

            Collider[] colliders = m_Instance.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = false;
            }

            ParticleSystem[] particleSystems = m_Instance.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < particleSystems.Length; i++)
            {
                ParticleSystem particleSystem = particleSystems[i];
                particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                particleSystem.gameObject.SetActive(false);
            }
        }

        private void ConfigurePreviewSkinnedMeshRenderers()
        {
            if (m_Instance == null)
            {
                return;
            }

            SkinnedMeshRenderer[] renderers = m_Instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                SkinnedMeshRenderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                renderer.updateWhenOffscreen = true;
                renderer.forceMatrixRecalculationPerRender = true;
            }
        }

        private void PrepareGrid()
        {
            DestroyGrid();

            float modelExtent = Mathf.Max(m_InitialBounds.extents.magnitude, 0.5f);
            float gridHalf = Mathf.Max(MinGridHalfSize, Mathf.Ceil(modelExtent * 12f));
            int cellCount = Mathf.RoundToInt(gridHalf / FarGridSpacing);
            gridHalf = Mathf.Max(FarGridSpacing, cellCount * FarGridSpacing);
            float gridSize = gridHalf * 2f;
            m_GridSpacing = CloseGridSpacing;

            // Create material from URP grid shader
            Shader shader = Shader.Find("Hidden/XAnimation/AnimationPreviewGrid");
            if (shader == null)
            {
                Debug.LogWarning("XAnimation preview grid shader not found (Hidden/XAnimation/AnimationPreviewGrid).");
                return;
            }

            m_GridMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            m_GridMaterial.SetColor("_BGColor", new Color(0.075f, 0.082f, 0.09f, 0.58f));
            m_GridMaterial.SetColor("_GridColor", new Color(0.58f, 0.64f, 0.68f, 0.22f));
            m_GridMaterial.SetColor("_MajorGridColor", new Color(0.74f, 0.80f, 0.86f, 0.42f));
            m_GridMaterial.SetColor("_CenterLineColor", new Color(0.42f, 0.66f, 0.95f, 0.60f));
            m_GridMaterial.SetFloat("_GridWidth", 0.015f);
            m_GridMaterial.SetFloat("_MajorGridWidth", 0.035f);
            m_GridMaterial.SetFloat("_CenterLineWidth", 0.05f);
            m_GridMaterial.SetFloat("_GridSpacing", m_GridSpacing);
            m_GridMaterial.SetFloat("_MajorGridInterval", 5f);
            m_GridMaterial.SetFloat("_GridSize", gridSize);

            // Create a Plane GameObject as the grid surface
            // Unity built-in Plane is 10x10 units, so scale to match gridSize
            m_GridPlane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            m_GridPlane.name = "__PreviewGridPlane__";
            m_GridPlane.hideFlags = HideFlags.HideAndDontSave;

            // Remove collider (not needed in preview)
            Collider collider = m_GridPlane.GetComponent<Collider>();
            if (collider != null)
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }

            MeshRenderer renderer = m_GridPlane.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = m_GridMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            float planeScale = gridSize / 10f; // built-in Plane is 10 units wide
            m_GridPlane.transform.localScale = new Vector3(planeScale, 1f, planeScale);
            UpdateGridTransform();

            m_GridPlane.SetActive(m_GridVisible);
            m_PreviewUtility.AddSingleGO(m_GridPlane);
        }

        private void UpdateGridMaterialForCamera()
        {
            if (m_GridMaterial == null)
            {
                return;
            }

            float closeGridCellPixels = GetCloseGridCellPixelSize();
            float targetSpacing = m_GridSpacing;
            if (m_GridSpacing < FarGridSpacing && closeGridCellPixels <= SwitchToFarGridCellPixels)
            {
                targetSpacing = FarGridSpacing;
            }
            else if (m_GridSpacing > CloseGridSpacing && closeGridCellPixels >= SwitchToCloseGridCellPixels)
            {
                targetSpacing = CloseGridSpacing;
            }

            if (Mathf.Approximately(targetSpacing, m_GridSpacing))
            {
                return;
            }

            m_GridSpacing = targetSpacing;
            float widthScale = Mathf.Sqrt(m_GridSpacing);
            m_GridMaterial.SetFloat("_GridSpacing", m_GridSpacing);
            m_GridMaterial.SetFloat("_GridWidth", 0.015f * widthScale);
            m_GridMaterial.SetFloat("_MajorGridWidth", 0.035f * widthScale);
            m_GridMaterial.SetFloat("_CenterLineWidth", 0.05f * widthScale);
        }

        private float GetCloseGridCellPixelSize()
        {
            float distance = GetGridViewDistance();
            int pixelHeight = Mathf.Max(m_RenderTextureSize.y, 1);
            float fieldOfView = m_PreviewUtility?.camera != null ? m_PreviewUtility.camera.fieldOfView : 30f;
            float viewHeightMeters = 2f * distance * Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad);
            return CloseGridSpacing * pixelHeight / Mathf.Max(viewHeightMeters, 0.0001f);
        }

        private float GetGridViewDistance()
        {
            Quaternion rotation = Quaternion.Euler(m_CameraPitch, m_CameraYaw, 0f);
            Vector3 forward = rotation * Vector3.forward;
            float gridY = m_GridPlane != null ? m_GridPlane.transform.position.y : 0f;
            if (Mathf.Abs(forward.y) > 0.0001f)
            {
                float hitDistance = (gridY - m_CameraPosition.y) / forward.y;
                if (hitDistance > 0f)
                {
                    return Mathf.Max(hitDistance, 0.05f);
                }
            }

            float heightFromGrid = Mathf.Abs(m_CameraPosition.y - gridY);
            float pitchRadians = Mathf.Max(Mathf.Abs(m_CameraPitch) * Mathf.Deg2Rad, 5f * Mathf.Deg2Rad);
            return Mathf.Max(heightFromGrid / Mathf.Sin(pitchRadians), 0.05f);
        }

        private void DestroyGrid()
        {
            if (m_GridPlane != null)
            {
                UnityEngine.Object.DestroyImmediate(m_GridPlane);
                m_GridPlane = null;
            }

            if (m_GridMaterial != null)
            {
                UnityEngine.Object.DestroyImmediate(m_GridMaterial);
                m_GridMaterial = null;
            }
        }

        private void UpdateGridTransform()
        {
            if (m_GridPlane == null)
            {
                return;
            }

            m_GridPlane.transform.position = Vector3.zero;
        }

        private static void DestroyLight(ref GameObject lightGo)
        {
            if (lightGo != null)
            {
                UnityEngine.Object.DestroyImmediate(lightGo);
                lightGo = null;
            }
        }

        private void OnCueTriggered(XAnimationCueEvent cueEvent)
        {
            AppendLog($"Cue [{cueEvent.channelName}] {cueEvent.clipKey} -> {cueEvent.eventKey} @ {cueEvent.normalizedTime:0.00} weight={cueEvent.weight:0.###}");
        }

        private void OnStateEnter(XAnimationStateEvent stateEvent)
        {
            AppendLog($"Enter [{stateEvent.channelName}] {stateEvent.stateKey}{FormatTemporaryStateSuffix(stateEvent.isTemporaryState)}");
        }

        private void OnStateExit(XAnimationStateEvent stateEvent)
        {
            string reason = stateEvent.exitReason?.ToString() ?? "Unknown";
            AppendLog($"Exit [{stateEvent.channelName}] {stateEvent.stateKey}{FormatTemporaryStateSuffix(stateEvent.isTemporaryState)} ({reason})");
        }

        private void AppendLog(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            m_CueLogs.Add(new PreviewLogEntry(m_NextLogId++, message));
            m_LogVersion++;
        }

        private static string FormatTemporaryStateSuffix(bool isTemporaryState)
        {
            return isTemporaryState ? " (temp)" : string.Empty;
        }

        private static XAnimationAutoTransitionConfig FindAutoTransition(XAnimationAsset asset, string channelName, string preStateKey)
        {
            XAnimationAutoTransitionConfig[] transitions = asset?.autoTransitions ?? Array.Empty<XAnimationAutoTransitionConfig>();
            for (int i = 0; i < transitions.Length; i++)
            {
                XAnimationAutoTransitionConfig transition = transitions[i];
                if (IsAutoTransitionForState(asset, transition, channelName, preStateKey))
                {
                    return transition;
                }
            }

            return null;
        }

        private static bool IsAutoTransitionForState(
            XAnimationAsset asset,
            XAnimationAutoTransitionConfig transition,
            string channelName,
            string preStateKey)
        {
            if (transition == null ||
                string.IsNullOrWhiteSpace(channelName) ||
                string.IsNullOrWhiteSpace(preStateKey) ||
                !string.Equals(transition.preStateKey, preStateKey, StringComparison.Ordinal))
            {
                return false;
            }

            string transitionChannelName = ResolveAutoTransitionChannelName(asset, transition);
            return string.Equals(transitionChannelName, channelName, StringComparison.Ordinal);
        }

        private static string ResolveAutoTransitionChannelName(XAnimationAsset asset, XAnimationAutoTransitionConfig transition)
        {
            string channelName = transition?.channelName?.Trim();
            if (!string.IsNullOrWhiteSpace(channelName))
            {
                return channelName;
            }

            XAnimationStateConfig[] states = asset?.states ?? Array.Empty<XAnimationStateConfig>();
            string resolvedChannelName = string.Empty;
            for (int i = 0; i < states.Length; i++)
            {
                XAnimationStateConfig state = states[i];
                if (state == null ||
                    !string.Equals(state.key, transition?.preStateKey, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(resolvedChannelName))
                {
                    return string.Empty;
                }

                resolvedChannelName = state.channelName ?? string.Empty;
            }

            return resolvedChannelName;
        }

        private XAnimationDefaultTransitionConfig GetDefaultTransitionConfig(int transitionIndex)
        {
            EnsureLoaded();
            XAnimationDefaultTransitionConfig[] transitions = m_CompiledAsset.Asset.defaultTransitions ?? Array.Empty<XAnimationDefaultTransitionConfig>();
            if (transitionIndex < 0 || transitionIndex >= transitions.Length || transitions[transitionIndex] == null)
            {
                throw new XAnimationException($"XAnimation default transition index '{transitionIndex}' does not exist.");
            }

            return transitions[transitionIndex];
        }

        private XAnimationDefaultTransitionConfig CreateDefaultTransitionConfig(int transitionIndex)
        {
            IReadOnlyList<XAnimationCompiledState> states = m_CompiledAsset.States;
            if (states.Count < 2)
            {
                throw new XAnimationException("XAnimation default transition requires at least two states.");
            }

            HashSet<string> occupiedPairs = CollectDefaultTransitionPairKeys();
            XAnimationDefaultTransitionConfig[] transitions = m_CompiledAsset.Asset.defaultTransitions ?? Array.Empty<XAnimationDefaultTransitionConfig>();
            string preferredChannelName = null;
            float fadeIn = 0.15f;
            float fadeOut = 0.15f;
            float enterTime = 0f;
            int priority = 0;
            bool interruptible = true;
            if (transitionIndex >= 0 && transitionIndex < transitions.Length)
            {
                XAnimationDefaultTransitionConfig source = transitions[transitionIndex];
                preferredChannelName = source?.channelName;
                fadeIn = source?.fadeIn ?? fadeIn;
                fadeOut = source?.fadeOut ?? fadeOut;
                enterTime = source?.enterTime ?? enterTime;
                priority = source?.priority ?? priority;
                interruptible = source?.interruptible ?? interruptible;
            }

            for (int preIndex = 0; preIndex < states.Count; preIndex++)
            {
                for (int nextIndex = 0; nextIndex < states.Count; nextIndex++)
                {
                    if (preIndex == nextIndex)
                    {
                        continue;
                    }

                    string preStateKey = states[preIndex].Key;
                    string nextStateKey = states[nextIndex].Key;
                    string channelName = ResolveDefaultTransitionChannelName(preferredChannelName, preStateKey, nextStateKey);
                    string pairKey = XAnimationCompiledAsset.BuildTransitionPairKey(channelName, preStateKey, nextStateKey);
                    if (!occupiedPairs.Contains(pairKey))
                    {
                        return new XAnimationDefaultTransitionConfig
                        {
                            channelName = channelName,
                            preStateKey = preStateKey,
                            nextStateKey = nextStateKey,
                            fadeIn = fadeIn,
                            fadeOut = fadeOut,
                            enterTime = enterTime,
                            priority = priority,
                            interruptible = interruptible,
                        };
                    }
                }
            }

            throw new XAnimationException("所有可用的 Default Transition state pair 都已经配置。");
        }

        private void ValidateDefaultTransitionPairChange(int transitionIndex, string channelName, string preStateKey, string nextStateKey)
        {
            if (string.IsNullOrWhiteSpace(channelName))
            {
                throw new XAnimationException("XAnimation default transition channelName cannot be empty.");
            }

            if (string.IsNullOrWhiteSpace(preStateKey))
            {
                throw new XAnimationException("XAnimation default transition preStateKey cannot be empty.");
            }

            if (string.IsNullOrWhiteSpace(nextStateKey))
            {
                throw new XAnimationException("XAnimation default transition nextStateKey cannot be empty.");
            }

            if (string.Equals(preStateKey, nextStateKey, StringComparison.Ordinal))
            {
                throw new XAnimationException($"XAnimation default transition cannot transition state '{preStateKey}' to itself.");
            }

            m_CompiledAsset.GetChannel(channelName);
            m_CompiledAsset.GetState(channelName, preStateKey);
            m_CompiledAsset.GetState(channelName, nextStateKey);
            XAnimationDefaultTransitionConfig[] transitions = m_CompiledAsset.Asset.defaultTransitions ?? Array.Empty<XAnimationDefaultTransitionConfig>();
            for (int i = 0; i < transitions.Length; i++)
            {
                if (i == transitionIndex)
                {
                    continue;
                }

                XAnimationDefaultTransitionConfig transition = transitions[i];
                if (transition != null &&
                    string.Equals(transition.channelName, channelName, StringComparison.Ordinal) &&
                    string.Equals(transition.preStateKey, preStateKey, StringComparison.Ordinal) &&
                    string.Equals(transition.nextStateKey, nextStateKey, StringComparison.Ordinal))
                {
                    throw new XAnimationException($"XAnimation default transition pair '{channelName}: {preStateKey}' -> '{nextStateKey}' is duplicated.");
                }
            }
        }

        private HashSet<string> CollectDefaultTransitionPairKeys()
        {
            HashSet<string> pairKeys = new(StringComparer.Ordinal);
            XAnimationDefaultTransitionConfig[] transitions = m_CompiledAsset.Asset.defaultTransitions ?? Array.Empty<XAnimationDefaultTransitionConfig>();
            for (int i = 0; i < transitions.Length; i++)
            {
                XAnimationDefaultTransitionConfig transition = transitions[i];
                if (transition != null &&
                    !string.IsNullOrWhiteSpace(transition.channelName) &&
                    !string.IsNullOrWhiteSpace(transition.preStateKey) &&
                    !string.IsNullOrWhiteSpace(transition.nextStateKey))
                {
                    pairKeys.Add(XAnimationCompiledAsset.BuildTransitionPairKey(transition.channelName, transition.preStateKey, transition.nextStateKey));
                }
            }

            return pairKeys;
        }

        private string ResolveDefaultTransitionChannelName(string preferredChannelName, string preStateKey, string nextStateKey)
        {
            if (!string.IsNullOrWhiteSpace(preferredChannelName))
            {
                return preferredChannelName.Trim();
            }

            if (!string.IsNullOrWhiteSpace(preStateKey) &&
                m_CompiledAsset.TryGetStateIndex(preStateKey, out int preStateIndex))
            {
                string channelName = m_CompiledAsset.States[preStateIndex].Config.channelName;
                if (!string.IsNullOrWhiteSpace(channelName))
                {
                    return channelName;
                }
            }

            if (!string.IsNullOrWhiteSpace(nextStateKey) &&
                m_CompiledAsset.TryGetStateIndex(nextStateKey, out int nextStateIndex))
            {
                string channelName = m_CompiledAsset.States[nextStateIndex].Config.channelName;
                if (!string.IsNullOrWhiteSpace(channelName))
                {
                    return channelName;
                }
            }

            IReadOnlyList<XAnimationCompiledChannel> channels = m_CompiledAsset.Channels;
            if (channels.Count == 0)
            {
                throw new XAnimationException("XAnimation default transition requires at least one channel.");
            }

            return channels[0].Name;
        }

        private XAnimationCompiledState ResolveAutoTransitionPreState(string preferredChannelName, string preferredPreStateKey)
        {
            preferredChannelName = preferredChannelName?.Trim();
            preferredPreStateKey = preferredPreStateKey?.Trim();
            if (!string.IsNullOrWhiteSpace(preferredPreStateKey) &&
                !string.IsNullOrWhiteSpace(preferredChannelName) &&
                m_CompiledAsset.TryGetStateIndex(preferredChannelName, preferredPreStateKey, out int preferredStateIndex) &&
                !m_CompiledAsset.States[preferredStateIndex].Config.loop &&
                FindAutoTransition(m_CompiledAsset.Asset, preferredChannelName, preferredPreStateKey) == null)
            {
                return m_CompiledAsset.States[preferredStateIndex];
            }

            IReadOnlyList<XAnimationCompiledState> states = m_CompiledAsset.States;
            for (int i = 0; i < states.Count; i++)
            {
                XAnimationCompiledState state = states[i];
                string channelName = state.Config.channelName;
                string stateKey = state.Key;
                if ((string.IsNullOrWhiteSpace(preferredChannelName) ||
                        string.Equals(channelName, preferredChannelName, StringComparison.Ordinal)) &&
                    !state.Config.loop &&
                    FindAutoTransition(m_CompiledAsset.Asset, channelName, stateKey) == null)
                {
                    return state;
                }
            }

            throw new XAnimationException("所有可用的非循环 state 都已经配置了 Auto Transition。");
        }

        private static XAnimationAutoTransitionConfig GetOrCreateAutoTransition(XAnimationAsset asset, string channelName, string preStateKey)
        {
            XAnimationAutoTransitionConfig transition = FindAutoTransition(asset, channelName, preStateKey);
            if (transition != null)
            {
                transition.channelName = channelName;
                return transition;
            }

            transition = new XAnimationAutoTransitionConfig
            {
                channelName = channelName,
                preStateKey = preStateKey,
                nextStateKey = string.Empty,
                exitTime = 1f,
                transitionDuration = 0f,
                enterTime = 0f,
            };

            asset.autoTransitions = AppendItem(asset.autoTransitions, transition);
            return transition;
        }

        private static void RenameAutoTransitionReferences(XAnimationAsset asset, string channelName, string oldKey, string newKey)
        {
            XAnimationAutoTransitionConfig[] transitions = asset?.autoTransitions ?? Array.Empty<XAnimationAutoTransitionConfig>();
            for (int i = 0; i < transitions.Length; i++)
            {
                XAnimationAutoTransitionConfig transition = transitions[i];
                if (transition == null)
                {
                    continue;
                }

                bool preStateInEditedChannel = string.Equals(ResolveAutoTransitionChannelName(asset, transition), channelName, StringComparison.Ordinal);
                if (preStateInEditedChannel)
                {
                    transition.channelName = channelName;
                    ReplaceIfEqual(ref transition.preStateKey, oldKey, newKey);
                    ReplaceIfEqual(ref transition.nextStateKey, oldKey, newKey);
                }
            }
        }

        private static void RenameDefaultTransitionReferences(XAnimationAsset asset, string channelName, string oldKey, string newKey)
        {
            XAnimationDefaultTransitionConfig[] transitions = asset?.defaultTransitions ?? Array.Empty<XAnimationDefaultTransitionConfig>();
            for (int i = 0; i < transitions.Length; i++)
            {
                XAnimationDefaultTransitionConfig transition = transitions[i];
                if (transition == null ||
                    !string.Equals(transition.channelName, channelName, StringComparison.Ordinal))
                {
                    continue;
                }

                ReplaceIfEqual(ref transition.preStateKey, oldKey, newKey);
                ReplaceIfEqual(ref transition.nextStateKey, oldKey, newKey);
            }
        }

        private static void RenameStateGateReferences(XAnimationAsset asset, string channelName, string oldKey, string newKey)
        {
            XAnimationStateConfig[] states = asset?.states ?? Array.Empty<XAnimationStateConfig>();
            for (int i = 0; i < states.Length; i++)
            {
                XAnimationStateConfig state = states[i];
                if (state == null ||
                    !string.Equals(state.channelName, channelName, StringComparison.Ordinal))
                {
                    continue;
                }

                RenameStateKeyArray(state.allowedNextStateKeys, oldKey, newKey);
                RenameStateKeyArray(state.allowedPreviousStateKeys, oldKey, newKey);
            }
        }

        private static void RenameStateKeyArray(string[] values, string oldKey, string newKey)
        {
            if (values == null)
            {
                return;
            }

            for (int i = 0; i < values.Length; i++)
            {
                ReplaceIfEqual(ref values[i], oldKey, newKey);
            }
        }

        private static void ClearAutoTransitionReferences(XAnimationAsset asset, string channelName, string deletedStateKey)
        {
            XAnimationAutoTransitionConfig[] transitions = asset?.autoTransitions ?? Array.Empty<XAnimationAutoTransitionConfig>();
            if (transitions.Length == 0)
            {
                return;
            }

            List<XAnimationAutoTransitionConfig> remaining = new(transitions.Length);
            for (int i = 0; i < transitions.Length; i++)
            {
                XAnimationAutoTransitionConfig transition = transitions[i];
                if (transition == null)
                {
                    continue;
                }

                bool preStateInEditedChannel = string.Equals(ResolveAutoTransitionChannelName(asset, transition), channelName, StringComparison.Ordinal);
                if (preStateInEditedChannel &&
                    string.Equals(transition.preStateKey, deletedStateKey, StringComparison.Ordinal))
                {
                    continue;
                }

                if (preStateInEditedChannel &&
                    string.Equals(transition.nextStateKey, deletedStateKey, StringComparison.Ordinal))
                {
                    transition.channelName = channelName;
                    transition.nextStateKey = string.Empty;
                    transition.exitTime = 1f;
                    transition.transitionDuration = 0f;
                    transition.enterTime = 0f;
                }

                remaining.Add(transition);
            }

            asset.autoTransitions = remaining.ToArray();
        }

        private static void ClearDefaultTransitionReferences(XAnimationAsset asset, string channelName, string deletedStateKey)
        {
            XAnimationDefaultTransitionConfig[] transitions = asset?.defaultTransitions ?? Array.Empty<XAnimationDefaultTransitionConfig>();
            if (transitions.Length == 0)
            {
                return;
            }

            List<XAnimationDefaultTransitionConfig> remainingTransitions = new(transitions.Length);
            for (int i = 0; i < transitions.Length; i++)
            {
                XAnimationDefaultTransitionConfig transition = transitions[i];
                if (transition == null ||
                    (string.Equals(transition.channelName, channelName, StringComparison.Ordinal) &&
                        (string.Equals(transition.preStateKey, deletedStateKey, StringComparison.Ordinal) ||
                         string.Equals(transition.nextStateKey, deletedStateKey, StringComparison.Ordinal))))
                {
                    continue;
                }

                remainingTransitions.Add(transition);
            }

            asset.defaultTransitions = remainingTransitions.ToArray();
        }

        private static void ClearStateGateReferences(XAnimationAsset asset, string channelName, string deletedStateKey)
        {
            XAnimationStateConfig[] states = asset?.states ?? Array.Empty<XAnimationStateConfig>();
            for (int i = 0; i < states.Length; i++)
            {
                XAnimationStateConfig state = states[i];
                if (state == null ||
                    !string.Equals(state.channelName, channelName, StringComparison.Ordinal))
                {
                    continue;
                }

                state.allowedNextStateKeys = RemoveStateKeyFromArray(state.allowedNextStateKeys, deletedStateKey);
                state.allowedPreviousStateKeys = RemoveStateKeyFromArray(state.allowedPreviousStateKeys, deletedStateKey);
            }
        }

        private static string[] RemoveStateKeyFromArray(string[] values, string deletedStateKey)
        {
            if (values == null || values.Length == 0)
            {
                return Array.Empty<string>();
            }

            List<string> remaining = new(values.Length);
            for (int i = 0; i < values.Length; i++)
            {
                string value = values[i];
                if (!string.IsNullOrWhiteSpace(value) &&
                    !string.Equals(value, deletedStateKey, StringComparison.Ordinal))
                {
                    remaining.Add(value);
                }
            }

            return remaining.Count == 0 ? Array.Empty<string>() : remaining.ToArray();
        }

        private static bool IsStateKeyInChannel(XAnimationStateConfig[] states, string channelName, string stateKey)
        {
            if (states == null || string.IsNullOrWhiteSpace(channelName) || string.IsNullOrWhiteSpace(stateKey))
            {
                return false;
            }

            for (int i = 0; i < states.Length; i++)
            {
                XAnimationStateConfig state = states[i];
                if (state != null &&
                    string.Equals(state.channelName, channelName, StringComparison.Ordinal) &&
                    string.Equals(state.key, stateKey, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

    }
}
#endif
