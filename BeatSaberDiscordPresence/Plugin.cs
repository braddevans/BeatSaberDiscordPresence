#region

using System;
using System.Collections;
using System.Configuration;
using System.Linq;
using System.Reflection;
using BS_Utils.Gameplay;
using HarmonyLib;
using IPA;
using IPA.Config;
using IPA.Loader;
using UnityEngine;
using UnityEngine.SceneManagement;
using IPALogger = IPA.Logging.Logger;
using Object = UnityEngine.Object;

#endregion

namespace BeatSaberDiscordPresence {
    [Plugin(RuntimeOptions.DynamicInit)]
    public partial class Plugin {
        public const string HarmonyId = "uk.co.breadhub.BeatSaberDiscordPresence";

        // origional variables start
        private const string MenuSceneName = "MenuViewControllers";
        private const string GameSceneName = "GameCore";

        private const string DiscordAppID = "658039028825718827";
        private static readonly DiscordRpc.RichPresence Presence = new DiscordRpc.RichPresence();
        private FieldInfo _360DegreeBeatmapCharacteristic;
        private FieldInfo _90DegreeBeatmapCharacteristic;

        private gameMode _gamemode = gameMode.Standard;
        private readonly FieldInfo _gameplayCoreSceneSetupDataField;
        private GameplayCoreSceneSetupData _gameplaySetup;

        private readonly bool _init;
        private MainFlowCoordinator _mainFlowCoordinator;
        private GameplayCoreSceneSetupData _mainSetupData;
        private readonly FieldInfo _oneColorBeatmapCharacteristic;
        private FieldInfo clientInstanceField;

        private FieldInfo clientInstanceInroomField;

        private readonly MonoBehaviour gameObject;
        private static Harmony harmonyInstance;

        private static IPALogger logger;

        [Init]
        public Plugin(IPALogger _logger) {
            instance = this;
            log = _logger;
            log?.Debug("Logger initialized.");
            if (_init) return;
            _init = true;

            logger = log;

            harmonyInstance = new Harmony(HarmonyId);

            gameObject = Resources.FindObjectsOfTypeAll<MonoBehaviour>()
                .First(c => c.GetType().Name == "PluginComponent");

            try {
                logger?.Info("Initializing");

                logger?.Info("Starting Discord RichPresence");
                var handlers = new DiscordRpc.EventHandlers();
                DiscordRpc.Initialize(DiscordAppID, ref handlers, false, string.Empty);

                logger?.Info("Fetching nonpublic fields");
                _gameplayCoreSceneSetupDataField = typeof(GameplayCoreSceneSetupData).GetField("_sceneSetupData",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                _oneColorBeatmapCharacteristic =
                    typeof(GameplayCoreSceneSetupData).GetField("_oneColorBeatmapCharacteristic",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                _90DegreeBeatmapCharacteristic =
                    typeof(GameplayCoreSceneSetupData).GetField("_90DegreeBeatmapCharacteristic",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                _360DegreeBeatmapCharacteristic =
                    typeof(GameplayCoreSceneSetupData).GetField("_360DegreeBeatmapCharacteristic",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                logger?.Info("Init done !");
            }
            catch (Exception e) {
                logger?.Error("Unable to initialize plugin:\n" + e);
            }
        }

        private static Plugin instance { get; set; }
        internal static IPALogger log { get; private set; }

        private static BeatSaberDiscordPresenceController pluginController =>
            BeatSaberDiscordPresenceController.Instance;

        private HarmonyMethod GetVoidPatch() {
            return new HarmonyMethod(typeof(Plugin).GetMethod("VoidPatch", (BindingFlags) (-1)));
        }



        #region Disableable
        [OnEnable]
        public void OnEnable() {
            new GameObject("BeatSaberDiscordPresenceController").AddComponent<BeatSaberDiscordPresenceController>();
            //ApplyHarmonyPatches();

            logger.Info("Looking for BeatSaberMultiplayer");
            var beatSaberMultiplayer = PluginManager.GetPluginFromId("BeatSaberMultiplayer");
            if (beatSaberMultiplayer != null) {
                var multiplayerClientType = beatSaberMultiplayer.Assembly.GetType("BeatSaberMultiplayer.Client");
                if (multiplayerClientType != null) {
                    clientInstanceField = multiplayerClientType.GetField("instance", (BindingFlags) (-1));
                    clientInstanceInroomField = multiplayerClientType.GetField("inRoom", (BindingFlags) (-1));
                    logger.Info("BeatSaberMultiplayer found and linked.");
                }
                else {
                    logger.Warn(
                        "Found BeatSaberMultiplayer, but not type BeatSaberMultiplayer.Client. Multiplayer won't be shown on discord.");
                }
            }

            //add the OnActiveSceneChanged method to the scene manager's activeSceneChanged 
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
            OnApplicationStart();

            //Menu scene loaded
            Presence.details = "In Menu";
            Presence.state = string.Empty;
            Presence.startTimestamp = default;
            Presence.largeImageKey = "default";
            Presence.largeImageText = "Beat Saber";
            Presence.smallImageKey = "";
            Presence.smallImageText = "";
            DiscordRpc.UpdatePresence(Presence);
        }

        /// <summary>
        ///     Called when the plugin is disabled and on Beat Saber quit. It is important to clean up any Harmony patches,
        ///     GameObjects, and Monobehaviours here.
        ///     The game should be left in a state as if the plugin was never started.
        ///     Methods marked [OnDisable] must return void or Task.
        /// </summary>
        [OnDisable]
        public void OnDisable() {
            if (pluginController != null)
                Object.Destroy(pluginController);
            DiscordRpc.Shutdown();
            OnApplicationQuit();
            RemoveHarmonyPatches();
        }

        /// <summary>
        /// Called when the active scene is changed.
        /// </summary>
        /// <param name="prevScene">The scene you are transitioning from.</param>
        /// <param name="nextScene">The scene you are transitioning to.</param>
        public void OnActiveSceneChanged(Scene prevScene, Scene nextScene) {
            updatePresence(nextScene);
        }

        public void OnApplicationStart() {
            logger.Debug("OnApplicationStart");
            ApplyHarmonyPatches();
        }

        /// <summary>
        /// Attempts to apply all the Harmony patches in this assembly.
        /// </summary>
        public static void ApplyHarmonyPatches() {
            try {
                logger.Debug("Applying Harmony patches.");
                harmonyInstance.PatchAll(Assembly.GetExecutingAssembly());
            }
            catch (Exception ex) {
                logger.Critical("Error applying Harmony patches: " + ex.Message);
                logger.Debug(ex);
            }
        }

        /// <summary>
        /// Attempts to remove all the Harmony patches that used our HarmonyId.
        /// </summary>
        public static void RemoveHarmonyPatches() {
            try {
                // Removes all patches with this HarmonyId
                harmonyInstance.UnpatchAll(HarmonyId);
            }
            catch (Exception ex) {
                logger.Critical("Error removing Harmony patches: " + ex.Message);
                logger.Debug(ex);
            }
        }

        public void OnApplicationQuit() {
            logger.Debug("OnApplicationQuit");
        }

        private void updatePresence(Scene newScene) {
            switch (newScene.name) {
                case MenuSceneName:
                    //Menu scene loaded
                    Presence.details = "In Main Menu";
                    Presence.state = string.Empty;
                    Presence.startTimestamp = default;
                    Presence.largeImageKey = "default";
                    Presence.largeImageText = "Beat Saber";
                    Presence.smallImageKey = "";
                    Presence.smallImageText = "";
                    DiscordRpc.UpdatePresence(Presence);
                    break;
                case GameSceneName:
                    gameObject.StartCoroutine(updatePresenceAfterFrame());
                    Presence.details = _gameplaySetup.difficultyBeatmap.level.songName;
                    Presence.state = gameObject.enabled ? "Playing" : "Paused";
                    Presence.startTimestamp = DateTime.Now.ToUnixTime();
                    Presence.largeImageKey = "solo";
                    Presence.largeImageText = "Beat Saber";
                    Presence.smallImageKey = "";
                    Presence.smallImageText = "";
                    DiscordRpc.UpdatePresence(Presence);
                    break;
                default:
                    gameObject.StartCoroutine(updatePresenceAfterFrame());
                    Presence.details = newScene.name;
                    //Presence.details = _gameplaySetup.difficultyBeatmap.level.songName;
                    Presence.state = gameObject.enabled ? "Playing" : "Paused";
                    Presence.startTimestamp = DateTime.Now.ToUnixTime();
                    Presence.largeImageKey = "solo";
                    Presence.largeImageText = "Beat Saber";
                    Presence.smallImageKey = "";
                    Presence.smallImageText = "";
                    DiscordRpc.UpdatePresence(Presence);
                    break;
            }
        }

        private IEnumerator updatePresenceAfterFrame() {
            yield return true;
            _mainFlowCoordinator = Resources.FindObjectsOfTypeAll<MainFlowCoordinator>().FirstOrDefault();
            _gameplaySetup = BS_Utils.Plugin.LevelData.GameplayCoreSceneSetupData;
            
            if (BS_Utils.Plugin.LevelData.IsSet) {
                if (_gameplaySetup != null)
                    _mainSetupData =
                        _gameplayCoreSceneSetupDataField.GetValue(_gameplaySetup) as GameplayCoreSceneSetupData;
                
                // Check if every required object is found
                if (_mainSetupData == null || _gameplaySetup == null || _mainFlowCoordinator == null) {
                    Presence.details = "Playing";
                    DiscordRpc.UpdatePresence(Presence);
                    yield break;
                }

                // Set presence main values
                var diff = _mainSetupData.difficultyBeatmap;

                Presence.details = $"{diff.level.songName} | {diff.difficulty.Name()}";
                Presence.state = "";

                if (diff.level.levelID.Contains('∎')) Presence.state += "Custom | ";

                if (_mainSetupData.practiceSettings != null)
                    Presence.state += "Practice | ";

                Presence.state += getFlowTypeHumanReadable() + " ";

                // Update gamemode (Standard / One Saber / No Arrow)
                if (_mainSetupData.gameplayModifiers.noArrows || diff.parentDifficultyBeatmapSet.beatmapCharacteristic
                    .ToString().ToLower().Contains("noarrow"))
                    _gamemode = gameMode.NoArrows;
                else if (diff.parentDifficultyBeatmapSet.beatmapCharacteristic ==
                         (BeatmapCharacteristicSO) _oneColorBeatmapCharacteristic.GetValue(_gameplaySetup))
                    _gamemode = gameMode.OneSaber;
                else if (diff.parentDifficultyBeatmapSet.beatmapCharacteristic.ToString().ToLower()
                    .Contains("90degree"))
                    _gamemode = gameMode.NinetyDegree;
                else if (diff.parentDifficultyBeatmapSet.beatmapCharacteristic.ToString().ToLower()
                    .Contains("360degree"))
                    _gamemode = gameMode.ThreeSixtyDegree;
                else _gamemode = gameMode.Standard;

                var gameplayModeText = _gamemode == gameMode.OneSaber ? "One Saber" :
                    _gamemode == gameMode.NoArrows ? "No Arrow" :
                    _gamemode == gameMode.NinetyDegree ? "90º" :
                    _gamemode == gameMode.ThreeSixtyDegree ? "360º" : "Standard";
                Presence.state += gameplayModeText;

                // Set music speak
                if (_mainSetupData.practiceSettings != null) {
                    if (Math.Abs(_mainSetupData.practiceSettings.songSpeedMul - 1.0f) > 9999999)
                        Presence.state += " | Speed x" + _mainSetupData.practiceSettings.songSpeedMul;
                }
                else {
                    if (Math.Abs(_mainSetupData.gameplayModifiers.songSpeedMul - 1.0f) > 99999999)
                        Presence.state += " | Speed x" + _mainSetupData.gameplayModifiers.songSpeedMul;
                }

                // Set common gameplay modifiers
                if (_mainSetupData.gameplayModifiers.noFailOn0Energy)
                    Presence.state += " | No Fail";
                if (_mainSetupData.gameplayModifiers.instaFail)
                    Presence.state += " | Instant Fail";
                if (_mainSetupData.gameplayModifiers.disappearingArrows)
                    Presence.state += " | Disappearing Arrows";
                if (_mainSetupData.gameplayModifiers.ghostNotes)
                    Presence.state += " | Ghost Notes";


                Presence.largeImageKey = "default";
                Presence.largeImageText = "Beat Saber";
                Presence.smallImageKey = getFlowTypeHumanReadable() == "Party" ? "party" :
                    _gamemode == gameMode.OneSaber ? "one_saber" :
                    _gamemode == gameMode.NoArrows ? "no_arrows" :
                    _gamemode == gameMode.NinetyDegree ? "90" :
                    _gamemode == gameMode.ThreeSixtyDegree ? "360" : "solo";
                Presence.smallImageText = gameplayModeText;
                Presence.startTimestamp = (long) DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;

                // Set startTimestamp offset if we are in training mode
                if (_mainSetupData.practiceSettings != null) {
                    if (_mainSetupData.practiceSettings.startInAdvanceAndClearNotes)
                        Presence.startTimestamp -=
                            (long) Mathf.Max(0f, _mainSetupData.practiceSettings.startSongTime - 3f);
                    else
                        Presence.startTimestamp -= (long) _mainSetupData.practiceSettings.startSongTime;
                }

                DiscordRpc.UpdatePresence(Presence);
            }
        }

        public void OnUpdate() {
            DiscordRpc.RunCallbacks();
        }


        private string getFlowTypeHumanReadable() {
            var t = _mainFlowCoordinator.childFlowCoordinator.GetType();
            logger.Debug("Current Flow Coordinator: " + t);
            if (isConnectedToMultiplayer())
                return "Multiplayer";
            if (t == typeof(ArcadeFlowCoordinator))
                return "Arcade"; // Unused ?
            if (t == typeof(PartyFreePlayFlowCoordinator))
                return "Party";
            return t == typeof(CampaignFlowCoordinator) ? "Campaign" : "Solo";
        }

        private bool isConnectedToMultiplayer() {
            object client;
            return clientInstanceInroomField != null && (client = clientInstanceField.GetValue(null)) != null &&
                   (bool) clientInstanceInroomField.GetValue(client);
        }
    }

    #endregion
}