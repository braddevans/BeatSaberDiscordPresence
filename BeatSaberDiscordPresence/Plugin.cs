using System;
using System.Linq;
using IllusionPlugin;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BeatSaberDiscordPresence
{
	public class Plugin : IPlugin
	{
		private const string MenuSceneName = "Menu";
		private const string GameSceneName = "GameCore";
		private const string DiscordAppID = "445053620698742804";
		public static readonly DiscordRpc.RichPresence Presence = new DiscordRpc.RichPresence();
		private StandardLevelSceneSetupDataSO _mainSetupData;
        private GameplayCoreSceneSetup _gameplaySetup;
        private MainFlowCoordinator _mainFlowCoordinator;
        private bool _init;

        private BeatmapCharacteristicSelectionViewController _beatmapCharacteristicSelectionViewController;
        private GameMode _gamemode = GameMode.Standard;

        public string Name
		{
			get { return "Discord Presence"; }
		}

		public string Version
		{
			get { return "v2.0.3"; }
		}
		
		public void OnApplicationStart()
		{
			if (_init) return;
			_init = true;
            SceneManager.sceneLoaded += SceneManagerOnSceneLoaded;
            SceneManager.activeSceneChanged += SceneManagerOnActiveSceneChanged;

            var handlers = new DiscordRpc.EventHandlers();
			DiscordRpc.Initialize(DiscordAppID, ref handlers, false, string.Empty);
		}

        public void OnApplicationQuit()
		{
            SceneManager.activeSceneChanged -= SceneManagerOnActiveSceneChanged;
            DiscordRpc.Shutdown();
        }

        private void SceneManagerOnActiveSceneChanged(Scene oldScene, Scene newScene)
        {
            UpdatePresence(newScene);
        }

        private void SceneManagerOnSceneLoaded(Scene newScene, LoadSceneMode mode)
        {
            if(newScene.name == MenuSceneName)
            {
                _beatmapCharacteristicSelectionViewController = Resources.FindObjectsOfTypeAll<BeatmapCharacteristicSelectionViewController>().FirstOrDefault();
                if (_beatmapCharacteristicSelectionViewController == null)
                    return;
                _beatmapCharacteristicSelectionViewController.didSelectBeatmapCharacteristicEvent += this.OnDidSelectBeatmapCharacteristicEvent;
            }
        }

        private void OnDidSelectBeatmapCharacteristicEvent(BeatmapCharacteristicSelectionViewController viewController, BeatmapCharacteristicSO characteristic)
        {
            switch (characteristic.characteristicName)
            {
                case "No Arrows":
                    _gamemode = GameMode.NoArrows;
                    break;
                case "One Saber":
                    _gamemode = GameMode.OneSaber;
                    break;
                default:
                    _gamemode = GameMode.Standard;
                    break;
            }
        }

        private void UpdatePresence(Scene newScene) {

			if (newScene.name == MenuSceneName)
			{
				//Menu scene loaded
				Presence.details = "In Menu";
				Presence.state = string.Empty;
				Presence.startTimestamp = default(long);
				Presence.largeImageKey = "default";
				Presence.largeImageText = "Beat Saber";
				Presence.smallImageKey = "solo";
				Presence.smallImageText = "Solo Standard";
				DiscordRpc.UpdatePresence(Presence);
			}
			else if (newScene.name == GameSceneName)
			{
				_mainSetupData = Resources.FindObjectsOfTypeAll<StandardLevelSceneSetupDataSO>().FirstOrDefault();
                _gameplaySetup = Resources.FindObjectsOfTypeAll<GameplayCoreSceneSetup>().FirstOrDefault();
                _mainFlowCoordinator = Resources.FindObjectsOfTypeAll<MainFlowCoordinator>().FirstOrDefault();

                if (_mainSetupData == null || _gameplaySetup == null || _mainFlowCoordinator == null)
				{
					Console.WriteLine("Discord Presence: Error finding the scriptable objects required to update presence.");
					return;
				}
                //Main game scene loaded;

                var diff = _mainSetupData.difficultyBeatmap;
                var level = diff.level;

				Presence.details = $"{level.songName} | {diff.difficulty.Name()}";
				Presence.state = "";
				if (level.levelID.Contains('∎'))
				{
					Presence.state = "Custom | ";
				}
                
                if (IsParty())
                    Presence.state += "Party";
                else
                    Presence.state += "Solo";

                var gameplayModeText = _gamemode == GameMode.OneSaber ? "One Saber" : _gamemode == GameMode.NoArrows ? "No Arrow" : "Standard";
                Presence.state += gameplayModeText;

                if (_mainSetupData.gameplayCoreSetupData.gameplayModifiers.songSpeedMul != 1.0f)
                    Presence.state += " [Speed x" + _mainSetupData.gameplayCoreSetupData.gameplayModifiers.songSpeedMul + "]";
                if (_mainSetupData.gameplayCoreSetupData.gameplayModifiers.noFail)
					Presence.state += " [No Fail]";
                if (_mainSetupData.gameplayCoreSetupData.gameplayModifiers.instaFail)
                    Presence.state += " [Instant Fail]";
                if (_mainSetupData.gameplayCoreSetupData.playerSpecificSettings.swapColors)
					Presence.state += " [Mirrored]";
                if (_mainSetupData.gameplayCoreSetupData.gameplayModifiers.disappearingArrows)
                    Presence.state += " [Disappearing Arrows]";


                Presence.largeImageKey = "default";
				Presence.largeImageText = "Beat Saber";
                Presence.smallImageKey = IsParty() ? "party" : _gamemode == GameMode.OneSaber ? "one_saber" : _gamemode == GameMode.NoArrows ? "no_arrows" : "solo";
                Presence.smallImageText = gameplayModeText;
				Presence.startTimestamp = (long) DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
				DiscordRpc.UpdatePresence(Presence);
			}
		}

		public void OnLevelWasLoaded(int level)
		{
			
		}

		public void OnLevelWasInitialized(int level)
		{
			
		}

		public void OnUpdate()
		{
			DiscordRpc.RunCallbacks();
		}

		public void OnFixedUpdate()
		{
			
		}

        private bool IsParty()
        {
            return _mainFlowCoordinator.childFlowCoordinator.GetType() == typeof(PartyFreePlayFlowCoordinator);
        }

        private enum GameMode
        {
            Standard,
            OneSaber,
            NoArrows
        }
    }
}
