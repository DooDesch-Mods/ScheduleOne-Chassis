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
global using AvatarEffects = ScheduleOne.AvatarFramework.AvatarEffects;
global using FishNetObject = FishNet.Object.NetworkObject;
global using GameConsole = ScheduleOne.Console;
global using GameInput = ScheduleOne.GameInput;
global using GameQuestManager = ScheduleOne.Quests.QuestManager;
global using EQuestState = ScheduleOne.Quests.EQuestState;
global using GameStringList = System.Collections.Generic.List<string>;
