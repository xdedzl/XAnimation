#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using UEvent = UnityEngine.Event;
using XAnimationEngine;
using static XAnimationEditor.XAnimationEditorParameterUtility;
using static XAnimationEditor.XAnimationEditorUi;

namespace XAnimationEditor
{
    public sealed partial class XAnimationPreviewWindow
    {
        private void LoadPreview()
        {
            try
            {
                if (!ConfirmUnsavedChangesBeforeReset())
                {
                    SetStatus("已取消重载，当前修改尚未保存。");
                    return;
                }

                GameObject prefab = m_PrefabField.value as GameObject;
                TextAsset assetText = m_AssetField.value as TextAsset;
                if (prefab == null)
                {
                    throw new XAnimationException("请选择一个 prefab 资源。");
                }

                if (assetText == null)
                {
                    throw new XAnimationException("请选择一个 .xanimation 或 .xanimationoverride 资源。");
                }

                string assetPath = AssetDatabase.GetAssetPath(assetText);
                if (string.IsNullOrWhiteSpace(assetPath))
                {
                    throw new XAnimationException("无法获取 XAnimationAsset 的资源路径。");
                }

                DisposeSession();
                m_Session = new XAnimationEditorPreviewSession();
                m_Session.AssetChanged += MarkAssetDirty;
                m_Session.Load(prefab, assetPath);
                m_SelectedPrefab = prefab;
                m_SelectedAsset = assetText;
                ClearAssetDirty();
                SaveLastPreviewAssetPaths(assetText, prefab);
                m_ShouldAutoReloadPreview = true;
                m_IsPaused = false;
                double now = EditorApplication.timeSinceStartup;
                bool isPreviewVisible = IsPreviewTabVisible();
                m_UpdateCoordinator.Reset(now, isPreviewVisible);
                m_Session.SetPaused(!isPreviewVisible);
                m_Session.SetGlobalSpeed(1f);
                m_PreviewRootMotionEnabled = m_Session.GetRootMotionEnabled();
                m_Session.SetRootMotionEnabled(m_PreviewRootMotionEnabled);
                MarkEventUiDirty();
                SetPauseButtonState(false, false);
                SetStepForwardButtonEnabled(true);
                m_PlaybackHudView?.Refresh();
                m_GridToggle.SetValueWithoutNotify(true);
                RebuildCorePreviewLists();
                RebuildDefaultTransitionsEditor();
                RebuildChannelPresentation();
                RefreshAssetsToolbarButtons();
                RefreshPlaybackViews();
                RefreshCueLogView(force: true);
                SetStatus("预览已加载。");
                ApplyPendingPlaybackRequest();
                ApplyPendingFocusState();
                RenderPreview();
            }
            catch (Exception ex)
            {
                m_ShouldAutoReloadPreview = false;
                DisposeSession();
                ClearDebugViews();
                SetStatus(ex.Message, true);
            }
        }

        private void ApplyPendingPlaybackRequest()
        {
            if (m_Session == null || !m_Session.IsLoaded || !m_PendingPlaybackRequest.HasValue)
            {
                return;
            }

            PendingPlaybackRequest request = m_PendingPlaybackRequest.Value;
            m_PendingPlaybackRequest = null;
            bool hasExplicitTransition = request.Transition != null;

            try
            {
                SetPlaybackSpeed(request.Speed, savePrefs: true, updateSession: false);

                if (request.IsStatePlayback)
                {
                    string channelName = string.IsNullOrWhiteSpace(request.ChannelName)
                        ? m_Session.CompiledAsset.GetState(request.StateKey).Config.channelName
                        : request.ChannelName;
                    ResumePlaybackChannel(channelName);
                    SetPauseButtonState(true, false);
                    SetStepForwardButtonEnabled(true);
                    m_Session.SetGlobalSpeed(GetPlaybackSpeed());
                    XAnimationTransitionOptions transition = hasExplicitTransition ? CloneTransitionOptions(request.Transition) : null;
                    if (string.IsNullOrWhiteSpace(request.ChannelName))
                    {
                        m_Session.PlayState(request.StateKey, transition);
                    }
                    else
                    {
                        m_Session.PlayState(request.ChannelName, request.StateKey, transition);
                    }

                    RefreshPlaybackViews();
                    FocusStateInInspector(request.ChannelName, request.StateKey);
                    SetStatus($"正在播放 state {request.StateKey}。");
                    return;
                }

                if (request.IsClipPlayback)
                {
                    if (string.IsNullOrWhiteSpace(request.ChannelName))
                    {
                        throw new XAnimationException("预览窗口播放 clip 需要 channelName。");
                    }

                    ResumePlaybackChannel(request.ChannelName);
                    SetPauseButtonState(true, false);
                    SetStepForwardButtonEnabled(true);
                    m_Session.SetGlobalSpeed(GetPlaybackSpeed());
                    m_Session.PlayClip(request.ClipKey, request.ChannelName, hasExplicitTransition ? CloneTransitionOptions(request.Transition) : null);
                    RefreshPlaybackViews();
                    FocusClipInInspector(request.ClipKey);
                    SetStatus($"正在 {request.ChannelName} 调试播放 {request.ClipKey}。");
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                SetStatus(ex.Message, true);
            }
        }

        private void StopAllClips()
        {
            if (m_Session == null || !m_Session.IsLoaded)
            {
                return;
            }

            m_Session.StopAll();
            IReadOnlyList<XAnimationCompiledChannel> channels = m_Session.CompiledAsset.Channels;
            for (int i = 0; i < channels.Count; i++)
            {
                m_Session.ResumeChannel(channels[i].Name);
            }
            m_IsPaused = false;
            m_Session.SetPaused(false);
            SetPauseButtonState(false, false);
            SetStepForwardButtonEnabled(HasAnyPlayingChannel());
            MarkStatePlaybackUiDirty();
            MarkClipPlaybackUiDirty();
            RefreshPlaybackViews();
            SetStatus("已停止全部通道。");
        }

        private void TogglePause()
        {
            if (m_Session == null || !m_Session.IsLoaded)
            {
                return;
            }

            if (!HasAnyPlayingChannel())
            {
                if (!TryPlayFirstStateFromOverlay())
                {
                    SetStatus("当前没有可播放的 state。", true);
                }
                return;
            }

            SetPlaybackPaused(!m_IsPaused);
            SetPauseButtonState(true, m_IsPaused);
            SetStepForwardButtonEnabled(true);
            MarkClipPlaybackUiDirty();
            if (TryGetNonBasePauseTarget(out string channelName))
            {
                SetStatus(m_IsPaused ? $"已暂停 {channelName} Channel。" : $"已继续 {channelName} Channel。");
            }
            else
            {
                SetStatus(m_IsPaused ? "已暂停动画预览。" : "已继续动画预览。");
            }
        }

        private void StepForward()
        {
            if (m_Session == null || !m_Session.IsLoaded)
            {
                return;
            }

            if (!m_IsPaused)
            {
                SetPlaybackPaused(true);
            }

            StepPausedPlayback(1f / 60f);
            SetPauseButtonState(true, true);
            SetStepForwardButtonEnabled(true);
            MarkEventUiDirty();
            RefreshPlaybackViews();
            RefreshCueLogView(force: true);
            RenderPreview();
            Repaint();
            SetStatus("已向后推进一帧。");
        }

        private void SetPlaybackTargetChannel(string channelName)
        {
            m_PlayTargetChannelName = channelName ?? string.Empty;
            m_PlaybackHudView?.Refresh();
            SavePlaybackPrefs();
            RefreshPlaybackPauseState();
        }

        private void SetSelectedChannelWeight(float weight)
        {
            float channelWeight = Mathf.Max(0f, weight);
            m_Session.SetChannelWeight(m_PlayTargetChannelName, channelWeight);
            m_Session.SyncPreviewFrame();
            RefreshPlaybackViews();
            RenderPreview();
            SetStatus($"{m_PlayTargetChannelName} weight = {channelWeight:0.###}。");
        }

        private void PlaySelectedChannel()
        {
            m_Session.SetPaused(false);
            m_Session.ResumeChannel(m_PlayTargetChannelName);
            if (m_Session.GetChannelState(m_PlayTargetChannelName) == null)
            {
                XAnimationCompiledState state = FindFirstSelectedChannelState();
                m_Session.PlayState(m_PlayTargetChannelName, state.Key, BuildPreviewTransitionOptions());
            }

            RefreshPlaybackPauseState();
            RefreshPlaybackViews();
            RenderPreview();
            SetStatus($"正在播放 {m_PlayTargetChannelName} Channel。");
        }

        private XAnimationCompiledState FindFirstSelectedChannelState()
        {
            IReadOnlyList<XAnimationCompiledState> states = m_Session.CompiledAsset.States;
            for (int i = 0; i < states.Count; i++)
            {
                if (string.Equals(states[i].Config.channelName, m_PlayTargetChannelName, StringComparison.Ordinal))
                {
                    return states[i];
                }
            }

            return null;
        }

        private void PauseSelectedChannel()
        {
            m_Session.PauseChannel(m_PlayTargetChannelName);
            RefreshPlaybackPauseState();
            RefreshPlaybackViews();
            RenderPreview();
            SetStatus($"已暂停 {m_PlayTargetChannelName} Channel。");
        }

        private void StopSelectedChannel()
        {
            m_Session.SetPaused(false);
            m_Session.ResumeChannel(m_PlayTargetChannelName);
            m_Session.Stop(m_PlayTargetChannelName);
            RefreshPlaybackPauseState();
            MarkStatePlaybackUiDirty();
            MarkClipPlaybackUiDirty();
            RefreshPlaybackViews();
            RenderPreview();
            SetStatus($"已停止 {m_PlayTargetChannelName} Channel。");
        }

        private void RefreshPlaybackPauseState()
        {
            if (m_Session == null || !m_Session.IsLoaded)
            {
                m_IsPaused = false;
                return;
            }

            m_IsPaused = m_Session.IsPaused;
            if (!m_IsPaused && TryGetNonBasePauseTarget(out string channelName))
            {
                m_IsPaused = m_Session.IsChannelPaused(channelName);
            }
        }

        private bool TryGetNonBasePauseTarget(out string channelName)
        {
            channelName = m_PlayTargetChannelName;
            if (string.IsNullOrWhiteSpace(channelName) || m_Session.GetChannelState(channelName) == null)
            {
                return false;
            }

            XAnimationCompiledChannel channel = m_Session.CompiledAsset.GetChannel(channelName);
            return channel.Config.layerType != XAnimationChannelLayerType.Base;
        }

        private void SetPlaybackPaused(bool paused)
        {
            if (TryGetNonBasePauseTarget(out string channelName))
            {
                m_Session.SetPaused(false);
                m_Session.SetChannelPaused(channelName, paused);
            }
            else
            {
                m_Session.SetPaused(paused);
            }

            m_IsPaused = paused;
        }

        private void ResumePlaybackChannel(string channelName)
        {
            SetPlaybackTargetChannel(channelName);
            m_Session.SetPaused(false);
            m_Session.ResumeChannel(channelName);
            m_IsPaused = false;
        }

        private void RestorePlaybackPauseState()
        {
            if (TryGetNonBasePauseTarget(out string channelName) && m_Session.IsChannelPaused(channelName))
            {
                m_Session.SetPaused(false);
                return;
            }

            m_Session.SetPaused(m_IsPaused);
        }

        private void StepPausedPlayback(float deltaTime)
        {
            if (TryGetNonBasePauseTarget(out string channelName) && m_Session.IsChannelPaused(channelName))
            {
                m_Session.ResumeChannel(channelName);
                m_Session.Step(deltaTime);
                m_Session.PauseChannel(channelName);
                return;
            }

            m_Session.Step(deltaTime);
        }

        private void ResetPreviewTransform()
        {
            if (m_Session == null || !m_Session.IsLoaded)
            {
                return;
            }

            m_Session.ResetTransform();
            SetStatus("预览对象已回到初始位置。");
        }

        private void ResetPreviewCamera()
        {
            if (m_Session == null || !m_Session.IsLoaded)
            {
                return;
            }

            m_Session.ResetCamera();
            RenderPreview();
            SetStatus("预览视角已重置。");
        }

        private void RenderPreview()
        {
            if (m_Session == null || !m_Session.IsLoaded || m_PreviewImage == null)
            {
                if (m_PreviewImage != null)
                {
                    m_PreviewImage.image = null;
                }
                return;
            }

            Rect rect = m_PreviewImage.contentRect;
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            m_Session.Render(rect.size);
            m_PreviewImage.image = m_Session.PreviewTexture;
        }

        private void ScheduleAssetSave()
        {
            if (m_Session == null || !m_Session.IsLoaded || rootVisualElement == null)
            {
                return;
            }

            MarkAssetDirty();
        }

        private bool RestartClipIfPlaying(string clipKey, string channelName)
        {
            if (m_Session == null || !m_Session.IsLoaded)
            {
                return false;
            }

            string playingChannelName = FindPlayingChannelName(clipKey);
            if (string.IsNullOrEmpty(playingChannelName))
            {
                return false;
            }

            string resolvedClipChannel = string.IsNullOrWhiteSpace(channelName) ? playingChannelName : channelName;
            ResumePlaybackChannel(resolvedClipChannel);
            m_Session.PlayClip(clipKey, resolvedClipChannel, BuildPreviewTransitionOptions());
            RefreshPlaybackViews();
            return true;
        }

        private bool RestartStateIfPlaying(string stateKey, string channelName)
        {
            if (m_Session == null || !m_Session.IsLoaded)
            {
                return false;
            }

            string playingChannelName = FindPlayingStateChannelName(stateKey);
            if (string.IsNullOrEmpty(playingChannelName))
            {
                return false;
            }

            ResumePlaybackChannel(playingChannelName);
            m_Session.PlayState(playingChannelName, stateKey, BuildPreviewTransitionOptions());
            RefreshStatePlaybackViews();
            return true;
        }

        private string FindPlayingStateChannelName(string stateKey)
        {
            if (m_Session == null || !m_Session.IsLoaded)
            {
                return null;
            }

            IReadOnlyList<XAnimationCompiledChannel> channels = m_Session.CompiledAsset.Channels;
            for (int i = 0; i < channels.Count; i++)
            {
                string channelName = channels[i].Name;
                XAnimationChannelState state = m_Session.GetChannelState(channelName);
                if (state != null && string.Equals(state.stateKey, stateKey, StringComparison.Ordinal))
                {
                    return channelName;
                }
            }

            return null;
        }

        private string FindPlayingChannelName(string clipKey)
        {
            if (m_Session == null || !m_Session.IsLoaded)
            {
                return null;
            }

            IReadOnlyList<XAnimationCompiledChannel> channels = m_Session.CompiledAsset.Channels;
            for (int i = 0; i < channels.Count; i++)
            {
                string channelName = channels[i].Name;
                XAnimationChannelState state = m_Session.GetChannelState(channelName);
                if (state != null && string.Equals(state.clipKey, clipKey, StringComparison.Ordinal))
                {
                    return channelName;
                }
            }

            return null;
        }

        private static ObjectField CreateClipObjectField(string assetPath, float marginLeft = 0f, string label = null, bool editable = false)
        {
            AnimationClip clip = string.IsNullOrWhiteSpace(assetPath)
                ? null
                : XAnimationEditorAssetResolver.ResolveAnimationClip(assetPath);
            ObjectField field = string.IsNullOrWhiteSpace(label) ? new ObjectField() : new ObjectField(label);
            field.objectType = typeof(AnimationClip);
            field.allowSceneObjects = false;
            field.value = clip;
            field.tooltip = assetPath;
            field.style.flexGrow = 1;
            field.style.minHeight = 20;
            field.style.fontSize = 10;
            field.style.alignSelf = Align.Stretch;
            field.pickingMode = editable ? PickingMode.Position : PickingMode.Ignore;
            field.SetEnabled(editable);
            if (string.IsNullOrWhiteSpace(label))
            {
                field.style.flexBasis = 0;
                field.style.minWidth = 0;
            }
            if (marginLeft > 0f)
            {
                field.style.marginLeft = marginLeft;
            }

            if (!editable)
            {
                field.RegisterValueChangedCallback(evt => field.SetValueWithoutNotify(evt.previousValue));
            }

            return field;
        }

        private void ChangeClipPath(XAnimationCompiledClip clip, ObjectField field, AnimationClip previousClip, AnimationClip newClip)
        {
            if (m_Session == null || !m_Session.IsLoaded)
            {
                field.SetValueWithoutNotify(previousClip);
                return;
            }

            if (newClip == null)
            {
                field.SetValueWithoutNotify(previousClip);
                SetStatus("clip 动画资源不能为空。", true);
                return;
            }

            string clipPath = XAnimationEditorAssetResolver.BuildClipPath(newClip);
            if (string.IsNullOrWhiteSpace(clipPath))
            {
                field.SetValueWithoutNotify(previousClip);
                SetStatus("无法获取所选 AnimationClip 的资源路径。", true);
                return;
            }

            try
            {
                m_Session.SetClipPath(clip.Key, clipPath);
                RestartClipIfPlaying(clip.Key, m_PlayTargetChannelName);
                SetStatus($"{clip.Key} clip = {newClip.name}。");
                RebuildClipPresentation();
            }
            catch (Exception ex)
            {
                field.SetValueWithoutNotify(previousClip);
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private VisualElement CreateClipEditor(XAnimationCompiledClip clip)
        {
            XAnimationClipConfig config = clip.Config;
            VisualElement editor = CreateSubBox();
            editor.style.marginLeft = 4;
            editor.style.marginRight = 4;
            editor.style.marginTop = 1;
            editor.style.marginBottom = 2;

            if (m_Session != null && m_Session.IsOverrideAsset)
            {
                string originalClipPath = m_Session.GetOriginalClipPath(clip.Key);
                ObjectField originalClipField = CreateClipObjectField(originalClipPath, label: "originalClip");
                originalClipField.tooltip = "Base XAnimation 中的原始动画资源。Override 预览中不允许从这里修改。";
                editor.Add(originalClipField);
            }

            editor.Add(CreateClipCueEditor(clip));

            return editor;
        }

        private void ApplyClipButtonStyle(Button btn, bool isPlaying)
        {
            btn.text = isPlaying && !m_IsPaused ? "Ⅱ" : "▶";
            ApplyClipIconButtonStyle(btn, isPlaying && !m_IsPaused ? AccentColor : null);
        }

        private void SetStopAllButtonEnabled(bool enabled)
        {
            m_PlaybackHudView?.Refresh();
        }

        private void SetAddChannelButtonEnabled(bool enabled)
        {
            if (m_AddChannelButton == null)
            {
                return;
            }

            m_AddChannelButton.SetEnabled(enabled);
            m_AddChannelButton.style.opacity = enabled ? 1f : 0.45f;
            m_AddChannelButton.tooltip = m_Session != null && m_Session.IsOverrideAsset
                ? "Override 资源不能新增 channel。"
                : "新增一个 channel。";
        }

        private void SetAddParameterButtonEnabled(bool enabled)
        {
            if (m_AddParameterButton == null)
            {
                return;
            }

            m_AddParameterButton.SetEnabled(enabled);
            m_AddParameterButton.style.opacity = enabled ? 1f : 0.45f;
            m_AddParameterButton.tooltip = m_Session != null && m_Session.IsOverrideAsset
                ? "Override 资源不能新增 parameter。"
                : "新增一个 parameter。";
        }

        private void SetAddClipButtonEnabled(bool enabled)
        {
            if (m_AddClipButton != null)
            {
                m_AddClipButton.SetEnabled(enabled);
                m_AddClipButton.style.opacity = enabled ? 1f : 0.45f;
                m_AddClipButton.tooltip = m_Session != null && m_Session.IsOverrideAsset
                    ? "Override 资源不能新增 clip。"
                    : "新增一个全局 clip 资源叶子。";
            }

            if (m_AddClipGroupButton != null)
            {
                m_AddClipGroupButton.SetEnabled(enabled);
                m_AddClipGroupButton.style.opacity = enabled ? 1f : 0.45f;
                m_AddClipGroupButton.tooltip = m_Session != null && m_Session.IsOverrideAsset
                    ? "Override 资源不能新增 clip folder。"
                    : "在根层级新增一个 clip folder。";
            }
        }

        private void SetStepForwardButtonEnabled(bool enabled)
        {
            m_PlaybackHudView?.Refresh();
        }

        private void SetAutoTransitionButtonsEnabled(bool addEnabled)
        {
            if (m_AddAutoTransitionButton != null)
            {
                m_AddAutoTransitionButton.SetEnabled(addEnabled);
                m_AddAutoTransitionButton.style.opacity = addEnabled ? 1f : 0.45f;
                m_AddAutoTransitionButton.tooltip = m_Session != null && m_Session.IsOverrideAsset
                    ? "Override 资源不能新增 Auto Transition。"
                    : addEnabled
                        ? "新增一个 Auto Transition。"
                        : "所有可用的非循环 state 都已经配置了 Auto Transition。";
            }
        }

        private void SetDefaultTransitionButtonsEnabled(bool addEnabled)
        {
            if (m_AddDefaultTransitionButton != null)
            {
                m_AddDefaultTransitionButton.SetEnabled(addEnabled);
                m_AddDefaultTransitionButton.style.opacity = addEnabled ? 1f : 0.45f;
                m_AddDefaultTransitionButton.tooltip = m_Session != null && m_Session.IsOverrideAsset
                    ? "Override 资源不能新增 Default Transition。"
                    : addEnabled
                        ? "新增一个 Default Transition 分组。"
                        : "Default Transition 至少需要两个 state。";
            }
        }

        private void SetPauseButtonState(bool enabled, bool paused, bool? hasActivePlayback = null)
        {
            m_PlaybackHudView?.Refresh();
        }

        private bool TryPlayFirstStateFromOverlay()
        {
            if (m_Session == null || !m_Session.IsLoaded)
            {
                return false;
            }

            IReadOnlyList<XAnimationCompiledState> states = m_Session.CompiledAsset.States;
            if (states == null || states.Count == 0)
            {
                return false;
            }

            XAnimationCompiledState firstState = states[0];
            if (firstState == null || string.IsNullOrWhiteSpace(firstState.Key))
            {
                return false;
            }

            ResumePlaybackChannel(firstState.Config.channelName);
            SetPauseButtonState(true, false);
            SetStepForwardButtonEnabled(true);
            m_Session.SetGlobalSpeed(GetPlaybackSpeed());
            m_Session.PlayState(firstState.Config.channelName, firstState.Key, BuildPreviewTransitionOptions());

            RefreshPlaybackViews();
            FocusStateInInspector(firstState.Config.channelName, firstState.Key);
            SetStatus($"正在播放 state {firstState.Key}。");
            return true;
        }

        private void RefreshClipPlayingStates()
        {
            if (m_ClipRowMap.Count == 0)
            {
                SetStopAllButtonEnabled(false);
                m_IsPaused = false;
                SetPauseButtonState(false, false);
                SetStepForwardButtonEnabled(false);
                return;
            }

            // Collect currently playing clip keys
            HashSet<string> playingClipKeys = null;
            Dictionary<string, float> clipProgressByKey = null;
            if (m_Session != null && m_Session.IsLoaded)
            {
                IReadOnlyList<XAnimationCompiledChannel> channels = m_Session.CompiledAsset.Channels;
                for (int i = 0; i < channels.Count; i++)
                {
                    XAnimationChannelState state = m_Session.GetChannelState(channels[i].Name);
                    if (state == null)
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(state.clipKey))
                    {
                        playingClipKeys ??= new HashSet<string>(StringComparer.Ordinal);
                        playingClipKeys.Add(state.clipKey);
                        clipProgressByKey ??= new Dictionary<string, float>(StringComparer.Ordinal);
                        float progress = Mathf.Clamp01(state.normalizedTime);
                        if (!clipProgressByKey.TryGetValue(state.clipKey, out float existingProgress) || progress > existingProgress)
                        {
                            clipProgressByKey[state.clipKey] = progress;
                        }
                    }

                    XAnimationBlendClipState[] blendClips = state.blendClips;
                    if (blendClips == null)
                    {
                        continue;
                    }

                    for (int blendIndex = 0; blendIndex < blendClips.Length; blendIndex++)
                    {
                        XAnimationBlendClipState blendClip = blendClips[blendIndex];
                        if (blendClip == null || string.IsNullOrEmpty(blendClip.clipKey))
                        {
                            continue;
                        }

                        playingClipKeys ??= new HashSet<string>(StringComparer.Ordinal);
                        playingClipKeys.Add(blendClip.clipKey);
                        clipProgressByKey ??= new Dictionary<string, float>(StringComparer.Ordinal);
                        float blendProgress = Mathf.Clamp01(blendClip.normalizedTime);
                        if (!clipProgressByKey.TryGetValue(blendClip.clipKey, out float existingBlendProgress) || blendProgress > existingBlendProgress)
                        {
                            clipProgressByKey[blendClip.clipKey] = blendProgress;
                        }
                    }
                }
            }

            bool hasPlaying = HasAnyPlayingChannel();
            SetStopAllButtonEnabled(hasPlaying);
            if (!hasPlaying)
            {
                m_IsPaused = false;
            }
            bool canPlayFirstState = m_Session != null &&
                                     m_Session.IsLoaded &&
                                     m_Session.CompiledAsset?.States != null &&
                                     m_Session.CompiledAsset.States.Count > 0;
            SetPauseButtonState(hasPlaying || canPlayFirstState, m_IsPaused);
            SetStepForwardButtonEnabled(hasPlaying);
            RefreshPlaybackScrubber();

            foreach (KeyValuePair<string, VisualElement> kvp in m_ClipRowMap)
            {
                bool isPlaying = playingClipKeys != null && playingClipKeys.Contains(kvp.Key);
                if (m_ClipVisualStateMap.TryGetValue(kvp.Key, out ClipRowVisualState visualState))
                {
                    visualState.Playing = isPlaying;
                    visualState.Progress = isPlaying && clipProgressByKey != null && clipProgressByKey.TryGetValue(kvp.Key, out float progress)
                        ? progress
                        : 0f;
                    ApplyClipRowVisualState(kvp.Key);
                }
                else
                {
                    kvp.Value.style.backgroundColor = isPlaying ? PlayingBg : Color.clear;
                }

                if (kvp.Value.childCount > 0 && kvp.Value[0] is Label lbl)
                {
                    lbl.style.color = isPlaying ? Color.white : TextNormal;
                }
                if (m_ClipButtonMap.TryGetValue(kvp.Key, out Button btn))
                {
                    ApplyClipButtonStyle(btn, isPlaying);
                }
            }
        }

        private void RefreshStatePlayingStates()
        {
            if (m_StateRowMap.Count == 0)
            {
                RefreshBlendSampleRuntimeState();
                return;
            }

            HashSet<string> playingStateKeys = null;
            Dictionary<string, float> stateProgressByKey = null;
            if (m_Session != null && m_Session.IsLoaded)
            {
                IReadOnlyList<XAnimationCompiledChannel> channels = m_Session.CompiledAsset.Channels;
                for (int i = 0; i < channels.Count; i++)
                {
                    XAnimationChannelState state = m_Session.GetChannelState(channels[i].Name);
                    if (state != null && !string.IsNullOrEmpty(state.stateKey))
                    {
                        playingStateKeys ??= new HashSet<string>(StringComparer.Ordinal);
                        string stateUiKey = BuildStateUiKey(state.channelName, state.stateKey);
                        playingStateKeys.Add(stateUiKey);
                        stateProgressByKey ??= new Dictionary<string, float>(StringComparer.Ordinal);
                        stateProgressByKey[stateUiKey] = Mathf.Clamp01(state.normalizedTime);
                    }
                }
            }

            foreach (KeyValuePair<string, VisualElement> kvp in m_StateRowMap)
            {
                bool isPlaying = playingStateKeys != null && playingStateKeys.Contains(kvp.Key);
                if (m_StateVisualStateMap.TryGetValue(kvp.Key, out RowVisualState visualState))
                {
                    visualState.Playing = isPlaying;
                    visualState.Progress = isPlaying && stateProgressByKey != null && stateProgressByKey.TryGetValue(kvp.Key, out float progress)
                        ? progress
                        : 0f;
                    ApplyStateRowVisualState(kvp.Key);
                }
                else
                {
                    kvp.Value.style.backgroundColor = isPlaying ? PlayingBg : ListRowEvenBg;
                }
                if (m_StateButtonMap.TryGetValue(kvp.Key, out Button button))
                {
                    ApplyClipButtonStyle(button, isPlaying);
                }
            }

            RefreshBlendSampleRuntimeState();
        }

        private bool HasAnyPlayingChannel()
        {
            if (m_Session == null || !m_Session.IsLoaded)
            {
                return false;
            }

            IReadOnlyList<XAnimationCompiledChannel> channels = m_Session.CompiledAsset.Channels;
            for (int i = 0; i < channels.Count; i++)
            {
                if (m_Session.GetChannelState(channels[i].Name) != null)
                {
                    return true;
                }
            }

            return false;
        }

        private bool CanScrubPlayback()
        {
            return m_IsPaused && TryGetDominantPlaybackState(out _);
        }

        private bool TryBeginPlaybackScrub()
        {
            if (m_Session == null || !m_Session.IsLoaded)
            {
                return false;
            }

            if (!TryGetDominantPlaybackState(out _))
            {
                if (!TryPlayFirstStateFromOverlay())
                {
                    return false;
                }

                if (!TryGetDominantPlaybackState(out _))
                {
                    return false;
                }
            }

            SetPlaybackPaused(true);
            SetPauseButtonState(true, true);
            SetStepForwardButtonEnabled(true);
            return true;
        }

        private void RefreshPlaybackScrubber()
        {
            m_PlaybackHudView?.Refresh();
        }

        private void PlayPreviewAction()
        {
            if (m_Session == null || !m_Session.IsLoaded)
            {
                SetStatus("请先加载预览。", true);
                return;
            }

            EnsureActionStateSelection();
            if (string.IsNullOrWhiteSpace(m_ActionStateKey))
            {
                SetStatus("请选择 Action state。", true);
                return;
            }

            if (m_ActionReturnMode == XAnimationActionReturnMode.State)
            {
                EnsureActionReturnStateSelection();
            }

            if (m_ActionReturnMode == XAnimationActionReturnMode.State &&
                string.IsNullOrWhiteSpace(m_ActionReturnStateKey))
            {
                SetStatus("returnMode = State 时需要选择 returnState。", true);
                return;
            }

            XAnimationActionOptions options = new()
            {
                transition = BuildPreviewTransitionOptions(),
                force = m_ActionForce,
                cancelableAfter = Mathf.Max(0f, m_ActionCancelableAfter),
                cancelFadeOut = Mathf.Max(0f, m_ActionCancelFadeOut),
                returnMode = m_ActionReturnMode,
                returnStateKey = m_ActionReturnMode == XAnimationActionReturnMode.State ? m_ActionReturnStateKey : null,
                returnTransition = null,
            };

            ResumePlaybackChannel(m_Session.CompiledAsset.GetState(m_ActionStateKey).Config.channelName);
            SetPauseButtonState(true, false);
            SetStepForwardButtonEnabled(true);
            m_Session.SetGlobalSpeed(GetPlaybackSpeed());

            m_LastActionExitResult = null;
            m_ActionHandle = m_Session.PlayAction(m_ActionStateKey, options);
            m_ActionHandle.OnExit(result =>
            {
                m_LastActionExitResult = result;
                RefreshActionDebugView();
                Repaint();
            });

            RefreshPlaybackViews();
            FocusStateInInspector(m_ActionStateKey);
            SetStatus(m_ActionHandle.IsValid
                ? $"正在 PlayAction {m_ActionStateKey}。"
                : $"PlayAction {m_ActionStateKey} 被拒绝。", !m_ActionHandle.IsValid);
        }

        private void CancelPreviewAction()
        {
            if (m_ActionHandle == null || !m_ActionHandle.IsValid)
            {
                SetStatus("当前没有有效的 Action handle。", true);
                RefreshActionDebugView();
                return;
            }

            bool canceled = m_ActionHandle.Cancel();
            RefreshPlaybackViews();
            SetStatus(canceled ? "已请求取消 Action。" : "当前 Action 尚不可取消。", !canceled);
        }

        private void RefreshActionDebugView()
        {
            m_PlaybackHudView?.Refresh();
        }

        private void ClearActionDebugRuntimeState()
        {
            m_ActionHandle = null;
            m_LastActionExitResult = null;
            RefreshActionDebugView();
        }

        private void EnsureActionStateSelection()
        {
            List<string> choices = CollectStateKeyChoices();
            string selected = !string.IsNullOrWhiteSpace(m_ActionStateKey) && choices.Contains(m_ActionStateKey)
                ? m_ActionStateKey
                : choices.Count > 0 ? choices[0] : string.Empty;

            m_ActionStateKey = selected;
        }

        private void EnsureActionReturnStateSelection()
        {
            List<string> choices = CollectStateKeyChoices();
            string selected = !string.IsNullOrWhiteSpace(m_ActionReturnStateKey) && choices.Contains(m_ActionReturnStateKey)
                ? m_ActionReturnStateKey
                : choices.Count > 0 ? choices[0] : string.Empty;

            m_ActionReturnStateKey = selected;
        }

        private List<string> CollectStateKeyChoices()
        {
            List<string> choices = new();
            if (m_Session == null || !m_Session.IsLoaded || m_Session.CompiledAsset?.States == null)
            {
                return choices;
            }

            IReadOnlyList<XAnimationCompiledState> states = m_Session.CompiledAsset.States;
            for (int i = 0; i < states.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(states[i].Key))
                {
                    choices.Add(states[i].Key);
                }
            }

            return choices;
        }

        private string BuildActionStatusText()
        {
            if (m_ActionHandle == null)
            {
                return "Status: no action";
            }

            string channelName = string.IsNullOrWhiteSpace(m_ActionHandle.ChannelName)
                ? "-"
                : m_ActionHandle.ChannelName;
            string stateKey = string.IsNullOrWhiteSpace(m_ActionHandle.StateKey)
                ? "-"
                : m_ActionHandle.StateKey;
            string statusText = $"Status: {m_ActionHandle.Status} | State: {stateKey} | Channel: {channelName} | CanCancel: {m_ActionHandle.CanCancel}";
            if (m_LastActionExitResult != null)
            {
                statusText += $" | Return: {m_LastActionExitResult.ReturnStarted}";
            }

            return statusText;
        }

        private bool TryGetDominantPlaybackState(out XAnimationChannelState dominantState)
        {
            dominantState = null;
            if (m_Session == null || !m_Session.IsLoaded)
            {
                return false;
            }

            float bestWeight = -1f;
            IReadOnlyList<XAnimationCompiledChannel> channels = m_Session.CompiledAsset.Channels;
            for (int i = 0; i < channels.Count; i++)
            {
                XAnimationChannelState state = m_Session.GetChannelState(channels[i].Name);
                if (state == null)
                {
                    continue;
                }

                float stateWeight = Mathf.Max(state.weight, state.channelWeight);
                XAnimationBlendClipState[] blendClips = state.blendClips;
                if (blendClips != null)
                {
                    for (int blendIndex = 0; blendIndex < blendClips.Length; blendIndex++)
                    {
                        XAnimationBlendClipState blendClip = blendClips[blendIndex];
                        if (blendClip != null)
                        {
                            stateWeight = Mathf.Max(stateWeight, blendClip.weight);
                        }
                    }
                }

                if (stateWeight > bestWeight)
                {
                    bestWeight = stateWeight;
                    dominantState = state;
                }
            }

            return dominantState != null;
        }

        private void SeekDominantPlayback(float normalizedTime)
        {
            if (m_Session == null || !m_Session.IsLoaded || !TryGetDominantPlaybackState(out XAnimationChannelState state))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(state.channelName) ||
                !m_Session.SeekChannel(state.channelName, normalizedTime))
            {
                return;
            }

            SetPlaybackTargetChannel(state.channelName);
            SetPlaybackPaused(true);
            SetPauseButtonState(true, true);
            SetStepForwardButtonEnabled(true);
            m_Session.SetGlobalSpeed(GetPlaybackSpeed());

            StepPausedPlayback(0.0001f);
            m_PlaybackHudView?.Refresh();
            MarkEventUiDirty();
            RefreshPlaybackAndLogViews();
            RenderPreview();
            Repaint();
        }

        private static string BuildStateUiKey(XAnimationCompiledState state)
        {
            return state == null
                ? string.Empty
                : BuildStateUiKey(state.Config.channelName, state.Key);
        }

        private static string BuildStateUiKey(string channelName, string stateKey)
        {
            return XAnimationCompiledAsset.BuildStateScopeKey(channelName, stateKey);
        }

        private bool TryGetCompiledStateByUiKey(string stateUiKey, out XAnimationCompiledState state)
        {
            state = null;
            if (m_Session == null || !m_Session.IsLoaded || string.IsNullOrWhiteSpace(stateUiKey))
            {
                return false;
            }

            IReadOnlyList<XAnimationCompiledState> states = m_Session.CompiledAsset.States;
            for (int i = 0; i < states.Count; i++)
            {
                XAnimationCompiledState candidate = states[i];
                if (candidate != null && string.Equals(BuildStateUiKey(candidate), stateUiKey, StringComparison.Ordinal))
                {
                    state = candidate;
                    return true;
                }
            }

            return false;
        }

        private string ResolveStateKeyFromUiKey(string stateUiKey)
        {
            return TryGetCompiledStateByUiKey(stateUiKey, out XAnimationCompiledState state) ? state.Key : null;
        }

        private string ResolveStateUiKey(string stateKey)
        {
            if (m_Session?.CompiledAsset?.States == null)
            {
                return stateKey;
            }

            IReadOnlyList<XAnimationCompiledState> states = m_Session.CompiledAsset.States;
            for (int i = 0; i < states.Count; i++)
            {
                XAnimationCompiledState state = states[i];
                if (state != null && string.Equals(state.Key, stateKey, StringComparison.Ordinal))
                {
                    return BuildStateUiKey(state);
                }
            }

            return stateKey;
        }

        private void ApplyStateRowVisualState(string stateUiKey)
        {
            if (!m_StateRowMap.TryGetValue(stateUiKey, out VisualElement row) ||
                !m_StateVisualStateMap.TryGetValue(stateUiKey, out RowVisualState visualState))
            {
                return;
            }

            ApplyRowVisualState(row, visualState);
        }

        private void RefreshBlendSampleRuntimeState()
        {
            if (m_BlendSampleRowMap.Count == 0 && m_FreeformBlendGraphElement == null)
            {
                return;
            }

            Dictionary<string, float> sampleWeightByRowKey = null;
            Dictionary<string, Dictionary<int, float>> sampleWeightsByState = null;
            if (m_Session != null && m_Session.IsLoaded)
            {
                IReadOnlyList<XAnimationCompiledChannel> channels = m_Session.CompiledAsset.Channels;
                for (int i = 0; i < channels.Count; i++)
                {
                    XAnimationChannelState channelState = m_Session.GetChannelState(channels[i].Name);
                    if (channelState?.blendClips == null || string.IsNullOrWhiteSpace(channelState.stateKey))
                    {
                        continue;
                    }

                    for (int blendIndex = 0; blendIndex < channelState.blendClips.Length; blendIndex++)
                    {
                        XAnimationBlendClipState blendClip = channelState.blendClips[blendIndex];
                        if (blendClip == null || string.IsNullOrWhiteSpace(blendClip.clipKey))
                        {
                            continue;
                        }

                        if (!TryFindBlendSampleRowKey(
                                channels[i].Name,
                                channelState.stateKey,
                                blendClip.clipKey,
                                blendClip.positionX,
                                blendClip.positionY,
                                out string rowKey,
                                out int sampleIndex))
                        {
                            continue;
                        }

                        sampleWeightByRowKey ??= new Dictionary<string, float>(StringComparer.Ordinal);
                        if (!sampleWeightByRowKey.TryGetValue(rowKey, out float existingWeight) || blendClip.weight > existingWeight)
                        {
                            sampleWeightByRowKey[rowKey] = Mathf.Clamp01(blendClip.weight);
                        }

                        sampleWeightsByState ??= new Dictionary<string, Dictionary<int, float>>(StringComparer.Ordinal);
                        string stateUiKey = BuildStateUiKey(channels[i].Name, channelState.stateKey);
                        if (!sampleWeightsByState.TryGetValue(stateUiKey, out Dictionary<int, float> stateWeights))
                        {
                            stateWeights = new Dictionary<int, float>();
                            sampleWeightsByState[stateUiKey] = stateWeights;
                        }

                        float clampedWeight = Mathf.Clamp01(blendClip.weight);
                        if (!stateWeights.TryGetValue(sampleIndex, out float existingSampleWeight) || clampedWeight > existingSampleWeight)
                        {
                            stateWeights[sampleIndex] = clampedWeight;
                        }
                    }
                }
            }

            foreach (KeyValuePair<string, RowVisualState> kvp in m_BlendSampleRowMap)
            {
                RowVisualState visualState = kvp.Value;
                visualState.Playing = false;
                visualState.Hovered = false;
                visualState.Progress = sampleWeightByRowKey != null && sampleWeightByRowKey.TryGetValue(kvp.Key, out float weight)
                    ? weight
                    : 0f;
                ApplyRowProgressVisualState(visualState);
            }

            RefreshGlobalBlendGraph(sampleWeightsByState);
        }

        private bool TryFindBlendSampleRowKey(
            string channelName,
            string stateKey,
            string clipKey,
            float positionX,
            float positionY,
            out string rowKey,
            out int sampleIndex)
        {
            rowKey = null;
            sampleIndex = -1;
            if (string.IsNullOrWhiteSpace(clipKey))
            {
                return false;
            }

            if (!TryGetCompiledBlendGraphState(channelName, stateKey, out XAnimationCompiledState scopedState))
            {
                return false;
            }

            if (scopedState is XAnimationCompiledBlend1DState blendState)
            {
                for (int i = 0; i < blendState.Samples.Count; i++)
                {
                    XAnimationBlend1DSampleConfig sample = blendState.Samples[i].Config;
                    if (!string.Equals(sample.clipKey, clipKey, StringComparison.Ordinal) ||
                        !Mathf.Approximately(sample.threshold, positionX))
                    {
                        continue;
                    }

                    sampleIndex = i;
                    rowKey = BuildBlendSampleRuntimeKey(channelName, stateKey, i);
                    return true;
                }

                return false;
            }

            if (!TryGetDirectionalBlendSamples(scopedState, out IReadOnlyList<XAnimationCompiledBlend2DSimpleDirectionalSample> samples))
            {
                return false;
            }

            for (int i = 0; i < samples.Count; i++)
            {
                XAnimationBlend2DSimpleDirectionalSampleConfig sample = samples[i].Config;
                if (!string.Equals(sample.clipKey, clipKey, StringComparison.Ordinal) ||
                    !Mathf.Approximately(sample.positionX, positionX) ||
                    !Mathf.Approximately(sample.positionY, positionY))
                {
                    continue;
                }

                sampleIndex = i;
                rowKey = BuildBlendSampleRuntimeKey(channelName, stateKey, i);
                return true;
            }

            return false;
        }

        private void RefreshGlobalBlendGraph(Dictionary<string, Dictionary<int, float>> sampleWeightsByState = null)
        {
            if ((m_FreeformBlendGraphElement == null && m_Blend1DGraphElement == null) || m_FreeformBlendGraphOverlay == null)
            {
                return;
            }

            if (!TryResolveBlendGraphState(out XAnimationCompiledState resolvedState))
            {
                m_CurrentFreeformGraphStateKey = null;
                SetBlendGraphOverlayTitle("Blend Graph");
                m_FreeformBlendGraphOverlay.style.display = DisplayStyle.None;
                if (m_FreeformBlendGraphHintLabel != null)
                {
                    m_FreeformBlendGraphHintLabel.style.display = DisplayStyle.None;
                }

                return;
            }

            string stateKey = resolvedState.Key;
            string stateUiKey = BuildStateUiKey(resolvedState);
            m_CurrentFreeformGraphStateKey = stateUiKey;
            m_FreeformBlendGraphOverlay.style.display = DisplayStyle.Flex;
            Dictionary<int, float> stateSampleWeights = sampleWeightsByState != null && sampleWeightsByState.TryGetValue(stateUiKey, out Dictionary<int, float> stateWeights)
                ? stateWeights
                : null;
            XAnimationCompiledState compiledState = resolvedState;

            if (compiledState is XAnimationCompiledBlend1DState blend1DState)
            {
                UpdateBlend1DGraph(stateKey, blend1DState, stateSampleWeights);
                return;
            }

            if (compiledState is XAnimationCompiledBlend2DSimpleDirectionalState simpleDirectionalState)
            {
                UpdateDirectionalBlendGraph(
                    stateKey,
                    m_FreeformBlendGraphElement,
                    simpleDirectionalState,
                    "Simple 2D Directional Blend",
                    stateSampleWeights);
                return;
            }

            if (compiledState is XAnimationCompiledBlend2DFreeformDirectionalState freeformState)
            {
                UpdateDirectionalBlendGraph(
                    stateKey,
                    m_FreeformBlendGraphElement,
                    freeformState,
                    "Freeform 2D Blend",
                    stateSampleWeights);
                return;
            }

            m_FreeformBlendGraphOverlay.style.display = DisplayStyle.None;
        }

        private void UpdateBlend1DGraph(
            string stateKey,
            XAnimationCompiledBlend1DState blend1DState,
            Dictionary<int, float> sampleWeights = null)
        {
            if (m_Blend1DGraphElement == null || m_Session == null || !m_Session.IsLoaded || blend1DState == null)
            {
                return;
            }

            SetBlendGraphOverlayTitle("Blend1D");
            m_Blend1DGraphElement.style.display = DisplayStyle.Flex;
            if (m_FreeformBlendGraphElement != null)
            {
                m_FreeformBlendGraphElement.style.display = DisplayStyle.None;
            }

            XAnimationStateConfig config = blend1DState.Config;
            List<XAnimationBlend1DGraphElement.SampleViewData> sampleViews = new(blend1DState.Samples.Count);
            for (int i = 0; i < blend1DState.Samples.Count; i++)
            {
                XAnimationBlend1DSampleConfig sample = blend1DState.Samples[i].Config;
                float weight = sampleWeights != null && sampleWeights.TryGetValue(i, out float sampleWeight) ? sampleWeight : 0f;
                sampleViews.Add(new XAnimationBlend1DGraphElement.SampleViewData(sample.clipKey, sample.threshold, weight));
            }
            sampleViews.Sort((left, right) => left.Threshold.CompareTo(right.Threshold));

            bool hasParameter = !string.IsNullOrWhiteSpace(config.parameterName);
            float currentValue = GetBlend1DPreviewValue(config.parameterName);
            float minValue = 0f;
            float maxValue = 1f;
            if (hasParameter && TryGetBlend1DPreviewRange(config.parameterName, out float parameterMin, out float parameterMax))
            {
                minValue = parameterMin;
                maxValue = parameterMax;
            }
            else if (blend1DState.Samples.Count > 0)
            {
                minValue = blend1DState.Samples[0].Threshold;
                maxValue = blend1DState.Samples[blend1DState.Samples.Count - 1].Threshold;
            }

            if (m_FreeformBlendGraphHintLabel != null)
            {
                m_FreeformBlendGraphHintLabel.text = hasParameter
                    ? stateKey
                    : $"State: {stateKey}\nRead-only because parameter is missing.";
                m_FreeformBlendGraphHintLabel.style.display = DisplayStyle.Flex;
            }

            m_Blend1DGraphElement.SetData(new XAnimationBlend1DGraphElement.GraphData(
                sampleViews,
                currentValue,
                minValue,
                maxValue,
                hasParameter,
                hasParameter ? () => BeginBlend1DDragPreview(config.channelName, stateKey) : null,
                hasParameter ? value => UpdateBlend1DPreviewValue(config.channelName, stateKey, config, value) : null));
        }

        private void UpdateDirectionalBlendGraph(
            string stateKey,
            XAnimationDirectionalBlendGraphElement graph,
            XAnimationCompiledState directionalState,
            string title,
            Dictionary<int, float> sampleWeights = null)
        {
            if (graph == null ||
                m_Session == null ||
                !m_Session.IsLoaded ||
                directionalState == null ||
                string.IsNullOrWhiteSpace(stateKey) ||
                !TryGetDirectionalBlendSamples(directionalState, out IReadOnlyList<XAnimationCompiledBlend2DSimpleDirectionalSample> samples))
            {
                return;
            }

            SetBlendGraphOverlayTitle(title);
            graph.style.display = DisplayStyle.Flex;
            if (m_Blend1DGraphElement != null)
            {
                m_Blend1DGraphElement.style.display = DisplayStyle.None;
            }

            XAnimationStateConfig config = directionalState.Config;
            List<XAnimationDirectionalBlendGraphElement.SampleViewData> sampleViews = new(samples.Count);
            for (int i = 0; i < samples.Count; i++)
            {
                XAnimationBlend2DSimpleDirectionalSampleConfig sample = samples[i].Config;
                float weight = sampleWeights != null && sampleWeights.TryGetValue(i, out float sampleWeight) ? sampleWeight : 0f;
                sampleViews.Add(new XAnimationDirectionalBlendGraphElement.SampleViewData(
                    sample.clipKey,
                    sample.positionX,
                    sample.positionY,
                    weight));
            }

            bool hasParameters =
                !string.IsNullOrWhiteSpace(config.parameterXName) &&
                !string.IsNullOrWhiteSpace(config.parameterYName);
            Vector2 currentPosition = GetFreeformDirectionalPreviewPosition(config);
            if (m_FreeformBlendGraphHintLabel != null)
            {
                m_FreeformBlendGraphHintLabel.text = hasParameters
                    ? stateKey
                    : $"State: {stateKey}\nRead-only because parameterX / parameterY is missing.";
                m_FreeformBlendGraphHintLabel.style.display = DisplayStyle.Flex;
            }

            graph.SetData(new XAnimationDirectionalBlendGraphElement.GraphData(
                sampleViews,
                currentPosition,
                hasParameters,
                hasParameters ? () => BeginFreeformDirectionalDragPreview(config.channelName, stateKey) : null,
                hasParameters ? position => UpdateFreeformDirectionalPreviewPosition(config.channelName, stateKey, config, position) : null));
        }

        private bool TryResolveBlendGraphState(out XAnimationCompiledState state)
        {
            state = null;
            if (m_Session == null || !m_Session.IsLoaded)
            {
                return false;
            }

            IReadOnlyList<XAnimationCompiledChannel> channels = m_Session.CompiledAsset.Channels;
            for (int i = 0; i < channels.Count; i++)
            {
                XAnimationChannelState channelState = m_Session.GetChannelState(channels[i].Name);
                if (channelState == null || string.IsNullOrWhiteSpace(channelState.stateKey))
                {
                    continue;
                }

                if (!TryGetCompiledBlendGraphState(channels[i].Name, channelState.stateKey, out XAnimationCompiledState channelCompiledState) ||
                    !IsBlendGraphCompatibleState(channelCompiledState))
                {
                    continue;
                }

                state = channelCompiledState;
                return true;
            }

            foreach (string expandedStateUiKey in m_ExpandedStateKeys)
            {
                if (TryGetCompiledStateByUiKey(expandedStateUiKey, out XAnimationCompiledState expandedState) &&
                    IsBlendGraphCompatibleState(expandedState))
                {
                    state = expandedState;
                    return true;
                }
            }

            if (!string.IsNullOrWhiteSpace(m_LastInteractedFreeformStateKey) &&
                TryGetCompiledStateByUiKey(m_LastInteractedFreeformStateKey, out XAnimationCompiledState interactedState) &&
                IsBlendGraphCompatibleState(interactedState))
            {
                state = interactedState;
                return true;
            }

            return false;
        }

        private bool TryResolveBlendGraphStateKey(out string stateKey)
        {
            stateKey = null;
            if (!TryResolveBlendGraphState(out XAnimationCompiledState state))
            {
                return false;
            }

            stateKey = state.Key;
            return true;
        }

        private bool TryGetCompiledBlendGraphState(string stateKey, out XAnimationCompiledState compiledState)
        {
            compiledState = null;
            if (m_Session == null || !m_Session.IsLoaded || string.IsNullOrWhiteSpace(stateKey))
            {
                return false;
            }

            XAnimationCompiledAsset compiledAsset = m_Session.CompiledAsset;
            if (compiledAsset == null || !compiledAsset.TryGetStateIndex(stateKey, out int stateIndex))
            {
                return false;
            }

            compiledState = compiledAsset.States[stateIndex];
            return compiledState != null;
        }

        private bool TryGetCompiledBlendGraphState(string channelName, string stateKey, out XAnimationCompiledState compiledState)
        {
            compiledState = null;
            if (m_Session == null ||
                !m_Session.IsLoaded ||
                string.IsNullOrWhiteSpace(channelName) ||
                string.IsNullOrWhiteSpace(stateKey))
            {
                return false;
            }

            XAnimationCompiledAsset compiledAsset = m_Session.CompiledAsset;
            if (compiledAsset == null || !compiledAsset.TryGetStateIndex(channelName, stateKey, out int stateIndex))
            {
                return false;
            }

            compiledState = compiledAsset.States[stateIndex];
            return compiledState != null;
        }

        private bool IsBlendGraphCompatibleState(string stateKey)
        {
            if (!TryGetCompiledBlendGraphState(stateKey, out XAnimationCompiledState compiledState))
            {
                return false;
            }

            return compiledState switch
            {
                XAnimationCompiledBlend1DState blend1DState => blend1DState.Samples.Count > 0,
                XAnimationCompiledBlend2DSimpleDirectionalState simpleDirectionalState => simpleDirectionalState.Samples.Count > 0,
                XAnimationCompiledBlend2DFreeformDirectionalState freeformState => freeformState.Samples.Count > 0,
                _ => false,
            };
        }

        private static bool IsBlendGraphCompatibleState(XAnimationCompiledState compiledState)
        {
            return compiledState switch
            {
                XAnimationCompiledBlend1DState blend1DState => blend1DState.Samples.Count > 0,
                XAnimationCompiledBlend2DSimpleDirectionalState simpleDirectionalState => simpleDirectionalState.Samples.Count > 0,
                XAnimationCompiledBlend2DFreeformDirectionalState freeformState => freeformState.Samples.Count > 0,
                _ => false,
            };
        }

        private bool TryGetFreeformDirectionalState(string stateKey, out XAnimationCompiledBlend2DFreeformDirectionalState state)
        {
            state = null;
            if (!TryGetCompiledBlendGraphState(stateKey, out XAnimationCompiledState compiledState))
            {
                return false;
            }

            state = compiledState as XAnimationCompiledBlend2DFreeformDirectionalState;
            return state != null && state.Samples.Count > 0;
        }

        private void MarkFreeformStateInteracted(string stateKey)
        {
            if (string.IsNullOrWhiteSpace(stateKey) || !TryGetCompiledBlendGraphState(stateKey, out XAnimationCompiledState state))
            {
                return;
            }

            MarkFreeformStateInteracted(state.Config.channelName, stateKey);
        }

        private void MarkFreeformStateInteracted(string channelName, string stateKey)
        {
            if (string.IsNullOrWhiteSpace(channelName) ||
                string.IsNullOrWhiteSpace(stateKey) ||
                !TryGetCompiledBlendGraphState(channelName, stateKey, out XAnimationCompiledState state) ||
                !IsBlendGraphCompatibleState(state))
            {
                return;
            }

            m_LastInteractedFreeformStateKey = BuildStateUiKey(channelName, stateKey);
        }

        private Vector2 GetFreeformDirectionalPreviewPosition(XAnimationStateConfig config)
        {
            if (config == null)
            {
                return Vector2.zero;
            }

            float x = 0f;
            float y = 0f;
            if (!string.IsNullOrWhiteSpace(config.parameterXName) &&
                m_Session != null &&
                m_Session.TryGetPreviewParameter(config.parameterXName, out float previewX))
            {
                x = previewX;
            }

            if (!string.IsNullOrWhiteSpace(config.parameterYName) &&
                m_Session != null &&
                m_Session.TryGetPreviewParameter(config.parameterYName, out float previewY))
            {
                y = previewY;
            }

            return new Vector2(x, y);
        }

        private void BeginFreeformDirectionalDragPreview(string channelName, string stateKey)
        {
            if (m_Session == null || !m_Session.IsLoaded || string.IsNullOrWhiteSpace(stateKey))
            {
                return;
            }

            MarkFreeformStateInteracted(channelName, stateKey);

            if (!TryGetCompiledBlendGraphState(channelName, stateKey, out XAnimationCompiledState state))
            {
                return;
            }

            if (state == null || IsStateCurrentlyPlaying(stateKey, state.Config.channelName))
            {
                return;
            }

            ResumePlaybackChannel(state.Config.channelName);
            m_Session.PlayState(state.Config.channelName, stateKey, BuildPreviewTransitionOptions());
            RefreshStatePlaybackViews();
            RenderPreview();
            Repaint();
        }

        private void BeginBlend1DDragPreview(string channelName, string stateKey)
        {
            if (m_Session == null || !m_Session.IsLoaded || string.IsNullOrWhiteSpace(stateKey))
            {
                return;
            }

            MarkFreeformStateInteracted(channelName, stateKey);
            if (!TryGetCompiledBlendGraphState(channelName, stateKey, out XAnimationCompiledState state))
            {
                return;
            }

            if (state == null || IsStateCurrentlyPlaying(stateKey, state.Config.channelName))
            {
                return;
            }

            ResumePlaybackChannel(state.Config.channelName);
            m_Session.PlayState(state.Config.channelName, stateKey, BuildPreviewTransitionOptions());
            RefreshStatePlaybackViews();
            RenderPreview();
            Repaint();
        }

        private void UpdateFreeformDirectionalPreviewPosition(string channelName, string stateKey, XAnimationStateConfig config, Vector2 position)
        {
            if (m_Session == null || !m_Session.IsLoaded || config == null)
            {
                return;
            }

            MarkFreeformStateInteracted(channelName, stateKey);

            bool changed = false;
            if (!string.IsNullOrWhiteSpace(config.parameterXName))
            {
                changed |= TrySetPreviewParameter(config.parameterXName, position.x);
            }

            if (!string.IsNullOrWhiteSpace(config.parameterYName))
            {
                changed |= TrySetPreviewParameter(config.parameterYName, position.y);
            }

            if (changed)
            {
                RefreshPreviewAfterParameterChanged(rebuildParameterList: true);
            }

            string stateUiKey = BuildStateUiKey(channelName, stateKey);
            if (string.Equals(m_CurrentFreeformGraphStateKey, stateUiKey, StringComparison.Ordinal))
            {
                if (!TryGetCompiledBlendGraphState(channelName, stateKey, out XAnimationCompiledState currentState))
                {
                    m_CurrentFreeformGraphStateKey = null;
                }
                else if (currentState is XAnimationCompiledBlend2DSimpleDirectionalState simpleDirectionalState)
                {
                    UpdateDirectionalBlendGraph(
                        stateKey,
                        m_FreeformBlendGraphElement,
                        simpleDirectionalState,
                        "Simple 2D Directional Blend");
                }
                else if (currentState is XAnimationCompiledBlend2DFreeformDirectionalState freeformState)
                {
                    UpdateDirectionalBlendGraph(
                        stateKey,
                        m_FreeformBlendGraphElement,
                        freeformState,
                        "Freeform 2D Blend");
                }
            }
        }

        private float GetBlend1DPreviewValue(string parameterName)
        {
            if (string.IsNullOrWhiteSpace(parameterName) || m_Session == null || !m_Session.IsLoaded)
            {
                return 0f;
            }

            if (m_Session.TryGetPreviewParameter(parameterName, out float value))
            {
                return value;
            }

            RebuildParameterList();
            return m_Session.TryGetPreviewParameter(parameterName, out value) ? value : 0f;
        }

        private void UpdateBlend1DPreviewValue(string channelName, string stateKey, XAnimationStateConfig config, float value)
        {
            if (m_Session == null || !m_Session.IsLoaded || config == null)
            {
                return;
            }

            MarkFreeformStateInteracted(channelName, stateKey);
            if (!string.IsNullOrWhiteSpace(config.parameterName))
            {
                if (TrySetPreviewParameter(config.parameterName, value))
                {
                    RefreshPreviewAfterParameterChanged(rebuildParameterList: true);
                }
            }

            string stateUiKey = BuildStateUiKey(channelName, stateKey);
            if (string.Equals(m_CurrentFreeformGraphStateKey, stateUiKey, StringComparison.Ordinal) &&
                TryGetCompiledBlendGraphState(channelName, stateKey, out XAnimationCompiledState currentState) &&
                currentState is XAnimationCompiledBlend1DState blend1DState)
            {
                UpdateBlend1DGraph(stateKey, blend1DState);
            }
        }

        private bool IsStateCurrentlyPlaying(string stateKey, string channelName)
        {
            if (m_Session == null || !m_Session.IsLoaded || string.IsNullOrWhiteSpace(stateKey))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(channelName))
            {
                XAnimationChannelState channelState = m_Session.GetChannelState(channelName);
                return channelState != null && string.Equals(channelState.stateKey, stateKey, StringComparison.Ordinal);
            }

            IReadOnlyList<XAnimationCompiledChannel> channels = m_Session.CompiledAsset.Channels;
            for (int i = 0; i < channels.Count; i++)
            {
                XAnimationChannelState channelState = m_Session.GetChannelState(channels[i].Name);
                if (channelState != null && string.Equals(channelState.stateKey, stateKey, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static string BuildBlendSampleRuntimeKey(string channelName, string stateKey, int sampleIndex)
        {
            return $"blend-sample:{BuildStateUiKey(channelName, stateKey)}:{sampleIndex}";
        }

        private void RefreshStatePlaybackViews()
        {
            RefreshStatePlayingStates();
            RefreshChannelStates();
            m_PlaybackHudView?.Refresh();
        }

        private void RefreshPlaybackViews()
        {
            RefreshStatePlayingStates();
            RefreshClipPlayingStates();
            RefreshChannelStates();
            m_PlaybackHudView?.Refresh();
        }

        private void RefreshPlaybackAndLogViews()
        {
            RefreshPlaybackViews();
            RefreshCueLogView(force: true);
        }

        private void RebuildChannelPresentation()
        {
            RebuildChannelControls();
            RefreshPlayTargetChannelChoices();
        }

        private void RebuildCorePreviewLists()
        {
            RebuildParameterList();
            RebuildStateList();
            RebuildClipList();
        }

        private void RebuildStatePresentation(bool includeClipList = false, bool includeChannelPresentation = false)
        {
            RebuildStateList();
            if (includeClipList)
            {
                RebuildClipList();
            }

            if (includeChannelPresentation)
            {
                RebuildChannelPresentation();
            }

            RefreshStatePlaybackViews();
            if (includeClipList)
            {
                RefreshClipPlayingStates();
            }
        }

        private void RebuildClipPresentation()
        {
            RebuildClipList();
            RefreshClipPlayingStates();
            RefreshChannelStates();
        }

        private void RebuildStructureAndPlaybackViews()
        {
            RebuildStateList();
            RebuildClipList();
            RebuildChannelPresentation();
            RefreshPlaybackViews();
        }

        private void ApplyClipRowVisualState(string clipKey)
        {
            if (!m_ClipRowMap.TryGetValue(clipKey, out VisualElement row) ||
                !m_ClipVisualStateMap.TryGetValue(clipKey, out ClipRowVisualState visualState))
            {
                return;
            }

            row.style.backgroundColor = visualState.Playing
                ? visualState.Flashing ? ClipFocusFlashBg : PlayingBg
                : visualState.Flashing
                    ? ClipFocusFlashBg
                    : visualState.Hovered
                        ? HoverBg
                        : visualState.BaseColor;
            ApplyRowProgressVisualState(visualState);
        }

        private void RebuildChannelControls()
        {
            m_ChannelControlsContainer.Clear();
            m_ChannelLabelMap.Clear();
            m_ChannelStateLabels.Clear();
            m_ChannelRowMap.Clear();
            SetAddChannelButtonEnabled(m_Session != null && m_Session.IsLoaded && !m_Session.IsOverrideAsset);

            if (m_Session == null || !m_Session.IsLoaded)
            {
                return;
            }

            IReadOnlyList<XAnimationCompiledChannel> channels = m_Session.CompiledAsset.Channels;
            for (int i = 0; i < channels.Count; i++)
            {
                XAnimationCompiledChannel channel = (XAnimationCompiledChannel)channels[i];

                VisualElement controlRow = CreateListGroup();
                m_ChannelRowMap[channel.Name] = controlRow;

                VisualElement channelHeader = CreateListHeader(0);
                channelHeader.tooltip = "单击 channel 名称展开/收起配置和预览调试信息；右键 Rename 编辑名称。";

                Label channelFoldoutLabel = CreateFoldoutGlyph(true);
                channelHeader.Add(channelFoldoutLabel);

                EditableLabel channelLabel = new(channel.Name);
                ConfigureEditableNameLabel(channelLabel, 160f);
                channelLabel.tooltip = "单击展开/收起这个 channel 的配置和预览调试信息；右键 Rename 编辑名称。";
                channelLabel.SetEditable(true, EditableLabelEditTrigger.ContextMenu);
                channelLabel.EditStarted += BeginNameEdit;
                channelLabel.EditEnded += EndNameEdit;
                channelLabel.ValueCommitted += (_, newValue) => RenameChannel(channel.Name, newValue, channelLabel);
                m_ChannelLabelMap[channel.Name] = channelLabel;
                channelHeader.Add(channelLabel);

                VisualElement channelHeaderSpacer = new();
                channelHeaderSpacer.style.flexGrow = 1;
                channelHeader.Add(channelHeaderSpacer);

                Button deleteChannelButton = new(() => DeleteChannel(channel.Name))
                {
                    text = "⌫"
                };
                deleteChannelButton.tooltip = m_Session.IsOverrideAsset
                    ? "Override 资源不能删除 channel。"
                    : "删除这个 channel，并在确认后连带删除其下 clip。";
                deleteChannelButton.SetEnabled(!m_Session.IsOverrideAsset);
                ApplyTrashButtonIcon(deleteChannelButton);
                ApplyClipIconButtonStyle(deleteChannelButton);
                deleteChannelButton.style.marginLeft = 4;
                channelHeader.Add(deleteChannelButton);
                controlRow.Add(channelHeader);

                VisualElement channelContent = new VisualElement();
                ApplyPrettyContentStyle(channelContent);
                VisualElement configBox = CreateSubBox();
                configBox.Add(CreateChannelConfigEditor(channel));
                channelContent.Add(configBox);
                VisualElement debugBox = CreateSubBox();

                Label stateLabel = new(BuildChannelStateText(channel, null));
                stateLabel.tooltip = stateLabel.text;
                stateLabel.style.whiteSpace = WhiteSpace.Normal;
                stateLabel.style.fontSize = 11;
                stateLabel.style.color = TextMuted;
                stateLabel.style.marginBottom = 2;
                stateLabel.style.height = ChannelStateLabelHeight;
                stateLabel.style.minHeight = ChannelStateLabelHeight;
                stateLabel.style.maxHeight = ChannelStateLabelHeight;
                stateLabel.style.overflow = Overflow.Hidden;
                stateLabel.style.paddingLeft = 3;
                stateLabel.style.paddingRight = 3;
                stateLabel.style.paddingTop = 2;
                stateLabel.style.paddingBottom = 2;
                stateLabel.style.backgroundColor = ListHeaderBg;
                debugBox.Add(stateLabel);
                m_ChannelStateLabels[channel.Name] = stateLabel;
                channelContent.Add(debugBox);

                channelLabel.RegisterCallback<MouseDownEvent>(evt =>
                {
                    if (evt.button != 0)
                    {
                        return;
                    }

                    if (channelLabel.IsEditing)
                    {
                        return;
                    }

                    bool expanded = channelContent.style.display != DisplayStyle.None;
                    channelContent.style.display = expanded ? DisplayStyle.None : DisplayStyle.Flex;
                    channelHeader.style.borderBottomWidth = expanded ? 0f : PrettyBorderWidth;
                    SetFoldoutGlyphText(channelFoldoutLabel, !expanded);
                    evt.StopPropagation();
                });

                controlRow.Add(channelContent);

                m_ChannelControlsContainer.Add(controlRow);
            }

            TryBeginPendingRename();
            RefreshSearchIndex();
        }

    }
}
#endif
