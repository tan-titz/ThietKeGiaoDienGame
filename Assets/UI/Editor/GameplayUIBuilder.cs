using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GearUI.EditorTools
{
    /// <summary>
    /// Builds the gameplay screen (Build + Combat states) from the Figma file
    /// "Dựng lại UI trong Unity" (frames "Main" and "Main - Buy gear", 402x874).
    ///
    /// Every x/y/w/h below is a Figma render-bound in frame coordinates (px, top-left origin).
    /// The canvas reference resolution is the Figma frame (402x874) with Expand matching,
    /// so 1 canvas unit == 1 Figma px and extra screen space (9:21 tall phones, 3:4 tablets)
    /// is added around the anchored groups instead of squashing the layout.
    /// </summary>
    public static class GameplayUIBuilder
    {
        const string SpriteDir = "Assets/UI/Sprites/";
        const string FontDir = "Assets/UI/Fonts/";
        const string ScenePath = "Assets/Scenes/Gameplay.unity";
        const float FrameW = 402f, FrameH = 874f;

        // Build-state board is the combat board scaled by this factor (376.075 / 311 in Figma).
        const float BuildScale = 376.075439f / 311f;
        static readonly Vector2 CombatBoardOrigin = new Vector2(12f, 463f);
        static readonly Vector2 BuildBoardOrigin = new Vector2(13f, 199f);

        static readonly Color White = Color.white;
        static readonly Color Gold = new Color32(0xFF, 0xE5, 0x61, 0xFF);
        static readonly Color Cyan = new Color32(0x41, 0xE2, 0xFF, 0xFF);

        static TMP_FontAsset font;
        static Material matNavy, matBlack, matPlain;
        static readonly List<Behaviour> combatAnimators = new List<Behaviour>();

        // ------------------------------------------------------------------ menu

        [MenuItem("Tools/Gear UI/Build Gameplay Scene", priority = 0)]
        public static void BuildScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            AssetDatabase.ImportAsset(SpriteDir.TrimEnd('/'), ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
            EnsureFontAssets();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            combatAnimators.Clear();

            CreateCamera();
            CreateEventSystem();
            Build();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuildSettings(ScenePath);
            Debug.Log("[GearUI] Gameplay scene built: " + ScenePath);
        }

        [MenuItem("Tools/Gear UI/Preview Build State", priority = 20)]
        public static void PreviewBuild() => Preview(false);

        [MenuItem("Tools/Gear UI/Preview Combat State", priority = 21)]
        public static void PreviewCombat() => Preview(true);

        static void Preview(bool combat)
        {
            var c = Object.FindFirstObjectByType<GameplayUIController>(FindObjectsInactive.Include);
            if (c == null) { Debug.LogWarning("[GearUI] No GameplayUIController in the open scene."); return; }
            Undo.RegisterFullObjectHierarchyUndo(c.gameObject, "Preview state");
            if (combat) c.PreviewCombat(); else c.PreviewBuild();
            EditorSceneManager.MarkSceneDirty(c.gameObject.scene);
        }

        // ------------------------------------------------------------------ scene

        static void Build()
        {
            // Canvas
            var canvasGo = new GameObject("GameplayCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.layer = 5;
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = false;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(FrameW, FrameH);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            scaler.referencePixelsPerUnit = 100f;
            var root = canvasGo.transform;

            // Background: covers the whole screen, keeps the square art's aspect.
            var bgRoot = Stretch(NewRT("Background", root));
            var bg = Img(bgRoot, "bg_environment", 0, 0, 874, 874, "Environment");
            var bgRt = bg.rectTransform;
            bgRt.anchorMin = bgRt.anchorMax = new Vector2(0.5f, 0.5f);
            bgRt.pivot = new Vector2(0.5f, 0.5f);
            bgRt.anchoredPosition = Vector2.zero;
            var fitter = bg.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = 1f;

            var safe = Stretch(NewRT("SafeArea", root));
            safe.gameObject.AddComponent<SafeAreaFitter>();

            // ---- Combat world (enemies, boat) — anchored to the top like the HUD.
            var battle = FrameContainer("BattleLayer", safe, new Vector2(0.5f, 1f));
            var battleGroup = battle.gameObject.AddComponent<CanvasGroup>();
            BuildBattle(battle);

            var ammo = FrameContainer("AmmoCounters", safe, new Vector2(1f, 1f));
            var ammoGroup = ammo.gameObject.AddComponent<CanvasGroup>();
            BuildAmmo(ammo);

            // ---- Bottom: board (shared by both states)
            var bottom = FrameContainer("BottomArea", safe, new Vector2(0.5f, 0f));
            var board = NewRT("Board", bottom);
            Place(board, CombatBoardOrigin.x, CombatBoardOrigin.y, 311, 411);
            var boardParts = BuildBoard(board);

            // Purple shop bar stretches across the full width (tablets).
            var shopBar = NewRT("ShopBar", safe);
            shopBar.anchorMin = new Vector2(0f, 0f);
            shopBar.anchorMax = new Vector2(1f, 0f);
            shopBar.pivot = new Vector2(0.5f, 0f);
            shopBar.sizeDelta = new Vector2(0f, 201f);
            shopBar.anchoredPosition = new Vector2(0f, FrameH - 577f - 201f);
            var shopBarGroup = shopBar.gameObject.AddComponent<CanvasGroup>();
            var barImg = Img(shopBar, "bar_bottom", 0, 0, 402, 201, "BarBackground");
            Stretch(barImg.rectTransform);
            barImg.type = UnityEngine.UI.Image.Type.Sliced;

            var front = FrameContainer("BottomFront", safe, new Vector2(0.5f, 0f));
            var shop = Group("ShopControls", front, out var shopGroup);
            BuildShop(shop, out var startButton);

            var boosters = Group("Boosters", front, out var boostersGroup);
            BuildBoosters(boosters);

            // ---- HUD (shared)
            var hud = Stretch(NewRT("HUD", safe));
            var hudLeft = FrameContainer("HUD_Left", hud, new Vector2(0f, 1f));
            var hudCenter = FrameContainer("HUD_Center", hud, new Vector2(0.5f, 1f));
            var hudRight = FrameContainer("HUD_Right", hud, new Vector2(1f, 1f));
            BuildHud(hudLeft, hudCenter, hudRight, out var closeButton, out var bonusGroup);

            // ---- Ad banner placeholder (bottom, full width)
            var ad = NewRT("AdBanner", safe);
            ad.anchorMin = new Vector2(0f, 0f);
            ad.anchorMax = new Vector2(1f, 0f);
            ad.pivot = new Vector2(0.5f, 0f);
            ad.sizeDelta = new Vector2(0f, 96f);
            ad.anchoredPosition = Vector2.zero;
            var adImg = ad.gameObject.AddComponent<Image>();
            adImg.color = new Color32(0x16, 0x1A, 0x24, 0xFF);
            var adLabel = Txt(ad, "BANNER AD", 0, 0, 402, 96, 16, new Color(1, 1, 1, 0.35f), matPlain, "Label");
            Stretch(adLabel.rectTransform);

            // ---- Controller
            var controller = canvasGo.AddComponent<GameplayUIController>();
            controller.board = board;
            controller.buildBoardPosition = new Vector2(BuildBoardOrigin.x, -BuildBoardOrigin.y);
            controller.buildBoardScale = BuildScale;
            controller.combatBoardPosition = new Vector2(CombatBoardOrigin.x, -CombatBoardOrigin.y);
            controller.combatBoardScale = 1f;
            var buildOnly = new List<CanvasGroup> { shopBarGroup, shopGroup, bonusGroup };
            buildOnly.AddRange(boardParts.buildOnly);
            var combatOnly = new List<CanvasGroup> { battleGroup, ammoGroup, boostersGroup };
            combatOnly.AddRange(boardParts.combatOnly);
            controller.buildOnly = buildOnly.ToArray();
            controller.combatOnly = combatOnly.ToArray();
            controller.buildSlideDown = new[] { shopBar, shop };
            controller.slideBase = new[] { shopBar.anchoredPosition, shop.anchoredPosition };
            controller.combatAnimators = combatAnimators.ToArray();
            controller.startButton = startButton;
            controller.exitCombatButton = closeButton;
            controller.startState = GameplayState.Build;
            controller.PreviewBuild();
        }

        // ------------------------------------------------------------------ HUD

        static void BuildHud(RectTransform left, RectTransform center, RectTransform right, out Button closeButton, out CanvasGroup bonusGroup)
        {
            // Left: settings + gold
            var settings = Img(left, "hud_settings", 13.4f, 70.4f, 26.7f, 30.7f, "SettingsButton");
            MakeButton(settings);
            Img(left, "hud_gold_bg", 6f, 111.1f, 76f, 28.3f, "GoldBg");
            Img(left, "icon_sun", 9f, 108f, 34f, 34.7f, "GoldIcon");
            Txt(left, "165", 42f, 114.1f, 38f, 24f, 18f, Gold, matPlain, "GoldText");

            // Center: level, progress, difficulty
            Txt(center, "Level 12", 130f, 43f, 142f, 24f, 18f, White, matNavy, "LevelText");
            var progressBg = Img(center, "slider_bg", 62f, 82f, 277f, 10f, "ProgressBg");
            progressBg.type = UnityEngine.UI.Image.Type.Sliced;
            var progressFill = Img(center, "slider_fill_cyan", 63f, 83f, 103f, 8f, "ProgressFill");
            progressFill.type = UnityEngine.UI.Image.Type.Sliced;
            Img(center, "hud_skull_marker", 113.2f, 65.3f, 12.7f, 22.3f, "WaveMarker1");
            Img(center, "hud_skull_marker", 169.2f, 65.3f, 12.7f, 22.3f, "WaveMarker2");
            Img(center, "hud_skull_marker", 287.2f, 65.3f, 12.7f, 22.3f, "WaveMarker3");
            Img(center, "hud_flag_finish", 335f, 65f, 14f, 19.3f, "FinishFlag");
            Img(center, "hud_distance_pointer", 143f, 86f, 44f, 20f, "DistancePointer");
            Txt(center, "123m", 148f, 93f, 34f, 12f, 12f, White, matNavy, "DistanceText");
            Img(center, "hud_tag_hard", 174f, 117f, 54f, 16f, "DifficultyTag");
            Txt(center, "Hard", 182f, 118f, 38f, 16f, 14f, White, matNavy, "DifficultyText");

            // Right: close, speed, bonus gold (build only)
            var close = Img(right, "hud_close", 366.2f, 70.7f, 30f, 31.3f, "CloseButton");
            closeButton = MakeButton(close);
            var speed = Img(right, "hud_button_xspeed", 366f, 109f, 32f, 32f, "SpeedButton");
            MakeButton(speed);
            Txt(speed.rectTransform, "x2", 369f - 366f, 114f - 109f, 26f, 24f, 16f, White, matBlack, "Label");

            var bonus = Group("BonusGoldButton", right, out bonusGroup);
            var yellow = Img(bonus, "button_big_yellow", 274f, 153f, 120f, 54f, "Button");
            MakeButton(yellow);
            Img(bonus, "icon_sun", 301.1f, 163.1f, 30.8f, 31.2f, "Icon");
            Txt(bonus, "50", 331f, 167f, 33f, 29f, 24f, White, matNavy, "Amount");
            Img(bonus, "icon_video_ad", 361f, 146f, 34f, 24f, "VideoBadge");
        }

        // ------------------------------------------------------------------ Battle

        static void BuildBattle(RectTransform p)
        {
            var boat = Img(p, "boat", 197f, 195f, 194.7f, 156f, "Boat");
            Bob(boat, 1.5f, 0.5f);

            var enemies = NewRT("Enemies", p); Stretch(enemies);
            Bob(Img(enemies, "mob_flying", 71.1f, 131f, 40f, 27.3f, "Mob_Flying_1"), 3f, 0.9f);
            Bob(Img(enemies, "mob_flying", 150.1f, 146f, 40f, 27.3f, "Mob_Flying_2"), 3f, 0.9f);
            Bob(Img(enemies, "mob_melee", 110f, 352.1f, 39.7f, 34f, "Mob_Melee_1"));
            Bob(Img(enemies, "mob_melee", 10f, 351.1f, 39.7f, 34f, "Mob_Melee_2"));
            Bob(Img(enemies, "mob_melee", 160f, 257.1f, 39.7f, 34f, "Mob_Melee_3"));
            Bob(Img(enemies, "mob_melee", 62f, 343.1f, 39.7f, 34f, "Mob_Melee_4"));
            Bob(Img(enemies, "mob_melee", 141f, 308.1f, 39.7f, 34f, "Mob_Melee_5"));
            Bob(Img(enemies, "mob_range", 32f, 391f, 44f, 36.3f, "Mob_Range_1"));
            Bob(Img(enemies, "mob_range", 109f, 228f, 44f, 36.3f, "Mob_Range_2"));
            Bob(Img(enemies, "mob_range", 173f, 275f, 44f, 36.3f, "Mob_Range_3"));

            var crew = NewRT("Crew", p); Stretch(crew);
            Img(crew, "char_nerd", 280.5f, 266.5f, 28.3f, 40.3f, "Char_Nerd");
            Img(crew, "char_emo", 307.4f, 225.4f, 25f, 37f, "Char_Emo");
            Img(crew, "char_tomboy", 337.4f, 231.4f, 30f, 44f, "Char_Tomboy");

            Bob(Img(enemies, "mob_fast", 169f, 338.4f, 48f, 45f, "Mob_Fast"));

            var bars = NewRT("HealthBars", p); Stretch(bars);
            HpBar(bars, "Hp_Mob_A", 189f, 262f, 29f, 25f, false);
            HpBar(bars, "Hp_Mob_B", 164f, 244f, 29f, 13f, false);
            HpBar(bars, "Hp_Boat", 302f, 168f, 39f, 29f, true);
            HpBar(bars, "Hp_Mob_C", 156f, 134f, 29f, 23f, false);
            HpBar(bars, "Hp_Boss", 15f, 195f, 99f, 75f, false);
            Txt(bars, "120", 309f, 165f, 24f, 12f, 12f, White, matBlack, "Hp_Boat_Text");

            var status = NewRT("BoatStatus", p); Stretch(status);
            Img(status, "icon_status_poison", 314f, 180f, 15.7f, 15.7f, "PoisonIcon");
            Txt(status, "2", 322f, 189f, 11f, 9f, 12f, White, matBlack, "PoisonStacks");
            Img(status, "icon_status_shield", 313f, 142f, 16.3f, 16.3f, "ShieldIcon");
            Txt(status, "89", 309f, 153f, 24f, 12f, 12f, Cyan, matBlack, "ShieldValue");

            Img(crew, "char_sexy", 311.8f, 272.8f, 34.7f, 37.7f, "Char_Sexy");
            crew.SetAsLastSibling();

            var boss = Img(p, "boss1", 0f, 215f, 149.6f, 123.3f, "Boss");
            Bob(boss, 2f, 0.6f);
            HpBarOnTop(bars);
        }

        static void HpBarOnTop(RectTransform bars) => bars.SetAsLastSibling();

        static void HpBar(RectTransform p, string name, float x, float y, float w, float fillW, bool friendly)
        {
            var root = NewRT(name, p);
            Place(root, x, y, w, 10f);
            var bg = Img(root, "slider_bg", 0f, 0f, w, 10f, "Bg");
            bg.type = UnityEngine.UI.Image.Type.Sliced;
            var fill = Img(root, friendly ? "slider_fill_green" : "slider_fill_red", 1f, 1f, fillW, 8f, "Fill");
            fill.type = UnityEngine.UI.Image.Type.Sliced;
        }

        static void BuildAmmo(RectTransform p)
        {
            AmmoRow(p, 306f, "bullet_pumpkin", 311f, "2", "Ammo_Pumpkin");
            AmmoRow(p, 334f, "bullet_chili", 338f, "0", "Ammo_Chili");
            AmmoRow(p, 362f, "bullet_berry", 367f, "5", "Ammo_Berry");
            AmmoRow(p, 390f, "bullet_pea", 395f, "13", "Ammo_Pea");
        }

        static void AmmoRow(RectTransform p, float y, string bullet, float bulletY, string count, string name)
        {
            var row = NewRT(name, p);
            Place(row, 360f, y, 53f, 22f);
            Img(row, "ammo_bg", 0f, 0f, 53f, 22f, "Bg");
            Img(row, bullet, 6f, bulletY - y, 12.3f, 12.3f, "Icon");
            Txt(row, count, 23f, 8f, 16f, 9f, 14f, White, matPlain, "Count");
        }

        // ------------------------------------------------------------------ Board

        struct BoardParts
        {
            public CanvasGroup[] buildOnly;
            public CanvasGroup[] combatOnly;
        }

        /// <summary>Board children use board-local coordinates (Figma combat coords minus (12,463)).</summary>
        static BoardParts BuildBoard(RectTransform b)
        {
            Img(b, "board_panel", 0f, 0f, 311f, 411f, "Panel");
            Img(b, "board_top_bg", 8f, 8f, 295f, 100f, "SlotsBackground");

            // Stat bars: red/green with trend arrows in combat, navy while building.
            var statsCombat = Group("StatBars_Combat", b, out var statsCombatGroup);
            Img(statsCombat, "stat_bar_red", 18f, 13f, 136f, 22f, "SpeedBar");
            Img(statsCombat, "stat_bar_green", 159f, 13f, 137f, 22f, "FireRateBar");
            Img(statsCombat, "arrow_down", 132f, 20f, 17.3f, 11.3f, "SpeedTrend");
            Img(statsCombat, "arrow_up", 275f, 18f, 17f, 11f, "FireRateTrend");

            var statsBuild = Group("StatBars_Build", b, out var statsBuildGroup);
            Img(statsBuild, "stat_bar_navy", 17.37f, 13.23f, 136.45f, 21.5f, "SpeedBar");
            Img(statsBuild, "stat_bar_navy", 158.78f, 13.23f, 137.27f, 21.5f, "FireRateBar");

            var stats = NewRT("Stats", b); Stretch(stats);
            Img(stats, "icon_move_speed", 27f, 16f, 17.7f, 15f, "SpeedIcon");
            Txt(stats, "52.3mph", 57f, 16f, 62f, 19f, 12f, White, matPlain, "SpeedValue");
            Img(stats, "icon_fire_rate", 166f, 16f, 21f, 16f, "FireRateIcon");
            Txt(stats, "73.3", 199f, 16f, 62f, 19f, 14f, White, matPlain, "FireRateValue");

            // Weapon slots row
            var slots = NewRT("WeaponSlots", b); Stretch(slots);
            Slot(slots, "Slot_1", "slot_active", 18f, "boat_wheel", 20f, 49f, 40f, 40f, "0.24/s", 7f, true);
            Slot(slots, "Slot_2", "slot_active", 66f, "gun_pea_slot", 66f, 48f, 42f, 42f, "1.17/s", 54f, true);
            Slot(slots, "Slot_3", "slot_empty", 112f, "gun_berry_slot", 112f, 48f, 42f, 42f, null, 0f, false);
            Slot(slots, "Slot_4", "slot_disabled", 159f, null, 0, 0, 0, 0, null, 0f, false);
            Slot(slots, "Slot_5", "slot_disabled", 206f, null, 0, 0, 0, 0, null, 0f, false);
            Slot(slots, "Slot_6", "slot_empty", 253f, "boat_sail", 257f, 48f, 36f, 43.3f, null, 0f, false);

            var wheel = slots.Find("Slot_1/Item");
            if (wheel != null) combatAnimators.Add(AddSpinner(wheel.gameObject, -90f));

            Img(slots, "gear_connected_hl", 23f, 86f, 32f, 32f, "Link_1");
            Img(slots, "gear_connected_hl", 71f, 86f, 32f, 32f, "Link_2");
            Img(slots, "gear_connected", 116f, 86f, 32f, 32f, "Link_3");
            Img(slots, "gear_connected", 258f, 86f, 32f, 32f, "Link_6");

            // Ammo bonus tags (combat only) sit on top of slots 2 and 3.
            var tags = Group("AmmoTags_Combat", b, out var tagsGroup);
            var tagA = NewRT("AmmoTag_Pea", tags); Place(tagA, 70f, 37f, 34f, 14f);
            Img(tagA, "tag_small", 0f, 0f, 34f, 14f, "Bg");
            Img(tagA, "bullet_pea", 4f, 1f, 12.3f, 12.3f, "Icon");
            Txt(tagA, "+1", 14f, 3f, 16f, 9f, 10f, White, matPlain, "Value");
            var tagB = NewRT("AmmoTag_Berry", tags); Place(tagB, 116f, 37f, 34f, 14f);
            Img(tagB, "tag_small", 0f, 0f, 34f, 14f, "Bg");
            Img(tagB, "bullet_berry", 3f, 1f, 12.3f, 12.3f, "Icon");
            Txt(tagB, "+2", 14f, 3f, 16f, 9f, 10f, White, matPlain, "Value");

            // Lily-pad grid
            Img(b, "board_pads", 10f, 114f, 292f, 192.3f, "Pads");

            var itemsCombat = Group("Items_Combat", b, out var itemsCombatGroup);
            BuildBoardItemsCombat(itemsCombat);
            var itemsBuild = Group("Items_Build", b, out var itemsBuildGroup);
            BuildBoardItemsBuild(itemsBuild);

            return new BoardParts
            {
                combatOnly = new[] { statsCombatGroup, tagsGroup, itemsCombatGroup },
                buildOnly = new[] { statsBuildGroup, itemsBuildGroup },
            };
        }

        static void Slot(RectTransform p, string name, string frame, float x, string item, float ix, float iy, float iw, float ih, string rate, float rateX, bool hasRate)
        {
            var slot = NewRT(name, p);
            Place(slot, x, 40f, 42f, 60f);
            Img(slot, frame, 0f, 0f, 42f, 60f, "Frame");
            if (item != null) Img(slot, item, ix - x, iy - 40f, iw, ih, "Item");
            if (hasRate) Txt(slot, rate, rateX - x, 75f - 40f, 65f, 24f, 10f, White, matNavy, "Rate");
        }

        static void BuildBoardItemsCombat(RectTransform items)
        {
            // Order follows the Figma layer order (later = on top).
            Gear(items, "gear_10", 14.7f, 111.2f, 48f, "2", 31f, 124.8f);
            Gear(items, "gear_12", 203f, 203f, 48f, "1", 220.3f, 218f);
            Gear(items, "gear_4", 155.7f, 156f, 48f, "4", 173f, 172f);
            Gear(items, "gear_9", 13f, 156f, 48f, "4", 30.3f, 172f);
            Gear(items, "gear_8", 62.6f, 111f, 48f, "8", 80f, 125f);
            Gear(items, "gear_2", 157.7f, 202.2f, 48f, "2", 174f, 215.8f);
            Img(items, "gear_weapon", 157.8f, 248.8f, 48.7f, 48.7f, "Gear_Weapon_1");
            Img(items, "gear_weapon", 203.8f, 114.8f, 48.7f, 48.7f, "Gear_Weapon_2");
            Gear(items, "gear_11", 203f, 158f, 48f, "1", 220.3f, 173f);
            Img(items, "gear_stats", 13.8f, 203.8f, 48.7f, 48.7f, "Gear_Stats");
            var rotor = Img(items, "gear_rotor", 44f, 148f, 82.3f, 82.3f, "Gear_Rotor");
            combatAnimators.Add(AddSpinner(rotor.gameObject, -150f));
            Img(items, "gear_x15", 108f, 250f, 48f, 48f, "Gear_X1_5");
            Img(items, "x15_label", 115f, 266f, 37f, 20f, "X1_5_Label");
            Gear(items, "gear_1", 108.7f, 158f, 48f, "1", 126f, 173f);
            Img(items, "gear_frozen", 108f, 251f, 48f, 48f, "Gear_Frozen");
            Img(items, "gear_frozen_mask", 158f, 221f, 48f, 30f, "Frozen_Overlay");
            Img(items, "stone", 249f, 252f, 48f, 48f, "Stone");
            Img(items, "gear_ad", 203f, 204f, 48f, 48f, "Gear_AdLocked");
            Img(items, "gear_earning", 12.8f, 249.8f, 48.7f, 48.7f, "Gear_Earning");
            Img(items, "gear_spider", 156f, 114f, 48f, 48f, "Gear_Spider");
            Img(items, "peanut_drone", 210f, 122f, 34f, 34f, "Icon_PeanutDrone");
            Img(items, "flower_crit_dmg", 18f, 210f, 36f, 36f, "Icon_CritDmg");
            Img(items, "flower_extra_gold", 18f, 255.6f, 36.3f, 37.3f, "Icon_ExtraGold");
        }

        static void BuildBoardItemsBuild(RectTransform items)
        {
            // Build frame coordinates converted into board space: ((x-13)/S, (y-199)/S).
            Gear(items, "gear_10", 14.72f, 111.23f, 48f, "2", 31.01f, 124.79f);
            Gear(items, "gear_8", 62.68f, 110.98f, 48f, "8", 79.97f, 125.04f);
            Gear(items, "gear_1", 108.66f, 158.03f, 48f, "1", 126.03f, 173f);
            Gear(items, "gear_12", 203.02f, 203.02f, 48f, "1", 220.3f, 217.99f);
            Gear(items, "gear_4", 155.72f, 155.96f, 48f, "4", 173f, 172f);
            Gear(items, "gear_9", 12.98f, 155.96f, 48f, "4", 30.27f, 172f);
            Gear(items, "gear_2", 157.7f, 202.19f, 48f, "2", 173.99f, 215.75f);
            Img(items, "gear_weapon", 157.78f, 248.83f, 48.7f, 48.7f, "Gear_Weapon_1");
            Img(items, "gear_weapon", 203.76f, 114.78f, 48.7f, 48.7f, "Gear_Weapon_2");
            Gear(items, "gear_11", 203.02f, 158.03f, 48f, "1", 220.3f, 173f);
            Img(items, "gear_earning", 15.8f, 249.83f, 48.7f, 48.7f, "Gear_Earning");
            Img(items, "gear_stats", 251.81f, 249.83f, 48.7f, 48.7f, "Gear_Stats");
            Img(items, "gear_rotor", 43.99f, 148.03f, 82.3f, 82.3f, "Gear_Rotor");
            Img(items, "flower_regen", 24.81f, 258.84f, 29.77f, 29.77f, "Icon_Regeneration");
            Img(items, "sunbloom_burst", 168.7f, 259.67f, 28.1f, 28.1f, "Icon_SunbloomBurst");
            Img(items, "heart_boom", 213.36f, 122.39f, 28.1f, 28.1f, "Icon_HeartBoom");
            Img(items, "flower_luck", 261.32f, 258.84f, 29.77f, 29.77f, "Icon_Luck");
        }

        static void Gear(RectTransform p, string sprite, float x, float y, float size, string number, float tx, float ty)
        {
            var img = Img(p, sprite, x, y, size, size, "Gear_" + sprite.Replace("gear_", "") + "_" + number);
            Txt(img.rectTransform, number, tx - x, ty - y, 14f, 24f, 28f, White, matNavy, "Level");
        }

        // ------------------------------------------------------------------ Bottom controls

        static void BuildShop(RectTransform p, out Button startButton)
        {
            Img(p, "white_rec", 16f, 696f, 370f, 73f, "ButtonsTray");

            ShopCard(p, "ShopCard_1", 16f, "gear_14", 28f, 614f, "1", 48.9f, 632.1f, "5", 31.5f, 30f, 50f);
            ShopCard(p, "ShopCard_2", 112f, "gear_13", 124f, 614f, "2", 143.7f, 630.4f, "10", 126.5f, 125f, 145f);
            var free = ShopCard(p, "ShopCard_3", 208f, "gear_15", 221f, 615f, "4", 240f, 631f, "Free", 222.5f, 221f, 241f);
            Img(free, "info_badge", 202.9f - 208f, 598.9f - 602f, 24.3f, 24.3f, "InfoBadge");
            Img(free, "icon_video_ad", 261f - 208f, 599f - 602f, 34f, 24f, "VideoBadge");
            var empty = Img(p, "button_buy_gear", 304f, 602f, 82f, 82f, "ShopCard_4");
            MakeButton(empty);

            var refresh = Img(p, "button_big_green", 63f, 706f, 102f, 54f, "RefreshButton");
            MakeButton(refresh);
            Txt(refresh.rectTransform, "Refresh", 76f - 63f, 711f - 706f, 76f, 28f, 16f, White, matNavy, "Label");
            Img(refresh.rectTransform, "icon_sun", 96.5f - 63f, 732.5f - 706f, 19.3f, 19.3f, "CostIcon");
            Txt(refresh.rectTransform, "5", 115f - 63f, 730f - 706f, 16.9f, 29f, 20f, White, matNavy, "Cost");

            var start = Img(p, "button_big_cyan", 173f, 706f, 172f, 54f, "StartButton");
            startButton = MakeButton(start);
            Txt(start.rectTransform, "START!", 188f - 173f, 720f - 706f, 146f, 29f, 24f, White, matNavy, "Label");
        }

        static RectTransform ShopCard(RectTransform p, string name, float x, string gear, float gx, float gy, string level, float lx, float ly, string price, float sunX, float tagX, float priceX)
        {
            var card = Img(p, "button_buy_gear", x, 602f, 82f, 82f, name);
            MakeButton(card);
            var rt = card.rectTransform;
            var g = Img(rt, gear, gx - x, gy - 602f, 58f, 58f, "Gear");
            Txt(g.rectTransform, level, lx - gx, ly - gy, 16.9f, 29f, 33.86f, White, matNavy, "Level");
            Img(rt, "price_tag", tagX - x, 658f - 602f, 55f, 22f, "PriceTag");
            Img(rt, "icon_sun", sunX - x, 659.5f - 602f, 19.3f, 19.3f, "PriceIcon");
            Txt(rt, price, priceX - x, 665f - 602f, 31f, 12f, 16f, White, matPlain, "Price");
            return rt;
        }

        static void BuildBoosters(RectTransform p)
        {
            for (int i = 0; i < 4; i++)
            {
                float y = 491f + i * 73f;
                var btn = Img(p, "button_booster", 334f, y, 58f, 58f, "Booster_" + (i + 1));
                MakeButton(btn);
                var rt = btn.rectTransform;
                if (i == 0) Img(rt, "icon_booster_heal", 12f, 12f, 36f, 36f, "Icon");
                Img(rt, "dot_green", 37.9f, 37.9f, 24.3f, 24.3f, "CountBadge");
                Txt(rt, "3", 43f, 39f, 14f, 24f, 18f, White, matNavy, "Count");
            }
        }

        // ------------------------------------------------------------------ helpers

        static RectTransform NewRT(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = 5 };
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        static RectTransform Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        /// <summary>A 402x874 container using Figma frame coordinates, pinned to an anchor of its parent.</summary>
        static RectTransform FrameContainer(string name, Transform parent, Vector2 anchor)
        {
            var rt = NewRT(name, parent);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.sizeDelta = new Vector2(FrameW, FrameH);
            rt.anchoredPosition = Vector2.zero;
            return rt;
        }

        static RectTransform Group(string name, RectTransform parent, out CanvasGroup group)
        {
            var rt = Stretch(NewRT(name, parent));
            group = rt.gameObject.AddComponent<CanvasGroup>();
            return rt;
        }

        /// <summary>Top-left placement in the parent's Figma coordinate space.</summary>
        static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        static Sprite LoadSprite(string name)
        {
            var s = AssetDatabase.LoadAssetAtPath<Sprite>(SpriteDir + name + ".png");
            if (s == null) Debug.LogError("[GearUI] Missing sprite: " + name);
            return s;
        }

        static Image Img(Transform parent, string sprite, float x, float y, float w, float h, string name)
        {
            var rt = NewRT(name ?? sprite, parent);
            Place(rt, x, y, w, h);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = LoadSprite(sprite);
            img.raycastTarget = false;
            return img;
        }

        static TextMeshProUGUI Txt(Transform parent, string text, float x, float y, float w, float h, float size, Color color, Material mat, string name)
        {
            var rt = NewRT(name, parent);
            Place(rt, x, y, w, h);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = font;
            t.fontSharedMaterial = mat;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.raycastTarget = false;
            return t;
        }

        static Button MakeButton(Image img)
        {
            img.raycastTarget = true;
            var b = img.gameObject.AddComponent<Button>();
            b.transition = Selectable.Transition.ColorTint;
            var colors = b.colors;
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            b.colors = colors;
            img.gameObject.AddComponent<PressScale>();
            // Scale around the centre. Children are anchored to the parent's rect corners,
            // so moving the pivot does not move them.
            var rt = img.rectTransform;
            var size = rt.sizeDelta;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition += new Vector2(size.x * 0.5f, -size.y * 0.5f);
            return b;
        }

        static Spinner AddSpinner(GameObject go, float speed)
        {
            var rt = (RectTransform)go.transform;
            var size = rt.sizeDelta;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition += new Vector2(size.x * 0.5f, -size.y * 0.5f);
            var s = go.AddComponent<Spinner>();
            s.degreesPerSecond = speed;
            s.enabled = false;
            return s;
        }

        static void Bob(Image img, float amplitude = 1.5f, float frequency = 0.8f)
        {
            var b = img.gameObject.AddComponent<Bobber>();
            b.amplitude = amplitude;
            b.frequency = frequency;
            b.enabled = false;
            combatAnimators.Add(b);
        }

        static void CreateCamera()
        {
            var go = new GameObject("Main Camera", typeof(Camera));
            go.tag = "MainCamera";
            var cam = go.GetComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color32(0x3B, 0x8F, 0xC8, 0xFF);
            go.transform.position = new Vector3(0, 0, -10);
        }

        static void CreateEventSystem()
        {
            var go = new GameObject("EventSystem", typeof(EventSystem));
            var inputModule = System.Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (inputModule != null) go.AddComponent(inputModule);
            else go.AddComponent<StandaloneInputModule>();
        }

        static void AddSceneToBuildSettings(string path)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (scenes.Exists(s => s.path == path)) return;
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        // ------------------------------------------------------------------ fonts

        static void EnsureFontAssets()
        {
            string faPath = FontDir + "Lalezar SDF.asset";
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(faPath);
            if (font == null)
            {
                var ttf = AssetDatabase.LoadAssetAtPath<Font>(FontDir + "Lalezar-Regular.ttf");
                font = TMP_FontAsset.CreateFontAsset(ttf, 90, 9, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, false);
                font.name = "Lalezar SDF";
                AssetDatabase.CreateAsset(font, faPath);
                font.atlasTextures[0].name = "Lalezar SDF Atlas";
                AssetDatabase.AddObjectToAsset(font.atlasTextures[0], font);
                font.material.name = "Lalezar SDF Material";
                AssetDatabase.AddObjectToAsset(font.material, font);
                font.TryAddCharacters("0123456789.,+-/!?:%x ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz");
                EditorUtility.SetDirty(font);
                AssetDatabase.SaveAssets();
            }

            matPlain = font.material;
            matNavy = OutlineMaterial("Lalezar SDF - Outline Navy", new Color32(0x2A, 0x3C, 0x5E, 0xFF), 0.3f);
            matBlack = OutlineMaterial("Lalezar SDF - Outline Black", Color.black, 0.3f);
        }

        static Material OutlineMaterial(string name, Color color, float width)
        {
            string path = FontDir + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(font.material) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = font.material.shader;
            m.SetTexture(ShaderUtilities.ID_MainTex, font.atlasTexture);
            m.SetColor(ShaderUtilities.ID_OutlineColor, color);
            m.SetFloat(ShaderUtilities.ID_OutlineWidth, width);
            m.SetFloat(ShaderUtilities.ID_FaceDilate, 0.1f);
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
