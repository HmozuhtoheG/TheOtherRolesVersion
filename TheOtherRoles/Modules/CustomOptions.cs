using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AmongUs.Data;
using AmongUs.GameOptions;
using BepInEx.Configuration;
using HarmonyLib;
using Hazel;
using Il2CppSystem.Linq;
using Reactor.Utilities.Extensions;
using TheOtherRoles.MetaContext;
using TheOtherRoles.Modules;
using TheOtherRoles.Roles;
using TheOtherRoles.Utilities;
using TMPro;
using UnityEngine;
using static TheOtherRoles.CustomOption;

namespace TheOtherRoles {
    public class CustomOption {
        public enum CustomOptionType {
            General,
            Impostor,
            Neutral,
            Crewmate,
            Modifier,
            Guesser,
            HideNSeekMain,
            HideNSeekRoles,
            ZombieMain
        }

        public static List<CustomOption> options = new();
        public static int preset = 0;
        public static ConfigEntry<string> vanillaSettings;

        public int id;
        public string name;
        public string format;
        public System.Object[] selections;

        public int defaultSelection;
        public ConfigEntry<int> entry;
        public int selection;
        public OptionBehaviour optionBehaviour;
        public CustomOption parent;
        public bool isHeader;
        public CustomOptionType type;
        public Action onChange = null;
        public string heading = "";
        public bool invertedParent;
        public Color color;
        public List<CustomOption> children;

        // Option creation

        public CustomOption(int id, CustomOptionType type, string name,  System.Object[] selections, System.Object defaultValue, CustomOption parent, bool isHeader, string format, Color color, Action onChange = null, string heading = "", bool invertedParent = false) {
            this.id = id;
            this.name = parent == null ? name : "- " + name;
            this.format = format;
            this.selections = selections;
            int index = Array.IndexOf(selections, defaultValue);
            this.defaultSelection = index >= 0 ? index : 0;
            this.parent = parent;
            this.isHeader = isHeader;
            this.type = type;
            this.onChange = onChange;
            this.heading = heading;
            this.invertedParent = invertedParent;
            this.color = color;
            this.children = [];
            if (parent != null) parent.children.Add(this);
            selection = 0;
            if (id != 0) {
                try {
                    entry = TheOtherRolesPlugin.Instance.Config.Bind($"Preset{preset}", id.ToString(), defaultSelection);
                    selection = Mathf.Clamp(entry.Value, 0, selections.Length - 1);
                } catch {
                    selection = Mathf.Clamp(defaultSelection, 0, selections.Length - 1);
                }
            }
            options.Add(this);
        }

        public static CustomOption Create(int id, CustomOptionType type, string name, string[] selections, CustomOption parent = null, bool isHeader = false, string format = "", Action onChange = null, string heading = "", bool invertedParent = false, Color color = default) {
            return new CustomOption(id, type, name, selections, "", parent, isHeader, format, color == default ? Color.white : color, onChange, heading, invertedParent);
        }

        public static CustomOption Create(int id, CustomOptionType type, string name, float defaultValue, float min, float max, float step, CustomOption parent = null, bool isHeader = false, string format = "", Action onChange = null, string heading = "", bool invertedParent = false, Color color = default) {
            List<object> selections = new();
            for (float s = min; s <= max; s += step)
                selections.Add(s);
            return new CustomOption(id, type, name, selections.ToArray(), defaultValue, parent, isHeader, format, color == default ? color == default ? Color.white : color : color, onChange, heading, invertedParent);
        }

        public static CustomOption Create(int id, CustomOptionType type, string name, bool defaultValue, CustomOption parent = null, bool isHeader = false, string format = "", Action onChange = null, string heading = "", bool invertedParent = false, Color color = default) {
            return new CustomOption(id, type, name, new string[]{ "optionOff", "optionOn" }, defaultValue ? "optionOn" : "optionOff", parent, isHeader, format, Color.white, onChange, heading, invertedParent);
        }

        public static CustomOption Create(int id, CustomOptionType type, string name, List<RoleId> roleId, CustomOption parent = null, bool isHeader = false, Color color = default) {
            return new CustomOption(id, type, name, roleId.Select(x => x == RoleId.Jester ? "optionOff" : RoleInfo.allRoleInfos.FirstOrDefault(y => y.roleId == x
            && y.color != Palette.ImpostorRed && !y.isOrgNeutral).nameKey).ToArray(), 0, parent, isHeader, "", color == default ? Color.white : color);
        }

        // Static behaviour

        public static void switchPreset(int newPreset) {
            saveVanillaOptions();
            CustomOption.preset = newPreset;
            vanillaSettings = TheOtherRolesPlugin.Instance.Config.Bind($"Preset{preset}", "GameOptions", "");
            loadVanillaOptions();
            foreach (CustomOption option in CustomOption.options) {
                if (option.id == 0) continue;

                try {
                    option.entry = TheOtherRolesPlugin.Instance.Config.Bind($"Preset{preset}", option.id.ToString(), option.defaultSelection);
                    option.selection = Mathf.Clamp(option.entry.Value, 0, option.selections.Length - 1);
                } catch {
                    option.selection = Mathf.Clamp(option.defaultSelection, 0, option.selections.Length - 1);
                }
                if (option.optionBehaviour != null && option.optionBehaviour is StringOption stringOption) {
                    stringOption.oldValue = stringOption.Value = option.selection;
                    stringOption.ValueText.text = option.getString();
                }
            }

            // make sure to reload all tabs, even the ones in the background, because they might have changed when the preset was switched!
            if (AmongUsClient.Instance?.AmHost == true)
            {
                GameOptionsMenuStartPatch.MarkAllTabsDirty();
                GameOptionsMenuStartPatch.RebuildActiveTabIfDirty();
            }
        }

        public static void saveVanillaOptions() {
            vanillaSettings.Value = Convert.ToBase64String(GameOptionsManager.Instance.gameOptionsFactory.ToBytes(GameManager.Instance.LogicOptions.currentGameOptions, false));
        }

        public static bool loadVanillaOptions() {
            string optionsString = vanillaSettings.Value;
            if (optionsString == "") return false;
            IGameOptions gameOptions = GameOptionsManager.Instance.gameOptionsFactory.FromBytes(Convert.FromBase64String(optionsString));
            if (gameOptions.Version < 8)
            {
                TheOtherRolesPlugin.Logger.LogMessage("tried to paste old settings, not doing this!");
                return false;
            }
            GameOptionsManager.Instance.GameHostOptions = gameOptions;
            GameManager.Instance.LogicOptions.SetGameOptions(gameOptions);
            GameOptionsManager.Instance.CurrentGameOptions = gameOptions;
            GameManager.Instance.LogicOptions.SyncOptions();
            return true;
        }

        public static void ShareOptionChange(uint optionId) {
            var option = options.FirstOrDefault(x => x.id == optionId);
            if (option == null) return;
            var writer = AmongUsClient.Instance!.StartRpcImmediately(PlayerControl.LocalPlayer.NetId, (byte)CustomRPC.ShareOptions, SendOption.Reliable, -1);
            writer.Write((byte)1);
            writer.WritePacked((uint)option.id);
            writer.WritePacked(Convert.ToUInt32(option.selection));
            AmongUsClient.Instance.FinishRpcImmediately(writer);
        }

        public static void ShareOptionSelections() {
            if (PlayerControl.AllPlayerControls.Count <= 1 || AmongUsClient.Instance!.AmHost == false && PlayerControl.LocalPlayer == null) return;
            var optionsList = new List<CustomOption>(CustomOption.options);
            while (optionsList.Any())
            {
                byte amount = (byte) Math.Min(optionsList.Count, 200); // takes less than 3 bytes per option on average
                var writer = AmongUsClient.Instance!.StartRpcImmediately(PlayerControl.LocalPlayer.NetId, (byte)CustomRPC.ShareOptions, SendOption.Reliable, -1);
                writer.Write(amount);
                for (int i = 0; i < amount; i++)
                {
                    var option = optionsList[0];
                    optionsList.RemoveAt(0);
                    writer.WritePacked((uint) option.id);
                    writer.WritePacked(Convert.ToUInt32(option.selection));
                }
                AmongUsClient.Instance.FinishRpcImmediately(writer);
            }
        }

        public static bool ShouldBeEnabled(CustomOption option)
        {
            bool enabled = true;
            var parent = option.parent;
            while (parent != null && enabled)
            {
                enabled = parent.selection != 0 || parent.invertedParent;
                parent = parent.parent;
            }
            return enabled;
        }

        // Getter

        public int getSelection() {
            return selection;
        }

        public bool getBool() {
            return selection > 0;
        }

        public float getFloat() {
            return (float)selections[selection];
        }

        public int getQuantity() {
            return selection + 1;
        }

        public string getString()
        {
            string sel = selections[selection].ToString();
            if (format != "")
            {
                string unitFormat = ModTranslation.getString(format, tryFind: true) ?? "{0}";
                return string.Format(unitFormat, sel);
            }

            if (sel is "optionOn"  or "deputyOnImmediately" or "deputyOnAfterMeeting" or "mayorOnUntilMeeting" or "mayorOnBeforeVoting")
            {
                return "<color=#FFFF00FF>" + ModTranslation.getString(sel) + "</color>";
            }
            else if (sel == "optionOff")
            {
                return "<color=#CCCCCCFF>" + ModTranslation.getString(sel) + "</color>";
            }

            return ModTranslation.getString(sel);
        }

        public string getName()
        {
            return ModTranslation.getString(name);
        }

        public string getHeading()
        {
            if (heading == "") return "";
            return ModTranslation.getString(heading);
        }

        public Color getColor()
        {
            return color;
        }

        // Option changes

        public void updateSelection(int newSelection, bool notifyUsers = true) {
            newSelection = Mathf.Clamp((newSelection + selections.Length) % selections.Length, 0, selections.Length - 1);
            bool doNeedNotifier = AmongUsClient.Instance?.AmClient == true && notifyUsers && selection != newSelection;
            if (doNeedNotifier)
            {
                try
                {
                    selection = newSelection;
                    if (GameStartManager.Instance != null && GameStartManager.Instance.LobbyInfoPane != null && GameStartManager.Instance.LobbyInfoPane.LobbyViewSettingsPane != null && GameStartManager.Instance.LobbyInfoPane.LobbyViewSettingsPane.gameObject.activeSelf)
                    {
                        LobbyViewSettingsPaneChangeTabPatch.Postfix(GameStartManager.Instance.LobbyInfoPane.LobbyViewSettingsPane, GameStartManager.Instance.LobbyInfoPane.LobbyViewSettingsPane.currentTab);
                    }
                    HelpMenu.OnUpdateOptions();
                }
                catch { }
            }
            selection = newSelection;
            try {
                if (onChange != null) onChange();
            } catch { }
            if (doNeedNotifier) {
                CustomOption originalParent = parent;
                if (originalParent != null) {
                    while (originalParent.parent != null)
                        originalParent = originalParent.parent;
                }
                DestroyableSingleton<HudManager>.Instance.Notifier.AddModSettingsChangeMessage((StringNames)(this.id + 6000), getString(),
                    (originalParent != null ? originalParent.getName().Replace("- ", "") + ": " : "") + getName().Replace("- ", ""), false);
            }
            if (optionBehaviour != null && optionBehaviour is StringOption stringOption) {
                stringOption.oldValue = stringOption.Value = selection;
                stringOption.ValueText.text = getString();

                if (AmongUsClient.Instance?.AmHost == true && PlayerControl.LocalPlayer) {
                    if (id == 0 && selection != preset) {
                        switchPreset(selection); // Switch presets
                        ShareOptionSelections();
                    } else if (entry != null) {
                        entry.Value = selection; // Save selection to config
                        TheOtherRoles.clearAndReloadRoles();
                        ShareOptionChange((uint)id);// Share single selection
                    }
                }
            } else if (id == 0 && AmongUsClient.Instance?.AmHost == true && PlayerControl.LocalPlayer) {  // Share the preset switch for random maps, even if the menu isnt open!
                switchPreset(selection);
                ShareOptionSelections();// Share all selections
            }

            if (AmongUsClient.Instance?.AmHost == true)
            {
                var currentTab = GameOptionsMenuStartPatch.currentTabs.FirstOrDefault(x => x.active).GetComponent<GameOptionsMenu>();
                if (currentTab != null)
                {
                    var optionType = options.First(x => x.optionBehaviour == currentTab.Children[0]).type;
                    GameOptionsMenuStartPatch.updateGameOptionsMenu(optionType, currentTab);
                }

            }
        }

        public static byte[] serializeOptions() {
            using (MemoryStream memoryStream = new()) {
                using (BinaryWriter binaryWriter = new(memoryStream)) {
                    int lastId = -1;
                    foreach (var option in CustomOption.options.OrderBy(x => x.id)) {
                        if (option.id == 0) continue;
                        bool consecutive = lastId + 1 == option.id;
                        lastId = option.id;

                        binaryWriter.Write((byte)(option.selection + (consecutive ? 128 : 0)));
                        if (!consecutive) binaryWriter.Write((ushort)option.id);
                    }
                    binaryWriter.Flush();
                    memoryStream.Position = 0L;
                    return memoryStream.ToArray();
                }
            }
        }

        public static int deserializeOptions(byte[] inputValues) {
            BinaryReader reader = new(new MemoryStream(inputValues));
            int lastId = -1;
            bool somethingApplied = false;
            int errors = 0;
            while (reader.BaseStream.Position < inputValues.Length) {
                try {
                    int selection = reader.ReadByte();
                    int id = -1;
                    bool consecutive = selection >= 128;
                    if (consecutive) {
                        selection -= 128;
                        id = lastId + 1;
                    } else {
                        id = reader.ReadUInt16();
                    }
                    if (id == 0) continue;
                    lastId = id;
                    CustomOption option = options.First(option => option.id == id);
                    try {
                        option.entry = TheOtherRolesPlugin.Instance.Config.Bind($"Preset{preset}", option.id.ToString(), option.defaultSelection);
                    } catch { }
                    option.selection = selection;
                    if (option.optionBehaviour != null && option.optionBehaviour is StringOption stringOption)
                    {
                        stringOption.oldValue = stringOption.Value = option.selection;
                        stringOption.ValueText.text = option.getString();
                    }
                    somethingApplied = true;
                } catch (Exception e) {
                    TheOtherRolesPlugin.Logger.LogWarning($"id:{lastId}:{e}: while deserializing - tried to paste invalid settings!");
                    errors++;
                }
            }
            return Convert.ToInt32(somethingApplied) + (errors > 0 ? 0 : 1);
        }

        // Copy to or paste from clipboard (as string)
        public static void copyToClipboard() {
            GUIUtility.systemCopyBuffer = $"{TheOtherRolesPlugin.VersionString}!{Convert.ToBase64String(serializeOptions())}!{vanillaSettings.Value}";
        }

        public static int pasteFromClipboard() {
            string allSettings = GUIUtility.systemCopyBuffer;
            int torOptionsFine = 0;
            bool vanillaOptionsFine = false;
            try {
                var settingsSplit = allSettings.Split("!");
                Version versionInfo = Version.Parse(settingsSplit[0]);
                string torSettings = settingsSplit[1];
                string vanillaSettingsSub = settingsSplit[2];
                torOptionsFine = deserializeOptions(Convert.FromBase64String(torSettings));

                try
                {
                    if (AmongUsClient.Instance?.AmHost == true)
                    {
                        GameOptionsMenuStartPatch.MarkAllTabsDirty();
                        GameOptionsMenuStartPatch.RebuildActiveTabIfDirty();
                    }
                }
                catch
                { }

                ShareOptionSelections();
                if (TheOtherRolesPlugin.Version > versionInfo && versionInfo < Version.Parse("1.2.7"))
                {
                    vanillaOptionsFine = false;
                    FastDestroyableSingleton<HudManager>.Instance.Chat.AddChat(PlayerControl.LocalPlayer, "Host Info: Pasting vanilla settings failed, TOR Options applied!");
                }
                else
                {
                    vanillaSettings.Value = vanillaSettingsSub;
                    vanillaOptionsFine = loadVanillaOptions();
                }
            } catch (Exception e) {
                TheOtherRolesPlugin.Logger.LogWarning($"{e}: tried to paste invalid settings!\n{allSettings}");
                string errorStr = allSettings.Length > 2 ? allSettings.Substring(0, 3) : "(empty clipboard) ";
                FastDestroyableSingleton<HudManager>.Instance.Chat.AddChat(PlayerControl.LocalPlayer, $"Host Info: You tried to paste invalid settings: \"{errorStr}...\"");
                SoundEffectsManager.play("fail");
            }
            return Convert.ToInt32(vanillaOptionsFine) + torOptionsFine;
        }
    }

    public class CustomRoleOption : CustomOption
    {
        public CustomOption countOption = null;
        public bool roleEnabled = true;

        public bool enabled
        {
            get
            {
                return roleEnabled && selection > 0;
            }
        }

        public int rate
        {
            get
            {
                return enabled ? selection : 0;
            }
        }

        public int count
        {
            get
            {
                if (!enabled)
                    return 0;

                if (countOption != null)
                    return Mathf.RoundToInt(countOption.getFloat());

                return 1;
            }
        }

        public (int, int) data
        {
            get
            {
                return (rate, count);
            }
        }

        public CustomRoleOption(int id, CustomOptionType type, string name, Color color, int max = 24, bool roleEnabled = true) :
            base(id, type, Helpers.cs(color, name), CustomOptionHolder.rates, "", null, true, "", color)
        {
            this.roleEnabled = roleEnabled;

            if (max <= 0 || !roleEnabled) {
                this.roleEnabled = false;
            }

            if (max > 1)
                countOption = Create(id + 20000, type, "roleNumAssigned", 1f, 1f, max, 1f, this, false, "unitPlayers");
        }
    }

    [HarmonyPatch(typeof(GameSettingMenu), nameof(GameSettingMenu.ChangeTab))]
    class GameOptionsMenuChangeTabPatch
    {
        public static void Postfix(GameSettingMenu __instance, int tabNum, bool previewOnly)
        {
            if (previewOnly) return;
            foreach (var tab in GameOptionsMenuStartPatch.currentTabs)
            {
                if (tab != null)
                    tab.SetActive(false);
            }
            if (tabNum > 2)
            {
                tabNum -= 3;
                GameOptionsMenuStartPatch.activeTabIndex = tabNum;
                GameOptionsMenuStartPatch.RebuildTabIfDirty(tabNum);
                if (tabNum < GameOptionsMenuStartPatch.currentTabs.Count)
                    GameOptionsMenuStartPatch.currentTabs[tabNum].SetActive(true);
                // 进入 TOR 设置页（阵营/General）→ 显示顶部标签栏
                if (GameOptionsMenuStartPatch.headerTabBarObject != null)
                    GameOptionsMenuStartPatch.headerTabBarObject.SetActive(true);
            }
            else
            {
                GameOptionsMenuStartPatch.activeTabIndex = -1;
                // 切回原版（游戏设置：地图/人数/模式）→ 隐藏顶部标签栏
                if (GameOptionsMenuStartPatch.headerTabBarObject != null)
                    GameOptionsMenuStartPatch.headerTabBarObject.SetActive(false);
            }
            // 同步顶部标签栏高亮
            GameOptionsMenuStartPatch.UpdateHeaderTabHighlight(GameOptionsMenuStartPatch.activeTabIndex);
        }
    }

    [HarmonyPatch(typeof(LobbyViewSettingsPane), nameof(LobbyViewSettingsPane.SetTab))]
    class LobbyViewSettingsPaneRefreshTabPatch
    {
        public static bool Prefix(LobbyViewSettingsPane __instance)
        {
            if ((int)__instance.currentTab < 15)
            {
                LobbyViewSettingsPaneChangeTabPatch.Postfix(__instance, __instance.currentTab);
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(LobbyViewSettingsPane), nameof(LobbyViewSettingsPane.ChangeTab))]
    class LobbyViewSettingsPaneChangeTabPatch
    {
        public static void Postfix(LobbyViewSettingsPane __instance, StringNames category)
        {
            int tabNum = (int)category;

            foreach (var pbutton in LobbyViewSettingsPatch.currentButtons)
            {
                pbutton.SelectButton(false);
            }
            if (tabNum > 20) // StringNames are in the range of 3000+ 
                return;
            __instance.taskTabButton.SelectButton(false);

            if (tabNum > 2)
            {
                tabNum -= 3;
                //GameOptionsMenuStartPatch.currentTabs[tabNum].SetActive(true);
                LobbyViewSettingsPatch.currentButtons[tabNum].SelectButton(true);
                LobbyViewSettingsPatch.drawTab(__instance, LobbyViewSettingsPatch.currentButtonTypes[tabNum]);
            }
        }
    }

    [HarmonyPatch(typeof(LobbyViewSettingsPane), nameof(LobbyViewSettingsPane.Update))]
    class LobbyViewSettingsPaneUpdatePatch
    {
        public static void Postfix(LobbyViewSettingsPane __instance)
        {
            if (LobbyViewSettingsPatch.currentButtons.Count == 0)
            {
                LobbyViewSettingsPatch.gameModeChangedFlag = true;
                LobbyViewSettingsPatch.Postfix(__instance);

            }
        }
    }

    [HarmonyPatch(typeof(LobbyViewSettingsPane), nameof(LobbyViewSettingsPane.Awake))]
    class LobbyViewSettingsPatch
    {
        public static List<PassiveButton> currentButtons = new();
        public static List<int> currentButtonTargets = new();
        public static List<CustomOptionType> currentButtonTypes = new();
        public static bool gameModeChangedFlag = false;
        private const float RowSlideOffset = 0.18f;

        public static void createCustomButton(LobbyViewSettingsPane __instance, int targetMenu, string buttonName, string buttonText, CustomOptionType optionType)
        {
            buttonName = "View" + buttonName;
            var buttonTemplate = GameObject.Find("OverviewTab");
            var torSettingsButton = GameObject.Find(buttonName);
            if (torSettingsButton == null)
            {
                torSettingsButton = GameObject.Instantiate(buttonTemplate, buttonTemplate.transform.parent);
                torSettingsButton.transform.localPosition += Vector3.right * 1.75f * (targetMenu - 2);
                torSettingsButton.name = buttonName;
                __instance.StartCoroutine(Effects.Lerp(2f, new Action<float>(p => { torSettingsButton.transform.FindChild("FontPlacer").GetComponentInChildren<TextMeshPro>().text = buttonText; })));
                var torSettingsPassiveButton = torSettingsButton.GetComponent<PassiveButton>();
                torSettingsPassiveButton.OnClick.RemoveAllListeners();
                torSettingsPassiveButton.OnClick.AddListener((System.Action)(() => {
                    __instance.ChangeTab((StringNames)targetMenu);
                }));
                torSettingsPassiveButton.OnMouseOut.RemoveAllListeners();
                torSettingsPassiveButton.OnMouseOver.RemoveAllListeners();
                torSettingsPassiveButton.SelectButton(false);
                currentButtons.Add(torSettingsPassiveButton);
                currentButtonTypes.Add(optionType);
            }
        }

        public static void Postfix(LobbyViewSettingsPane __instance)
        {
            currentButtons.ForEach(x => { if (x != null) x?.Destroy(); });
            currentButtons.Clear();
            currentButtonTypes.Clear();

            removeVanillaTabs(__instance);

            createSettingTabs(__instance);

        }

        public static void removeVanillaTabs(LobbyViewSettingsPane __instance)
        {
            GameObject.Find("RolesTabs")?.Destroy();
            var overview = GameObject.Find("OverviewTab");
            if (!gameModeChangedFlag)
            {
                overview.transform.localScale = new Vector3(0.5f * overview.transform.localScale.x, overview.transform.localScale.y, overview.transform.localScale.z);
                overview.transform.localPosition += new Vector3(-1.2f, 0f, 0f);

            }
            overview.transform.Find("FontPlacer").transform.localScale = new Vector3(1.35f, 1f, 1f);
            overview.transform.Find("FontPlacer").transform.localPosition = new Vector3(-0.6f, -0.1f, 0f);
            gameModeChangedFlag = false;
        }

        public static void drawTab(LobbyViewSettingsPane __instance, CustomOptionType optionType)
        {

            var relevantOptions = options.Where(x => x.type == optionType || x.type == CustomOption.CustomOptionType.Guesser && optionType == CustomOptionType.General).ToList();

            if ((int)optionType == 99)
            {
                // Create 4 Groups with Role settings only
                relevantOptions.Clear();
                relevantOptions.AddRange(options.Where(x => x.type == CustomOptionType.Impostor && x.isHeader));
                relevantOptions.AddRange(options.Where(x => x.type == CustomOptionType.Neutral && x.isHeader));
                relevantOptions.AddRange(options.Where(x => x.type == CustomOptionType.Crewmate && x.isHeader));
                relevantOptions.AddRange(options.Where(x => x.type == CustomOptionType.Modifier && x.isHeader));
                foreach (var option in options)
                {
                    if (option.parent != null && option.parent.getSelection() > 0)
                    {
                        if (option.id == 103) //Deputy
                            relevantOptions.Insert(relevantOptions.IndexOf(CustomOptionHolder.sheriffSpawnRate) + 1, option);
                        else if (option.id == 224) //Sidekick
                            relevantOptions.Insert(relevantOptions.IndexOf(CustomOptionHolder.jackalSpawnRate) + 1, option);
                        else if (option.id == 918) // Immoralist
                            relevantOptions.Insert(relevantOptions.IndexOf(CustomOptionHolder.foxSpawnRate) + 1, option);
                        else if (option.id == 8000) //Prosecutor
                            relevantOptions.Insert(relevantOptions.IndexOf(CustomOptionHolder.evilHackerSpawnRate) + 1, option);
                    }
                }
            }

            if (TORMapOptions.gameMode == CustomGamemodes.Guesser) // Exclude guesser options in neutral mode
                relevantOptions = [.. relevantOptions.Where(x => !new List<int> { 310, 311, 312, 313, 314, 315, 316, 317, 318, 319, 7006 }.Contains(x.id))];
            else
                relevantOptions = relevantOptions.Where(x => x.id != 7007).ToList();

            if (TORMapOptions.gameMode != CustomGamemodes.FreePlay)
                relevantOptions = relevantOptions.Where(x => x.id != 10429).ToList();

            for (int j = 0; j < __instance.settingsInfo.Count; j++)
            {
                __instance.settingsInfo[j].gameObject.Destroy();
            }
            __instance.settingsInfo.Clear();

            float num = 1.44f;
            int i = 0;
            int singles = 1;
            int headers = 0;
            int lines = 0;
            var curType = CustomOptionType.Modifier;
            int numBonus = 0;
            var animatedRows = new List<(GameObject Obj, Vector3 TargetPos)>();

            foreach (var option in relevantOptions)
            {
                if (option.isHeader && (int)optionType != 99 || (int)optionType == 99 && curType != option.type)
                {
                    curType = option.type;
                    if (i != 0) {
                        num -= 0.85f;
                        numBonus++;
                    }
                    if (i % 2 != 0) singles++;
                    headers++; // for header
                    CategoryHeaderMasked categoryHeaderMasked = UnityEngine.Object.Instantiate<CategoryHeaderMasked>(__instance.categoryHeaderOrigin);
                    categoryHeaderMasked.SetHeader(StringNames.ImpostorsCategory, 61);
                    string titleText = option.heading != "" ? option.getHeading() : option.getName();
                    categoryHeaderMasked.Title.text = titleText;
                    if ((int)optionType == 99)
                        categoryHeaderMasked.Title.text = new Dictionary<CustomOptionType, string>() { { CustomOptionType.Impostor, ModTranslation.getString("impostorRoles") }, { CustomOptionType.Neutral, ModTranslation.getString("neutralRoles") },
                            { CustomOptionType.Crewmate, ModTranslation.getString("crewmateRoles") }, { CustomOptionType.Modifier, ModTranslation.getString("modifiers") } }[curType];
                    categoryHeaderMasked.transform.SetParent(__instance.settingsContainer);
                    var headerTargetPos = new Vector3(-9.77f, num, -2f);
                    categoryHeaderMasked.transform.localScale = Vector3.zero;
                    categoryHeaderMasked.transform.localPosition = headerTargetPos + new Vector3(0f, -RowSlideOffset, 0f);
                    __instance.settingsInfo.Add(categoryHeaderMasked.gameObject);
                    animatedRows.Add((categoryHeaderMasked.gameObject, headerTargetPos));
                    if ((int)optionType != 99) {
                        categoryHeaderMasked.transform.GetChild(0).GetComponent<SpriteRenderer>().color = option.getColor();
                        categoryHeaderMasked.transform.GetChild(1).GetComponent<SpriteRenderer>().color = option.getColor();
                    }
                    num -= 1.05f;
                    i = 0;
                }
                else if (!ShouldBeEnabled(option)) continue;  // Hides options, for which the parent is disabled!
                if (option == CustomOptionHolder.crewmateRolesCountMax || option == CustomOptionHolder.neutralRolesCountMax || option == CustomOptionHolder.impostorRolesCountMax || option == CustomOptionHolder.modifiersCountMax || option == CustomOptionHolder.crewmateRolesFill)
                    continue;

                ViewSettingsInfoPanel viewSettingsInfoPanel = UnityEngine.Object.Instantiate<ViewSettingsInfoPanel>(__instance.infoPanelOrigin);
                viewSettingsInfoPanel.transform.SetParent(__instance.settingsContainer);
                float num2;
                if (i % 2 == 0) {
                    lines++;
                    num2 = -8.95f;
                    if (i > 0) {
                        num -= 0.85f;
                    }
                }
                else {
                    num2 = -3f;
                }
                var rowTargetPos = new Vector3(num2, num, -2f);
                viewSettingsInfoPanel.transform.localScale = Vector3.zero;
                viewSettingsInfoPanel.transform.localPosition = rowTargetPos + new Vector3(0f, -RowSlideOffset, 0f);
                int value = option.getSelection();
                var settingTuple = handleSpecialOptionsView(option, option.getName(), option.getString());
                viewSettingsInfoPanel.SetInfo(StringNames.ImpostorsCategory, settingTuple.Item2, 61);
                viewSettingsInfoPanel.titleText.text = settingTuple.Item1;
                if (option.isHeader && (int)optionType != 99 && option.heading == "" && (option.type == CustomOptionType.Neutral || option.type == CustomOptionType.Crewmate || option.type == CustomOptionType.Impostor || option.type == CustomOptionType.Modifier)) {
                    viewSettingsInfoPanel.titleText.text = ModTranslation.getString("optionSpawnChance");
                }
                if ((int)optionType == 99) {
                    if (option.type == CustomOptionType.Modifier)
                        viewSettingsInfoPanel.settingText.text = viewSettingsInfoPanel.settingText.text + GameOptionsDataPatch.buildModifierExtras(option);
                    if (option is CustomRoleOption roleOption)
                        viewSettingsInfoPanel.settingText.text += roleOption.enabled && roleOption.countOption != null ? $" ({roleOption.count})" : "";
                }
                __instance.settingsInfo.Add(viewSettingsInfoPanel.gameObject);
                animatedRows.Add((viewSettingsInfoPanel.gameObject, rowTargetPos));

                i++;
            }
            float actual_spacing = (headers * 1.05f + lines * 0.85f) / (headers + lines) * 1.01f;
            __instance.scrollBar.CalculateAndSetYBounds(__instance.settingsInfo.Count + singles * 2 + headers, 2f, 6f, actual_spacing);

            if (animatedRows.Count > 0)
            {
                int total = animatedRows.Count;
                const float duration = 0.32f;
                const float staggerSpan = 0.6f;
                __instance.StartCoroutine(Effects.Lerp(duration, new Action<float>(p =>
                {
                    for (int k = 0; k < total; k++)
                    {
                        var (row, targetPos) = animatedRows[k];
                        if (row == null) continue;
                        float rowStart = staggerSpan * k / Mathf.Max(1, total - 1);
                        float rowP = Mathf.Clamp01((p - rowStart) / (1f - staggerSpan));
                        float eased = 1f - Mathf.Pow(1f - rowP, 3f);
                        row.transform.localScale = Vector3.one * eased;
                        row.transform.localPosition = targetPos + new Vector3(0f, -RowSlideOffset * (1f - eased), 0f);
                    }
                })));
            }
        }
        private static Tuple<string, string> handleSpecialOptionsView(CustomOption option, string defaultString, string defaultVal)
        {
            string name = defaultString;
            string val = defaultVal;
            if (option == CustomOptionHolder.crewmateRolesCountMin)
            {
                val = "";
                name = ModTranslation.getString("crewmateRoles");
                var min = CustomOptionHolder.crewmateRolesCountMin.getSelection();
                var max = CustomOptionHolder.crewmateRolesCountMax.getSelection();
                if (CustomOptionHolder.crewmateRolesFill.getBool())
                {
                    var crewCount = PlayerControl.AllPlayerControls.Count - GameOptionsManager.Instance.currentGameOptions.NumImpostors;
                    int minNeutral = CustomOptionHolder.neutralRolesCountMin.getSelection();
                    int maxNeutral = CustomOptionHolder.neutralRolesCountMax.getSelection();
                    if (minNeutral > maxNeutral) minNeutral = maxNeutral;
                    min = crewCount - maxNeutral;
                    max = crewCount - minNeutral;
                    if (min < 0) min = 0;
                    if (max < 0) max = 0;
                    val = ModTranslation.getString("crewmateFill");
                }
                if (min > max) min = max;
                val += (min == max) ? $"{max}" : $"{min} - {max}";
            }
            if (option == CustomOptionHolder.neutralRolesCountMin)
            {
                name = ModTranslation.getString("neutralRoles");
                var min = CustomOptionHolder.neutralRolesCountMin.getSelection();
                var max = CustomOptionHolder.neutralRolesCountMax.getSelection();
                if (min > max) min = max;
                val = (min == max) ? $"{max}" : $"{min} - {max}";
            }
            if (option == CustomOptionHolder.impostorRolesCountMin)
            {
                name = ModTranslation.getString("impostorRoles");
                var min = CustomOptionHolder.impostorRolesCountMin.getSelection();
                var max = CustomOptionHolder.impostorRolesCountMax.getSelection();
                if (max > GameOptionsManager.Instance.currentGameOptions.NumImpostors) max = GameOptionsManager.Instance.currentGameOptions.NumImpostors;
                if (min > max) min = max;
                val = (min == max) ? $"{max}" : $"{min} - {max}";
            }
            if (option == CustomOptionHolder.modifiersCountMin)
            {
                name = ModTranslation.getString("modifiers");
                var min = CustomOptionHolder.modifiersCountMin.getSelection();
                var max = CustomOptionHolder.modifiersCountMax.getSelection();
                if (min > max) min = max;
                val = (min == max) ? $"{max}" : $"{min} - {max}";
            }
            return new(name, val);
        }

        public static void createSettingTabs(LobbyViewSettingsPane __instance)
        {
            // Handle different gamemodes and tabs needed therein.
            int next = 3;
            if (TORMapOptions.gameMode == CustomGamemodes.Guesser || TORMapOptions.gameMode == CustomGamemodes.Classic || TORMapOptions.gameMode == CustomGamemodes.FreePlay)
            {

                // create TOR settings
                createCustomButton(__instance, next++, "TORSettings", ModTranslation.getString("torNewSettings"), CustomOptionType.General);
                // create TOR settings
                createCustomButton(__instance, next++, "RoleOverview", ModTranslation.getString("roleOverview"), (CustomOptionType)99);
                // IMp
                createCustomButton(__instance, next++, "ImpostorSettings", ModTranslation.getString("impostorRoles"), CustomOptionType.Impostor);

                // Neutral
                createCustomButton(__instance, next++, "NeutralSettings", ModTranslation.getString("neutralRoles"), CustomOptionType.Neutral);
                // Crew
                createCustomButton(__instance, next++, "CrewmateSettings", ModTranslation.getString("crewmateRoles"), CustomOptionType.Crewmate);
                // Modifier
                createCustomButton(__instance, next++, "ModifierSettings", ModTranslation.getString("modifiers"), CustomOptionType.Modifier);

            }
            else if (TORMapOptions.gameMode == CustomGamemodes.HideNSeek)
            {
                // create Main HNS settings
                createCustomButton(__instance, next++, "HideNSeekMain", ModTranslation.getString("hideNSeekMain"), CustomOptionType.HideNSeekMain);
                // create HNS Role settings
                createCustomButton(__instance, next++, "HideNSeekRoles", ModTranslation.getString("hideNSeekRoles"), CustomOptionType.HideNSeekRoles);
            }
            else if (TORMapOptions.gameMode == CustomGamemodes.Zombie)
            {
                // create Zombie settings
                createCustomButton(__instance, next++, "ZombieMain", ModTranslation.getString("zombieMain"), CustomOptionType.ZombieMain);
            }
        }
    }

    [HarmonyPatch(typeof(GameOptionsMenu), nameof(GameOptionsMenu.CreateSettings))]
    class GameOptionsMenuCreateSettingsPatch
    {
        public static void Postfix(GameOptionsMenu __instance)
        {
            if (__instance.gameObject.name == "GAME SETTINGS TAB")
                adaptTaskCount(__instance);
        }

        private static void adaptTaskCount(GameOptionsMenu __instance)
        {
            // Adapt task count for main options
            var commonTasksOption = __instance.Children.ToArray().FirstOrDefault(x => x.TryCast<NumberOption>()?.intOptionName == Int32OptionNames.NumCommonTasks).Cast<NumberOption>();
            if (commonTasksOption != null) commonTasksOption.ValidRange = new FloatRange(0f, 4f);
            var shortTasksOption = __instance.Children.ToArray().FirstOrDefault(x => x.TryCast<NumberOption>()?.intOptionName == Int32OptionNames.NumShortTasks).TryCast<NumberOption>();
            if (shortTasksOption != null) shortTasksOption.ValidRange = new FloatRange(0f, 23f);
            var longTasksOption = __instance.Children.ToArray().FirstOrDefault(x => x.TryCast<NumberOption>()?.intOptionName == Int32OptionNames.NumLongTasks).TryCast<NumberOption>();
            if (longTasksOption != null) longTasksOption.ValidRange = new FloatRange(0f, 15f);
        }
    }

    class CreateGameOptionsTORBehaviour : MonoBehaviour
    {
        static CreateGameOptionsTORBehaviour() => ClassInjector.RegisterTypeInIl2Cpp<CreateGameOptionsTORBehaviour>();

        public CreateOptionsPicker MyPicker;

        void Awake()
        {
            MyPicker = gameObject.GetComponent<CreateOptionsPicker>();

            // This screen's own "Game Mode" child (if any) isn't what actually displays the mode on
            // the Create Game screen - that's driven by the separate GameModeText object handled in
            // Patches/CredentialsPatch.cs, which is where the mode cycler is now wired up instead.
            var gameModeSection = MyPicker.transform.FindChild("Game Mode");
            if (gameModeSection != null) gameModeSection.gameObject.SetActive(false);

            var impostorsRoot = MyPicker.transform.FindChild("Impostors");
            if (impostorsRoot)
            {
                impostorsRoot.transform.localPosition = new(-1.955f, -0.44f, 0f);

                var temp = impostorsRoot.transform.GetChild(1);

                var list = MyPicker.ImpostorButtons.ToList();
                for (int i = 4; i <= 6; i++)
                {
                    var obj = GameObject.Instantiate(temp, impostorsRoot);
                    obj.name = i.ToString();
                    obj.transform.localPosition = new((i - 1) * 0.6f, 0f, 0f);
                    obj.GetChild(0).GetComponent<TextMeshPro>().text = i.ToString();
                    var passiveButton = obj.gameObject.GetComponent<PassiveButton>();
                    passiveButton.OnClick = new();
                    int impostors = i;
                    passiveButton.OnClick.AddListener((Action)(() => MyPicker.SetImpostorButtons(impostors)));

                    list.Add(obj.GetComponent<ImpostorsOptionButton>());
                }

                MyPicker.ImpostorButtons = list.ToArray();
            }
        }

        void OnEnable()
        {
            if (!MyPicker) return;

            bool isCustomServer = Helpers.isCustomServer();

            if (MyPicker.MaxPlayersRoot)
            {
                MyPicker.optionsMenu.ControllerSelectable.Clear();
                MyPicker.MaxPlayerButtons.Clear();

                Helpers.Sequential(MyPicker.MaxPlayersRoot.childCount).Skip(1).Select(i => MyPicker.MaxPlayersRoot.GetChild(i).gameObject).ToArray().Do(GameObject.Destroy);

                for (int i = 4; i <= (isCustomServer ? 24 : 15); i++)
                {
                    SpriteRenderer spriteRenderer = GameObject.Instantiate<SpriteRenderer>(MyPicker.MaxPlayerButtonPrefab, MyPicker.MaxPlayersRoot);
                    spriteRenderer.transform.localPosition = new Vector3((i - 4) % 12 * 0.5f, i / 16 * -0.47f, 0f);
                    int numPlayers = i;
                    spriteRenderer.name = numPlayers.ToString();
                    PassiveButton component = spriteRenderer.GetComponent<PassiveButton>();
                    component.OnClick.AddListener((Action)(() => MyPicker.SetMaxPlayersButtons(numPlayers)));
                    spriteRenderer.GetComponentInChildren<TextMeshPro>().text = numPlayers.ToString();
                    MyPicker.MaxPlayerButtons.Add(spriteRenderer);
                    MyPicker.optionsMenu.ControllerSelectable.Add(component);
                }
            }

            var subMenu = MyPicker.transform.FindChild("SubMenu");
            subMenu.transform.localPosition = new(1.11f, isCustomServer ? -0.4f : 0f, 0f);
            subMenu.GetComponent<ShiftButtonsCrossplayEnabled>().enabled = false;

            for (int i = 4; i <= 6; i++) MyPicker.ImpostorButtons[i - 1].gameObject.SetActive(isCustomServer);

            var options = MyPicker.GetTargetOptions();
            MyPicker.SetMaxPlayersButtons(isCustomServer ? 24 : 15);
        }
    }

    [HarmonyPatch(typeof(CreateGameOptions), nameof(CreateGameOptions.UpdateServerText))]
    public static class CreateGameOptionsUpdateRegionPatch
    {
        private static void Postfix(CreateGameOptions __instance)
        {
            __instance.capacityOption.ValidRange.max = Helpers.isCustomServer() ? 24f : 15f;
            __instance.capacityOption.Value = __instance.capacityOption.ValidRange.Clamp(__instance.capacityOption.Value);
            __instance.capacityOption.UpdateValue();
            __instance.capacityOption.AdjustButtonsActiveState();
        }
    }

    [HarmonyPatch(typeof(CreateOptionsPicker), nameof(CreateOptionsPicker.Awake))]
    class CreateGameOptionsShowPatch
    {
        public static bool Prefix(CreateOptionsPicker __instance)
        {
            // Set MaxImpostors values
            int[] maxImpostors = Helpers.MaxImpostors;
            LegacyGameOptions.MaxImpostors = maxImpostors;
            NormalGameOptionsV10.MaxImpostors = maxImpostors;
            NormalGameOptionsV09.MaxImpostors = maxImpostors;
            NormalGameOptionsV08.MaxImpostors = maxImpostors;
            NormalGameOptionsV07.MaxImpostors = maxImpostors;

            // Set RecommendedImpostors values
            int[] recommendedImpostors = Helpers.RecommendedImpostors;
            LegacyGameOptions.RecommendedImpostors = recommendedImpostors;
            NormalGameOptionsV10.RecommendedImpostors = recommendedImpostors;
            NormalGameOptionsV09.RecommendedImpostors = recommendedImpostors;
            NormalGameOptionsV08.RecommendedImpostors = recommendedImpostors;
            NormalGameOptionsV07.RecommendedImpostors = recommendedImpostors;

            // Set RecommendedKillCooldown values
            int[] recommendedKillCooldown = Helpers.RecommendedKillCooldown;
            LegacyGameOptions.RecommendedKillCooldown = recommendedKillCooldown;
            NormalGameOptionsV10.RecommendedKillCooldown = recommendedKillCooldown;
            NormalGameOptionsV09.RecommendedKillCooldown = recommendedKillCooldown;
            NormalGameOptionsV08.RecommendedKillCooldown = recommendedKillCooldown;
            NormalGameOptionsV07.RecommendedKillCooldown = recommendedKillCooldown;

            // Set MinPlayers values
            int[] minPlayers = Helpers.MinPlayers;
            LegacyGameOptions.MinPlayers = minPlayers;
            NormalGameOptionsV10.MinPlayers = minPlayers;
            NormalGameOptionsV09.MinPlayers = minPlayers;
            NormalGameOptionsV08.MinPlayers = minPlayers;
            NormalGameOptionsV07.MinPlayers = minPlayers;

            DataManager.Settings.Multiplayer.LastPlayedGameMode = AmongUs.GameOptions.GameModes.Normal;
            DataManager.Settings.Save();
            GameOptionsManager.Instance.SwitchGameMode(AmongUs.GameOptions.GameModes.Normal);

            __instance.gameObject.AddComponent<CreateGameOptionsTORBehaviour>();

            return false;
        }
    }

    [HarmonyPatch(typeof(CreateOptionsPicker), nameof(CreateOptionsPicker.Refresh))]
    internal class CreateGameOptionsStartPatch
    {
        private static int impostors = 1;

        public static void Prefix(CreateOptionsPicker __instance)
        {
            impostors = GameOptionsManager.Instance.CurrentGameOptions.GetInt(Int32OptionNames.NumImpostors);
            Debug.Log("Impostors(A): " + impostors.ToString());
        }

        public static void Postfix(CreateOptionsPicker __instance)
        {
            impostors = Math.Min(impostors, Helpers.isCustomServer() ? 6 : 3);
            __instance.SetImpostorButtons(impostors);
            Debug.Log("Impostors(B): " + impostors.ToString());
        }
    }

    [HarmonyPatch(typeof(CreateGameOptions), nameof(CreateGameOptions.Confirm))]
    public static class CreateGameOptionsConfirmPatch
    {
        private static bool Prefix(CreateGameOptions __instance)
        {
            if (!DestroyableSingleton<MatchMaker>.Instance.Connecting(__instance))
                return false;
            GameOptionsManager.Instance.GameHostOptions.TryGetInt(Int32OptionNames.MaxPlayers, out int index);
            int[] maxImpostors = Helpers.MaxImpostors;
            GameOptionsManager.Instance.GameHostOptions.TryGetInt(Int32OptionNames.NumImpostors, out int num);
            if (num > maxImpostors[index])
                GameOptionsManager.Instance.GameHostOptions.SetInt(Int32OptionNames.NumImpostors, maxImpostors[index]);
            if (num == 0)
                GameOptionsManager.Instance.GameHostOptions.SetInt(Int32OptionNames.NumImpostors, 1);
            __instance.CoStartGame();
            return false;
        }
    }

    [HarmonyPatch(typeof(CreateGameOptions), nameof(CreateGameOptions.OpenConfirmPopup))]
    static class CreateGameOptionsOpenConfirmPopupPatch
    {
        static void Postfix(CreateGameOptions __instance)
        {
            __instance.containerConfirm.GetChild(10).gameObject.SetActive(false);
            __instance.containerConfirm.GetChild(8).localPosition = new(4f, - 0.47f, - 0.1f);
            __instance.containerConfirm.GetChild(5).GetChild(2).GetComponent<TextMeshPro>().SetText(
                TORMapOptions.gameMode is CustomGamemodes.Classic ? DestroyableSingleton<TranslationController>.Instance.GetString(StringNames.GameTypeClassic) :
                (TORMapOptions.gameMode is CustomGamemodes.Guesser ? ModTranslation.getString("gamemodeGuesser") :
                (TORMapOptions.gameMode is CustomGamemodes.Zombie ? ModTranslation.getString("gamemodeZombie") : ModTranslation.getString("gamemodeHideNSeek"))));
        }
    }

    [HarmonyPatch(typeof(CreateGameOptions), nameof(CreateGameOptions.Show))]
    static class CreateGameOptionsOpenShowPatch
    {
        static void Postfix(CreateGameOptions __instance)
        {
            if ((CreateGameOptionsPatch.modeButtonGS != null && CreateGameOptionsPatch.modeButtonGS.IsSelected()) ||
                (CreateGameOptionsPatch.modeButtonHK != null && CreateGameOptionsPatch.modeButtonHK.IsSelected()) ||
                (CreateGameOptionsPatch.modeButtonZM != null && CreateGameOptionsPatch.modeButtonZM.IsSelected()))
                __instance.modeButtons[0].SelectButton(false);
        }
    }

    [HarmonyPatch(typeof(CreateGameOptions), nameof(CreateGameOptions.Start))]
    public static class CreateGameOptionsPatch
    {
        public static PassiveButton modeButtonGS;
        public static PassiveButton modeButtonHK;
        public static PassiveButton modeButtonZM;

        private static void Postfix(CreateGameOptions __instance)
        {
            __instance.tooltip.transform.parent.gameObject.SetActive(false);
            __instance.mapPicker.transform.SetLocalY(-1.245f);
            __instance.capacityOption.transform.SetLocalY(-1.15f);
            __instance.levelButtons[0].transform.parent.gameObject.SetActive(false);
            __instance.serverButton.transform.parent.SetLocalY(-1.84f);
            __instance.serverDropdown.transform.SetLocalY(-2.63f);
            __instance.modeButtons[0].transform.parent.SetLocalY(-2.55f);
            __instance.modeButtons[1].gameObject.SetActive(false);

            TORMapOptions.gameMode = CustomGamemodes.Classic;

            modeButtonGS = UnityEngine.Object.Instantiate(__instance.modeButtons[0], __instance.modeButtons[0].transform);
            modeButtonGS.name = "TORGUESSER";
            changeButtonText(modeButtonGS, ModTranslation.getString("torGuesser"));
            modeButtonGS.transform.localPosition = new Vector3(0f, -0.75f, -3f);
            modeButtonGS.OnClick.RemoveAllListeners();
            __instance.StartCoroutine(Effects.Lerp(0.1f, new Action<float>(p => modeButtonGS.SelectButton(false))));
            modeButtonGS.OnClick.AddListener((Action)(() =>
            {
                TORMapOptions.gameMode = CustomGamemodes.Guesser;
                modeButtonGS.SelectButton(true);
                __instance.modeButtons[0].SelectButton(false);
                modeButtonHK.SelectButton(false);
                modeButtonZM.SelectButton(false);
            }
            ));

            modeButtonHK = UnityEngine.Object.Instantiate(modeButtonGS, __instance.modeButtons[0].transform);
            modeButtonHK.name = "TORHIDENSEEK";
            changeButtonText(modeButtonHK, ModTranslation.getString("torHideNSeek"));
            modeButtonHK.transform.localPosition = new Vector3(2.91f, 0f, -3f);
            modeButtonHK.OnClick.RemoveAllListeners();
            __instance.StartCoroutine(Effects.Lerp(0.1f, new Action<float>(p => modeButtonHK.SelectButton(false))));
            modeButtonHK.OnClick.AddListener((Action)(() =>
            {
                TORMapOptions.gameMode = CustomGamemodes.HideNSeek;
                modeButtonHK.SelectButton(true);
                __instance.modeButtons[0].SelectButton(false);
                modeButtonGS.SelectButton(false);
                modeButtonZM.SelectButton(false);
            }
            ));

            modeButtonZM = UnityEngine.Object.Instantiate(modeButtonHK, __instance.modeButtons[0].transform);
            modeButtonZM.name = "TORZOMBIE";
            changeButtonText(modeButtonZM, ModTranslation.getString("torZombie"));
            modeButtonZM.transform.localPosition = new Vector3(2.91f, -0.75f, -3f);
            modeButtonZM.OnClick.RemoveAllListeners();
            __instance.StartCoroutine(Effects.Lerp(0.1f, new Action<float>(p => modeButtonZM.SelectButton(false))));
            modeButtonZM.OnClick.AddListener((Action)(() =>
            {
                TORMapOptions.gameMode = CustomGamemodes.Zombie;
                modeButtonZM.SelectButton(true);
                __instance.modeButtons[0].SelectButton(false);
                modeButtonGS.SelectButton(false);
                modeButtonHK.SelectButton(false);
            }
            ));

            __instance.modeButtons[0].OnClick.AddListener((Action)(() =>
            {
                TORMapOptions.gameMode = CustomGamemodes.Classic;
                modeButtonGS.SelectButton(false);
                modeButtonHK.SelectButton(false);
                modeButtonZM.SelectButton(false);
            }
            ));
        }

        private static void changeButtonText(PassiveButton passiveButton, string buttonText)
        {
            var selectedInactive = passiveButton.transform.FindChild("SelectedInactive/ClassicText");
            var inactive = passiveButton.transform.FindChild("Inactive/ClassicText");
            var highlight = passiveButton.transform.FindChild("Highlight/ClassicText");
            var selectedHighlight = passiveButton.transform.FindChild("SelectedHighlight/ClassicText");

            selectedInactive.gameObject.GetComponentInChildren<TextTranslatorTMP>().Destroy();
            selectedInactive.gameObject.GetComponentInChildren<TMP_Text>().SetText(buttonText);

            inactive.gameObject.GetComponentInChildren<TextTranslatorTMP>().Destroy();
            inactive.gameObject.GetComponentInChildren<TMP_Text>().SetText(buttonText);

            highlight.gameObject.GetComponentInChildren<TextTranslatorTMP>().Destroy();
            highlight.gameObject.GetComponentInChildren<TMP_Text>().SetText(buttonText);

            selectedHighlight.gameObject.GetComponentInChildren<TextTranslatorTMP>().Destroy();
            selectedHighlight.gameObject.GetComponentInChildren<TMP_Text>().SetText(buttonText);
        }
    }

    [HarmonyPatch(typeof(NumberOption), nameof(NumberOption.SetUpFromData))]
    public static class TryGetIntArrayV09Patch
    {
        private static bool Prefix(NumberOption __instance, [HarmonyArgument(0)] BaseGameSetting data, [HarmonyArgument(1)] int maskLayer)
        {
            if (data.Type != OptionTypes.Int || data.Title != StringNames.GameNumImpostors)
                return true;
            __instance.data = data;
            __instance.GetComponentsInChildren<SpriteRenderer>(true).Do(r => r.material.SetInt(PlayerMaterial.MaskLayer, maskLayer));
            __instance.GetComponentsInChildren<TextMeshPro>(true).Do(t =>
            {
                t.fontMaterial.SetFloat("_StencilComp", 3f);
                t.fontMaterial.SetFloat("_Stencil", maskLayer);
            });
            IntGameSetting intGameSetting = data.Cast<IntGameSetting>();
            __instance.Title = intGameSetting.Title;
            __instance.Value = intGameSetting.Value;
            __instance.Increment = intGameSetting.Increment;
            GameOptionsManager.Instance.CurrentGameOptions.TryGetInt(Int32OptionNames.MaxPlayers, out int index);
            __instance.ValidRange = new FloatRange(intGameSetting.ValidRange.min, AmongUsClient.Instance?.AmLocalHost ?? false ? 6f : Helpers.MaxImpostors[index]);
            __instance.FormatString = intGameSetting.FormatString;
            __instance.ZeroIsInfinity = intGameSetting.ZeroIsInfinity;
            __instance.SuffixType = intGameSetting.SuffixType;
            __instance.intOptionName = intGameSetting.OptionName;
            return false;
        }
    }

    [HarmonyPatch(typeof(GameSettingMenu), nameof(GameSettingMenu.Start))]
    class GameOptionsMenuStartPatch
    {
        public static List<GameObject> currentTabs = new();
        public static List<PassiveButton> currentButtons = new();
        public static List<int> currentButtonTargets = new();
        public static Dictionary<byte, GameOptionsMenu> currentGOMs = new();
        public static List<CustomOptionType> currentTabTypes = new();
        public static List<bool> currentTabsDirty = new();
        public static int activeTabIndex = -1;
        public static List<PassiveButton> headerTabButtons = new();
        public static List<SpriteRenderer> headerTabIcons = new();
        public static GameObject headerTabBarObject = null;      // 顶部标签栏根对象（用于显隐）
        public static List<int> headerTabTargets = new();         // 顶部标签栏所有标签的 targetMenu（含 General + 各阵营）
        public static List<int> roleTabTargets = new();           // 「角色设置」对应的阵营 targetMenu（不含 General）
        public static int generalTabTarget = 3;                   // TOR General「游戏设置」标签的 targetMenu
        public static List<GameObject> headerTabFrames = new();   // 每个标签的圆角矩形边框（用于高亮着色）

        public static void Postfix(GameSettingMenu __instance)
        {
            currentTabs.ForEach(x => { if (x != null) x?.Destroy(); });
            currentButtons.ForEach(x => { if (x != null) x?.Destroy(); });
            currentTabs = new();
            currentButtons = new();
            currentButtonTargets = new();
            currentGOMs.Clear();
            currentTabTypes = new();
            currentTabsDirty = new();
            activeTabIndex = -1;
            headerTabButtons.ForEach(x => { if (x != null) x?.Destroy(); });
            headerTabButtons = new();
            headerTabIcons = new();
            headerTabBarObject = null;
            headerTabTargets = new();
            roleTabTargets = new();
            headerTabFrames = new();
            generalTabTarget = 3;

            if (GameOptionsManager.Instance.currentGameOptions.GameMode == GameModes.HideNSeek) return;

            removeVanillaTabs(__instance);

            createSettingTabs(__instance);

            createHeaderTabBar(__instance);

            buildLeftNavigation(__instance);

            // 默认进入「角色设置」：显示顶部标签栏并切换到顶栏第一个标签（General/游戏设置）
            if (headerTabTargets.Count > 0)
            {
                headerTabBarObject?.SetActive(true);
                __instance.ChangeTab(headerTabTargets[0], false);
            }

            var GOMGameObject = GameObject.Find("GAME SETTINGS TAB");


            // create copy to clipboard and paste from clipboard buttons.
            var template = GameObject.Find("PlayerOptionsMenu(Clone)").transform.Find("CloseButton").gameObject;
            var holderGO = new GameObject("copyPasteButtonParent");
            var bgrenderer = holderGO.AddComponent<SpriteRenderer>();
            bgrenderer.sprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.CopyPasteBG.png", 175f);
            holderGO.transform.SetParent(template.transform.parent, false);
            holderGO.transform.localPosition = template.transform.localPosition + new Vector3(-8.3f, 0.73f, -2f);
            holderGO.layer = template.layer;
            holderGO.SetActive(true);
            var copyButton = GameObject.Instantiate(template, holderGO.transform);
            copyButton.transform.localPosition = new Vector3(-0.3f, 0.02f, -2f);
            var copyButtonPassive = copyButton.GetComponent<PassiveButton>();
            var copyButtonRenderer = copyButton.GetComponentInChildren<SpriteRenderer>();
            var copyButtonActiveRenderer = copyButton.transform.GetChild(1).GetComponent<SpriteRenderer>();
            copyButtonRenderer.sprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.Copy.png", 100f);
            copyButton.transform.GetChild(1).transform.localPosition = Vector3.zero;
            copyButtonActiveRenderer.sprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.CopyActive.png", 100f);
            copyButtonPassive.OnClick.RemoveAllListeners();
            copyButtonPassive.OnClick = new UnityEngine.UI.Button.ButtonClickedEvent();
            copyButtonPassive.OnClick.AddListener((System.Action)(() => {
                copyToClipboard();
                copyButtonRenderer.color = Color.green;
                copyButtonActiveRenderer.color = Color.green;
                __instance.StartCoroutine(Effects.Lerp(1f, new System.Action<float>((p) => {
                    if (p > 0.95)
                    {
                        copyButtonRenderer.color = Color.white;
                        copyButtonActiveRenderer.color = Color.white;
                    }
                })));
            }));
            var pasteButton = GameObject.Instantiate(template, holderGO.transform);
            pasteButton.transform.localPosition = new Vector3(0.3f, 0.02f, -2f);
            var pasteButtonPassive = pasteButton.GetComponent<PassiveButton>();
            var pasteButtonRenderer = pasteButton.GetComponentInChildren<SpriteRenderer>();
            var pasteButtonActiveRenderer = pasteButton.transform.GetChild(1).GetComponent<SpriteRenderer>();
            pasteButtonRenderer.sprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.Paste.png", 100f);
            pasteButtonActiveRenderer.sprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.PasteActive.png", 100f);
            pasteButtonPassive.OnClick.RemoveAllListeners();
            pasteButtonPassive.OnClick = new UnityEngine.UI.Button.ButtonClickedEvent();
            pasteButtonPassive.OnClick.AddListener((System.Action)(() => {
                pasteButtonRenderer.color = Color.yellow;
                int success = pasteFromClipboard();
                pasteButtonRenderer.color = success == 3 ? Color.green : success == 0 ? Color.red : Color.yellow;
                pasteButtonActiveRenderer.color = success == 3 ? Color.green : success == 0 ? Color.red : Color.yellow;
                __instance.StartCoroutine(Effects.Lerp(1f, new System.Action<float>((p) => {
                    if (p > 0.95)
                    {
                        pasteButtonRenderer.color = Color.white;
                        pasteButtonActiveRenderer.color = Color.white;
                    }
                })));
            }));
        }

        private static void createSettings(GameOptionsMenu menu, List<CustomOption> options)
        {
            float num = 1.5f;
            foreach (CustomOption option in options)
            {
                if (option.isHeader)
                {
                    CategoryHeaderMasked categoryHeaderMasked = UnityEngine.Object.Instantiate<CategoryHeaderMasked>(menu.categoryHeaderOrigin, Vector3.zero, Quaternion.identity, menu.settingsContainer);
                    categoryHeaderMasked.SetHeader(StringNames.ImpostorsCategory, 20);
                    string titleText = option.heading != "" ? option.getHeading() : option.getName();
                    categoryHeaderMasked.Title.text = titleText;
                    categoryHeaderMasked.transform.localScale = Vector3.one * 0.63f;
                    categoryHeaderMasked.transform.localPosition = new Vector3(-0.903f, num, -2f);
                    categoryHeaderMasked.transform.GetChild(0).GetComponent<SpriteRenderer>().color = option.getColor();
                    categoryHeaderMasked.transform.GetChild(1).GetComponent<SpriteRenderer>().color = option.getColor();
                    num -= 0.63f;
                }
                else if (!ShouldBeEnabled(option)) continue;  // Hides options, for which the parent is disabled!
                else if (option.parent != null && option.parent.selection != 0 && option.invertedParent) continue;
                OptionBehaviour optionBehaviour = UnityEngine.Object.Instantiate<StringOption>(menu.stringOptionOrigin, Vector3.zero, Quaternion.identity, menu.settingsContainer);
                optionBehaviour.transform.localPosition = new Vector3(0.952f, num, -2f);
                optionBehaviour.SetClickMask(menu.ButtonClickMask);

                // "SetUpFromData"
                SpriteRenderer[] componentsInChildren = optionBehaviour.GetComponentsInChildren<SpriteRenderer>(true);
                for (int i = 0; i < componentsInChildren.Length; i++)
                {
                    componentsInChildren[i].material.SetInt(PlayerMaterial.MaskLayer, 20);
                }
                foreach (TextMeshPro textMeshPro in optionBehaviour.GetComponentsInChildren<TextMeshPro>(true))
                {
                    textMeshPro.fontMaterial.SetFloat("_StencilComp", 3f);
                    textMeshPro.fontMaterial.SetFloat("_Stencil", 20);
                }

                var stringOption = optionBehaviour as StringOption;
                stringOption.OnValueChanged = new Action<OptionBehaviour>((o) => { });
                stringOption.TitleText.text = option.getName();
                if (option.isHeader && option.heading == "" && (option.type == CustomOptionType.Neutral || option.type == CustomOptionType.Crewmate || option.type == CustomOptionType.Impostor || option.type == CustomOptionType.Modifier))
                {
                    stringOption.TitleText.text = ModTranslation.getString("optionSpawnChance");
                }
                if (stringOption.TitleText.text.Length > 25)
                    stringOption.TitleText.fontSize = 2.2f;
                if (stringOption.TitleText.text.Length > 40)
                    stringOption.TitleText.fontSize = 2f;
                stringOption.Value = stringOption.oldValue = option.selection;
                stringOption.ValueText.text = option.getString();
                option.optionBehaviour = stringOption;

                menu.Children.Add(optionBehaviour);
                num -= 0.45f;
                menu.scrollBar.SetYBoundsMax(-num - 1.65f);
            }

            for (int i = 0; i < menu.Children.Count; i++)
            {
                OptionBehaviour optionBehaviour = menu.Children[i];
                if (AmongUsClient.Instance && !AmongUsClient.Instance.AmHost)
                {
                    optionBehaviour.SetAsPlayer();
                }
            }
        }

        private static void removeVanillaTabs(GameSettingMenu __instance)
        {
            GameObject.Find("What Is This?")?.Destroy();
            GameObject.Find("GamePresetButton")?.Destroy();
            GameObject.Find("RoleSettingsButton")?.Destroy();
            __instance.ChangeTab(1, false);
        }

        public static void createCustomButton(GameSettingMenu __instance, int targetMenu, string buttonName, string buttonText)
        {
            var leftPanel = GameObject.Find("LeftPanel");
            var buttonTemplate = GameObject.Find("GameSettingsButton");
            if (targetMenu == 3)
            {
                buttonTemplate.transform.localPosition -= Vector3.up * 0.85f;
                buttonTemplate.transform.localScale *= Vector2.one * 0.75f;
            }
            var torSettingsButton = GameObject.Find(buttonName);
            if (torSettingsButton == null)
            {
                torSettingsButton = GameObject.Instantiate(buttonTemplate, leftPanel.transform);
                torSettingsButton.transform.localPosition += Vector3.up * 0.5f * (targetMenu - 2);
                torSettingsButton.name = buttonName;
                __instance.StartCoroutine(Effects.Lerp(2f, new Action<float>(p => { torSettingsButton.transform.FindChild("FontPlacer").GetComponentInChildren<TextMeshPro>().text = buttonText; })));
                var torSettingsPassiveButton = torSettingsButton.GetComponent<PassiveButton>();
                torSettingsPassiveButton.OnClick.RemoveAllListeners();
                torSettingsPassiveButton.OnClick.AddListener((System.Action)(() => {
                    __instance.ChangeTab(targetMenu, false);
                }));
                torSettingsPassiveButton.OnMouseOut.RemoveAllListeners();
                torSettingsPassiveButton.OnMouseOver.RemoveAllListeners();
                torSettingsPassiveButton.SelectButton(false);
                currentButtons.Add(torSettingsPassiveButton);
                currentButtonTargets.Add(targetMenu);
            }
        }

        private static void buildLeftNavigation(GameSettingMenu __instance)
        {
            // 标题 + 说明卡片（放在左栏左上角，位于按钮上方）
            var leftPanel = GameObject.Find("LeftPanel");
            if (leftPanel == null) return;
            TOROptionsUI.BuildLeftHeader(leftPanel.transform, new Vector3(0f, 1.9f, -2f));
        }

        public static void createGameOptionsMenu(GameSettingMenu __instance, CustomOptionType optionType, string settingName)
        {
            var tabTemplate = GameObject.Find("GAME SETTINGS TAB");
            currentTabs.RemoveAll(x => x == null);

            var torSettingsTab = GameObject.Instantiate(tabTemplate, tabTemplate.transform.parent);
            torSettingsTab.name = settingName;

            var torSettingsGOM = torSettingsTab.GetComponent<GameOptionsMenu>();

            currentTabs.Add(torSettingsTab);
            currentTabTypes.Add(optionType);
            currentTabsDirty.Add(true);
            torSettingsTab.SetActive(false);
            currentGOMs.Add((byte)optionType, torSettingsGOM);
        }

        public static void RebuildTabIfDirty(int index)
        {
            if (index < 0 || index >= currentTabsDirty.Count || !currentTabsDirty[index]) return;
            var optionType = currentTabTypes[index];
            if (currentGOMs.TryGetValue((byte)optionType, out var gom) && gom != null)
                updateGameOptionsMenu(optionType, gom);
            currentTabsDirty[index] = false;
        }

        public static void RebuildActiveTabIfDirty()
        {
            if (activeTabIndex >= 0) RebuildTabIfDirty(activeTabIndex);
        }

        public static void MarkAllTabsDirty()
        {
            for (int i = 0; i < currentTabsDirty.Count; i++) currentTabsDirty[i] = true;
        }
        public static void updateGameOptionsMenu(CustomOptionType optionType, GameOptionsMenu torSettingsGOM)
        {
            foreach (var child in torSettingsGOM.Children)
            {
                child.Destroy();
            }
            torSettingsGOM.scrollBar.transform.FindChild("SliderInner").DestroyChildren();
            torSettingsGOM.Children.Clear();
            var relevantOptions = options.Where(x => x.type == optionType).ToList();
            if (TORMapOptions.gameMode == CustomGamemodes.Guesser) // Exclude guesser options in neutral mode
                relevantOptions = relevantOptions.Where(x => !(new List<int> { 310, 311, 312, 313, 314, 315, 316, 317, 318, 319, 7006 }).Contains(x.id)).ToList();
            else
                relevantOptions = relevantOptions.Where(x => x.id != 7007).ToList();

            if (TORMapOptions.gameMode != CustomGamemodes.FreePlay)
                relevantOptions = relevantOptions.Where(x => x.id != 10429).ToList();
            createSettings(torSettingsGOM, relevantOptions);
        }

        private static void createSettingTabs(GameSettingMenu __instance)
        {
            // 先创建所有 tab（右侧详情页）；其顺序决定 targetMenu(索引+3)。
            // 左侧只保留「游戏设置(原版) / 角色设置」；TOR 各设置页（General + 阵营）全部进入顶部标签栏。
            int next = 3;
            headerTabTargets = new();
            roleTabTargets = new();
            if (TORMapOptions.gameMode == CustomGamemodes.Guesser || TORMapOptions.gameMode == CustomGamemodes.Classic || TORMapOptions.gameMode == CustomGamemodes.FreePlay)
            {
                createGameOptionsMenu(__instance, CustomOptionType.General, "TORSettings");
                headerTabTargets.Add(next++); // General == 3

                if (TORMapOptions.gameMode == CustomGamemodes.Guesser)
                {
                    createGameOptionsMenu(__instance, CustomOptionType.Guesser, "GuesserSettings");
                    headerTabTargets.Add(next++); // Guesser == 4
                }

                createGameOptionsMenu(__instance, CustomOptionType.Impostor, "ImpostorSettings");
                headerTabTargets.Add(next); roleTabTargets.Add(next++); // Impostor
                createGameOptionsMenu(__instance, CustomOptionType.Neutral, "NeutralSettings");
                headerTabTargets.Add(next); roleTabTargets.Add(next++); // Neutral
                createGameOptionsMenu(__instance, CustomOptionType.Crewmate, "CrewmateSettings");
                headerTabTargets.Add(next); roleTabTargets.Add(next++); // Crewmate
                createGameOptionsMenu(__instance, CustomOptionType.Modifier, "ModifierSettings");
                headerTabTargets.Add(next); roleTabTargets.Add(next++); // Modifier
            }
            else if (TORMapOptions.gameMode == CustomGamemodes.HideNSeek)
            {
                createGameOptionsMenu(__instance, CustomOptionType.HideNSeekMain, "HideNSeekMain");
                headerTabTargets.Add(next++); // Main == 3
                createGameOptionsMenu(__instance, CustomOptionType.HideNSeekRoles, "HideNSeekRoles");
                headerTabTargets.Add(next); roleTabTargets.Add(next++); // Roles == 4
            }
            else if (TORMapOptions.gameMode == CustomGamemodes.Zombie)
            {
                createGameOptionsMenu(__instance, CustomOptionType.ZombieMain, "ZombieMain");
                headerTabTargets.Add(next++); // Main == 3
            }

            generalTabTarget = 3;

            // 左侧「游戏设置」保持原版按钮（地图/人数/模式），仅替换显示文本；不改动点击。
            var vanillaGameBtn = GameObject.Find("GameSettingsButton");
            vanillaGameBtn.transform.localPosition += Vector3.up * 0.3f;
            if (vanillaGameBtn != null)
            {
                __instance.StartCoroutine(Effects.Lerp(2f, new Action<float>(p =>
                {
                    var tmp = vanillaGameBtn.transform.FindChild("FontPlacer")?.GetComponentInChildren<TextMeshPro>();
                    if (tmp != null)
                    {
                        tmp.GetComponent<TextTranslatorTMP>()?.Destroy();
                        tmp.text = ModTranslation.getString("gameSettingsTab");
                    }
                })));
            }

            int roleTarget = headerTabTargets.Count > 0 ? headerTabTargets[0] : generalTabTarget;
            createNavButton(__instance, roleTarget, "TOR-RoleSettings", ModTranslation.getString("roleSettingsTab"));
        }
        /// 创建左侧「角色设置」导航按钮（实例化自原生模板，上移放置）。
        /// 点击后切换到第一个阵营 tab 并显示顶部标签栏。

        private static void createNavButton(GameSettingMenu __instance, int targetMenu, string buttonName, string buttonText)
        {
            var leftPanel = GameObject.Find("LeftPanel");
            var buttonTemplate = GameObject.Find("GameSettingsButton");
            if (leftPanel == null || buttonTemplate == null) return;

            if (GameObject.Find(buttonName) != null) return;

            var navButton = GameObject.Instantiate(buttonTemplate, leftPanel.transform);
            navButton.name = buttonName;
            navButton.transform.localPosition += Vector3.up * 1f; // 上移，紧邻「游戏设置」按钮

            __instance.StartCoroutine(Effects.Lerp(2f, new Action<float>(p =>
            {
                var tmp = navButton.transform.FindChild("FontPlacer")?.GetComponentInChildren<TextMeshPro>();
                if (tmp != null)
                {
                    tmp.GetComponent<TextTranslatorTMP>()?.Destroy();
                    tmp.text = buttonText;
                }
            })));
            var passiveButton = navButton.GetComponent<PassiveButton>();
            passiveButton.OnClick.RemoveAllListeners();
            passiveButton.OnClick.AddListener((System.Action)(() =>
            {
                __instance.ChangeTab(targetMenu, false); // ChangeTab 内会据 tabNum 显隐顶栏
            }));
            passiveButton.OnMouseOut.RemoveAllListeners();
            passiveButton.OnMouseOver.RemoveAllListeners();
            passiveButton.SelectButton(false);
        }

        private static string GetTabIconResource(CustomOptionType type)
        {
            switch (type)
            {
                case CustomOptionType.Impostor: return "TheOtherRoles.Resources.TabIconImpostor.png";
                case CustomOptionType.Neutral: return "TheOtherRoles.Resources.TabIconNeutral.png";
                case CustomOptionType.Crewmate: return "TheOtherRoles.Resources.TabIconCrewmate.png";
                case CustomOptionType.Modifier: return "TheOtherRoles.Resources.TabIconModifier.png";
                case CustomOptionType.Guesser: return "TheOtherRoles.Resources.TabIconGuesserSettings.png";
                case CustomOptionType.HideNSeekMain:
                case CustomOptionType.HideNSeekRoles: return "TheOtherRoles.Resources.TabIconHideNSeekSettings.png";
                // General/游戏设置：用通用 TabIcon
                default: return "TheOtherRoles.Resources.TabIcon.png";
            }
        }

        private static void createHeaderTabBar(GameSettingMenu __instance)
        {
            if (headerTabTargets.Count == 0) return;

            int uiLayer = LayerMask.NameToLayer("UI");
            float barX = -0.05f;
            if (TORMapOptions.gameMode == CustomGamemodes.Guesser) barX += 0.45f;
            if (TORMapOptions.gameMode == CustomGamemodes.HideNSeek) barX -= 1f;
            var bar = Helpers.CreateObject("TORHeaderTabBar", __instance.transform, new Vector3(barX, 2.25f, -2f), uiLayer);
            headerTabBarObject = bar;

            int count = headerTabTargets.Count;
            float startX = -(count - 1) * 0.7f / 2f;

            for (int i = 0; i < count; i++)
            {
                int targetMenu = headerTabTargets[i];
                int tabIndex = targetMenu - 3; // targetMenu == 索引 + 3

                CustomOptionType type;
                if (tabIndex >= 0 && tabIndex < currentTabTypes.Count)
                    type = currentTabTypes[tabIndex];
                else
                    type = CustomOptionType.General;

                var sprite = Helpers.loadSpriteFromResources(GetTabIconResource(type), 175f);
                if (sprite == null) sprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.TabIcon.png", 175f);

                float x = startX + i * 0.7f;
                var tab = Helpers.CreateObject("HeaderTab_" + type, bar.transform, new Vector3(x, 0f, -0.1f), uiLayer);

                // 圆角矩形边框（外层，选中天蓝 / 未选白）
                var frame = Helpers.CreateObject<SpriteRenderer>("Frame", tab.transform, new Vector3(0f, 0f, 0f), uiLayer);
                frame.sprite = VanillaAsset.TextButtonSprite;
                frame.drawMode = SpriteDrawMode.Sliced;
                frame.tileMode = SpriteTileMode.Continuous;
                frame.size = new Vector2(0.62f, 0.65f);
                frame.color = Color.white;
                frame.sortingOrder = 11;
                headerTabFrames.Add(frame.gameObject);

                // 圆角矩形内底（深色，形成描边效果）
                var inner = Helpers.CreateObject<SpriteRenderer>("Inner", tab.transform, new Vector3(0f, 0f, -0.05f), uiLayer);
                inner.sprite = VanillaAsset.TextButtonSprite;
                inner.drawMode = SpriteDrawMode.Sliced;
                inner.tileMode = SpriteTileMode.Continuous;
                inner.size = new Vector2(0.54f, 0.52f);
                inner.color = new Color(0.12f, 0.13f, 0.16f, 1f);
                inner.sortingOrder = 11;

                // 保留阵营图标（叠在圆角框内部）
                var icon = Helpers.CreateObject<SpriteRenderer>("Icon", tab.transform, new Vector3(0f, 0f, -0.1f), uiLayer);
                icon.sprite = sprite;
                icon.transform.localScale = new Vector3(0.52f, 0.52f, 1f);
                icon.sortingOrder = 12;
                headerTabIcons.Add(icon);

                var collider = tab.AddComponent<BoxCollider2D>();
                collider.size = new Vector2(0.62f, 0.56f);
                collider.isTrigger = true;

                // 不传 buttonRenderer：避免 OnMouseOut 把选中标签的天蓝边框重置为白。
                // 边框颜色仅由 UpdateHeaderTabHighlight 按「当前页」维护。
                var button = tab.SetUpButton(false, null);
                button.OnClick.AddListener((System.Action)(() => {
                    __instance.ChangeTab(targetMenu, false);
                }));
                headerTabButtons.Add(button);
            }

            UpdateHeaderTabHighlight(activeTabIndex);
        }

        /// <summary>
        /// 根据当前 activeTabIndex（tab 索引）给顶部标签栏圆角边框着色：选中天蓝、其余白。
        /// </summary>
        public static void UpdateHeaderTabHighlight(int selectedTabIndex)
        {
            int selectedTarget = selectedTabIndex + 3; // 转回 targetMenu
            Color skyBlue = new Color(0.35f, 0.75f, 0.95f, 1f);
            for (int i = 0; i < headerTabFrames.Count; i++)
            {
                var frame = headerTabFrames[i];
                if (frame == null) continue;
                var sr = frame.GetComponent<SpriteRenderer>();
                if (sr == null) continue;
                bool active = i < headerTabTargets.Count && headerTabTargets[i] == selectedTarget;
                sr.color = active ? skyBlue : Color.white;
            }
        }
    }



    [HarmonyPatch(typeof(StringOption), nameof(StringOption.Initialize))]
    public class StringOptionEnablePatch {
        public static bool Prefix(StringOption __instance) {
            CustomOption option = CustomOption.options.FirstOrDefault(option => option.optionBehaviour == __instance);
            if (option == null) return true;

            __instance.OnValueChanged = new Action<OptionBehaviour>((o) => {});
            __instance.Value = __instance.oldValue = option.selection;
            __instance.ValueText.text = option.getString();
            
            return false;
        }
    }

    [HarmonyPatch(typeof(StringOption), nameof(StringOption.Increase))]
    public class StringOptionIncreasePatch
    {
        public static bool Prefix(StringOption __instance)
        {
            CustomOption option = CustomOption.options.FirstOrDefault(option => option.optionBehaviour == __instance);
            if (option == null) return true;
            option.updateSelection(option.selection + 1);
            return false;
        }
    }

    [HarmonyPatch(typeof(StringOption), nameof(StringOption.Decrease))]
    public class StringOptionDecreasePatch
    {
        public static bool Prefix(StringOption __instance)
        {
            CustomOption option = CustomOption.options.FirstOrDefault(option => option.optionBehaviour == __instance);
            if (option == null) return true;
            option.updateSelection(option.selection - 1);
            return false;
        }
    }

    [HarmonyPatch(typeof(LobbyInfoPane), nameof(LobbyInfoPane.RefreshPane))]
    public class LobbyPaneRefreshPatch
    {
        public static void Postfix()
        {
            HelpMenu.OnUpdateOptions();
        }
    }

    [HarmonyPatch(typeof(OptionsConsole), nameof(OptionsConsole.CanUse))]
    public class OptionsConsoleCanUsePatch
    {
        public static void Prefix(OptionsConsole __instance)
        {
            __instance.HostOnly = false;
        }
    }

    [HarmonyPatch(typeof(OptionsConsole), nameof(OptionsConsole.Use))]
    public class OptionsConsoleUsePatch
    {
        public static bool Prefix(OptionsConsole __instance)
        {
            if (__instance.MenuPrefab != null && !__instance.MenuPrefab.TryGetComponent<GameSettingMenu>(out _)) return true;

            __instance.CanUse(PlayerControl.LocalPlayer.Data, out var flag, out _);
            if (!flag) return false;

            PlayerControl.LocalPlayer.NetTransform.Halt();

            if (AmongUsClient.Instance.AmHost)
            {
                GameObject gameObject = GameObject.Instantiate<GameObject>(__instance.MenuPrefab);
                gameObject.transform.SetParent(Camera.main.transform, false);
                gameObject.transform.localPosition = __instance.CustomPosition;
                DestroyableSingleton<TransitionFade>.Instance.DoTransitionFade(null, gameObject.gameObject, null);
            }
            else
            {
                HelpMenu.TryOpenHelpScreen(HelpMenu.HelpTab.Options);
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.RpcSyncSettings))]
    public class RpcSyncSettingsPatch
    {
        public static void Postfix()
        {
            CustomOption.saveVanillaOptions();
        }
    }

    [HarmonyPatch(typeof(PlayerPhysics._CoSpawnPlayer_d__42), "MoveNext")]
    public class AmongUsClientOnPlayerJoinedPatch {
        public static void Postfix() {
            if (PlayerControl.LocalPlayer != null && AmongUsClient.Instance.AmHost) {
                    GameManager.Instance.LogicOptions.SyncOptions();
                    CustomOption.ShareOptionSelections();
            }
        }
    }


    [HarmonyPatch] 
    class GameOptionsDataPatch
    {
        /*private static IEnumerable<MethodBase> TargetMethods() {
            return typeof(IGameOptionsExtensions.).GetMethods().Where(x => x.ReturnType == typeof(string) && x.GetParameters().Length == 1 && x.GetParameters()[0].ParameterType == typeof(int));
        }*/

        public static string buildRoleOptions() {
            var impRoles = buildOptionsOfType(CustomOption.CustomOptionType.Impostor, true) + "\n";
            var neutralRoles = buildOptionsOfType(CustomOption.CustomOptionType.Neutral, true) + "\n";
            var crewRoles = buildOptionsOfType(CustomOption.CustomOptionType.Crewmate, true) + "\n";
            var modifiers = buildOptionsOfType(CustomOption.CustomOptionType.Modifier, true);
            return impRoles + neutralRoles + crewRoles + modifiers;
        }
        public static string buildModifierExtras(CustomOption customOption) {
            // find options children with quantity
            var children = CustomOption.options.Where(o => o.parent == customOption);
            var quantity = children.Where(o => o.name.Contains("Quantity")).ToList();
            if (customOption.getSelection() == 0) return "";
            if (quantity.Count == 1) return $" ({quantity[0].getQuantity()})";
            return "";
        }

        public static string buildOptionsOfType(CustomOption.CustomOptionType type, bool headerOnly) {
            StringBuilder sb = new("\n");
            var options = CustomOption.options.Where(o => o.type == type);
            if (TORMapOptions.gameMode == CustomGamemodes.Guesser) {
                if (type == CustomOption.CustomOptionType.General)
                    options = CustomOption.options.Where(o => o.type == type || o.type == CustomOption.CustomOptionType.Guesser);
                List<int> remove = new() { 308, 310, 311, 312, 313, 314, 315, 316, 317, 318, 319, 7006 };
                options = options.Where(x => !remove.Contains(x.id));
            } else if (TORMapOptions.gameMode == CustomGamemodes.Classic) 
                options = options.Where(x => !(x.type == CustomOption.CustomOptionType.Guesser || x == CustomOptionHolder.crewmateRolesFill || x.id == 7007));
            else if (TORMapOptions.gameMode == CustomGamemodes.HideNSeek)
                options = options.Where(x => (x.type == CustomOption.CustomOptionType.HideNSeekMain || x.type == CustomOption.CustomOptionType.HideNSeekRoles));
            else if (TORMapOptions.gameMode == CustomGamemodes.Zombie)
                options = options.Where(x => x.type == CustomOption.CustomOptionType.ZombieMain);
            if (TORMapOptions.gameMode != CustomGamemodes.FreePlay)
                options = options.Where(x => x.id != 10429);
            foreach (var option in options) {
                if (option.parent == null) {
                    string line = $"{option.getName().Replace("\n", " ")}: {option.getString()}";
                    if (type == CustomOption.CustomOptionType.Modifier) line += buildModifierExtras(option);
                    if (option is CustomRoleOption roleOption && roleOption.enabled && roleOption.countOption != null) line += $" ({roleOption.count})";
                    sb.AppendLine(line);
                }
                else if (option.parent.getSelection() > 0 || option.invertedParent && option.parent.getSelection() == 0) {
                    if (option.id == 103) //Deputy
                        sb.AppendLine($"- {Helpers.cs(Deputy.color, ModTranslation.getString("deputy"))}: {option.getString()}");
                    else if (option.id == 224) //Sidekick
                        sb.AppendLine($"- {Helpers.cs(Sidekick.color, ModTranslation.getString("sidekick"))}: {option.getString()}");
                    else if (option.id == 918) // Immoralist
                        sb.AppendLine($"- {Helpers.cs(Immoralist.color, ModTranslation.getString("immoralist"))}: {option.getString()}");
                    else if (option.id == 8000) // Created Madmate
                        sb.AppendLine($"- {Helpers.cs(Madmate.color, Madmate.fullName)}: {option.getString()}");
                    //else if (option.id == 358) //Prosecutor
                    //sb.AppendLine($"- {Helpers.cs(Lawyer.color, "Prosecutor")}: {option.selections[option.selection].ToString()}");
                }
            }
            if (headerOnly) return sb.ToString();
            else sb = new StringBuilder();

            foreach (CustomOption option in options) {
                if (TORMapOptions.gameMode == CustomGamemodes.HideNSeek && option.type != CustomOptionType.HideNSeekMain && option.type != CustomOptionType.HideNSeekRoles) continue;
                if (TORMapOptions.gameMode == CustomGamemodes.Zombie && option.type != CustomOptionType.ZombieMain) continue;
                if (option.parent != null) {
                    bool isIrrelevant = !ShouldBeEnabled(option);

                    Color c = isIrrelevant ? Color.grey : Color.white;  // No use for now
                    if (isIrrelevant) continue;
                    sb.AppendLine(Helpers.cs(c, $"{option.getName().Replace("\n", " ")}: {option.getString()}"));
                } else {
                    if (option == CustomOptionHolder.crewmateRolesCountMin) {
                        var optionName = CustomOptionHolder.cs(new Color(204f / 255f, 204f / 255f, 0, 1f), ModTranslation.getString("crewmateRoles"));
                        var min = CustomOptionHolder.crewmateRolesCountMin.getSelection();
                        var max = CustomOptionHolder.crewmateRolesCountMax.getSelection();
                        string optionValue = "";
                        if (CustomOptionHolder.crewmateRolesFill.getBool()) {
                            var crewCount = PlayerControl.AllPlayerControls.Count - GameOptionsManager.Instance.currentGameOptions.NumImpostors;
                            int minNeutral = CustomOptionHolder.neutralRolesCountMin.getSelection();
                            int maxNeutral = CustomOptionHolder.neutralRolesCountMax.getSelection();
                            if (minNeutral > maxNeutral) minNeutral = maxNeutral;
                            min = crewCount - maxNeutral;
                            max = crewCount - minNeutral;
                            if (min < 0) min = 0;
                            if (max < 0) max = 0;
                            optionValue = ModTranslation.getString("crewmateFill");
                        }
                        if (min > max) min = max;
                        optionValue += (min == max) ? $"{max}" : $"{min} - {max}";
                        sb.AppendLine($"{optionName}: {optionValue}");
                    } else if (option == CustomOptionHolder.neutralRolesCountMin) {
                        var optionName = CustomOptionHolder.cs(new Color(204f / 255f, 204f / 255f, 0, 1f), ModTranslation.getString("neutralRoles"));
                        var min = CustomOptionHolder.neutralRolesCountMin.getSelection();
                        var max = CustomOptionHolder.neutralRolesCountMax.getSelection();
                        if (min > max) min = max;
                        var optionValue = (min == max) ? $"{max}" : $"{min} - {max}";
                        sb.AppendLine($"{optionName}: {optionValue}");
                    } else if (option == CustomOptionHolder.impostorRolesCountMin) {
                        var optionName = CustomOptionHolder.cs(new Color(204f / 255f, 204f / 255f, 0, 1f), ModTranslation.getString("impostorRoles"));
                        var min = CustomOptionHolder.impostorRolesCountMin.getSelection();
                        var max = CustomOptionHolder.impostorRolesCountMax.getSelection();
                        if (max > GameOptionsManager.Instance.currentGameOptions.NumImpostors) max = GameOptionsManager.Instance.currentGameOptions.NumImpostors;
                        if (min > max) min = max;
                        var optionValue = (min == max) ? $"{max}" : $"{min} - {max}";
                        sb.AppendLine($"{optionName}: {optionValue}");
                    } else if (option == CustomOptionHolder.modifiersCountMin) {
                        var optionName = CustomOptionHolder.cs(new Color(204f / 255f, 204f / 255f, 0, 1f), ModTranslation.getString("modifiers"));
                        var min = CustomOptionHolder.modifiersCountMin.getSelection();
                        var max = CustomOptionHolder.modifiersCountMax.getSelection();
                        if (min > max) min = max;
                        var optionValue = (min == max) ? $"{max}" : $"{min} - {max}";
                        sb.AppendLine($"{optionName}: {optionValue}");
                    } else if ((option == CustomOptionHolder.crewmateRolesCountMax) || (option == CustomOptionHolder.neutralRolesCountMax) || (option == CustomOptionHolder.impostorRolesCountMax) || option == CustomOptionHolder.modifiersCountMax) {
                        continue;
                    } else {
                        sb.AppendLine($"\n{option.getName().Replace("\n", " ")}: {option.getString()}");
                    }
                }
            }
            return sb.ToString();
        }

        private static string[] mapName = new string[] { "Skeld", "Mira", "Polus", "Dlesk", "Airship", "Fungle" };
        public static string buildAllOptions() {
            StringBuilder builder = new();

            //バニラオプション
            var vanillaOptions = GameOptionsManager.Instance.currentNormalGameOptions;
            var translator = TranslationController.Instance;

            void AddHeader(string header)
            {
                builder.Append("\n\n");
                builder.Append(ModTranslation.getString(header));
            }
            void AddOption(StringNames option, string value, string format = "")
            {
                builder.Append("\n- ");
                builder.Append(translator.GetString(option));
                builder.Append(": ");
                builder.Append(string.IsNullOrEmpty(format) ? value : string.Format(ModTranslation.getString(format), value));
            }
            string SimpleTrigger(bool trigger) => trigger ? "<color=#FFFF00FF>" + ModTranslation.getString("optionOn") + "</color>" : "<color=#CCCCCCFF>" + ModTranslation.getString("optionOff") + "</color>";
            builder.Append(ModTranslation.getString("vanillaOptionsHeader"));
            AddOption(StringNames.GameMapName, ModTranslation.getString("map" + mapName[vanillaOptions.MapId]));
            AddHeader("vanillaOptionsImpostorHeader");
            AddOption(StringNames.GameNumImpostors, vanillaOptions.NumImpostors.ToString());
            AddOption(StringNames.GameKillCooldown, vanillaOptions.KillCooldown.ToString(), "unitSeconds");
            AddOption(StringNames.GameImpostorLight, vanillaOptions.ImpostorLightMod.ToString(), "unitTimes");
            AddOption(StringNames.GameKillDistance, translator.GetString(vanillaOptions.KillDistance switch { 0 => StringNames.SettingShort, 1 => StringNames.SettingMedium, _ => StringNames.SettingLong }));
            AddHeader("vanillaOptionsCrewmateHeader");
            AddOption(StringNames.GamePlayerSpeed, vanillaOptions.PlayerSpeedMod.ToString(), "unitTimes");
            AddOption(StringNames.GameCrewLight, vanillaOptions.CrewLightMod.ToString(), "unitTimes");
            AddHeader("vanillaOptionsMeetingHeader");
            AddOption(StringNames.GameNumMeetings, vanillaOptions.NumEmergencyMeetings.ToString());
            AddOption(StringNames.GameEmergencyCooldown, vanillaOptions.EmergencyCooldown.ToString(), "unitSeconds");
            AddOption(StringNames.GameDiscussTime, vanillaOptions.DiscussionTime.ToString(), "unitSeconds");
            AddOption(StringNames.GameVotingTime, vanillaOptions.VotingTime.ToString(), "unitSeconds");
            AddOption(StringNames.GameAnonymousVotes, SimpleTrigger(vanillaOptions.AnonymousVotes));
            AddOption(StringNames.GameConfirmImpostor, SimpleTrigger(vanillaOptions.ConfirmImpostor));
            AddHeader("vanillaOptionsTaskHeader");
            AddOption(StringNames.GameTaskBarMode, translator.GetString(vanillaOptions.TaskBarMode switch { AmongUs.GameOptions.TaskBarMode.Normal => StringNames.SettingNormalTaskMode, AmongUs.GameOptions.TaskBarMode.MeetingOnly => StringNames.SettingMeetingTaskMode, _ => StringNames.SettingInvisibleTaskMode }));
            AddOption(StringNames.GameCommonTasks, vanillaOptions.NumCommonTasks.ToString());
            AddOption(StringNames.GameLongTasks, vanillaOptions.NumLongTasks.ToString());
            AddOption(StringNames.GameShortTasks, vanillaOptions.NumShortTasks.ToString());
            AddOption(StringNames.GameVisualTasks, SimpleTrigger(vanillaOptions.VisualTasks));

            string hudString = "";
            if (TORMapOptions.gameMode == CustomGamemodes.HideNSeek) {
                hudString += buildOptionsOfType(CustomOptionType.HideNSeekMain, false) + buildOptionsOfType(CustomOptionType.HideNSeekRoles, false);
            } else if (TORMapOptions.gameMode == CustomGamemodes.Zombie) {
                hudString += buildOptionsOfType(CustomOptionType.ZombieMain, false);
            } else {
                hudString += buildOptionsOfType(CustomOptionType.General, false) + buildRoleOptions() + buildOptionsOfType(CustomOptionType.Impostor, false) +
                    buildOptionsOfType(CustomOptionType.Neutral, false) + buildOptionsOfType(CustomOptionType.Crewmate, false) +
                    buildOptionsOfType(CustomOptionType.Modifier, false);
            }

            return builder.ToString() + "\n\n" + hudString;
        }
    }

    [HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
    public class HudManagerUpdate {

        static PassiveButton toggleSettingsButton;
        static GameObject toggleSettingsButtonObject;
        static GameObject toggleZoomButtonObject;
        static PassiveButton toggleZoomButton;
        [HarmonyPostfix]
        public static void Postfix(HudManager __instance) {
            if (!toggleSettingsButton || !toggleSettingsButtonObject) {
                // add a special button for settings viewing:
                toggleSettingsButtonObject = GameObject.Instantiate(__instance.MapButton.gameObject, __instance.MapButton.transform.parent);
                toggleSettingsButtonObject.transform.localPosition = __instance.MapButton.transform.localPosition + new Vector3(0, -1.25f, -500f);
                toggleSettingsButtonObject.name = "TOGGLESETTINGSBUTTON";
                SpriteRenderer renderer = toggleSettingsButtonObject.transform.Find("Inactive").GetComponent<SpriteRenderer>();
                SpriteRenderer rendererActive = toggleSettingsButtonObject.transform.Find("Active").GetComponent<SpriteRenderer>();
                toggleSettingsButtonObject.transform.Find("Background").localPosition = Vector3.zero;
                renderer.sprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.Settings_Button.png", 100f);
                rendererActive.sprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.Settings_ButtonActive.png", 100);
                toggleSettingsButton = toggleSettingsButtonObject.GetComponent<PassiveButton>();
                toggleSettingsButton.OnClick.RemoveAllListeners();
                toggleSettingsButton.OnClick.AddListener((Action)(() => HelpMenu.TryOpenHelpScreen(HelpMenu.HelpTab.Options)));
            }
            toggleSettingsButtonObject.SetActive((GameStates.IsLobby ? __instance.SettingsButton?.gameObject?.active ?? false : __instance.MapButton?.gameObject?.active ?? false) && !(MapBehaviour.Instance && MapBehaviour.Instance.IsOpen) && GameOptionsManager.Instance.currentGameOptions.GameMode != GameModes.HideNSeek);
            toggleSettingsButtonObject.transform.localPosition = GameStates.IsLobby ? (__instance.SettingsButton?.transform?.localPosition ?? new Vector3()) + new Vector3(-1.45f, 0.03f, -200f) : __instance.MapButton.transform.localPosition + new Vector3(0, -0.8f, -500f);

            if (AmongUsClient.Instance.GameState != InnerNet.InnerNetClient.GameStates.Started) return;
            if (!toggleZoomButton || !toggleZoomButtonObject)
            {
                // add a special button for settings viewing:
                toggleZoomButtonObject = GameObject.Instantiate(__instance.MapButton.gameObject, __instance.MapButton.transform.parent);
                toggleZoomButtonObject.transform.localPosition = __instance.MapButton.transform.localPosition + new Vector3(0, -1.25f, -500f);
                toggleZoomButtonObject.name = "TOGGLEZOOMBUTTON";
                SpriteRenderer tZrenderer = toggleZoomButtonObject.transform.Find("Inactive").GetComponent<SpriteRenderer>();
                SpriteRenderer tZArenderer = toggleZoomButtonObject.transform.Find("Active").GetComponent<SpriteRenderer>();
                toggleZoomButtonObject.transform.Find("Background").localPosition = Vector3.zero;
                tZrenderer.sprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.Minus_Button.png", 100f);
                tZArenderer.sprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.Minus_ButtonActive.png", 100);
                toggleZoomButton = toggleZoomButtonObject.GetComponent<PassiveButton>();
                toggleZoomButton.OnClick.RemoveAllListeners();
                toggleZoomButton.OnClick.AddListener((Action)(() => Helpers.toggleZoom()));
            }
            if (PlayerControl.LocalPlayer != null)
            {
                var (playerCompleted, playerTotal) = TasksHandler.taskInfo(PlayerControl.LocalPlayer.Data);
                int numberOfLeftTasks = playerTotal - playerCompleted;
                bool zoomButtonActive = !(PlayerControl.LocalPlayer == null || !PlayerControl.LocalPlayer.Data.IsDead || (!RoleManager.IsGhostRole(PlayerControl.LocalPlayer.Data.RoleType) && !CustomGameModes.FreePlayGM.isFreePlayGM) || MeetingHud.Instance || ExileController.Instance);
                zoomButtonActive &= numberOfLeftTasks <= 0 || !CustomOptionHolder.finishTasksBeforeHauntingOrZoomingOut.getBool();
                toggleZoomButtonObject.SetActive(zoomButtonActive);
                var posOffset = Helpers.zoomOutStatus ? new Vector3(-1.27f, -7.92f, -52f) : new Vector3(0, -1.6f, -52f);
                toggleZoomButtonObject.transform.localPosition = HudManager.Instance.MapButton.transform.localPosition + posOffset;
            }
        }
    }
}
