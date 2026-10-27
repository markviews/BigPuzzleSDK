using System;
using System.Collections.Generic;

namespace BigPuzzleSDK {

    // ======================================================================================================
    // EVERY REFERENCE TO THE GAME'S OWN CONTENT, IN ONE PLACE.
    //
    // When a Big Walk update moves, renames or reworks something, the fix is almost always a string in
    // this file; nothing else needs editing. What lives here:
    //   * scene paths of the objects the puzzle components copy ("ExactPath..."), and the shorter
    //     fragments used to find them again by search if the exact path has moved ("PreferredPath")
    //   * names of objects, switches, states, materials and shader properties inside those copies
    //   * names of game methods and members the mod hooks or reads by name (see Hooks)
    //   * the game's sound sets
    // What can NOT live here: references to the game's types (PropHome, Prop, PlayerNetworking...) are
    // compile-time; if the game renames one, regenerate the Assembly-CSharp interop and fix the compile
    // errors. Names that are the SDK's own (CopyName, ...) stay with their components.
    //
    // After a game update: start the game once and look at the log for "[Template]" warnings: each one
    // names the template that was not found at its path and, when the search finds it, the new path.
    // ======================================================================================================
    internal static class GameRefs {

        // ---- Box ----
        internal static class Box {
            public const string ExactPath = "LandmarksPlayerCountAny/CoordinatesHolding/PositionerStart/GourdBox/OpenableBox";
            public const string TemplateName = "OpenableBox";  // the vanilla object that gets copied
            public const string PreferredPath = "GourdBox";
            public static readonly string[] DisabledChildren = { "BoxOpenSwitch", "BoxGourdHome", "ButtonPanel" };
        }

        // ---- Button ----
        internal static class Button {
            public const string TemplatePath = "LandmarksNonChallenge/SpawnCourtyardandHubPlatform/Positioner/TeachingArea/Positioner/ObjectsTeaching/Switch/PushButton/BasicPushButton";
        }

        // ---- Confetti ----
        internal static class Confetti {
            public const string ExactPath = "LandmarksPlayerCountAny/TellerWindow/Positioner/RewardConfetti";
            public const string TemplateName = "RewardConfetti";
            public const string PreferredPath = "TellerWindow/Positioner/RewardConfetti";
        }

        // ---- Display ----
        internal static class Display {
            public const string ExactPath = "LandmarksPlayerCount4/Contents/RingRoomChallenge/Positioner/RingRoomChamber/PlayerCountToggledContents/PegtileStuff/MasterSequenceSplitFlap1Slot";
            public const string ExactPath3 = "LandmarksPlayerCount3/Contents/RingRoomChallenge 3 Player/Positioner/RingRoomChamber (3)/PlayerCountToggledContents/PegtileStuff/MasterSequenceSplitFlap1Slot";
            public const string ExactPath2 = "LandmarksPlayerCount2/Contents/RingRoomChallenge 2 Player/Positioner/RingRoomChamber/PlayerCountToggledContents/PegtileStuff/MasterSequenceSplitFlap1Slot";
            public const string TemplateName = "MasterSequenceSplitFlap1Slot";
            // The bigger frames (the 2- and 3-slot ones sit in the 4-player lobby's Silent Gauntlet; the 5-slot one is in every lobby)
            public const string ExactPathTwo = "LandmarksPlayerCount4/Contents/SilentGauntlet 4Player/Positioner/Level0/GauntletChamber00PegBoard/4Player/Peg0/MasterSequenceSplitFlap2Slot";
            public const string ExactPathThree = "LandmarksPlayerCount4/Contents/SilentGauntlet 4Player/Positioner/Level0/GauntletChamber00PegBoard/3Player/Peg0/MasterSequenceSplitFlap3Slot";
            public const string ExactPathFive = "LandmarksPlayerCountAny/TellerWindow/Positioner/MasterSequenceSplitFlap5Slot";
        }

        // ---- Door ----
        internal static class Door {
            public const string ExactPath = "LandmarksPlayerCountAny/TellerWindow/Positioner/BasicDoor";
            public const string LogicName = "BasicDoorPeckLogic";
            public const string PreferredPath = "TellerWindow/Positioner/BasicDoor";
            public static readonly string[] LeafColliders = { "leftCollider", "rightCollider" };
        }

        // ---- Glass ----
        internal static class Glass {
            public const string WireMaterialName = "SoundproofWire";
        }

        // ---- GourdPlinth ----
        internal static class GourdPlinth {
            public const string ExactPath = "LandmarksNonChallenge/OverflowMonument/Positioner/GourdPlinth X13/Monuments/pivot/GourdPlinth X1";
            public const string TemplatePrefix = "GourdPlinth X";
            public const string PinName = "GourdPin";
            public const string ButtonedStateName = "flapIsButtonedSystem";
            public const string OpenStateName = "flapIsOpenSystem";
            public static readonly string[] RemovedComponents = { "PeckEffectGourdMap" };
        }

        // ---- GourdVice ----
        internal static class GourdVice {
            public const string ExactPath = "LandmarksPlayerCountAny/TellerWindow/Positioner/GourdVice";
            public const string VictorySwitchName = "GourdViceVictorySwitch"; // identifies a vanilla vice
            public const string PreferredPath = "TellerWindow";
            public const string PinnedSwitchName = "GourdViceOnGourdPinned";  // closes the vice on a gourd
            public const string LaunchSwitchName = "GourdViceLaunchSwitch";   // fires when the vice has opened
            public static readonly string[] RemovedComponents = { "PeckEffectGourdMap", "PeckEffectSavableHome" };
        }

        // ---- Item ----
        internal static class Item {
            public const string BellName = "cowbell_large_bell";
            public const string SwitchName = "CowbellSwitchSystem";
            public static readonly string[] RemovedParts = { "CowbellAudio" };
            public static readonly string[] HiddenModels = { "cowbell_large_clapper", "cowbell_medium_bell", "cowbell_small_bell" };
            public static readonly string[] SoundSets = {
        "sfx_prop_peg",   // Peg
        "sfx_prop_cowbell_large",   // Cowbell
        "sfx_prop_buoy",   // Buoy
        "sfx_prop_footy",   // Football
        "sfx_prop_cannonball",   // Cannonball
        "sfx_prop_torch",   // Torch
        "sfx_prop_foldingChair",   // FoldingChair
        "sfx_prop_walkieTalkie",   // WalkieTalkie
        "sfx_prop_binocular",   // Binoculars
        "sfx_prop_kettle",   // Kettle
        "sfx_prop_nightvisionGoggles",   // NightVisionGoggles
        "sfx_prop_laserPointer",   // LaserPointer
        "sfx_prop_clock",   // Clock
        "sfx_prop_pomodoro",   // Pomodoro
        "sfx_prop_sign",   // Sign
        "sfx_prop_flaregun",   // FlareGun
        "sfx_prop_blindfold",   // Blindfold
        "sfx_prop_megaphone",   // Megaphone
        "sfx_prop_salonBrush",   // SalonBrush
        "sfx_prop_laptop",   // Laptop
        "sfx_prop_foldingMap",   // FoldingMap
        "sfx_prop_coordinatesTracker",   // CoordinatesTracker
        "sfx_prop_gourdholder",   // GourdHolder
        "sfx_prop_gourd_cradle",   // GourdCradle
        "sfx_prop_key",   // Key
        "sfx_prop_permo_sign",   // PermoSign
    };
        }

        // ---- Light ----
        internal static class Light {
            public const string LensColorProperty = "_TintMask0";   // what the game's own coloured lamps set

            // Where each type's vanilla template sits (see Templates in Plugin.cs). Only TableRed has been
            // confirmed from the game so far; the others fall back to a search (with a warning showing the
            // real path) if the exact one is empty or wrong.

            public static string TemplateName(PuzzleLightType kind) {
                switch (kind) {
                    case PuzzleLightType.Pendant: return "Light_PendantLamp";
                    case PuzzleLightType.Spotlight_Black: return "Light_Spotlight";
                    case PuzzleLightType.Spotlight_Red: return "Light_DownLight";
                    case PuzzleLightType.Tube: return "Light_Tube";
                    case PuzzleLightType.Circle: return "TrainLightFlood";
                    case PuzzleLightType.Ring: return "Light_Fluoro_Ring";
                    case PuzzleLightType.Conductor: return "ConductorLight";
                    default: return "Light_Table_Red";
                }
            }

            public static string ExactPath(PuzzleLightType kind) {
                switch (kind) {
                    case PuzzleLightType.TableRed: return "LandmarksNonChallenge/SpawnCourtyardandHubPlatform/Positioner/TeachingArea/Positioner/ObjectsTeaching/Switch/PushButton/Light_Table_Red";
                    case PuzzleLightType.Pendant: return "LandmarksNonChallenge/SpawnCourtyardandHubPlatform/Positioner/TeachingArea/Positioner/ObjectsTeaching/Switch/LightSwitch/Light_PendantLamp";
                    case PuzzleLightType.Spotlight_Black: return "LandmarksNonChallenge/SpawnCourtyardandHubPlatform/Positioner/TeachingArea/Positioner/ObjectsTeaching/Light_Spotlight (6)";
                    case PuzzleLightType.Conductor: return "LandmarksPlayerCount4/Contents/ConductorConcert/Positioner/Room_Upper/BasicConductorPanel7/ConductorLight0";
                    default: return null;   // path not known yet: search
                }
            }

            // Other places a type's template sits when the first is not loaded (other player counts)
            public static string[] ExtraPaths(PuzzleLightType kind) {
                if (kind == PuzzleLightType.Conductor) return new[] {
                "LandmarksPlayerCount3/Contents/PointersParadise/Positioner/BasicConductorPanel6/ConductorLight",
                "LandmarksPlayerCount2/Contents/ConductorConcert 2 Player/Positioner/Room_Upper/BasicConductorPanel7/ConductorLight1" };
                return new string[0];
            }

            public static string PreferredPath(PuzzleLightType kind) {
                switch (kind) {
                    case PuzzleLightType.Pendant: return "ObjectsTeaching/Switch/LightSwitch/Light_PendantLamp";
                    case PuzzleLightType.Spotlight_Black: return "ObjectsTeaching/Light_Spotlight (6)";
                    case PuzzleLightType.Spotlight_Red: return "Hub Lighting/GameObject (1)/Light_DownLight (3)";
                    case PuzzleLightType.Tube: return "Lookout_RedCone/lighting and door/Roof/Light_Tube";
                    case PuzzleLightType.Circle: return "CarriageContentsFront/TrainLightFlood";
                    case PuzzleLightType.Ring: return "DoofChamber/Positioner/Lights/RingLight/Light_Fluoro_Ring";
                    case PuzzleLightType.Conductor: return "BasicConductorPanel7/ConductorLight0";
                    default: return "ObjectsTeaching/Switch/PushButton/Light_Table_Red";
                }
            }
        }

        // ---- LiveSignal ----
        internal static class LiveSignal {
            public const string ExactPath = "LandmarksPlayerCountAny/TellerWindow/Positioner/LiveSignal";
            public const string LogicName = "LiveSignalPeckLogic";
            public const string PreferredPath = "TellerWindow/Positioner/LiveSignal";
        }

        // ---- PegBoard ----
        internal static class PegBoard {
            public const string ExactPath = "LandmarksPlayerCountAny/TellerWindow/Positioner/PegBoard5Slot";   // not confirmed yet: the search warning shows the real one
            public const string TemplateName = "PegBoard5Slot";
            public const string PreferredPath = "TellerWindow/Positioner/PegBoard5Slot";
        }

        // ---- PegTile ----
        internal static class PegTile {
            public const string ExactPath = "LandmarksPlayerCountAny/BlindfoldFishTrap/Positioner/Rack/PegTileRack_SetA/Pegtiles/PegTileProp1";
            public const string TemplateName = "PegTileProp1";
            public const string PreferredPath = "PegTileRack_SetA/Pegtiles/PegTileProp1";
        }

        // ---- Pillow ----
        internal static class Pillow {
            public const string ExactPath = "LandmarksPlayerCount4/Contents/TallButtonChallenge/Positioner/GourdValet/gourdValetPillowModel";
            public const string TemplateName = "gourdValetPillowModel";
            public const string PreferredPath = "GourdValet/gourdValetPillowModel";
        }

        // ---- PokeButton ----
        internal static class PokeButton {
            public const string ExactPath = "LandmarksPlayerCount4/Contents/TallButtonChallenge/Positioner/PlayerCountAdjustment/BasicPokeButton";
            public const string ExactPath3 = "LandmarksPlayerCount3/Contents/TallButtonChallenge 3Player/Positioner/PlayerCountAdjustment/BasicPokeButton";
            public const string ExactPath2 = "LandmarksPlayerCount2/Contents/TallButtonChallenge 2Player/Positioner/PlayerCountAdjustment/BasicPokeButton";
            public const string TemplatePrefix = "BasicPokeButton";
            public const string LogicName = "PokeButtonPeckLogic";
            public const string PreferredPath = "TallButtonChallenge/Positioner/PlayerCountAdjustment/";
        }

        // ---- ProgressBar ----
        internal static class ProgressBar {
            public const string ExactPath = "LandmarksPlayerCount4/Contents/CenturionSong/CenturionConents/Positioner - signal room/CenturionRoom/BasicProgressBar";
            public const string ExactPath3 = "LandmarksPlayerCount3/Contents/CenturionSong 3Player/CenturionConents/Positioner - input room/CenturionRoom/BasicProgressBar";
            public const string ExactPath2 = "LandmarksPlayerCount2/Contents/CenturionSong 2Player/CenturionConents/Positioner - signal room/CenturionRoom/BasicProgressBar -outside";
            public const string SpiralPath = "LandmarksPlayerCountAny/MemoryBombs/Positioner/BasicProgressBar_spiral";
            public const string PreferredPath = "CenturionSong"; // the vanilla bar to copy, if it is loaded
        }

        // ---- Players (PuzzleSDK.players) ----
        internal static class Players {
            // A player's head slot (where the blindfold goes) is the one of their prop holders whose object name has one of these
            // in it (lower case). If a Big Walk update renames it, PuzzlePlayer.hat stops finding the blindfold.
            public static readonly string[] HatHomeNames = { "blindfold", "hat", "head" };
        }

        // ---- Seat ----
        internal static class Seat {
            // The same chair model in two places: the credits theatre (every lobby size), and the 4-player cabin
            public static readonly string[] Paths = {
            "LandmarksNonChallenge/CreditsTheatre/Positioner/Chair_Midcentury",
            "LandmarksPlayerCount4/Contents/CabinFeverLong/Positioner/CabinFeverHouse/Chair_Midcentury",
        };
        }

        // ---- SimPressSwitch ----
        internal static class SimPressSwitch {
            public const string ExactPath = "LandmarksPlayerCount4/Contents/SmallSimPressChallenge/Positioner/SimPressSwitch";
            public const string ExactPath3 = "LandmarksPlayerCount3/Contents/MediumSimPressChallenge 3Player/Positioner/MediumSimPressStation - 0/SimPressSwitch";
            public const string ExactPath2 = "LandmarksPlayerCount2/Contents/MediumSimPressChallenge 2Player/Positioner/MediumSimPressStation - 1/SimPressSwitch";
            public const string PreferredPath = "SmallSimPressChallenge/Positioner/SimPressSwitch";
        }

        // ---- StartSwitch ----
        internal static class StartSwitch {
            public const string ExactPath = "LandmarksPlayerCountAny/TellerWindow/Positioner/BasicKnob - primary";
            public const string PreferredPath = "TellerWindow/Positioner/BasicKnob - primary";
        }

        // ---- Switch ----
        internal static class Switch {
            public const string ExactPath = "LandmarksNonChallenge/SpawnCourtyardandHubPlatform/Positioner/TeachingArea/Positioner/ObjectsTeaching/Switch/LightSwitch/BasicLightSwitch";
            public const string TemplateName = "BasicLightSwitch";
            public const string PreferredPath = "ObjectsTeaching/Switch/LightSwitch/BasicLightSwitch";
        }

        // ---- SyncPrototype ----
        internal static class SyncPrototype {
            public const string ButtonPath = "LandmarksNonChallenge/SpawnCourtyardandHubPlatform/Positioner/TeachingArea/Positioner/ObjectsTeaching/Switch/PushButton/BasicPushButton";
        }

        // ---- Telescope ----
        internal static class Telescope {
            public const string ExactPath = "LandmarksPlayerCountAny/InvisibleInkChallenge/Positioner/FixedTelescope";
            public const string NormalPath = "LandmarksPlayerCountAny/OpticalTelegraph/Positioner/CentralArea/FixedTelescope";
            public const string PreferredPath = "InvisibleInkChallenge";
        }

        // ---- Timer ----
        internal static class Timer {
            public const string ExactPath = "LandmarksPlayerCount4/Contents/CabinFever/Positioner/CabinFeverHouse/TimerObject";
            public const string ExactPath3 = "LandmarksPlayerCount3/Contents/CabinFeverLong 3Player/Positioner/CabinFeverHouse/TimerObject";
            public const string ExactPath2 = "LandmarksPlayerCount2/Contents/CabinFever 2Player/Positioner/CabinFeverHouse/TimerObject";
            public const string PreferredPath = "CabinFever"; // the vanilla timer to copy, if it is loaded
        }

        // ---- Validator ----
        internal static class Validator {
            public const string ExactPath = "LandmarksPlayerCountAny/TellerWindow/Positioner/PegTileValidatorWithScreenAndButton/BasicStandaloneValidatorScreen";
            public const string ScreenName = "BasicStandaloneValidatorScreen";
            public const string PreferredPath = "TellerWindow/Positioner/PegTileValidatorWithScreenAndButton/BasicStandaloneValidatorScreen";
        }

        // ---- The component -> template table (PuzzleVanillaCopy) ----
        // Each component's template: exact paths (any lobby size), first one found wins
        internal static class Copies {
            public static readonly Dictionary<string, string[]> TemplatePaths = new Dictionary<string, string[]> {
                ["PuzzleVanillaItem Blindfold"] = new[] {
                    "LandmarksPlayerCountAny/BlindfoldCatwalk/Positioner/BlindfoldProp",
                    "LandmarksPlayerCountAny/BlindfoldFishTrap/Positioner/BlindfoldProp" },
                ["PuzzleVanillaItem FoldingChair"] = new[] {
                    "LandmarksPlayerCount4/Contents/ConductorConcert/Positioner/Room_Outside/foldingChair",
                    "LandmarksNonChallenge/SpeakerStage/Positioner/foldingChair" },
                ["PuzzleVanillaItem Torch"] = new[] {
                    "TunnelSystem/TunnelOutlet - Red/TorchParent/TorchProp",
                    "LandmarksNonChallenge/Lookouts/Lookout_RedCone/TorchProp",
                    "TunnelSystem/TunnelOutlet - Green/TorchParent/TorchProp" },
                ["PuzzleVanillaItem FmRadio"] = new[] {
                    "LandmarksNonChallenge/MusicGarden/Sports Valley - Greensteps/BroadcastStation/FmRadioProp",
                    "LandmarksNonChallenge/MusicGarden/SouthPeakArea - Abbey/BroadcastStation/FmRadioProp",
                    "LandmarksPlayerCountAny/MusicalHoliday/PositionerMusicGarden/BroadcastStation/FmRadioProp" },
                ["PuzzleVanillaItem WalkieTalkie"] = new[] {
                    "LandmarksNonChallenge/WalkieTalkieHolders/WalkieTalkieHolder - OceanTreasure2 (Blue)/WalkyTalkyHolder/WalkieTalkieHolder_base/WalkieTalkieProp",
                    "LandmarksNonChallenge/WalkieTalkieHolders/WalkieTalkieHolder - Red Cone Hill (Yellow)/WalkyTalkyHolder/WalkieTalkieHolder_base/WalkieTalkieProp",
                    "LandmarksNonChallenge/WalkieTalkieHolders/WalkieTalkieHolder - Red Cone Valley (Blue) (1)/WalkyTalkyHolder/WalkieTalkieHolder_base/WalkieTalkieProp" },
                ["PuzzleVanillaItem Binoculars"] = new[] {
                    "LandmarksNonChallenge/Lookouts/Lookout_RedCone/RoofStuff/BinocularsProp",
                    "LandmarksNonChallenge/PeakMarker/Positioner/BinocularsProp",
                    "LandmarksNonChallenge/Lookouts/Lookout_BlueTube/RoofStuff/BinocularsProp" },
                ["PuzzleVanillaItem XrayGoggles"] = new[] {
                    "LandmarksNonChallenge/Lookouts/Lookout_BlueTube/XrayGogglesProp",
                    "LandmarksNonChallenge/Lookouts/Lookout_YellowZigzag/XrayGogglesProp",
                    "LandmarksNonChallenge/Lookouts/Lookout_GreenHourglass/XrayGogglesProp" },
                ["PuzzleVanillaItem Clock"] = new[] {
                    "TrainSystem/TrainStop - South Valley Beach/WallHook/WallHookHome/ClockProp",
                    "TrainSystem/TrainStop - Green Diffuser/WallHook/WallHookHome/ClockProp",
                    "TrainSystem/TrainStop - Sports Valley/WallHook/WallHookHome/ClockProp" },
                ["PuzzleVanillaItem Laser"] = new[] {
                    "LandmarksNonChallenge/Lookouts/Lookout_RedCone/RoofStuff/LaserProp",
                    "LandmarksNonChallenge/MapRoom/Positioner/MapRoom_entrance/PropBench/LaserProp",
                    "LandmarksNonChallenge/Lookouts/Lookout_GreenHourglass/RoofStuff/LaserProp" },
                ["PuzzleVanillaItem Megaphone"] = new[] {
                    "LandmarksNonChallenge/MegaphoneHolders/PositionerPyramid/MegaphoneHolder_pyramid/MegaphoneProp",
                    "LandmarksNonChallenge/MegaphoneHolders/PositionerRamp/MegaphoneHolder_ramp/MegaphoneProp",
                    "LandmarksNonChallenge/MegaphoneHolders/PositionerSpiral/MegaphoneHolder_spiral/MegaphoneProp" },
                ["PuzzleVanillaItem Kettle"] = new[] {
                    "LandmarksPlayerCountAny/ScoutBombs/Positioner/Tokens/pivot/KettleProp",
                    "LandmarksPlayerCount4/Contents/SilentGauntlet 4Player/Positioner/Level4/GauntletChamberVolleyBall/KettleProp" },
                ["PuzzleVanillaItem CannonBall"] = new[] {
                    "LandmarksPlayerCountAny/CannonBallCarry/PositionerBase/BaseHome/cannonballProp" },
                ["PuzzleVanillaItem PermoSign"] = new[] {
                    "LandmarksPlayerCountAny/CoordinatesSimPress/Positioner/WallhookWithPermoSign/WallHook/WallHookHome/PermoSignProp",
                    "LandmarksPlayerCountAny/CoordinatesHolding/PositionerStart/WallhookWithPermoSign/WallHook/WallHookHome/PermoSignProp" },
                ["PuzzleVanillaItem SpareSign"] = new[] {
                    "LandmarksPlayerCountAny/CoordinatesSimPress/Positioner/SpareSignProp" },
                ["PuzzleVanillaItem GourdHolder"] = new[] {
                    "LandmarksNonChallenge/CartonPlaza/Positioner/GourdCartonProp" },
                ["PuzzleVanillaItem Belt"] = new[] {
                    "LandmarksNonChallenge/PackPavillions/UndergroundBackpackPavilion/PackPavilion Holster/PackHanger/PackHanger home/HolsterProp",
                    "LandmarksNonChallenge/PackPavillions/HighlandsBackpackPavillion/PackPavilion Holster/HolsterProp",
                    "LandmarksNonChallenge/PackPavillions/OverlookBackpackPavilion/PackPavilion Holster/PackHanger/PackHanger home/HolsterProp" },
                ["PuzzleVanillaItem Backpack"] = new[] {
                    "LandmarksNonChallenge/PackPavillions/CrevasseBackpackPavilion/BackpackPavilion/PackHanger/PackHanger home/BackpackProp",
                    "LandmarksNonChallenge/PackPavillions/ForestBackpackPavilion/BackpackPavilion/PackHanger/PackHanger home/BackpackProp",
                    "LandmarksNonChallenge/PackPavillions/MonkeyCreekBackpackPavillion/BackpackPavilion/PackHanger/PackHanger home/BackpackProp" },
                ["PuzzleVanillaItem Gps"] = new[] {
                    "LandmarksPlayerCountAny/CoordinatesSimPress/Positioner/CoordinateTrackerProp" },
                ["PuzzleVanillaItem FoldingMap"] = new[] {
                    "LandmarksNonChallenge/MapRoom/Positioner/MapRoom_entrance/PropBench/FoldingMapProp" },
                ["PuzzleVanillaItem Compass"] = new[] {
                    "LandmarksNonChallenge/MapRoom/Positioner/MapRoom_entrance/PropBench/CompassProp" },
                ["PuzzleVanillaItem FlareGun"] = new[] {
                    "LandmarksNonChallenge/DiffuserBuilding/Positioner/FlareGunProp",
                    "LandmarksNonChallenge/Diffuser_Peak/Positioner/FlareGunPropGreen",
                    "LandmarksNonChallenge/Diffuser_Spawn/Position/FlareGunPropYellow" },
                ["PuzzleVanillaItem Cowbell"] = new[] {
                    "LandmarksNonChallenge/SpawnCourtyardandHubPlatform/Positioner/TeachingArea/Positioner/ObjectsTeaching/PickUp/bellshelf/CowBellLargeProp" },
                ["PuzzleVanillaItem Paintbrush"] = new[] {
                    "LandmarksNonChallenge/Salon_Secret/Positioner/SalonBuilding/SalonBrushProp (1)",
                    "LandmarksNonChallenge/Salon/Positioner/SalonBuilding/SalonBrushProp (1)" },
                ["PuzzleVanillaItem CowbellMedium"] = new[] {
                    "LandmarksNonChallenge/SpawnCourtyardandHubPlatform/Positioner/TeachingArea/Positioner/ObjectsTeaching/PickUp/bellshelf/CowBellMediumProp" },
                ["PuzzleVanillaItem CowbellSmall"] = new[] {
                    "LandmarksNonChallenge/SpawnCourtyardandHubPlatform/Positioner/TeachingArea/Positioner/ObjectsTeaching/PickUp/bellshelf/CowBellSmallProp" },
                ["PuzzleVanillaItem Football"] = new[] {
                    "LandmarksNonChallenge/SpawnCourtyardandHubPlatform/Positioner/TeachingArea/Positioner/FootyProp" },
                ["PuzzleCountInput"] = new[] { "LandmarksPlayerCountAny/PerspectiveCounting/Positioner/InputPlatform/countingpivot/CountingMachine" },
                ["PuzzleCountValidator"] = new[] { "LandmarksPlayerCountAny/PerspectiveCounting/Positioner/InputPlatform/validatorpivot/CountingValidatorDisplay" },
                ["PuzzleIntercom"] = new[] { "LandmarksPlayerCount4/Contents/RingRoomChallenge/Positioner/RingRoomChamber/PlayerCountToggledContents/FixedRadio(sending)" },
                ["PuzzleIntercomSpeaker"] = new[] { "LandmarksPlayerCountAny/SingerAndSelecter/Positioner/Intercoms/Intercom(listening) (2)" },
                ["PuzzleSoundPeg"] = new[] { "LandmarksPlayerCountAny/SingerAndSelecter/Positioner/SelecterBuilding/PegTileSetSpeaker/PegTileProp Speaker" },
                ["PuzzleSoundPegAnswer"] = new[] { "LandmarksPlayerCountAny/SingerAndSelecter/Positioner/SelecterBuilding/MasterSequenceSpeaker5/MasterSequenceSpeaker" },
                ["PuzzleNoItemsGate"] = new[] { "LandmarksPlayerCountAny/PoetAndPreist/Positioner/PoetBuilding/BasicTurnstile OneWay - Priest" },
                ["PuzzleNoItemsGate TwoWay"] = new[] { "LandmarksPlayerCountAny/PoetAndPreist/Positioner/PoetBuilding/BasicTurnstile (1)" },
                ["PuzzleNoItemsGate BlindMask"] = new[] { "LandmarksPlayerCountAny/BlindfoldCatwalk/Positioner/BlindfoldTurnstile" },
                ["PuzzleTimerSpawner"] = new[] {
                    "LandmarksPlayerCount4/Contents/Obby/Positioner/ObbyBuilding/5/PomodoroDispenser",
                    "LandmarksPlayerCount3/Contents/Obby 3Player/Positioner/ObbyBuilding/5/PomodoroDispenser",
                    "LandmarksPlayerCount2/Contents/Obby 2Player/Positioner/ObbyBuilding/5/PomodoroDispenser" },
                ["PuzzleTimerGoal"] = new[] {
                    "LandmarksPlayerCount4/Contents/Obby/Positioner/ObbyBuilding/5/BasicReceptical",
                    "LandmarksPlayerCount3/Contents/Obby 3Player/Positioner/ObbyBuilding/5/BasicReceptical/PlacementCastable/pomodoro_holder",
                    "LandmarksPlayerCount2/Contents/Obby 2Player/Positioner/ObbyBuilding/5/BasicReceptical/PlacementCastable/pomodoro_holder" },
                ["PuzzleFireworkLauncher"] = new[] { "LandmarksPlayerCountAny/FlareRun/Positioner Rocket/FixedFlare" },
                ["PuzzleItem"] = new[] { "LandmarksNonChallenge/SpawnCourtyardandHubPlatform/Positioner/TeachingArea/Positioner/ObjectsTeaching/PickUp/bellshelf/CowBellLargeProp" },
                ["PuzzleCirclePeg"] = new[] { "LandmarksPlayerCountAny/EggHunt/Positioner/Eggs/PegTileHook_Round_red/PegSlot/PegSlotHome/PegTileRoundProp" },
                ["PuzzleCirclePegBoard"] = new[] { "LandmarksPlayerCountAny/EggHunt/Positioner/Building/PegBoard_EggHunt" },
                ["PuzzleBuoyLight"] = new[] {
                    "LandmarksNonChallenge/LooseBuoyPedestals/buoypedestal_Full_Orange",
                    "LandmarksNonChallenge/LooseBuoyPedestals/Anywhere/buoypedestal_Full_Orange",
                    "LandmarksNonChallenge/LooseBuoyPedestals/Intro/buoypedestalFull" },
                ["PuzzleBuoyLight Red"] = new[] {
                    "LandmarksPlayerCountAny/CannonBallCarry/buoypedestal_Full_Red",
                    "LandmarksNonChallenge/LooseBuoyPedestals/buoypedestal_Full_Red",
                    "LandmarksNonChallenge/LooseBuoyPedestals/Anywhere/buoypedestal_Full_Red" },
                ["PuzzleButtonNHold"] = new[] {
                    "LandmarksPlayerCount4/Contents/BlackTower/Positioner/NHoldFullSet/NHoldSet4/Pivot/ButtonNHoldFour",
                    // 3 and 2 players: the same button inside NHoldFullSet (the full set holds every size's group)
                    "LandmarksPlayerCount3/Contents/BlackTower PlayerCount3/Positioner/NHoldFullSet/NHoldSet4/Pivot/ButtonNHoldFour",
                    "LandmarksPlayerCount3/Contents/BlackTower PlayerCount3/Positioner/NHoldFullSet/NHoldSet2/Pivot/ButtonNHoldTwo",
                    "LandmarksPlayerCount2/Contents/BlackTower PlayerCount2/Positioner/NHoldFullSet/NHoldSet4/Pivot/ButtonNHoldFour",
                    "LandmarksPlayerCount2/Contents/BlackTower PlayerCount2/Positioner/NHoldFullSet/NHoldSet2/Pivot/ButtonNHoldTwo" },
            };
        }

        // ---- The game's EOS lobby (LobbyData.cs) ----
        // The lobby the game creates for a world, and the room code a player joins by. The SDK adds its
        // own attributes to it so a joining player can read them before connecting.
        internal static class Lobby {
            public const string ManagerType = "EOSPrivateLobbyManager";        // creates, joins and updates the lobby
            public const string UpdateAttribute = "UpdateAttribute";           // manager: adds one text attribute to a lobby change (parameters: update, attributeKey, attributeValue)
            public const string ConvertToLobbyInfo = "ConvertToLobbyInfo";     // manager: turns a found/joined lobby into the game's LobbyInfo
            public const string DetailsExtensionsType = "LobbyDetailsExtensions";
            public const string TryGetStringAttribute = "TryGetStringAttribute"; // static: reads one text attribute of a found lobby
        }

        // ---- The password screen (JoinPopup.cs) ----
        // The popup over it copies the screen's own Cancel button, and pressing it calls the screen's Cancel().
        // The type is PasswordMenu (compile-time); MainMenu/PasswordMenu/CancelButton is where the button sits.
        internal static class PasswordPopup {
            public const string CancelButtonName = "CancelButton";   // child of the PasswordMenu object
        }

        // ---- Game methods and members the mod hooks or reads by name ----
        // (Harmony patches and reflection. If one is renamed, the log says "could not hook <name>".)
        internal static class Hooks {
            public const string OnSetHeld = "OnSetHeld";                       // PlayerNetworking: a player's held item changed
            public const string SetLobbyVisionActive = "SetLobbyVisionActive"; // LobbyVisionToggler
            public const string ApplyVolume = "ApplyVolume";                   // SettingsWardrobe: master volume
            public const string ApplyVolumeSfx = "ApplyVolumeSfx";             // SettingsWardrobe: effects volume
            public const string DoPeck = "DoPeck";                             // TrackedPeckState: a state changed
            public const string IsSafeToPlace = "IsSafeToPlace";               // PropHome: can this item go here
            public const string MatchesConditions = "MatchesConditions";       // CastableOutcome: is this action offered
            public const string ServerSetPinned = "ServerSetPinned";           // Prop: the host puts an item in a holder
            public const string PredictivelyPlaceInHome = "PredictivelyPlaceInHome"; // Prop: a player's own prediction of that
            public const string WriteCurrentSaveData = "WriteCurrentSaveData"; // SaveManager: the game saved
            public const string DeleteSave = "Delete";                         // SaveManager: a save was deleted
            public const string MusicPlay = "Play";                            // MusicPlayer: plays a clip
        }
    }

} // namespace BigPuzzleSDK