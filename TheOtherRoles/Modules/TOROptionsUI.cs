using System;
using System.Collections.Generic;
using System.Linq;
using TheOtherRoles.MetaContext;
using TMPro;
using UnityEngine;

namespace TheOtherRoles.Modules
{
    /// <summary>
    /// 三栏式「游戏设置」界面的视觉构建工具。
    /// 依据参考图提供的配色与信息架构：
    ///   顶部 —— 深色背景条 + 阵营头像图标 Tab（大类入口）；
    ///   左侧 —— 「游戏设置」标题 + 说明卡片 + 三个纵向分类按钮（游戏设置/游戏预设/职业设置）；
    ///   右侧 —— 按阵营分组的彩色横条标题 + 「角色数量 / 几率%」双数字调节行。
    /// 本类只负责 UI 树构建与着色，不改动任何 CustomOption 数据/存档/RPC 逻辑。
    /// </summary>
    public static class TOROptionsUI
    {
        // —— 参考图配色 ——
        public static readonly Color PanelDark = new Color(0.12f, 0.13f, 0.16f, 0.96f);      // 面板深灰/近黑
        public static readonly Color BarDark   = new Color(0.14f, 0.16f, 0.20f, 0.95f);      // 顶部栏深色背景
        public static readonly Color Cyan      = new Color(0.10f, 0.55f, 0.58f, 1f);         // 青色/蓝绿（游戏设置）
        public static readonly Color Mint      = new Color(0.55f, 0.82f, 0.76f, 1f);         // 薄荷色（职业设置·选中）
        public static readonly Color Blue      = new Color(0.16f, 0.42f, 0.60f, 1f);         // 蓝色横条（船员分组）
        public static readonly Color Red       = new Color(0.62f, 0.18f, 0.18f, 1f);         // 红色横条（伪装者分组）
        public static readonly Color Purple    = new Color(0.42f, 0.26f, 0.56f, 1f);         // 紫色（中立分组）
        public static readonly Color Grey      = new Color(0.42f, 0.44f, 0.48f, 1f);         // 灰色（副职业分组）
        public static readonly Color IdleIcon  = new Color(0.62f, 0.68f, 0.74f, 1f);         // 未选中图标
        public static readonly Color ActiveIcon= new Color(1f, 1f, 1f, 1f);                  // 选中图标

        private const int UILayer = 5; // == LayerMask.NameToLayer("UI")

        /// <summary>阵营类型 → 分组横条颜色（与图片右侧彩色分组一致）。</summary>
        public static Color GroupColor(CustomOption.CustomOptionType type)
        {
            switch (type)
            {
                case CustomOption.CustomOptionType.Impostor: return Red;
                case CustomOption.CustomOptionType.Crewmate: return Blue;
                case CustomOption.CustomOptionType.Neutral: return Purple;
                case CustomOption.CustomOptionType.Modifier: return Grey;
                case CustomOption.CustomOptionType.General: return Cyan;
                case CustomOption.CustomOptionType.Guesser: return Mint;
                default: return Cyan;
            }
        }

        /// <summary>阵营类型 → 顶部 Tab 图标资源路径。</summary>
        public static string TabIconResource(CustomOption.CustomOptionType type)
        {
            switch (type)
            {
                case CustomOption.CustomOptionType.Impostor: return "TheOtherRoles.Resources.TabIconImpostor.png";
                case CustomOption.CustomOptionType.Neutral:  return "TheOtherRoles.Resources.TabIconNeutral.png";
                case CustomOption.CustomOptionType.Crewmate: return "TheOtherRoles.Resources.TabIconCrewmate.png";
                case CustomOption.CustomOptionType.Modifier: return "TheOtherRoles.Resources.TabIconModifier.png";
                case CustomOption.CustomOptionType.Guesser:  return "TheOtherRoles.Resources.TabIcon.png";
                case CustomOption.CustomOptionType.HideNSeekMain:
                case CustomOption.CustomOptionType.HideNSeekRoles: return "TheOtherRoles.Resources.TabIconHideNSeekSettings.png";
                default: return "TheOtherRoles.Resources.TabIconClassicMode.png";
            }
        }

        /// <summary>
        /// 创建顶部大 Tab 栏：深色背景条 + 居中的阵营头像图标按钮。
        /// 返回创建的图标列表，供高亮更新复用。
        /// </summary>
        public static List<SpriteRenderer> BuildHeaderTabBar(GameSettingMenu menu, Transform parent,
            List<CustomOption.CustomOptionType> types, List<int> targetMenus, int selectedIndex)
        {
            var icons = new List<SpriteRenderer>();
            if (types == null || types.Count == 0) return icons;

            int uiLayer = LayerMask.NameToLayer("UI");

            var bar = Helpers.CreateObject("TORHeaderTabBar", parent, new Vector3(-2.2f, 2.55f, -2f), uiLayer);

            var bg = Helpers.CreateObject<SpriteRenderer>("HeaderBackground", bar.transform, new Vector3(0f, 0f, 0.2f), uiLayer);
            bg.sprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.Settings_Button.png", 175f)
                        ?? Helpers.loadSpriteFromResources("TheOtherRoles.Resources.Background_Frame.png", 175f);
            bg.transform.localScale = new Vector3(6.4f, 0.62f, 1f);
            bg.color = BarDark;
            bg.sortingOrder = 10;

            int count = types.Count;
            float startX = -(count - 1) * 0.7f / 2f;

            for (int i = 0; i < count; i++)
            {
                var type = types[i];
                var sprite = Helpers.loadSpriteFromResources(TabIconResource(type), 175f)
                             ?? Helpers.loadSpriteFromResources("TheOtherRoles.Resources.TabIcon.png", 175f);
                if (sprite == null) continue;

                float x = startX + i * 0.7f;
                var tab = Helpers.CreateObject("HeaderTab_" + type, bar.transform, new Vector3(x, 0f, -0.1f), uiLayer);

                var icon = Helpers.CreateObject<SpriteRenderer>("Icon", tab.transform, new Vector3(0f, 0f, -0.2f), uiLayer);
                icon.sprite = sprite;
                icon.transform.localScale = new Vector3(0.72f, 0.72f, 1f);
                icon.sortingOrder = 12;
                icon.color = i == selectedIndex ? ActiveIcon : IdleIcon;
                icons.Add(icon);

                var collider = tab.AddComponent<BoxCollider2D>();
                collider.size = new Vector2(0.62f, 0.56f);
                collider.isTrigger = true;

                var button = tab.SetUpButton(false, icon, IdleIcon, ActiveIcon);
                int targetMenu = targetMenus[i];
                button.OnClick.AddListener((System.Action)(() => menu.ChangeTab(targetMenu, false)));
            }

            return icons;
        }

        /// <summary>
        /// 创建左侧标题 + 说明卡片（白描边深底 + 举问号头像 + 说明文字）。
        /// </summary>
        public static void BuildLeftHeader(Transform parent, Vector3 position)
        {
            int uiLayer = LayerMask.NameToLayer("UI");

            var root = Helpers.CreateObject("TORLeftHeader", parent, position, uiLayer);

            // 标题「游戏设置」
            var title = Helpers.CreateObject<TextMeshPro>("Title", root.transform, new Vector3(0f, 0f, -0.2f), uiLayer);
            title.font = VanillaAsset.StandardTextPrefab.font;
            title.alignment = TextAlignmentOptions.Left;
            title.fontSize = 1.1f;
            title.fontStyle = FontStyles.Bold;
            title.color = Color.white;
            title.text = ModTranslation.getString("gameSettingsTab");
            title.sortingOrder = 12;

            // 说明卡片背景（圆角矩形，白色描边深底）
            var cardBg = Helpers.CreateObject<SpriteRenderer>("HintCard", root.transform, new Vector3(0f, -0.62f, 0.1f), uiLayer);
            cardBg.sprite = VanillaAsset.TextButtonSprite;
            cardBg.drawMode = SpriteDrawMode.Sliced;
            cardBg.tileMode = SpriteTileMode.Continuous;
            cardBg.size = new Vector2(2.9f, 0.48f);
            cardBg.color = PanelDark;
            cardBg.sortingOrder = 11;

            // 问号头像（用 BlankButton 或 TabIcon 兜底）
            var avatar = Helpers.CreateObject<SpriteRenderer>("HintAvatar", root.transform, new Vector3(-1.15f, -0.62f, -0.1f), uiLayer);
            avatar.sprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.TabIcon.png", 175f)
                            ?? Helpers.loadSpriteFromResources("TheOtherRoles.Resources.BlankButton.png", 175f);
            avatar.transform.localScale = new Vector3(0.34f, 0.34f, 1f);
            avatar.color = Red;
            avatar.sortingOrder = 12;

            var hint = Helpers.CreateObject<TextMeshPro>("HintText", root.transform, new Vector3(-0.15f, -0.62f, -0.2f), uiLayer);
            hint.font = VanillaAsset.StandardTextPrefab.font;
            hint.alignment = TextAlignmentOptions.Left;
            hint.fontSize = 0.62f;
            hint.color = Color.white;
            hint.text = ModTranslation.getString("editRoleSettingsHint");
            hint.sortingOrder = 12;
        }

        /// <summary>
        /// 在左侧面板创建一个「分类导航按钮」（纯色填充 + 白色居中文字）。
        /// 返回按钮，用于选中态高亮。
        /// </summary>
        public static PassiveButton BuildNavButton(Transform parent, Vector3 position, string textKey, Color fill,
            Action onClick, bool selected = false)
        {
            int uiLayer = LayerMask.NameToLayer("UI");

            var obj = Helpers.CreateObject("TORNav_" + textKey, parent, position, uiLayer);

            var bgSprite = Helpers.CreateObject<SpriteRenderer>("Fill", obj.transform, new Vector3(0f, 0f, 0.1f), uiLayer);
            bgSprite.sprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.Settings_Button.png", 175f)
                              ?? Helpers.loadSpriteFromResources("TheOtherRoles.Resources.Background_Frame.png", 175f);
            bgSprite.transform.localScale = new Vector3(2.9f, 0.46f, 1f);
            bgSprite.color = fill;
            bgSprite.sortingOrder = 10;

            var label = Helpers.CreateObject<TextMeshPro>("Label", obj.transform, new Vector3(0f, 0f, -0.1f), uiLayer);
            label.font = VanillaAsset.StandardTextPrefab.font;
            label.alignment = TextAlignmentOptions.Center;
            label.fontSize = 0.72f;
            label.fontStyle = FontStyles.Bold;
            label.color = Color.white;
            label.text = ModTranslation.getString(textKey);
            label.sortingOrder = 11;

            var button = obj.SetUpButton(false, bgSprite, IdleIcon, ActiveIcon);
            button.OnClick.AddListener((System.Action)(() => onClick?.Invoke()));
            return button;
        }

        /// <summary>
        /// 在右侧详情列表创建一个「分组标题横条」（彩色填充 + 左侧小头像 + 标题文字）。
        /// </summary>
        public static GameObject BuildGroupHeader(Transform parent, Vector3 position, Color fill, string titleText,
            string avatarResource = "TheOtherRoles.Resources.TabIcon.png")
        {
            int uiLayer = LayerMask.NameToLayer("UI");
            var root = Helpers.CreateObject("TORGroupHeader", parent, position, uiLayer);

            var bar = Helpers.CreateObject<SpriteRenderer>("Bar", root.transform, new Vector3(0f, 0f, 0.1f), uiLayer);
            bar.sprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.Settings_Button.png", 175f)
                         ?? Helpers.loadSpriteFromResources("TheOtherRoles.Resources.Background_Frame.png", 175f);
            bar.transform.localScale = new Vector3(4.4f, 0.34f, 1f);
            bar.color = fill;
            bar.sortingOrder = 10;

            var avatar = Helpers.CreateObject<SpriteRenderer>("Avatar", root.transform, new Vector3(-1.95f, 0f, -0.1f), uiLayer);
            avatar.sprite = Helpers.loadSpriteFromResources(avatarResource, 175f)
                            ?? Helpers.loadSpriteFromResources("TheOtherRoles.Resources.TabIcon.png", 175f);
            avatar.transform.localScale = new Vector3(0.26f, 0.26f, 1f);
            avatar.color = Color.white;
            avatar.sortingOrder = 11;

            var text = Helpers.CreateObject<TextMeshPro>("Title", root.transform, new Vector3(-0.2f, 0f, -0.1f), uiLayer);
            text.font = VanillaAsset.StandardTextPrefab.font;
            text.alignment = TextAlignmentOptions.Left;
            text.fontSize = 0.66f;
            text.fontStyle = FontStyles.Bold;
            text.color = Color.white;
            text.text = titleText;
            text.sortingOrder = 11;

            return root;
        }
    }
}
