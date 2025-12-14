#if UNITY_EDITOR
using System;

namespace PrefabPreviewer
{
    internal static class ViewportXLocalization
    {
        internal enum Key
        {
            // General
            MissingUxml,
            MissingUss,
            PreviewRendererUnavailable,

            // Asset type labels
            AssetTypeTexture,
            AssetTypeSprite,
            AssetTypeMaterial,
            AssetTypeMesh,

            // Selection / Status
            SelectionNone,
            SelectionSelected,
            StatusPrefix,
            StatusNoAsset,
            StatusInitPreviewRendererFailed,
            StatusInstantiatePrefabFailed,
            StatusTypeUnavailable,
            StatusTypeName,

            // Content type labels
            ContentTypeModel,
            ContentTypeParticle,
            ContentTypeUGUI,
            ContentTypeUnknown,

            // Preview hints
            HintSelectPrefab,
            HintSelectPreviewableAsset,
            HintTextureUnavailable,
            HintGeneratingPreview,

            // Toolbar tooltips
            TooltipPlayPauseParticles,
            TooltipRestartParticles,
            TooltipGrid,
            TooltipAutoRotate,
            TooltipLighting,
            TooltipRefreshSelection,
            TooltipResetView,
            TooltipViewX,
            TooltipViewY,
            TooltipViewZ,
            TooltipFrame,
            TooltipSettings,

            // Play button label
            PlayButtonPause,
            PlayButtonPlay,

            // Settings overlay
            SettingsVersion,
            SettingsAuthor,
            SettingsVisitAuthor,
            SettingsDocumentation,
            SettingsLanguage,
            SettingsConfig,
            SettingsStoragePath,
            SettingsTools,
            SettingsAbout,
            SettingsToolIntroduction,

            // Language option labels
            LanguageOptionEnglish,
            LanguageOptionChinese,
        }

        internal static string Get(Key key, bool chinese)
        {
            return chinese ? GetChinese(key) : GetEnglish(key);
        }

        internal static string Format(Key key, bool chinese, params object[] args)
        {
            return string.Format(Get(key, chinese), args);
        }

        private static string GetChinese(Key key)
        {
            return key switch
            {
                Key.MissingUxml => "找不到 UXML：{0}",
                Key.MissingUss => "找不到 USS：{0}",
                Key.PreviewRendererUnavailable => "预览渲染器不可用",

                Key.AssetTypeTexture => "纹理",
                Key.AssetTypeSprite => "Sprite",
                Key.AssetTypeMaterial => "材质",
                Key.AssetTypeMesh => "网格",

                Key.SelectionNone => "未选中任何资产",
                Key.SelectionSelected => "选中：{0}",
                Key.StatusPrefix => "状态：{0}",
                Key.StatusNoAsset => "状态：未选中资产",
                Key.StatusInitPreviewRendererFailed => "状态：无法初始化预览渲染器",
                Key.StatusInstantiatePrefabFailed => "状态：无法实例化预制体",
                Key.StatusTypeUnavailable => "状态：{0}不可用",
                Key.StatusTypeName => "状态：{0}",

                Key.ContentTypeModel => "模型",
                Key.ContentTypeParticle => "粒子",
                Key.ContentTypeUGUI => "UGUI",
                Key.ContentTypeUnknown => "-",

                Key.HintSelectPrefab => "请选择一个预制体",
                Key.HintSelectPreviewableAsset => "请选择一个可预览的资产",
                Key.HintTextureUnavailable => "纹理不可用",
                Key.HintGeneratingPreview => "正在生成预览...",

                Key.TooltipPlayPauseParticles => "播放/暂停 粒子",
                Key.TooltipRestartParticles => "重播 粒子",
                Key.TooltipGrid => "网格",
                Key.TooltipAutoRotate => "自动旋转",
                Key.TooltipLighting => "灯光",
                Key.TooltipRefreshSelection => "刷新选中",
                Key.TooltipResetView => "重置视图 (A)",
                Key.TooltipViewX => "视图：X",
                Key.TooltipViewY => "视图：Y",
                Key.TooltipViewZ => "视图：Z",
                Key.TooltipFrame => "聚焦 (F)",
                Key.TooltipSettings => "设置",

                Key.PlayButtonPause => "暂停",
                Key.PlayButtonPlay => "播放",

                Key.SettingsVersion => "版本: {0}",
                Key.SettingsAuthor => "作者: {0}",
                Key.SettingsVisitAuthor => "访问作者主页",
                Key.SettingsDocumentation => "文档",
                Key.SettingsLanguage => "语言:",
                Key.SettingsConfig => "配置",
                Key.SettingsStoragePath => "存储路径: {0}",
                Key.SettingsTools => "工具说明",
                Key.SettingsAbout => "关于",
                Key.SettingsToolIntroduction => "ViewportX：用于在编辑器中预览 Prefab/模型/粒子/UGUI。支持旋转、缩放、平移、视图切换、网格与灯光等。",

                Key.LanguageOptionEnglish => "English",
                Key.LanguageOptionChinese => "中文",

                _ => string.Empty
            };
        }

        private static string GetEnglish(Key key)
        {
            return key switch
            {
                Key.MissingUxml => "Missing UXML: {0}",
                Key.MissingUss => "Missing USS: {0}",
                Key.PreviewRendererUnavailable => "Preview renderer unavailable",

                Key.AssetTypeTexture => "Texture",
                Key.AssetTypeSprite => "Sprite",
                Key.AssetTypeMaterial => "Material",
                Key.AssetTypeMesh => "Mesh",

                Key.SelectionNone => "No asset selected",
                Key.SelectionSelected => "Selected: {0}",
                Key.StatusPrefix => "Status: {0}",
                Key.StatusNoAsset => "Status: No asset selected",
                Key.StatusInitPreviewRendererFailed => "Status: Failed to initialize preview renderer",
                Key.StatusInstantiatePrefabFailed => "Status: Failed to instantiate prefab",
                Key.StatusTypeUnavailable => "Status: {0} unavailable",
                Key.StatusTypeName => "Status: {0}",

                Key.ContentTypeModel => "Model",
                Key.ContentTypeParticle => "Particle",
                Key.ContentTypeUGUI => "UGUI",
                Key.ContentTypeUnknown => "-",

                Key.HintSelectPrefab => "Please select a prefab",
                Key.HintSelectPreviewableAsset => "Please select a previewable asset",
                Key.HintTextureUnavailable => "Texture unavailable",
                Key.HintGeneratingPreview => "Generating preview...",

                Key.TooltipPlayPauseParticles => "Play/Pause Particles",
                Key.TooltipRestartParticles => "Restart Particles",
                Key.TooltipGrid => "Grid",
                Key.TooltipAutoRotate => "Auto Rotate",
                Key.TooltipLighting => "Lighting",
                Key.TooltipRefreshSelection => "Refresh Selection",
                Key.TooltipResetView => "Reset View (A)",
                Key.TooltipViewX => "View: X",
                Key.TooltipViewY => "View: Y",
                Key.TooltipViewZ => "View: Z",
                Key.TooltipFrame => "Frame (F)",
                Key.TooltipSettings => "Settings",

                Key.PlayButtonPause => "Pause",
                Key.PlayButtonPlay => "Play",

                Key.SettingsVersion => "Version: {0}",
                Key.SettingsAuthor => "Author: {0}",
                Key.SettingsVisitAuthor => "Visit Author's Homepage",
                Key.SettingsDocumentation => "Documentation",
                Key.SettingsLanguage => "Language:",
                Key.SettingsConfig => "Configuration",
                Key.SettingsStoragePath => "Storage: {0}",
                Key.SettingsTools => "About",
                Key.SettingsAbout => "About",
                Key.SettingsToolIntroduction => "ViewportX is a prefab preview tool for viewing Prefabs/models/particles/UGUI in the Editor. Supports orbit, zoom, pan, view axes, grid and lighting.",

                Key.LanguageOptionEnglish => "English",
                Key.LanguageOptionChinese => "Chinese",

                _ => string.Empty
            };
        }
    }
}
#endif
