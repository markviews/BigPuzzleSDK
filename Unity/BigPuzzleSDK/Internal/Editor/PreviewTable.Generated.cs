// GENERATED from the Ctrl+F3 template dump (Big Walk 1.6.0). Do not edit by hand.
// Each template: the label the SDK component maps to, the vanilla object's world scale, and
// its visible parts: mesh name and vertex count (to pick the right copy), position/rotation/scale
// relative to the template root, and a material kind per submesh.
namespace BigPuzzleSDK.GameAssets {
internal static partial class PreviewTable {
    internal static readonly Template[] Templates = {
        new Template("PuzzleButton", new V3(1f, 1f, 1f), new Part[] {
            new Part("BasicPushButton", 247, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // BasicPushButton/BasicPushButton
            new Part("BasicPushButtonCase", 706, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // BasicPushButton/BasicPushButtonCase
        }),
        new Template("PuzzleSwitch", new V3(1f, 1f, 1f), new Part[] {
            new Part("LightSwitch_Backing", 1208, new V3(0f, 0f, -0.052f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // BasicLightSwitch/LightSwitch_Backing
            new Part("LightSwitch_Switch", 274, new V3(0f, -0.025f, -0.007f), new Q(-0.573576f, 0f, 0f, 0.819152f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // BasicLightSwitch/LightSwitch_Switch_State1
        }),
        new Template("PuzzleBox", new V3(1f, 1f, 1f), new Part[] {
            new Part("box-base", 1196, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.Glass }),   // box-glass/box-base
            new Part("box-lid", 424, new V3(0f, 0.543f, -0.274f), new Q(-0.707107f, 0f, 0f, 0.707107f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.Glass }),   // box-glass/pivot/box-lid (1)
        }),
        new Template("PuzzleConfetti", new V3(1f, 1f, 1f), new Part[] {
            new Part("ConfettiDispenser", -1, new V3(0f, -0.05f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // ConfettiDispenser
        }),
        new Template("PuzzleDoor", new V3(1f, 1f, 1f), new Part[] {
            new Part("door_folding_frame", 472, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // DoorAnimatingParent/door_folding/door_folding_frame
            new Part("door_folding_panel", 68, new V3(0.585f, 0f, 0f), new Q(-0.499913f, 0.500087f, 0.499913f, 0.500087f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // DoorAnimatingParent/door_folding/door_folding_panel_L1
            new Part("door_folding_panel", 68, new V3(0.555f, 0f, 0f), new Q(-0.499913f, 0.500087f, 0.499913f, 0.500087f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // DoorAnimatingParent/door_folding/door_folding_panel_L2
            new Part("door_folding_panel", 68, new V3(0.525f, 0f, 0f), new Q(-0.499913f, 0.500087f, 0.499913f, 0.500087f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // DoorAnimatingParent/door_folding/door_folding_panel_L3
            new Part("door_folding_panel", 68, new V3(-0.525f, 0f, 0f), new Q(-0.499913f, -0.500087f, -0.499913f, 0.500087f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // DoorAnimatingParent/door_folding/door_folding_panel_R3
            new Part("door_folding_panel", 68, new V3(-0.555f, 0f, 0f), new Q(-0.499913f, -0.500087f, -0.499913f, 0.500087f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // DoorAnimatingParent/door_folding/door_folding_panel_R2
            new Part("door_folding_panel", 68, new V3(-0.585f, 0f, 0f), new Q(-0.499913f, -0.500087f, -0.499913f, 0.500087f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // DoorAnimatingParent/door_folding/door_folding_panel_R1
        }),
        new Template("PuzzleGourdPlinth", new V3(1f, 1f, 1f), new Part[] {
            new Part("GourdMonument_tower_1slot", 1104, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.VertexColor }),   // GourdMonument_tower_1slot (1)
            new Part("GourdMonument_flap", 948, new V3(0f, 0.828068f, -0.240397f), new Q(-0.55557f, 0f, 0f, 0.83147f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.Glass }),   // GourdPin/AnimatingParts/GourdMonument_flap (1)
        }),
        new Template("PuzzleGourdVice", new V3(1f, 1f, 1f), new Part[] {
            new Part("Cylinder", 216, new V3(0f, 0.181f, 0.684f), new Q(-0.505122f, 0.494825f, -0.494825f, 0.505122f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // AnimatingParts/vice/Cylinder
            new Part("vice-arm", 293, new V3(0f, 0f, 0.01f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // AnimatingParts/vice/vice-arm
            new Part("vice-base", 743, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // AnimatingParts/vice/vice-base
        }),
        new Template("PuzzleLiveSignal", new V3(1f, 1f, 1f), new Part[] {
            new Part("LiveSignal", 1187, new V3(0f, 0f, 0f), new Q(0f, 0f, 0f, 1f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // LiveSignal/LiveSignal
        }),
        new Template("PuzzlePegBoard", new V3(1f, 1f, 1f), new Part[] {
            new Part("PegBoard_5slot", 1185, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // PegBoard/PegBoard_5slot
        }),
        new Template("PuzzlePegBoard 1", new V3(1f, 1f, 1f), new Part[] {
            new Part("PegBoard_1slot", 636, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // PegBoard/PegBoard_1slot
        }),
        new Template("PuzzlePegBoard 1 Marker", new V3(1f, 1f, 1f), new Part[] {
            new Part("PegBoard_1slot", 636, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // PegBoard/PegBoard_1slot
            new Part("PegBoard_startmarker", 506, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // PegBoard/PegBoard_startmarker
        }),
        new Template("PuzzlePegBoard 2", new V3(1f, 1f, 1f), new Part[] {
            new Part("PegBoard_2slot", 767, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // PegBoard/PegBoard_2slot
        }),
        new Template("PuzzlePegBoard 2 Marker", new V3(1f, 1f, 1f), new Part[] {
            new Part("PegBoard_2slot", 767, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // PegBoard/PegBoard_2slot
            new Part("PegBoard_startmarker", 506, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // PegBoard/PegBoard_startmarker
        }),
        new Template("PuzzlePegBoard 3", new V3(1f, 1f, 1f), new Part[] {
            new Part("PegBoard_3slot", 905, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // PegBoard/PegBoard_3slot
        }),
        new Template("PuzzlePegBoard 3 Marker", new V3(1f, 1f, 1f), new Part[] {
            new Part("PegBoard_3slot", 905, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // PegBoard/PegBoard_3slot
            new Part("PegBoard_startmarker", 506, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // PegBoard/PegBoard_startmarker
        }),
        new Template("PuzzlePegBoard 4", new V3(1f, 1f, 1f), new Part[] {
            new Part("PegBoard_4slot", 1031, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // PegBoard/PegBoard_4slot
        }),
        new Template("PuzzlePegBoard 4 Marker", new V3(1f, 1f, 1f), new Part[] {
            new Part("PegBoard_4slot", 1031, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // PegBoard/PegBoard_4slot
            new Part("PegBoard_startmarker", 506, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // PegBoard/PegBoard_startmarker
        }),
        new Template("PuzzlePegBoard 5", new V3(1f, 1f, 1f), new Part[] {
            new Part("PegBoard_5slot", 1185, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // PegBoard/PegBoard_5slot
        }),
        new Template("PuzzlePegBoard 5 Marker", new V3(1f, 1f, 1f), new Part[] {
            new Part("PegBoard_5slot", 1185, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // PegBoard/PegBoard_5slot
            new Part("PegBoard_startmarker", 506, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // PegBoard/PegBoard_startmarker
        }),
        new Template("PuzzlePegBoard 6", new V3(1f, 1f, 1f), new Part[] {
            new Part("PegBoard_6slot", 1294, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // PegBoard/PegBoard_6slot
        }),
        new Template("PuzzlePegBoard 6 Marker", new V3(1f, 1f, 1f), new Part[] {
            new Part("PegBoard_6slot", 1294, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // PegBoard/PegBoard_6slot
            new Part("PegBoard_startmarker", 506, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // PegBoard/PegBoard_startmarker
        }),
        new Template("PuzzlePegBoard 12", new V3(1f, 1f, 1f), new Part[] {
            new Part("PegBoard_12slot", 1811, new V3(-0.9f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // PegBoard/PegBoard_12slot
        }),
        new Template("PuzzlePegBoard 12 Marker", new V3(1f, 1f, 1f), new Part[] {
            new Part("PegBoard_12slot", 1811, new V3(-0.9f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // PegBoard/PegBoard_12slot
            new Part("PegBoard_startmarker", 506, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // PegBoard/PegBoard_startmarker
        }),
        new Template("PuzzleButtonNHold 2", new V3(1f, 1f, 1f), new Part[] {
            new Part("Button-nHold-Button", 247, new V3(0f, -0.015f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // Button-nHold-Four/Button-nHold-Button
            new Part("Button-nHold-Case-Two", 800, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // Button-nHold-Four/Button-nHold-Case-Two
        }),
        new Template("PuzzleButtonNHold 3", new V3(1f, 1f, 1f), new Part[] {
            new Part("Button-nHold-Button", 247, new V3(0f, -0.015f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // Button-nHold-Four/Button-nHold-Button
            new Part("Button-nHold-Case-Three", 886, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // Button-nHold-Four/Button-nHold-Case-Three
        }),
        new Template("PuzzleButtonNHold 4", new V3(1f, 1f, 1f), new Part[] {
            new Part("Button-nHold-Button", 247, new V3(0f, -0.015f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // Button-nHold-Four/Button-nHold-Button
            new Part("Button-nHold-Case-Four", 980, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // Button-nHold-Four/Button-nHold-Case-Four
        }),
        new Template("PuzzlePegTile", new V3(1f, 1f, 1f), new Part[] {
            new Part("pegtile", 134, new V3(0f, 0f, 0f), new Q(-0.707107f, 0f, 0f, 0.707107f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.PegTileFace }),   // Kernal/model
        }),
        new Template("PuzzleStartSwitch", new V3(1f, 1f, 1f), new Part[] {
            new Part("backing", 160, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // rotatingSwitch/backing
            new Part("knob", 357, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // rotatingSwitch/knobState1
        }),
        new Template("PuzzleValidator", new V3(1f, 1f, 1f), new Part[] {
            new Part("validator_face", 689, new V3(0f, 0f, 0f), new Q(-0.707107f, 0f, 0f, 0.707107f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.Screen }),   // validator_base
        }),
        new Template("PuzzleDisplay", new V3(0.7f, 0.7f, 0.7f), new Part[] {
            new Part("splitflap_case_1slot", 718, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // splitflap_case_1slot
            new Part("splitflap_flap", 76, new V3(0f, 0f, 0f), new Q(-0.707107f, 0f, 0f, 0.707107f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.VertexColor }),   // SplitFlapGlyph/splitflap_flapUpper
            new Part("splitflap_flap", 76, new V3(0f, 0f, 0f), new Q(0.707107f, 0f, 0f, 0.707107f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.VertexColor }),   // SplitFlapGlyph/splitflap_flapLower
            new Part("splitflap_flap", 76, new V3(0f, 0f, 0f), new Q(-0.766044f, -8.62661e-17f, -8.62661e-17f, 0.642788f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.VertexColor }),   // SplitFlapGlyph/splitflap_flapRear
        }),
        new Template("PuzzleDisplay Two", new V3(0.7f, 0.7f, 0.7f), new Part[] {
            new Part("splitflap_case_2slot", 1138, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // splitflap_case_2slot
            new Part("splitflap_flap", 76, new V3(0f, 0f, 0f), new Q(-0.707107f, 0f, 0f, 0.707107f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.VertexColor }),   // SplitFlapGlyph/splitflap_flapUpper
            new Part("splitflap_flap", 76, new V3(0f, 0f, 0f), new Q(0.707107f, 0f, 0f, 0.707107f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.VertexColor }),   // SplitFlapGlyph/splitflap_flapLower
            new Part("splitflap_flap", 76, new V3(0f, 0f, 0f), new Q(-0.766044f, -8.62661e-17f, -8.62661e-17f, 0.642788f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.VertexColor }),   // SplitFlapGlyph/splitflap_flapRear
            new Part("splitflap_flap", 76, new V3(-0.5f, 0f, 0f), new Q(-0.707107f, 0f, 0f, 0.707107f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.VertexColor }),   // SplitFlapGlyph/splitflap_flapUpper
            new Part("splitflap_flap", 76, new V3(-0.5f, 0f, 0f), new Q(0.707107f, 0f, 0f, 0.707107f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.VertexColor }),   // SplitFlapGlyph/splitflap_flapLower
            new Part("splitflap_flap", 76, new V3(-0.5f, 0f, 0f), new Q(-0.766044f, -8.62661e-17f, -8.62661e-17f, 0.642788f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.VertexColor }),   // SplitFlapGlyph/splitflap_flapRear
        }),
        new Template("PuzzleDisplay Three", new V3(0.7f, 0.7f, 0.7f), new Part[] {
            new Part("splitflap_case_3slot", 1518, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // splitflap_case_3slot
            new Part("splitflap_flap", 76, new V3(0f, 0f, 0f), new Q(-0.707107f, 0f, 0f, 0.707107f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.VertexColor }),   // SplitFlapGlyph/splitflap_flapUpper
            new Part("splitflap_flap", 76, new V3(0f, 0f, 0f), new Q(0.707107f, 0f, 0f, 0.707107f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.VertexColor }),   // SplitFlapGlyph/splitflap_flapLower
            new Part("splitflap_flap", 76, new V3(0f, 0f, 0f), new Q(-0.766044f, -8.62661e-17f, -8.62661e-17f, 0.642788f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.VertexColor }),   // SplitFlapGlyph/splitflap_flapRear
            new Part("splitflap_flap", 76, new V3(-0.5f, 0f, 0f), new Q(-0.707107f, 0f, 0f, 0.707107f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.VertexColor }),   // SplitFlapGlyph/splitflap_flapUpper
            new Part("splitflap_flap", 76, new V3(-0.5f, 0f, 0f), new Q(0.707107f, 0f, 0f, 0.707107f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.VertexColor }),   // SplitFlapGlyph/splitflap_flapLower
            new Part("splitflap_flap", 76, new V3(-0.5f, 0f, 0f), new Q(-0.766044f, -8.62661e-17f, -8.62661e-17f, 0.642788f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.VertexColor }),   // SplitFlapGlyph/splitflap_flapRear
            new Part("splitflap_flap", 76, new V3(-1f, 0f, 0f), new Q(-0.707107f, 0f, 0f, 0.707107f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.VertexColor }),   // SplitFlapGlyph/splitflap_flapUpper
            new Part("splitflap_flap", 76, new V3(-1f, 0f, 0f), new Q(0.707107f, 0f, 0f, 0.707107f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.VertexColor }),   // SplitFlapGlyph/splitflap_flapLower
            new Part("splitflap_flap", 76, new V3(-1f, 0f, 0f), new Q(-0.766044f, -8.62661e-17f, -8.62661e-17f, 0.642788f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.VertexColor }),   // SplitFlapGlyph/splitflap_flapRear
        }),
        new Template("PuzzleDisplay Five", new V3(0.7f, 0.7f, 0.7f), new Part[] {
            new Part("splitflap_case_5slot", 2318, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // splitflap_case_5slot
            new Part("splitflap_flap", 76, new V3(0f, 0f, 0f), new Q(-0.707107f, 0f, 0f, 0.707107f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.VertexColor }),   // SplitFlapGlyph/splitflap_flapUpper
            new Part("splitflap_flap", 76, new V3(0f, 0f, 0f), new Q(0.707107f, 0f, 0f, 0.707107f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.VertexColor }),   // SplitFlapGlyph/splitflap_flapLower
            new Part("splitflap_flap", 76, new V3(0f, 0f, 0f), new Q(-0.766044f, -8.62661e-17f, -8.62661e-17f, 0.642788f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.VertexColor }),   // SplitFlapGlyph/splitflap_flapRear
            new Part("splitflap_flap", 76, new V3(-0.5f, 0f, 0f), new Q(-0.707107f, 0f, 0f, 0.707107f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.VertexColor }),   // SplitFlapGlyph/splitflap_flapUpper
            new Part("splitflap_flap", 76, new V3(-0.5f, 0f, 0f), new Q(0.707107f, 0f, 0f, 0.707107f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.VertexColor }),   // SplitFlapGlyph/splitflap_flapLower
            new Part("splitflap_flap", 76, new V3(-0.5f, 0f, 0f), new Q(-0.766044f, -8.62661e-17f, -8.62661e-17f, 0.642788f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.VertexColor }),   // SplitFlapGlyph/splitflap_flapRear
            new Part("splitflap_flap", 76, new V3(-1f, 0f, 0f), new Q(-0.707107f, 0f, 0f, 0.707107f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.VertexColor }),   // SplitFlapGlyph/splitflap_flapUpper
            new Part("splitflap_flap", 76, new V3(-1f, 0f, 0f), new Q(0.707107f, 0f, 0f, 0.707107f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.VertexColor }),   // SplitFlapGlyph/splitflap_flapLower
            new Part("splitflap_flap", 76, new V3(-1f, 0f, 0f), new Q(-0.766044f, -8.62661e-17f, -8.62661e-17f, 0.642788f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.VertexColor }),   // SplitFlapGlyph/splitflap_flapRear
            new Part("splitflap_flap", 76, new V3(-1.5f, 0f, 0f), new Q(-0.707107f, 0f, 0f, 0.707107f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.VertexColor }),   // SplitFlapGlyph/splitflap_flapUpper
            new Part("splitflap_flap", 76, new V3(-1.5f, 0f, 0f), new Q(0.707107f, 0f, 0f, 0.707107f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.VertexColor }),   // SplitFlapGlyph/splitflap_flapLower
            new Part("splitflap_flap", 76, new V3(-1.5f, 0f, 0f), new Q(-0.766044f, -8.62661e-17f, -8.62661e-17f, 0.642788f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.VertexColor }),   // SplitFlapGlyph/splitflap_flapRear
            new Part("splitflap_flap", 76, new V3(-2f, 0f, 0f), new Q(-0.707107f, 0f, 0f, 0.707107f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.VertexColor }),   // SplitFlapGlyph/splitflap_flapUpper
            new Part("splitflap_flap", 76, new V3(-2f, 0f, 0f), new Q(0.707107f, 0f, 0f, 0.707107f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.VertexColor }),   // SplitFlapGlyph/splitflap_flapLower
            new Part("splitflap_flap", 76, new V3(-2f, 0f, 0f), new Q(-0.766044f, -8.62661e-17f, -8.62661e-17f, 0.642788f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.VertexColor }),   // SplitFlapGlyph/splitflap_flapRear
        }),
        new Template("PuzzleTimer", new V3(1f, 1f, 1f), new Part[] {
            new Part("#text", 0, new V3(0f, 0f, 0.108606f), new Q(0f, 1f, 0f, 6.12323e-17f), new V3(4.722f, 4.722f, 4.722f), new Kind[0]),   // sizer/textBack
            new Part("#cube", 24, new V3(0f, 0f, 0f), new Q(0f, 0f, 0f, 1f), new V3(3.92398f, 1.5677f, 0.21249f), new[] { Kind.Screen }),   // sizer/Cube
        }),
        new Template("PuzzlePokeButton", new V3(1f, 1f, 1f), new Part[] {
            new Part("PokeButton_Backing", 770, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // BasicPokeButtonModel/PokeButton_Backing
            new Part("Pokebutton_Button", 226, new V3(0f, 0f, 0.03f), new Q(-0.707107f, 0f, 0f, 0.707107f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // BasicPokeButtonModel/Pokebutton_Button_Primed
        }),
        new Template("PuzzleProgressBar", new V3(1f, 1f, 1f), new Part[] {
            new Part("ProgressBar", 68, new V3(0f, 0f, 0.024f), new Q(0f, 0f, 0f, 1f), new V3(1f, 1f, 1f), new[] { Kind.Screen, Kind.VertexColor }),   // progressbar
        }),
        new Template("PuzzleSimPressSwitch", new V3(1f, 1f, 1f), new Part[] {
            new Part("syncswitch_base", 1277, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // BasicSyncSwitch
            new Part("syncswitch_lever", 136, new V3(0f, -6.63225e-06f, 0.019f), new Q(-0.25865f, 0f, 0f, 0.965971f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // BasicSyncSwitch/syncswitch_leverDownOff
        }),
        new Template("PuzzleLight TableRed", new V3(1f, 1f, 1f), new Part[] {
            new Part("lamp_red", 605, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.VertexColor }),   // lamp_red
        }),
        new Template("PuzzleLight Pendant", new V3(1f, 1f, 1f), new Part[] {
            new Part("Lamp_Pendant", 572, new V3(0f, 0f, 0f), new Q(-0.707107f, 0f, 0f, 0.707107f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // LightMesh
        }),
        new Template("PuzzleLight Spotlight_Black", new V3(1f, 1f, 1f), new Part[] {
            new Part("Spotlight_Hinge", 300, new V3(0f, 0.1f, 0f), new Q(0.612372f, 0.612372f, -0.353553f, 0.353553f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // Lamp_Spotlight/Spotlight_Hinge
            new Part("Spotlight_Lamp", 1314, new V3(0f, 0.1f, 0f), new Q(0.603392f, 0.0736876f, 0.683547f, -0.404044f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // Lamp_Spotlight/Spotlight_Lamp
        }),
        new Template("PuzzleLight Spotlight_Red", new V3(1f, 1f, 1f), new Part[] {
            new Part("DownLight_Hinge", 24, new V3(0f, 0.1f, 0f), new Q(0.26882f, 0.710679f, -0.650131f, 0.000992856f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // Lamp_Spotlight/Spotlight_Hinge
            new Part("DownLight_Lamp", 340, new V3(0f, 0.1f, 0f), new Q(0.885373f, 0f, 0f, -0.464881f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // Lamp_Spotlight/Spotlight_Lamp
        }),
        new Template("PuzzleLight Tube", new V3(1f, 1f, 1f), new Part[] {
            new Part("Lamp_Tube", 685, new V3(0f, 0f, 0.11f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.VertexColor }),   // Lamp_Tube/Lamp_Tube
            new Part("Lamp_Tube_wallmount", 408, new V3(0f, 0f, 0.11f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // Lamp_Tube/Lamp_Tube_wallmount
        }),
        new Template("PuzzleLight Circle", new V3(1f, 1f, 1f), new Part[] {
            new Part("TrainLightFlood", 422, new V3(0f, 0f, 0f), new Q(0f, 0f, 0f, 1f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // 
        }),
        new Template("PuzzleLight Ring", new V3(1f, 1f, 1f), new Part[] {
            new Part("BezierCircle.001", 674, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // Lamp_Fluoro
        }),
        new Template("PuzzleTelescope", new V3(1f, 1f, 1f), new Part[] {
            new Part("telescope_swivel_lv", 228, new V3(0f, 0f, 0f), new Q(0.5f, -0.5f, -0.5f, -0.5f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // swiveler/telescope_swivel_lv
            new Part("telescope_scope_lv", 951, new V3(-1.15907e-16f, 0.522f, -2.57366e-32f), new Q(0.550266f, -0.44408f, -0.550266f, -0.44408f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // swiveler/telescope_swivel_lv/telescope_scope_lv
            new Part("telescope_base_lv", 108, new V3(0f, 0f, 0f), new Q(-0.707107f, 0f, 0f, 0.707107f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // telescope_lobbyvision
        }),
        // The pillow keeps its own upright turn (270 on X, like the game's); the mod copies it the same way
        new Template("PuzzlePillow", new V3(1f, 1f, 1f), new Part[] {
            new Part("pillow_alltogether", 2198, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // gourdValetPillowModel
        }),
        // ---- Batch scan (low-priority components) ----
        new Template("PuzzleVanillaItem Blindfold", new V3(1f, 1f, 1f), new Part[] {
            new Part("blank.001", 5805, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(0.332f, 0.332f, 0.332f), new[] { Kind.VertexColor }),   // kernal/blindfold
        }),
        new Template("PuzzleCountInput", new V3(1f, 1f, 1f), new Part[] {
            new Part("counter_button_minus", 135, new V3(0.5f, -0.14f, 0.46f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // counter/counter_button_minus
            new Part("counter_button_plus", 135, new V3(0.5f, 0.14f, 0.46f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // counter/counter_button_plus
            new Part("counter_casing", 663, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // counter/counter_casing
            new Part("counter_casing_buttonpanel", 460, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // counter/counter_casing_buttonpanel
            new Part("counter_dial_ones", 292, new V3(-0.092f, 0f, 0.2f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // counter/counter_dial_ones
            new Part("counter_dial_ones", 292, new V3(0.092f, 0f, 0.2f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // counter/counter_dial_tens
        }),
        new Template("PuzzleCountValidator", new V3(1f, 1f, 1f), new Part[] {
            new Part("counter_dial_ones", 189, new V3(0.144f, 0f, 0.202f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(0.444f, 0.444f, 0.444f), new[] { Kind.VertexColor }),   // validator_counter/counter_dial_ones
            new Part("counter_dial_ones", 189, new V3(0.24f, 0f, 0.202f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(0.444f, 0.444f, 0.444f), new[] { Kind.VertexColor }),   // validator_counter/counter_dial_tens
            new Part("validator_face", 762, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(0.74f, 0.74f, 0.74f), new[] { Kind.VertexColor, Kind.Screen }),   // validator_counter/validator_face
        }),
        new Template("PuzzleIntercom", new V3(1f, 1f, 1f), new Part[] {
            new Part("microphone-wallmount", 805, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // microphone-wallmounted/microphone-wallmount
            new Part("microphone-wallmount-Button", 178, new V3(0f, -0.08f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // microphone-wallmounted/microphone-wallmount-Button
        }),
        new Template("PuzzleIntercomSpeaker", new V3(1f, 1f, 1f), new Part[] {
            new Part("Speaker-wallmounted", 1469, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.VertexColor, Kind.VertexColor }),   // speaker-wallmounted
        }),
        new Template("PuzzleSoundPeg", new V3(1f, 1f, 1f), new Part[] {
            new Part("pegtile_speaker", 1042, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.VertexColor, Kind.VertexColor }),   // Kernal/pegtile_speaker
        }),
        new Template("PuzzleSoundPegAnswer", new V3(1f, 1f, 1f), new Part[] {
            new Part("MasterSequenceSpeaker", 849, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.VertexColor }),   // MasterSequenceSpeaker
        }),
        new Template("PuzzleNoItemsGate BlindMask", new V3(1f, 1f, 1f), new Part[] {
            new Part("turnstile_bar_LOD0", 801, new V3(-1f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // door_turnstile/turnstile_bar/turnstile_bar_LOD0
            new Part("turnstile_frame_blindfold", 3132, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // door_turnstile/turnstile_frame/turnstile_frame_blindfold
            new Part("turnstile_plaque", 264, new V3(0f, 2.2f, 0.25f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.VertexColor }),   // door_turnstile/turnstile_plaque
            new Part("turnstile_plaque", 264, new V3(0f, 2.2f, -0.25f), new Q(0.707107f, 0f, 0f, 0.707107f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.VertexColor }),   // door_turnstile/turnstile_plaque (1)
            new Part("turnstile_spinner_blindfold", 6517, new V3(0f, 1.425f, 0.000497419f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // door_turnstile/turnstile_spinner/turnstile_spinner_blindfold
            new Part("turnstile_spinner_blindfold", 6517, new V3(0f, 1.425f, 0.000497419f), new Q(-0.353492f, 0.612266f, 0.612479f, 0.353615f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // door_turnstile/turnstile_spinner/turnstile_spinner_blindfold (1)
            new Part("turnstile_spinner_blindfold", 6517, new V3(0f, 1.425f, 0.000497419f), new Q(0.353492f, 0.612266f, 0.612479f, -0.353615f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // door_turnstile/turnstile_spinner/turnstile_spinner_blindfold (2)
            new Part("turnstile_spinner_LOD0", 1710, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // door_turnstile/turnstile_spinner/turnstile_spinner_LOD0
        }),
        new Template("PuzzleVanillaItem FoldingChair", new V3(1f, 1f, 1f), new Part[] {
            new Part("chair", 1138, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.Tinted }),   // Kernal/foldingchair
        }),
        new Template("PuzzleCirclePeg", new V3(1f, 1f, 1f), new Part[] {
            new Part("pegtile_round", 180, new V3(0f, 0f, 0f), new Q(-0.707107f, 0f, 0f, 0.707107f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.VertexColor }),   // Kernal/model
        }),
        new Template("PuzzleCirclePegBoard", new V3(1f, 1f, 1f), new Part[] {
            new Part("egghunt_pegboard", 3825, new V3(0f, 0f, 0f), new Q(-0.707107f, 0f, 0f, 0.707107f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // EggHunt_Pegboard
        }),
        new Template("PuzzleBuoyLight", new V3(1f, 1f, 1f), new Part[] {
            new Part("pedestal", 243, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // Model
            new Part("buoy", 730, new V3(0f, 0.8f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // BouyPedistalPropHome/BuoyLight/kernal/buoy
        }),
        new Template("PuzzleLight Conductor", new V3(1f, 1f, 1f), new Part[] {
            new Part("ConductorLight", 1334, new V3(0f, 0f, 0f), new Q(0f, 0f, 0f, 1f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // conductorLight
        }),
        new Template("PuzzleTelescope Normal", new V3(1f, 1f, 1f), new Part[] {
            new Part("telescope_base", 108, new V3(0f, 0f, 0f), new Q(-0.707107f, 0f, 0f, 0.707107f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // telescope
            new Part("telescope_swivel", 228, new V3(0f, 0f, 0f), new Q(-0.704007f, 0.0661392f, 0.0661392f, 0.704007f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // swivler/telescope_swivel
            new Part("telescope_scope", 960, new V3(-2.15877e-17f, 0.522f, -1.13879e-16f), new Q(-0.702715f, 0.0662603f, 0.0660178f, 0.705296f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // swivler/telescope_swivel/telescope_scope
        }),
        new Template("PuzzleProgressBar Spiral", new V3(1f, 1f, 1f), new Part[] {
            new Part("progressbar_spiral", 3978, new V3(0f, 1f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.Screen }),   // progressbar_spiral
        }),
        new Template("PuzzleTimerSpawner", new V3(1f, 1f, 1f), new Part[] {
            new Part("dispenser", 2942, new V3(0f, 0f, 0f), new Q(-0.707107f, 0f, 0f, 0.707107f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // PomodoroDispenserAnimator/AutomaticPitcher/dispenser
            new Part("ball_bottom", 494, new V3(0f, 1.017f, 0.25f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.VertexColor }),   // PomodoroDispenserAnimator/PomodoroDispenserHomeDispenser/Pomodoro/Kernal/pomodoro/ball_bottom
            new Part("ball_top", 637, new V3(0f, 1.017f, 0.25f), new Q(-0.707107f, 0f, 0f, 0.707107f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.VertexColor }),   // PomodoroDispenserAnimator/PomodoroDispenserHomeDispenser/Pomodoro/Kernal/pomodoro/ball_top
        }),
        new Template("PuzzleTimerGoal", new V3(1f, 1f, 1f), new Part[] {
            new Part("pomodoroholder", 1624, new V3(0f, 0.6f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // PlacementCastable/pomodoro_holder
        }),
        new Template("PuzzleNoItemsGate", new V3(1f, 1f, 1f), new Part[] {
            new Part("turnstile_bar_LOD0", 801, new V3(-1f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // door_turnstile/turnstile_bar/turnstile_bar_LOD0
            new Part("turnstile_frame_LOD0", 3132, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // door_turnstile/turnstile_frame/turnstile_frame_LOD0
            new Part("turnstile_plaque", 264, new V3(0f, 2.2f, 0.25f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.VertexColor }),   // door_turnstile/turnstile_plaque
            new Part("turnstile_plaque", 264, new V3(0f, 2.2f, -0.25f), new Q(0.707107f, 0f, 0f, 0.707107f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.VertexColor }),   // door_turnstile/turnstile_plaque (1)
            new Part("turnstile_spinner_LOD0", 1710, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // door_turnstile/turnstile_spinner/turnstile_spinner_LOD0
        }),
        new Template("PuzzleFireworkLauncher", new V3(1f, 1f, 1f), new Part[] {
            new Part("flaregun", 2723, new V3(0f, 0f, 0f), new Q(0.939693f, 0f, 0f, -0.34202f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.VertexColor, Kind.VertexColor }),   // Pivot/flaregun
            new Part("flaregunbarrel", 1063, new V3(0f, 0.106285f, 0.184479f), new Q(0.939693f, 0f, 0f, -0.34202f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.VertexColor }),   // Pivot/flaregun/flaregunbarrel
            new Part("flareguntimer", 18, new V3(0f, 0f, 0f), new Q(0.939693f, 0f, 0f, -0.34202f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // Pivot/flaregun/flaregunbarrel/flareguntimer
        }),
        // ---- Vanilla Item (from the Ctrl+F3 dump of 2026-10-15) ----
        new Template("PuzzleVanillaItem Torch", new V3(1f, 1f, 1f), new Part[] {
            new Part("torch", 4287, new V3(0f, 0f, 0f), new Q(-0.499913f, -0.500087f, -0.499913f, 0.500087f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // Kernal/torch
        }),
        new Template("PuzzleVanillaItem FmRadio", new V3(1f, 1f, 1f), new Part[] {
            new Part("radio_antenna", 200, new V3(-0.074f, 0.23f, -0.05f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // FmRadioKernal/Meshes/fmRadio/radio_antenna
            new Part("radio_body", 4090, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.Plain, Kind.Plain }),   // FmRadioKernal/Meshes/fmRadio/radio_body
            new Part("radio_button_up", 184, new V3(0f, 0.14f, 0.014f), new Q(0f, -0.707107f, 0f, 0.707107f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // FmRadioKernal/Meshes/fmRadio/radio_button_up
            new Part("radio_button_up", 184, new V3(0f, 0.09f, 0.014f), new Q(-0.707107f, 0f, 0.707107f, 0f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // FmRadioKernal/Meshes/fmRadio/radio_button_up (1)
            new Part("radio_onoff", 312, new V3(0.08f, 0.145f, 0f), new Q(0f, 0f, 0f, 1f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // FmRadioKernal/Meshes/fmRadio/radio_onoff
            new Part("radio_tuner", 4, new V3(-0.01f, 0.123f, 0.015f), new Q(0f, 0f, 0f, 1f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // FmRadioKernal/Meshes/fmRadio/radioTunerParent/radio_tuner - Moving
            new Part("radio_volume", 384, new V3(0.11f, 0f, 0f), new Q(0f, -0.707107f, 0f, 0.707107f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // FmRadioKernal/Meshes/fmRadio/radio_volume
            new Part("radio_stationindicators", 24, new V3(0.02f, 0.168f, 0.07f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // FmRadioKernal/Meshes/lights/radio_stationindicators (1)
            new Part("radio_stationindicators", 24, new V3(0.005f, 0.168f, 0.07f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // FmRadioKernal/Meshes/lights/radio_stationindicators (2)
            new Part("radio_stationindicators", 24, new V3(-0.01f, 0.168f, 0.07f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // FmRadioKernal/Meshes/lights/radio_stationindicators (3)
            new Part("radio_stationindicators", 24, new V3(-0.025f, 0.168f, 0.07f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // FmRadioKernal/Meshes/lights/radio_stationindicators (4)
            new Part("radio_stationindicators", 24, new V3(-0.04f, 0.168f, 0.07f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // FmRadioKernal/Meshes/lights/radio_stationindicators (5)
            new Part("radio_stationindicators", 24, new V3(-0.055f, 0.168f, 0.07f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // FmRadioKernal/Meshes/lights/radio_stationindicators (6)
            new Part("radio_stationindicators", 24, new V3(-0.07f, 0.168f, 0.07f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // FmRadioKernal/Meshes/lights/radio_stationindicators (7)
        }),
        new Template("PuzzleVanillaItem WalkieTalkie", new V3(1f, 1f, 1f), new Part[] {
            new Part("WalkieTalkie_button", 232, new V3(0.08f, 0.17f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // WalkieTalkieKernal/onWhenNotSending/walkietalkieA-Button (1)
            new Part("WalkieTalkie", 4739, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.Tinted, Kind.VertexColor, Kind.Plain, Kind.VertexColor, Kind.VertexColor, Kind.VertexColor, Kind.VertexColor, Kind.Plain }),   // WalkieTalkieKernal/WalkieTalkie/WalkieTalkie
        }),
        new Template("PuzzleVanillaItem Binoculars", new V3(1f, 1f, 1f), new Part[] {
            new Part("binoculars", 3794, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // BinocularsKernal/binoculars
        }),
        new Template("PuzzleVanillaItem XrayGoggles", new V3(1f, 1f, 1f), new Part[] {
            new Part("ItemFinder", 5516, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // BinocularsKernal/ItemFinder
        }),
        new Template("PuzzleVanillaItem Clock", new V3(1f, 1f, 1f), new Part[] {
            new Part("ClockFace", 1468, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.Plain }),   // Kernal/clock/ClockFace
            new Part("HourHand", 228, new V3(0f, 0f, 0f), new Q(0f, 0f, -0.470881f, 0.882197f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // Kernal/clock/HourHand
            new Part("SecondHand", 466, new V3(0f, 0f, 0f), new Q(0f, 0f, 0.866025f, 0.5f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // Kernal/clock/SecondHand
        }),
        new Template("PuzzleVanillaItem Laser", new V3(1f, 1f, 1f), new Part[] {
            new Part("laserpointer_LOD0", 477, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.Plain }),   // LaserKernal/laserpointer/laserpointer_LOD0
        }),
        new Template("PuzzleVanillaItem Megaphone", new V3(1f, 1f, 1f), new Part[] {
            new Part("megaphone_body", 4211, new V3(0f, 0f, 0f), new Q(0f, 0f, 0f, 1f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.Plain }),   // Kernal/megaphoneModel/megaphone_body
            new Part("megaphone_speaker", 470, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // Kernal/megaphoneModel/megaphone_speaker
        }),
        new Template("PuzzleVanillaItem Kettle", new V3(1f, 1f, 1f), new Part[] {
            new Part("kettle", 2404, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.Tinted, Kind.VertexColor }),   // Kernal/kettle
        }),
        new Template("PuzzleVanillaItem CannonBall", new V3(1f, 1f, 1f), new Part[] {
            new Part("#sphere", 515, new V3(0f, 0f, 0f), new Q(0f, 0f, 0f, 1f), new V3(0.18f, 0.18f, 0.18f), new[] { Kind.Cannonball }),   // Kernal/Sphere
        }),
        new Template("PuzzleVanillaItem PermoSign", new V3(1f, 1f, 1f), new Part[] {
            new Part("PermoSign", 391, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.VertexColor }),   // SignKernal/permoSign
        }),
        new Template("PuzzleVanillaItem SpareSign", new V3(1f, 1f, 1f), new Part[] {
            new Part("whiteboard", 1691, new V3(0f, 0f, 0f), new Q(-0.707107f, 0f, 0f, 0.707107f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.VertexColor }),   // SignKernal/sign
        }),
        new Template("PuzzleVanillaItem GourdHolder", new V3(1f, 1f, 1f), new Part[] {
            new Part("GourdCarton", 1748, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.Plain, Kind.VertexColor }),   // Kernal/gourdCarton
        }),
        new Template("PuzzleVanillaItem Belt", new V3(1f, 1f, 1f), new Part[] {
            new Part("belt_hung", 1310, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // Kernal/holster/belt_hung
            new Part("triangle", 714, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.Tinted, Kind.VertexColor }),   // Kernal/holster/triangle
        }),
        new Template("PuzzleVanillaItem Backpack", new V3(1f, 1f, 1f), new Part[] {
            new Part("backpack_frame", 988, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.Tinted }),   // Kernal/backpack/backpack_frame
            new Part("backpack_straps_hung", 832, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // Kernal/backpack/backpack_straps_hung
        }),
        new Template("PuzzleVanillaItem Gps", new V3(1f, 1f, 1f), new Part[] {
            new Part("coordinate-tracker", 2270, new V3(0f, 0f, 0f), new Q(0f, 0.707107f, 0.707107f, 0f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.VertexColor }),   // Kernal/coordinate-tracker
        }),
        new Template("PuzzleVanillaItem FoldingMap", new V3(1f, 1f, 1f), new Part[] {
            new Part("FurledMap", 178, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.Plain, Kind.Plain }),   // Kernal/foldingMap/FurledMap
        }),
        new Template("PuzzleVanillaItem Compass", new V3(1f, 1f, 1f), new Part[] {
            new Part("compass_base", 1175, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor, Kind.Plain }),   // Kernal/compass/compass_base
            new Part("compass_needle", 258, new V3(0f, 0f, 0f), new Q(-0.317226f, 0.631956f, 0.631956f, 0.317226f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // Kernal/compass/needleRotor/needle (1)
        }),
        new Template("PuzzleVanillaItem FlareGun", new V3(1f, 1f, 1f), new Part[] {
            new Part("flaregun", 2723, new V3(0f, 0f, 0f), new Q(-0.707107f, 0f, 0f, 0.707107f), new V3(1f, 1f, 1f), new[] { Kind.Tinted, Kind.VertexColor, Kind.VertexColor }),   // FlareGunKernal/flaregun
            new Part("flaregunbarrel", 1063, new V3(0f, -0.073f, 0.2f), new Q(-0.707107f, 0f, 0f, 0.707107f), new V3(1f, 1f, 1f), new[] { Kind.Tinted, Kind.VertexColor }),   // FlareGunKernal/flaregun/flaregunbarrel
            new Part("flareguntimer", 18, new V3(0f, 0f, 0f), new Q(-0.707107f, 0f, 0f, 0.707107f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // FlareGunKernal/flaregun/flaregunbarrel/flareguntimer
        }),
        new Template("PuzzleVanillaItem Cowbell", new V3(1f, 1f, 1f), new Part[] {
            new Part("cowbell_large_bell", 1248, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // Kernal/cowbell/cowbell_large_bell
            new Part("cowbell_large_clapper", 185, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // Kernal/cowbell/cowbell_large_bell/cowbell_large_clapper
        }),
        new Template("PuzzleVanillaItem Paintbrush", new V3(1f, 1f, 1f), new Part[] {
            new Part("salonbrush", 133, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.Plain, Kind.VertexColor, Kind.Plain }),   // Kernal/salonbrush
        }),
        new Template("PuzzleVanillaItem CowbellMedium", new V3(1f, 1f, 1f), new Part[] {
            new Part("cowbell_medium_bell", 1248, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // Kernal/cowbell/cowbell_medium_bell
            new Part("cowbell_medium_clapper", 185, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // Kernal/cowbell/cowbell_medium_bell/cowbell_medium_clapper
        }),
        new Template("PuzzleVanillaItem CowbellSmall", new V3(1f, 1f, 1f), new Part[] {
            new Part("cowbell_small_bell", 1252, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // Kernal/cowbell/cowbell_small_bell
            new Part("cowbell_small_clapper", 185, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // Kernal/cowbell/cowbell_small_bell/cowbell_small_clapper
        }),
        new Template("PuzzleVanillaItem Football", new V3(1f, 1f, 1f), new Part[] {
            new Part("footy", 1004, new V3(0f, 0f, 0f), new Q(-0.706983f, 0f, 0f, 0.70723f), new V3(1f, 1f, 1f), new[] { Kind.VertexColor }),   // FootyKernal/footy
        }),
    };

    // Every mesh the previews use, with the vertex count of the right copy
    internal static readonly (string name, int vertices)[] Meshes = {
        ("BasicPushButton", 247),
        ("BasicPushButtonCase", 706),
        ("BezierCircle.001", 674),
        ("Button-nHold-Button", 247),
        ("Button-nHold-Case-Four", 980),
        ("Button-nHold-Case-Three", 886),
        ("Button-nHold-Case-Two", 800),
        ("ClockFace", 1468),
        ("ConductorLight", 1334),
        ("Cylinder", 216),
        ("DownLight_Hinge", 24),
        ("DownLight_Lamp", 340),
        ("FurledMap", 178),
        ("GourdCarton", 1748),
        ("GourdMonument_flap", 948),
        ("GourdMonument_tower_1slot", 1104),
        ("HourHand", 228),
        ("ItemFinder", 5516),
        ("Lamp_Pendant", 572),
        ("Lamp_Tube", 685),
        ("Lamp_Tube_wallmount", 408),
        ("LightSwitch_Backing", 1208),
        ("LightSwitch_Switch", 274),
        ("LiveSignal", 1187),
        ("MasterSequenceSpeaker", 849),
        ("PegBoard_12slot", 1811),
        ("PegBoard_1slot", 636),
        ("PegBoard_2slot", 767),
        ("PegBoard_3slot", 905),
        ("PegBoard_4slot", 1031),
        ("PegBoard_5slot", 1185),
        ("PegBoard_6slot", 1294),
        ("PegBoard_startmarker", 506),
        ("PermoSign", 391),
        ("PokeButton_Backing", 770),
        ("Pokebutton_Button", 226),
        ("ProgressBar", 68),
        ("SecondHand", 466),
        ("Speaker-wallmounted", 1469),
        ("Spotlight_Hinge", 300),
        ("Spotlight_Lamp", 1314),
        ("TrainLightFlood", 422),
        ("WalkieTalkie", 4739),
        ("WalkieTalkie_button", 232),
        ("backing", 160),
        ("backpack_frame", 988),
        ("backpack_straps_hung", 832),
        ("ball_bottom", 494),
        ("ball_top", 637),
        ("belt_hung", 1310),
        ("binoculars", 3794),
        ("blank.001", 5805),
        ("box-base", 1196),
        ("box-lid", 424),
        ("buoy", 730),
        ("chair", 1138),
        ("compass_base", 1175),
        ("compass_needle", 258),
        ("coordinate-tracker", 2270),
        ("counter_button_minus", 135),
        ("counter_button_plus", 135),
        ("counter_casing", 663),
        ("counter_casing_buttonpanel", 460),
        ("counter_dial_ones", 189),
        ("counter_dial_ones", 292),
        ("cowbell_large_bell", 1248),
        ("cowbell_large_clapper", 185),
        ("cowbell_medium_bell", 1248),
        ("cowbell_medium_clapper", 185),
        ("cowbell_small_bell", 1252),
        ("cowbell_small_clapper", 185),
        ("dispenser", 2942),
        ("door_folding_frame", 472),
        ("door_folding_panel", 68),
        ("egghunt_pegboard", 3825),
        ("flaregun", 2723),
        ("flaregunbarrel", 1063),
        ("flareguntimer", 18),
        ("footy", 1004),
        ("kettle", 2404),
        ("knob", 357),
        ("lamp_red", 605),
        ("laserpointer_LOD0", 477),
        ("megaphone_body", 4211),
        ("megaphone_speaker", 470),
        ("microphone-wallmount", 805),
        ("microphone-wallmount-Button", 178),
        ("pedestal", 243),
        ("pegtile", 134),
        ("pegtile_round", 180),
        ("pegtile_speaker", 1042),
        ("pillow_alltogether", 2198),
        ("pomodoroholder", 1624),
        ("progressbar_spiral", 3978),
        ("radio_antenna", 200),
        ("radio_body", 4090),
        ("radio_button_up", 184),
        ("radio_onoff", 312),
        ("radio_stationindicators", 24),
        ("radio_tuner", 4),
        ("radio_volume", 384),
        ("salonbrush", 133),
        ("splitflap_case_1slot", 718),
        ("splitflap_case_2slot", 1138),
        ("splitflap_case_3slot", 1518),
        ("splitflap_case_5slot", 2318),
        ("splitflap_flap", 76),
        ("syncswitch_base", 1277),
        ("syncswitch_lever", 136),
        ("telescope_base", 108),
        ("telescope_base_lv", 108),
        ("telescope_scope", 960),
        ("telescope_scope_lv", 951),
        ("telescope_swivel", 228),
        ("telescope_swivel_lv", 228),
        ("torch", 4287),
        ("triangle", 714),
        ("turnstile_bar_LOD0", 801),
        ("turnstile_frame_LOD0", 3132),
        ("turnstile_frame_blindfold", 3132),
        ("turnstile_plaque", 264),
        ("turnstile_spinner_LOD0", 1710),
        ("turnstile_spinner_blindfold", 6517),
        ("validator_face", 689),
        ("validator_face", 762),
        ("vice-arm", 293),
        ("vice-base", 743),
        ("whiteboard", 1691),
    };

    // Objects the game merged into a scene's combined mesh: which submeshes are theirs, and the
    // matrix (3 rows of 4) that moves those points from world space back to the object's own space
    internal static readonly Slice[] Slices = {
        new Slice("ConfettiDispenser", "Combined Mesh (root: scene) 6", 63602, 135, 1, new float[] {
            -0.6038194f, 0f, -0.7971211f, -650.7857396f,
            0.797121f, -0.0003491f, -0.6038194f, -273.0013379f,
            -0.0002782f, -0.9999999f, 0.0002108f, 17.0252975f }),
    };
}
}