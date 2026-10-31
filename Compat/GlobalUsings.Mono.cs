// Mono backend (netstandard2.1) global usings.
//
// Every game namespace and type the source names is imported here and, with the Il2Cpp* names, in
// GlobalUsings.Il2Cpp.cs, so the rest of the source compiles unchanged on both backends.
// Because UnityEngine is imported here and System implicitly, write `UnityEngine.Object` / `UnityEngine.Random`.
// The ScheduleOne root namespace is not imported: its Console would collide with System.Console. Root types get
// an alias instead.

global using UnityEngine;
global using ScheduleOne.DevUtilities;    // NetworkSingleton<T>, Singleton<T>, PlayerSingleton<T>
global using ScheduleOne.PlayerScripts;   // Player (Player.Local), PlayerCamera, PlayerMovement
global using ScheduleOne.Vehicles;        // LandVehicle, VehicleSeat, VehicleCamera
global using ScheduleOne.Interaction;     // InteractableObject
global using ScheduleOne.AvatarFramework.Animation; // AvatarIKController, AvatarAnimation
global using RootMotion.FinalIK;          // BipedIK, IKSolverLimb
global using FishNet;                     // InstanceFinder

global using GameAvatar = ScheduleOne.AvatarFramework.Avatar;
global using NPC = ScheduleOne.NPCs.NPC;
global using GameNpcHealth = ScheduleOne.NPCs.NPCHealth;
global using GameNpcMovement = ScheduleOne.NPCs.NPCMovement;
global using GameRangedWeapon = ScheduleOne.Equipping.Equippable_RangedWeapon;
global using GameDeadBehaviour = ScheduleOne.NPCs.Behaviour.DeadBehaviour;
global using GamePoliceOfficer = ScheduleOne.Police.PoliceOfficer;
global using GamePatrolRoute = ScheduleOne.NPCs.Behaviour.VehiclePatrolRoute;
global using GamePlayerInventory = ScheduleOne.PlayerScripts.PlayerInventory;
global using AvatarEffects = ScheduleOne.AvatarFramework.AvatarEffects;
global using FishNetObject = FishNet.Object.NetworkObject;
global using GameConsole = ScheduleOne.Console;
global using GameInput = ScheduleOne.GameInput;
global using GameQuestManager = ScheduleOne.Quests.QuestManager;
global using EQuestState = ScheduleOne.Quests.EQuestState;
global using GameStringList = System.Collections.Generic.List<string>;
global using GameTimeManager = ScheduleOne.GameTime.TimeManager;
global using GameAudioManager = ScheduleOne.Audio.AudioManager;
global using EAudioType = ScheduleOne.Core.Audio.EAudioType;
global using WeatherHandler = ScheduleOne.Core.Weather.EnvironmentHandler;
global using IWeatherEntity = ScheduleOne.Core.Weather.IWeatherEntity;
global using GameEnvironment = ScheduleOne.Weather.EnvironmentManager;
global using GameLoadManager = ScheduleOne.Persistence.LoadManager;
global using GameSaveManager = ScheduleOne.Persistence.SaveManager;
global using GameText = TMPro.TextMeshPro;
global using GameFontAsset = TMPro.TMP_FontAsset;
global using GameTextAlign = TMPro.TextAlignmentOptions;
global using GameUiText = TMPro.TextMeshProUGUI;
global using GameDropdown = TMPro.TMP_Dropdown;
global using GameHud = ScheduleOne.UI.HUD;
global using GameMoneyManager = ScheduleOne.Money.MoneyManager;
global using VehicleSalesman = ScheduleOne.Dialogue.DialogueHandler_VehicleSalesman;
global using GameVehicleSound = ScheduleOne.Vehicles.Sound.VehicleSound;
global using GameVehicleModMenu = ScheduleOne.UI.VehicleModMenu;
global using GameVehicleCanvas = ScheduleOne.UI.VehicleCanvas;
global using GameVehicleColor = ScheduleOne.Vehicles.Modification.EVehicleColor;
global using VehicleSaleSign = ScheduleOne.Tools.VehicleSaleSign;
global using GameQualityDropdown = ScheduleOne.UI.Settings.QualityDropdown;
global using GamePopupSelector = ScheduleOne.UIPopupSelector;
global using GameMenuOption = ScheduleOne.UIPopupScreen_ContextMenu.ContextMenuOption;
