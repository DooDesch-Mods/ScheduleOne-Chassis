// IL2CPP backend (net6.0) global usings.
//
// Every game namespace and type the source names is imported here and, with the plain ScheduleOne.* names, in
// GlobalUsings.Mono.cs, so the rest of the source compiles unchanged on both backends.
// Because UnityEngine is imported here and System implicitly, write `UnityEngine.Object` / `UnityEngine.Random`.
// The ScheduleOne root namespace is not imported: its Console would collide with System.Console. Root types get
// an alias instead.

global using UnityEngine;
global using Il2CppScheduleOne.DevUtilities;    // NetworkSingleton<T>, Singleton<T>, PlayerSingleton<T>
global using Il2CppScheduleOne.PlayerScripts;   // Player (Player.Local), PlayerCamera, PlayerMovement
global using Il2CppScheduleOne.Vehicles;        // LandVehicle, VehicleSeat, VehicleCamera
global using Il2CppScheduleOne.Interaction;     // InteractableObject
global using Il2CppScheduleOne.AvatarFramework.Animation; // AvatarIKController, AvatarAnimation
global using Il2CppRootMotion.FinalIK;          // BipedIK, IKSolverLimb
global using Il2CppFishNet;                     // InstanceFinder

global using GameAvatar = Il2CppScheduleOne.AvatarFramework.Avatar;
global using NPC = Il2CppScheduleOne.NPCs.NPC;
global using GameNpcHealth = Il2CppScheduleOne.NPCs.NPCHealth;
global using GameNpcMovement = Il2CppScheduleOne.NPCs.NPCMovement;
global using GameRangedWeapon = Il2CppScheduleOne.Equipping.Equippable_RangedWeapon;
global using GameDeadBehaviour = Il2CppScheduleOne.NPCs.Behaviour.DeadBehaviour;
global using GamePoliceOfficer = Il2CppScheduleOne.Police.PoliceOfficer;
global using GamePatrolRoute = Il2CppScheduleOne.NPCs.Behaviour.VehiclePatrolRoute;
global using GamePlayerInventory = Il2CppScheduleOne.PlayerScripts.PlayerInventory;
global using AvatarEffects = Il2CppScheduleOne.AvatarFramework.AvatarEffects;
global using FishNetObject = Il2CppFishNet.Object.NetworkObject;
global using GameConsole = Il2CppScheduleOne.Console;
global using GameInput = Il2CppScheduleOne.GameInput;
global using GameQuestManager = Il2CppScheduleOne.Quests.QuestManager;
global using EQuestState = Il2CppScheduleOne.Quests.EQuestState;
global using GameStringList = Il2CppSystem.Collections.Generic.List<string>;
global using GameTimeManager = Il2CppScheduleOne.GameTime.TimeManager;
global using GameAudioManager = Il2CppScheduleOne.Audio.AudioManager;
global using EAudioType = Il2CppScheduleOne.Core.Audio.EAudioType;
global using WeatherHandler = Il2CppScheduleOne.Core.Weather.EnvironmentHandler;
global using IWeatherEntity = Il2CppScheduleOne.Core.Weather.IWeatherEntity;
global using GameEnvironment = Il2CppScheduleOne.Weather.EnvironmentManager;
global using GameLoadManager = Il2CppScheduleOne.Persistence.LoadManager;
global using GameSaveManager = Il2CppScheduleOne.Persistence.SaveManager;
global using GameText = Il2CppTMPro.TextMeshPro;
global using GameFontAsset = Il2CppTMPro.TMP_FontAsset;
global using GameTextAlign = Il2CppTMPro.TextAlignmentOptions;
global using GameUiText = Il2CppTMPro.TextMeshProUGUI;
global using GameDropdown = Il2CppTMPro.TMP_Dropdown;
global using GameHud = Il2CppScheduleOne.UI.HUD;
global using GameMoneyManager = Il2CppScheduleOne.Money.MoneyManager;
global using VehicleSalesman = Il2CppScheduleOne.Dialogue.DialogueHandler_VehicleSalesman;
global using GameVehicleSound = Il2CppScheduleOne.Vehicles.Sound.VehicleSound;
global using GameVehicleModMenu = Il2CppScheduleOne.UI.VehicleModMenu;
global using GameVehicleColor = Il2CppScheduleOne.Vehicles.Modification.EVehicleColor;
global using VehicleSaleSign = Il2CppScheduleOne.Tools.VehicleSaleSign;
global using GameQualityDropdown = Il2CppScheduleOne.UI.Settings.QualityDropdown;
global using GamePopupSelector = Il2CppScheduleOne.UIPopupSelector;
global using GameMenuOption = Il2CppScheduleOne.UIPopupScreen_ContextMenu.ContextMenuOption;
