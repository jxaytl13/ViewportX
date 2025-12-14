#if UNITY_EDITOR
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace PrefabPreviewer
{
    /// <summary>
    /// ViewportX 设置弹窗（Overlay）
    /// 
    /// 设计目标：
    /// 1) 不依赖 UXML / USS，全部用 C# 硬编码创建 UI 和样式；
    /// 2) 提供 Show/Hide 接口，作为一个可复用的“通用设置弹窗”模块；
    /// 3) 文案支持中英文切换（由外部决定当前语言并负责持久化）。
    /// 
    /// 用法（在任意 EditorWindow 的 CreateGUI 中）：
    /// var overlay = new ViewportXSettingsOverlay(...);
    /// rootVisualElement.Add(overlay.Root);
    /// overlay.Show(); / overlay.Hide();
    /// </summary>
    public sealed class ViewportXSettingsOverlay
    {
        // Overlay 根节点：铺满窗口，半透明背景，点击空白区域关闭。
        private readonly VisualElement _overlayRoot;

        // 弹窗主体节点：放置具体内容。
        private readonly VisualElement _dialogRoot;

        // 下面这些 UI 控件需要在语言切换时更新文案。
        private readonly Label _versionLabel;
        private readonly Label _authorLabel;
        private readonly Button _authorLinkButton;
        private readonly Button _documentLinkButton;
        private readonly Label _languageLabel;
        private readonly DropdownField _languageDropdown;
        private readonly Label _configTitleLabel;
        private readonly Label _configPathLabel;
        private readonly Label _toolsTitleLabel;
        private readonly Label _toolIntroductionLabel;

        private string _languageOptionEnglish;
        private string _languageOptionChinese;

        private readonly Func<string> _getConfigPath;
        private readonly Action<bool> _onLanguageChanged;

        public enum TextKey
        {
            SettingsVersion,
            SettingsAuthor,
            SettingsVisitAuthor,
            SettingsDocumentation,
            SettingsLanguage,
            SettingsConfig,
            SettingsStoragePath,
            SettingsTools,
            LanguageOptionEnglish,
            LanguageOptionChinese
        }

        private readonly Func<TextKey, bool, string> _getText;

        private readonly string _aboutVersion;
        private readonly string _aboutAuthor;
        private readonly string _aboutAuthorLink;
        private readonly string _aboutDocumentLink;

        private readonly string _toolIntroductionZh;
        private readonly string _toolIntroductionEn;

        private bool _isChinese;

        private const float LanguageDropdownWidth = 200f;

        /// <summary>
        /// 供外部挂到 EditorWindow.rootVisualElement 上。
        /// </summary>
        public VisualElement Root => _overlayRoot;

        public ViewportXSettingsOverlay(
            string aboutVersion,
            string aboutAuthor,
            string aboutAuthorLink,
            string aboutDocumentLink,
            string toolIntroductionZh,
            string toolIntroductionEn,
            Func<string> getConfigPath,
            bool initialChinese,
            Action<bool> onLanguageChanged,
            Func<TextKey, bool, string> getText = null)
        {
            _aboutVersion = aboutVersion;
            _aboutAuthor = aboutAuthor;
            _aboutAuthorLink = aboutAuthorLink;
            _aboutDocumentLink = aboutDocumentLink;
            _toolIntroductionZh = toolIntroductionZh;
            _toolIntroductionEn = toolIntroductionEn;
            _getConfigPath = getConfigPath;
            _isChinese = initialChinese;
            _onLanguageChanged = onLanguageChanged;
            _getText = getText ?? GetDefaultText;

            _overlayRoot = new VisualElement();
            _dialogRoot = new VisualElement();

            // -------- Overlay 样式（铺满窗口 + 半透明黑底）--------
            _overlayRoot.style.display = DisplayStyle.None;
            _overlayRoot.style.position = Position.Absolute;
            _overlayRoot.style.left = 0;
            _overlayRoot.style.right = 0;
            _overlayRoot.style.top = 0;
            _overlayRoot.style.bottom = 0;
            _overlayRoot.style.backgroundColor = new Color(0f, 0f, 0f, 0.5f);
            _overlayRoot.style.justifyContent = Justify.Center;
            _overlayRoot.style.alignItems = Align.Center;

            // 点击遮罩层空白区域关闭（点击弹窗内部不关闭）。
            _overlayRoot.RegisterCallback<MouseDownEvent>(OnOverlayMouseDown);

            // -------- Dialog 样式（固定宽高 + 深色面板）--------
            _dialogRoot.style.flexDirection = FlexDirection.Column;
            _dialogRoot.style.width = 500;
            _dialogRoot.style.height = 400;
            _dialogRoot.style.paddingLeft = 20;
            _dialogRoot.style.paddingRight = 20;
            _dialogRoot.style.paddingTop = 20;
            _dialogRoot.style.paddingBottom = 20;
            _dialogRoot.style.backgroundColor = new Color(40f / 255f, 40f / 255f, 40f / 255f, 1f);
            _dialogRoot.style.borderTopLeftRadius = 4;
            _dialogRoot.style.borderTopRightRadius = 4;
            _dialogRoot.style.borderBottomLeftRadius = 4;
            _dialogRoot.style.borderBottomRightRadius = 4;

            // 版本/作者
            _versionLabel = CreateInfoLabel();
            _authorLabel = CreateInfoLabel();

            // 链接按钮
            _authorLinkButton = CreateLinkButton();
            _authorLinkButton.clicked += () => Application.OpenURL(_aboutAuthorLink);

            _documentLinkButton = CreateLinkButton();
            _documentLinkButton.clicked += () => Application.OpenURL(_aboutDocumentLink);

            // 语言行
            var languageRow = new VisualElement();
            languageRow.style.flexDirection = FlexDirection.Row;
            languageRow.style.justifyContent = Justify.FlexStart;
            languageRow.style.alignItems = Align.Center;
            languageRow.style.height = 30;
            languageRow.style.marginTop = 20;
            languageRow.style.marginBottom = 10;
            languageRow.style.width = Length.Percent(100);

            _languageLabel = new Label();
            _languageLabel.style.flexGrow = 1;
            _languageLabel.style.flexShrink = 1;
            _languageLabel.style.minWidth = 0;
            _languageLabel.style.marginRight = 10;
            _languageLabel.style.height = 30;
            _languageLabel.style.unityTextAlign = TextAnchor.MiddleLeft;

            // 这个 spacer 用来占据中间剩余空间，把 Dropdown 顶到最右边。
            var languageSpacer = new VisualElement();
            languageSpacer.style.flexGrow = 1;
            languageSpacer.style.flexShrink = 1;
            languageSpacer.style.minWidth = 0;

            _languageDropdown = new DropdownField();
            _languageDropdown.style.flexGrow = 0;
            _languageDropdown.style.flexShrink = 0;
            _languageDropdown.style.width = LanguageDropdownWidth;
            _languageDropdown.style.minWidth = LanguageDropdownWidth;
            _languageDropdown.style.maxWidth = LanguageDropdownWidth;
            _languageDropdown.RegisterValueChangedCallback(evt =>
            {
                // DropdownField 的值本身就是字符串，这里用“是否中文”做统一出口。
                var chinese = evt.newValue == _languageOptionChinese;
                if (_isChinese == chinese)
                {
                    return;
                }

                _isChinese = chinese;
                _onLanguageChanged?.Invoke(_isChinese);
                ApplyLanguage(_isChinese);
            });

            languageRow.Add(_languageLabel);
            languageRow.Add(languageSpacer);
            languageRow.Add(_languageDropdown);

            // ScrollView：配置路径 + 工具说明
            var scroll = new ScrollView();
            scroll.style.flexGrow = 1;
            scroll.style.minHeight = 0;

            _configTitleLabel = CreateSectionTitleLabel();
            _configPathLabel = CreateToolTextLabel();
            _configPathLabel.style.whiteSpace = WhiteSpace.Normal;

            _toolsTitleLabel = CreateSectionTitleLabel();
            _toolIntroductionLabel = CreateToolTextLabel();
            _toolIntroductionLabel.style.whiteSpace = WhiteSpace.Normal;
            _toolIntroductionLabel.style.unityTextAlign = TextAnchor.UpperLeft;

            scroll.Add(_configTitleLabel);
            scroll.Add(_configPathLabel);
            scroll.Add(_toolsTitleLabel);
            scroll.Add(_toolIntroductionLabel);

            // 组装
            _dialogRoot.Add(_versionLabel);
            _dialogRoot.Add(_authorLabel);
            _dialogRoot.Add(_authorLinkButton);
            _dialogRoot.Add(_documentLinkButton);
            _dialogRoot.Add(languageRow);
            _dialogRoot.Add(scroll);

            _overlayRoot.Add(_dialogRoot);

            // 初始化文案
            ApplyLanguage(_isChinese);
        }

        /// <summary>
        /// 显示弹窗。
        /// </summary>
        public void Show()
        {
            _overlayRoot.style.display = DisplayStyle.Flex;
            _overlayRoot.Focus();
        }

        /// <summary>
        /// 隐藏弹窗。
        /// </summary>
        public void Hide()
        {
            _overlayRoot.style.display = DisplayStyle.None;
        }

        /// <summary>
        /// 外部可在加载配置后调用，以更新弹窗语言（不会触发 LanguageChanged 回调）。
        /// </summary>
        public void SetLanguageWithoutNotify(bool chinese)
        {
            _isChinese = chinese;
            ApplyLanguage(_isChinese);
        }

        private void OnOverlayMouseDown(MouseDownEvent evt)
        {
            // 如果点击到了弹窗外部，则关闭。
            var localPos = _dialogRoot.WorldToLocal(evt.mousePosition);
            if (!_dialogRoot.contentRect.Contains(localPos))
            {
                Hide();
                evt.StopPropagation();
            }
        }

        private void ApplyLanguage(bool chinese)
        {
            var configPath = _getConfigPath != null ? _getConfigPath() : string.Empty;

            if (chinese)
            {
                _versionLabel.text = string.Format(_getText(TextKey.SettingsVersion, true), _aboutVersion);
                _authorLabel.text = string.Format(_getText(TextKey.SettingsAuthor, true), _aboutAuthor);
                _authorLinkButton.text = _getText(TextKey.SettingsVisitAuthor, true);
                _documentLinkButton.text = _getText(TextKey.SettingsDocumentation, true);
                _languageLabel.text = _getText(TextKey.SettingsLanguage, true);
                _configTitleLabel.text = _getText(TextKey.SettingsConfig, true);
                _configPathLabel.text = string.Format(_getText(TextKey.SettingsStoragePath, true), configPath);
                _toolsTitleLabel.text = _getText(TextKey.SettingsTools, true);
                _toolIntroductionLabel.text = _toolIntroductionZh;
            }
            else
            {
                _versionLabel.text = string.Format(_getText(TextKey.SettingsVersion, false), _aboutVersion);
                _authorLabel.text = string.Format(_getText(TextKey.SettingsAuthor, false), _aboutAuthor);
                _authorLinkButton.text = _getText(TextKey.SettingsVisitAuthor, false);
                _documentLinkButton.text = _getText(TextKey.SettingsDocumentation, false);
                _languageLabel.text = _getText(TextKey.SettingsLanguage, false);
                _configTitleLabel.text = _getText(TextKey.SettingsConfig, false);
                _configPathLabel.text = string.Format(_getText(TextKey.SettingsStoragePath, false), configPath);
                _toolsTitleLabel.text = _getText(TextKey.SettingsTools, false);
                _toolIntroductionLabel.text = _toolIntroductionEn;
            }

            _languageOptionEnglish = _getText(TextKey.LanguageOptionEnglish, chinese);
            _languageOptionChinese = _getText(TextKey.LanguageOptionChinese, chinese);
            _languageDropdown.choices = new System.Collections.Generic.List<string> { _languageOptionEnglish, _languageOptionChinese };
            _languageDropdown.SetValueWithoutNotify(chinese ? _languageOptionChinese : _languageOptionEnglish);
        }

        private static string GetDefaultText(TextKey key, bool chinese)
        {
            if (chinese)
            {
                return key switch
                {
                    TextKey.SettingsVersion => "版本: {0}",
                    TextKey.SettingsAuthor => "作者: {0}",
                    TextKey.SettingsVisitAuthor => "访问作者主页",
                    TextKey.SettingsDocumentation => "文档",
                    TextKey.SettingsLanguage => "语言:",
                    TextKey.SettingsConfig => "配置",
                    TextKey.SettingsStoragePath => "存储路径: {0}",
                    TextKey.SettingsTools => "工具说明",
                    TextKey.LanguageOptionEnglish => "English",
                    TextKey.LanguageOptionChinese => "中文",
                    _ => string.Empty
                };
            }

            return key switch
            {
                TextKey.SettingsVersion => "Version: {0}",
                TextKey.SettingsAuthor => "Author: {0}",
                TextKey.SettingsVisitAuthor => "Visit Author's Homepage",
                TextKey.SettingsDocumentation => "Documentation",
                TextKey.SettingsLanguage => "Language:",
                TextKey.SettingsConfig => "Configuration",
                TextKey.SettingsStoragePath => "Storage: {0}",
                TextKey.SettingsTools => "About",
                TextKey.LanguageOptionEnglish => "English",
                TextKey.LanguageOptionChinese => "Chinese",
                _ => string.Empty
            };
        }

        private static Label CreateInfoLabel()
        {
            var label = new Label();
            label.style.height = 20;
            label.style.flexShrink = 0;
            label.style.flexGrow = 0;
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            label.style.marginBottom = 10;
            label.style.color = new Color(180f / 255f, 180f / 255f, 180f / 255f, 1f);
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.fontSize = 12;
            return label;
        }

        private static Label CreateSectionTitleLabel()
        {
            var label = new Label();
            label.style.marginTop = 20;
            label.style.marginBottom = 10;
            label.style.color = new Color(180f / 255f, 180f / 255f, 180f / 255f, 1f);
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.fontSize = 12;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            return label;
        }

        private static Label CreateToolTextLabel()
        {
            var label = new Label();
            label.style.color = new Color(180f / 255f, 180f / 255f, 180f / 255f, 1f);
            label.style.fontSize = 12;
            label.style.marginTop = 10;
            return label;
        }

        private static Button CreateLinkButton()
        {
            var button = new Button();

            // 默认样式
            var accent = new Color(198f / 255f, 255f / 255f, 0f / 255f, 1f);
            var dark = new Color(20f / 255f, 20f / 255f, 20f / 255f, 1f);

            button.style.height = 30;
            button.style.flexShrink = 0;
            button.style.marginTop = 5;
            button.style.marginBottom = 5;
            button.style.paddingLeft = 4;
            button.style.paddingRight = 4;
            button.style.paddingTop = 4;
            button.style.paddingBottom = 4;
            button.style.borderTopWidth = 1;
            button.style.borderRightWidth = 1;
            button.style.borderBottomWidth = 1;
            button.style.borderLeftWidth = 1;
            button.style.borderTopLeftRadius = 4;
            button.style.borderTopRightRadius = 4;
            button.style.borderBottomLeftRadius = 4;
            button.style.borderBottomRightRadius = 4;
            button.style.borderTopColor = accent;
            button.style.borderRightColor = accent;
            button.style.borderBottomColor = accent;
            button.style.borderLeftColor = accent;
            button.style.backgroundColor = new Color(0, 0, 0, 0);
            button.style.color = accent;

            // 简单 hover 效果（不用 USS 伪类）
            button.RegisterCallback<MouseEnterEvent>(_ =>
            {
                button.style.backgroundColor = accent;
                button.style.color = dark;
            });
            button.RegisterCallback<MouseLeaveEvent>(_ =>
            {
                button.style.backgroundColor = new Color(0, 0, 0, 0);
                button.style.color = accent;
            });

            return button;
        }
    }
}
#endif
