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
global using AvatarEffects = Il2CppScheduleOne.AvatarFramework.AvatarEffects;
global using FishNetObject = Il2CppFishNet.Object.NetworkObject;
global using GameConsole = Il2CppScheduleOne.Console;
global using GameInput = Il2CppScheduleOne.GameInput;
global using GameQuestManager = Il2CppScheduleOne.Quests.QuestManager;
global using EQuestState = Il2CppScheduleOne.Quests.EQuestState;
global using GameStringList = Il2CppSystem.Collections.Generic.List<string>;
