﻿using HollowKnight.Rando3Stats.Stats;
using HollowKnight.Rando3Stats.UI;
using Modding;
using SereCore;
using System.Linq;
using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using Rando = RandomizerMod.RandomizerMod;

namespace HollowKnight.Rando3Stats
{
    public class RandoStats : Mod
    {
        public static RandoStats? Instance { get; private set; }

        private const string END_GAME_COMPLETION = "End_Game_Completion";
        private const float LENGTH_OF_PRESS_TO_SKIP = 1.5f;

        private const float HORIZONTAL_PADDING = 5;
        private const float VERTICAL_PADDING = 10;

        private const float VERTICAL_SPACING = 8;
        private const float HORIZONTAL_SPACING = 10;

        private const int FONT_SIZE_H1 = 25;
        private const int FONT_SIZE_H2 = 18;
        private const int FONT_SIZE_H3 = 15;

        private float pressStartTime = 0;
        private bool holdToSkipLock = false;

        private AlignedRect? progressRect;
        private AlignedText? clipboardPrompt;
        private Layout? refLayout;

        public override ModSettings GlobalSettings
        {
            get => Settings;
            set => Settings = value is RandoStatsGlobalSettings gs ? gs : Settings;
        }

        public RandoStatsGlobalSettings Settings { get; private set; } = new RandoStatsGlobalSettings();

        public override string GetVersion()
        {
            string ver = "1.1.1";
            int minAPI = 45;

            bool apiTooLow = Convert.ToInt32(ModHooks.Instance.ModVersion.Split('-')[1]) < minAPI;
            if (apiTooLow)
            {
                ver += " (Update Modding API)";
            }
            return ver;
        }

        public override void Initialize()
        {
            if (Instance != null)
            {
                Instance.LogWarn("Initialized already!");
                return;
            }

            Instance = this;

            Log("RandoStats initializing...");

#if DEBUG
            On.GameManager.BeginSceneTransition += SkipTHK;
#endif
            On.GameCompletionScreen.Start += GameCompletionScreen_Start;
            On.InputHandler.CutsceneInput += InputHandler_CutsceneInput;

            ModHooks.Instance.LanguageGetHook += GetLanguageString;

            Log("RandoStats finished initializing.");
        }

        private string GetLanguageString(string key, string sheetTitle)
        {
            if (key == "PERMA_GAME_OVER_CONTINUE" && sheetTitle == "Credits List")
            {
                return "Hold any button to continue";
            }
            return Language.Language.GetInternal(key, sheetTitle);
        }

        private void GameCompletionScreen_Start(On.GameCompletionScreen.orig_Start orig, GameCompletionScreen self)
        {
            bool isRando = Rando.Instance.Settings.Randomizer;
            if (isRando)
            {
                // we don't need to see the recent items panel on the end screen, clear it out to make more room for stats!
                Assembly randoAsm = Assembly.GetAssembly(typeof(Rando));
                Type recents = randoAsm.GetType("RandomizerMod.RecentItems");
                MethodInfo hideRecents = recents.GetMethod("Hide", BindingFlags.Public | BindingFlags.Static);
                hideRecents.Invoke(null, null);

                holdToSkipLock = false;
                GameObject canvas = GuiManager.Instance.GetCanvasForScene("StatsCanvas");

                Log("Calculating statistics");

                IRandomizerStatistic totalLocationStat = new TotalLocationsChecked("Total");
                Layout totalLocationStatText = GetStatText(canvas, totalLocationStat);

                IRandomizerStatistic totalItemStat = new TotalItemsObtained("Total");
                Layout totalItemStatText = GetStatText(canvas, totalItemStat);

                IToggleableStatistic totalTransitionStat = new TotalTransitionsFound("Total");

                Layout locationPoolStatGroup = new DynamicGridLayout(canvas, HORIZONTAL_SPACING, VERTICAL_SPACING, 2, HorizontalAlignment.Center);
                refLayout = locationPoolStatGroup;
                foreach (IToggleableStatistic poolStat in LocationsCheckedByPoolGroup.GetAllPoolGroups())
                {
                    if (poolStat.IsEnabled)
                    {
                        locationPoolStatGroup.Children.Add(GetStatText(canvas, poolStat));
                    }
                }
                IRandomizerStatistic geoShopLocationStat = new GeoShopChecksSeen("Geo Shops");
                locationPoolStatGroup.Children.Add(GetStatText(canvas, geoShopLocationStat));

                Layout itemPoolStatGroup = new DynamicGridLayout(canvas, HORIZONTAL_SPACING, VERTICAL_SPACING, 2, HorizontalAlignment.Center);
                foreach (IToggleableStatistic poolStat in ItemsObtainedByPoolGroup.GetAllPoolGroups())
                {
                    if (poolStat.IsEnabled)
                    {
                        itemPoolStatGroup.Children.Add(GetStatText(canvas, poolStat));
                    }
                }

                Layout statGroupTopLeft = new VerticalStackLayout(canvas, VERTICAL_SPACING);
                statGroupTopLeft.Children.Add(new AlignedText(canvas, "Locations Found", GuiManager.Instance.TrajanBold, FONT_SIZE_H1)
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                });
                statGroupTopLeft.Children.Add(totalLocationStatText);
                statGroupTopLeft.Children.Add(locationPoolStatGroup);

                Layout statGroupTopRight = new VerticalStackLayout(canvas, VERTICAL_SPACING, HorizontalAlignment.Right);
                statGroupTopRight.Children.Add(new AlignedText(canvas, "Items Obtained", GuiManager.Instance.TrajanBold, FONT_SIZE_H1)
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                });
                statGroupTopRight.Children.Add(totalItemStatText);
                statGroupTopRight.Children.Add(itemPoolStatGroup);

                Layout statGroupBottomRight = new VerticalStackLayout(canvas, VERTICAL_SPACING, HorizontalAlignment.Right, VerticalAlignment.Bottom);

                if (totalTransitionStat.IsEnabled)
                {
                    Layout totalTransitionStatText = GetStatText(canvas, totalTransitionStat);
                    statGroupBottomRight.Children.Add(new AlignedText(canvas, "Transitions Found", GuiManager.Instance.TrajanBold, FONT_SIZE_H1)
                    {
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center
                    });
                    statGroupBottomRight.Children.Add(totalTransitionStatText);

                    Layout transitionAreaStatGroup = new DynamicGridLayout(canvas, HORIZONTAL_SPACING, VERTICAL_SPACING, 2, HorizontalAlignment.Center);
                    foreach (IToggleableStatistic areaStat in TransitionsFoundByArea.GetAllAreas())
                    {
                        if (areaStat.IsEnabled)
                        {
                            transitionAreaStatGroup.Children.Add(GetStatText(canvas, areaStat));
                        }
                    }
                    statGroupBottomRight.Children.Add(transitionAreaStatGroup);
                }

                Log("Starting layout step.");

                statGroupTopLeft.PositionAt(new Vector2(HORIZONTAL_PADDING, VERTICAL_PADDING));
                statGroupTopRight.PositionAt(new Vector2(1920 - HORIZONTAL_PADDING, VERTICAL_PADDING));
                statGroupBottomRight.PositionAt(new Vector2(1920 - HORIZONTAL_PADDING, 1080 - VERTICAL_PADDING));

                progressRect = new AlignedRect(canvas, Color.white, 40, 40, "ProgressRect")
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                progressRect.PositionAt(new Vector2(960, 1060));

                clipboardPrompt = new AlignedText(canvas, "Press Ctrl+C to copy completion", GuiManager.Instance.TrajanNormal, FONT_SIZE_H2, "CopyPrompt")
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                // team cherry why (everything is off-center by a different amount, this is roughly centered on the "hold any button" text
                clipboardPrompt.PositionAt(new Vector2(980, 955));

                Log("Completion screen setup complete");
            }
            else
            {
                Log("Not randomizer, skipping stats");
            }
            orig(self);
        }
        
        private bool AnyKeyExcept(params KeyCode[] keys)
        {
            return Input.anyKey && !keys.Any(Input.GetKey);
        }

        private void InputHandler_CutsceneInput(On.InputHandler.orig_CutsceneInput orig, InputHandler self)
        {
            string scene = GameManager.instance.GetSceneNameString();
            if (scene != END_GAME_COMPLETION)
            {
                // if we're in any other cutscene, just do the default behavior.
                orig(self);
            }
            else
            {
                if (holdToSkipLock) return;
                // if these don't exist something has gone badly; just do the default input behaviour instead
                if (progressRect == null || clipboardPrompt == null)
                {
                    orig(self);
                    return;
                }

                bool held = AnyKeyExcept(KeyCode.LeftControl, KeyCode.RightControl, KeyCode.LeftAlt, KeyCode.RightAlt)
                    || self.gameController.AnyButton.IsPressed;

                // if ctrl is held, trigger on the frame where c pressed
                if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && Input.GetKeyDown(KeyCode.C))
                {
                    StatFormatRegistry.GenerateBasicStats();
                    GUIUtility.systemCopyBuffer = StatFormatRegistry.Format(Settings.CompletionFormatString);
                    clipboardPrompt.Text = "Copied!";
                }

                if (held)
                {
                    if (pressStartTime <= float.Epsilon)
                    {
                        pressStartTime = Time.time;
                    }
                    else if (Time.time > pressStartTime + LENGTH_OF_PRESS_TO_SKIP)
                    {
                        // we've elapsed the designated time while held; we can now skip the cutscene.
                        // we should now further block the hold-to-skip behavior and animation until the next
                        // time we load into this scene.
                        holdToSkipLock = true;
                        GameManager.instance.SkipCutscene();
                        // fade our stuff out too
                        GameObject.Find("StatsCanvas").AddComponent<CanvasGroupLinearFade>().duration = 0.5f;
                    }
                    float progressPercentage = (Time.time - pressStartTime) / LENGTH_OF_PRESS_TO_SKIP;
                    progressRect.Width = GuiManager.ReferenceSize.x * progressPercentage;
                }
                else
                {
                    pressStartTime = 0;
                    progressRect.Width = 0;
                }
            }
        }

        private Layout GetStatText(GameObject canvas, IRandomizerStatistic stat)
        {
            string header = stat.GetHeader();
            string text = stat.GetContent();
            Layout statStack = new VerticalStackLayout(canvas, 5f, HorizontalAlignment.Center);
            statStack.Children.Add(new AlignedText(canvas, header, GuiManager.Instance.TrajanBold, FONT_SIZE_H2, "Stat_" + header)
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            statStack.Children.Add(new AlignedText(canvas, text, GuiManager.Instance.TrajanNormal, FONT_SIZE_H3, "StatValue_" + header)
            { 
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            return statStack;
        }

        private void SkipTHK(On.GameManager.orig_BeginSceneTransition orig, GameManager self, GameManager.SceneLoadInfo info)
        {
            if (info.SceneName == SceneNames.Room_Final_Boss_Core)
            {
                info.SceneName = "End_Game_Completion";
            }
            orig(self, info);
        }
    }
}
