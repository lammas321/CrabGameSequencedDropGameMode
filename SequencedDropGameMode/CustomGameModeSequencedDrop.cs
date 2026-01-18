using BepInEx;
using BepInEx.IL2CPP.Utils;
using CrabDevKit.Utilities;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace SequencedDropGameMode
{
    public sealed class CustomGameModeSequencedDrop : CustomGameModes.CustomGameMode
    {
        internal static CustomGameModeSequencedDrop Instance;
        internal static Deobf_GameModeBlockDrop BlockDrop;
        internal static readonly string SequencesPath = "SequencedDropSequences\\";
        internal static readonly int MaxHeight = 26;
        internal static readonly float MinSpeed = 0.8f;
        internal static readonly float MaxSpeed = 1.3f;
        internal static readonly float GoalSpeed = 1.5f;

        internal List<Sequence> sequences = [];
        internal float startTime = 0f;
        internal bool testing = false;
        internal int testingSequence = -1;
        internal Harmony patches;

        internal enum DropPosition
        {
            Pos0 = 1,
            Pos1 = 11,
            Pos2 = 0,
            Pos3 = 10,

            Pos4 = 18,
            Pos5 = 7,
            Pos6 = 19,
            Pos7 = 8,

            Pos8 = 16,
            Pos9 = 5,
            Pos10 = 15,
            Pos11 = 4,

            Pos12 = 14,
            Pos13 = 3,
            Pos14 = 13,
            Pos15 = 2,
        }
        internal struct InstructionDataDrop(DropPosition dropPosition, float dropTime) : IInstructionData
        {
            public DropPosition dropPosition = dropPosition;
            public float dropTime = dropTime;
        }
        internal struct InstructionDataMultiDrop(DropPosition[] dropPositions, float dropTime, int dropCount, float waitTime) : IInstructionData
        {
            public DropPosition[] dropPositions = dropPositions;
            public float dropTime = dropTime;
            public int dropCount = dropCount;
            public float waitTime = waitTime;
        }
        internal struct InstructionDataWait(float waitTime) : IInstructionData
        {
            public float waitTime = waitTime;
        }
        internal interface IInstructionData;
        internal enum InstructionType
        {
            Drop,
            MultiDrop,
            Wait
        }
        internal struct Instruction(InstructionType instructionType, IInstructionData instructionData)
        {
            public InstructionType instructionType = instructionType;
            public IInstructionData instructionData = instructionData;
        }
        internal struct Sequence(string name, int height, int minPlayers, int maxPlayers, Instruction[] instructions)
        {
            public string name = name;
            public int height = height;
            public int minPlayers = minPlayers;
            public int maxPlayers = maxPlayers;
            public Instruction[] instructions = instructions;
        }

        public CustomGameModeSequencedDrop() : base
        (
            name: "Sequenced Drop",
            description: "• The blocks will drop in a sequence\n\n• You must survive for the entire sequence\n\n• Be respectful, no pushing >=(",
            gameModeType: GameModeData_GameModeType.ColorDrop,
            vanillaGameModeType: GameModeData_GameModeType.ColorDrop,
            waitForRoundOverToDeclareSoloWinner: true,

            shortModeTime: 100,
            mediumModeTime: 130,
            longModeTime: 160,

            compatibleMapNames: [
                "Cheeky Chamber",
                "Lava Drop",
                "Peaceful Platform"
            ]
        )
            => Instance = this;

        public override void PreInit()
        {
            startTime = 0f;
            testing = false;
            testingSequence = -1;
            patches = Harmony.CreateAndPatchAll(GetType());
            LoadSequences();
        }
        public override void PostEnd()
        {
            startTime = 0f;
            testing = false;
            testingSequence = -1;
            patches?.UnpatchSelf();
            sequences.Clear();
        }

        private IEnumerable<string> ScrapeSequences(string directory)
        {
            return Directory.EnumerateDirectories(directory, "*", SearchOption.AllDirectories).Where(path => path.EndsWith("\\SequencedDropSequences"));
        }

        internal void LoadSequences()
        {
            sequences.Clear();

            foreach (string path in ScrapeSequences(Paths.PluginPath))
                foreach (string file in Directory.GetFiles(path, "*.txt", SearchOption.AllDirectories))
                {
                    string[] lines = File.ReadAllLines(file);
                    int sequencesIndex = -1;
                    for (int index = 0; index < lines.Length; index++)
                        if (lines[index].Trim().ToLower() == "sequence:")
                        {
                            sequencesIndex = index;
                            break;
                        }
                    if (sequencesIndex == -1)
                        continue;

                    string name = "Unnamed";
                    int height = 1;
                    int minPlayers = -1;
                    int maxPlayers = -1;
                    for (int index = 0; index < sequencesIndex; index++)
                    {
                        string line = lines[index].Trim();
                        if (line == "" || line.StartsWith('#'))
                            continue;
                        int splitIndex = line.IndexOf('=');
                        if (splitIndex == -1)
                            continue;

                        string key = line[..splitIndex].ToLower();
                        string value = line[(splitIndex + 1)..];

                        switch (key)
                        {
                            case "name":
                                {
                                    name = value;
                                    break;
                                }
                            case "height":
                                {
                                    if (int.TryParse(value, out int intValue))
                                        height = Math.Max(1, intValue);
                                    break;
                                }
                            case "minplayers":
                                {
                                    if (int.TryParse(value, out int intValue))
                                        minPlayers = Math.Max(-1, intValue);
                                    break;
                                }
                            case "maxplayers":
                                {
                                    if (int.TryParse(value, out int intValue))
                                        maxPlayers = Math.Max(-1, intValue);
                                    break;
                                }
                        }
                    }

                    List<Instruction> instructions = [];
                    for (int index = sequencesIndex + 1; index < lines.Length; index++)
                    {
                        string line = lines[index].Trim().ToLower();
                        if (line == "" || line.StartsWith('#'))
                            continue;
                        int splitIndex = line.IndexOf('=');
                        if (splitIndex == -1)
                            continue;

                        string key = line[..splitIndex];
                        string value = line[(splitIndex + 1)..].Replace(" ", "");

                        switch (key)
                        {
                            case "drop":
                                {
                                    string[] properties = value.Split(',', StringSplitOptions.RemoveEmptyEntries);
                                    if (properties.Length <= 1)
                                        continue;

                                    if (!int.TryParse(properties[0], out int intValue))
                                        break;
                                    if (float.TryParse(properties[1], out float floatValue))
                                        instructions.Add(new(InstructionType.Drop, new InstructionDataDrop(IndexToDropPosition(Math.Clamp(intValue, 0, 15)), Math.Max(float.Epsilon, floatValue))));
                                    break;
                                }
                            case "multidrop":
                                {
                                    if (!value.StartsWith('['))
                                        break;
                                    int multiDropEndIndex = value.IndexOf(']');
                                    if (multiDropEndIndex == -1)
                                        break;

                                    string[] multiDropIndexes = value[1..multiDropEndIndex].Split(',', StringSplitOptions.RemoveEmptyEntries);
                                    List<DropPosition> dropPositions = [];
                                    bool encounteredError = false;
                                    foreach (string dropIndexValue in multiDropIndexes)
                                    {
                                        if (!int.TryParse(dropIndexValue, out int intValue))
                                        {
                                            encounteredError = true;
                                            break;
                                        }
                                        dropPositions.Add(IndexToDropPosition(Math.Clamp(intValue, 0, 15)));
                                    }
                                    if (encounteredError)
                                        break;

                                    string[] properties = value[(multiDropEndIndex + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries);
                                    if (properties.Length == 0) continue;
                                    if (!float.TryParse(properties[0], out float floatValue))
                                        break;
                                    int dropCount = 1;
                                    if (properties.Length >= 2 && !int.TryParse(properties[1], out dropCount))
                                        break;
                                    float waitTime = 1f;
                                    if (properties.Length >= 3 && !float.TryParse(properties[2], out waitTime))
                                        break;

                                    instructions.Add(new(InstructionType.MultiDrop, new InstructionDataMultiDrop([.. dropPositions], Math.Max(float.Epsilon, floatValue), Math.Max(1, dropCount), Math.Max(float.Epsilon, waitTime))));
                                    break;
                                }
                            case "wait":
                                {
                                    if (float.TryParse(value, out float floatValue))
                                        instructions.Add(new(InstructionType.Wait, new InstructionDataWait(Math.Max(float.Epsilon, floatValue))));
                                    break;
                                }
                        }
                    }

                    sequences.Add(new(name, height, minPlayers, maxPlayers, [.. instructions]));
                }
        }


        internal float GetSpeedMultiplier(float timeRemaining)
            => testing ? 1f : Math.Min((timeRemaining / startTime * (GoalSpeed - MinSpeed)) + MinSpeed, MaxSpeed);

        internal DropPosition IndexToDropPosition(int index)
        {
            return index switch
            {
                0 => DropPosition.Pos0,
                1 => DropPosition.Pos1,
                2 => DropPosition.Pos2,
                3 => DropPosition.Pos3,
                4 => DropPosition.Pos4,
                5 => DropPosition.Pos5,
                6 => DropPosition.Pos6,
                7 => DropPosition.Pos7,
                8 => DropPosition.Pos8,
                9 => DropPosition.Pos9,
                10 => DropPosition.Pos10,
                11 => DropPosition.Pos11,
                12 => DropPosition.Pos12,
                13 => DropPosition.Pos13,
                14 => DropPosition.Pos14,
                15 => DropPosition.Pos15,
                _ => DropPosition.Pos0,
            };
        }
        internal int DropPositionToIndex(DropPosition dropPosition)
        {
            return dropPosition switch
            {
                DropPosition.Pos0 => 0,
                DropPosition.Pos1 => 1,
                DropPosition.Pos2 => 2,
                DropPosition.Pos3 => 3,
                DropPosition.Pos4 => 4,
                DropPosition.Pos5 => 5,
                DropPosition.Pos6 => 6,
                DropPosition.Pos7 => 7,
                DropPosition.Pos8 => 8,
                DropPosition.Pos9 => 9,
                DropPosition.Pos10 => 10,
                DropPosition.Pos11 => 11,
                DropPosition.Pos12 => 12,
                DropPosition.Pos13 => 13,
                DropPosition.Pos14 => 14,
                DropPosition.Pos15 => 15,
                _ => 0,
            };
        }

        internal DropPosition GetModifiedDropPosition(DropPosition dropPosition, bool flippedX, bool flippedY, bool rotated)
        {
            int index = DropPositionToIndex(dropPosition);
            int x = rotated ? (flippedY ? 3 - (index / 4) : index / 4) : (flippedX ? 3 - (index % 4) : index % 4);
            int y = rotated ? (flippedX ? index % 4 : 3 - (index % 4)) : (flippedY ? 3 - (index / 4) : index / 4);
            return IndexToDropPosition(x + (y * 4));
        }
        internal void Drop(DropPosition dropPosition, float dropTime)
        {
            foreach (ulong clientId in LobbyManager.steamIdToUID.Keys)
                ServerSend.SendBlockCrush(1 / dropTime, (int)dropPosition, clientId);
        }


        internal void EnableTesting()
        {
            testing = true;
            ServerSend.SendChatMessage(1, "Testing enabled.");
        }
        internal void PlayTestSequence()
        {
            testingSequence = -2;
            ServerSend.SendChatMessage(1, "Playing the debug test sequence.");
        }
        internal void TestSequence(string name)
        {
            foreach (Sequence sequence in sequences)
                if (sequence.name.ToLower().StartsWith(name))
                {
                    testingSequence = sequences.IndexOf(sequence);
                    ServerSend.SendChatMessage(1, $"Debugging: {sequence.name}");
                    return;
                }
            ServerSend.SendChatMessage(1, $"No sequence matched: {name}");
        }


        internal IEnumerator ProcessSequences()
        {
            System.Random random = new();
            int currentHeight = 0;
            yield return new WaitForSeconds(2f);
            startTime = BlockDrop.GetFreezeTime();

            while (currentHeight <= MaxHeight)
            {
                while (testing && testingSequence == -1)
                    yield return new WaitForEndOfFrame();
                Sequence currentSequence;
                bool flippedX = false;
                bool flippedY = false;
                bool rotated = false;
                if (testing)
                {
                    if (testingSequence == -2)
                        currentSequence = new("Test", 1, -1, -1, [
                            new Instruction(InstructionType.Drop, new InstructionDataDrop(DropPosition.Pos0, 0.125f)),
                                new Instruction(InstructionType.Wait, new InstructionDataWait(0.125f * 4f)),
                                new Instruction(InstructionType.Drop, new InstructionDataDrop(DropPosition.Pos1, 0.25f)),
                                new Instruction(InstructionType.Wait, new InstructionDataWait(0.25f * 4f)),
                                new Instruction(InstructionType.Drop, new InstructionDataDrop(DropPosition.Pos2, 0.375f)),
                                new Instruction(InstructionType.Wait, new InstructionDataWait(0.375f * 4f)),
                                new Instruction(InstructionType.Drop, new InstructionDataDrop(DropPosition.Pos3, 0.5f)),
                                new Instruction(InstructionType.Wait, new InstructionDataWait(0.5f * 4f)),
                                new Instruction(InstructionType.Drop, new InstructionDataDrop(DropPosition.Pos4, 0.625f)),
                                new Instruction(InstructionType.Wait, new InstructionDataWait(0.625f * 4f)),
                                new Instruction(InstructionType.Drop, new InstructionDataDrop(DropPosition.Pos5, 0.75f)),
                                new Instruction(InstructionType.Wait, new InstructionDataWait(0.75f * 4f)),
                                new Instruction(InstructionType.Drop, new InstructionDataDrop(DropPosition.Pos6, 0.875f)),
                                new Instruction(InstructionType.Wait, new InstructionDataWait(0.875f * 4f)),
                                new Instruction(InstructionType.Drop, new InstructionDataDrop(DropPosition.Pos7, 1f)),
                                new Instruction(InstructionType.Wait, new InstructionDataWait(1f * 4f)),
                                new Instruction(InstructionType.Drop, new InstructionDataDrop(DropPosition.Pos8, 1.125f)),
                                new Instruction(InstructionType.Wait, new InstructionDataWait(1.125f * 4f)),
                                new Instruction(InstructionType.Drop, new InstructionDataDrop(DropPosition.Pos9, 1.25f)),
                                new Instruction(InstructionType.Wait, new InstructionDataWait(1.25f * 4f)),
                                new Instruction(InstructionType.Drop, new InstructionDataDrop(DropPosition.Pos10, 1.375f)),
                                new Instruction(InstructionType.Wait, new InstructionDataWait(1.375f * 4f)),
                                new Instruction(InstructionType.Drop, new InstructionDataDrop(DropPosition.Pos11, 1.5f)),
                                new Instruction(InstructionType.Wait, new InstructionDataWait(1.5f * 4f)),
                                new Instruction(InstructionType.Drop, new InstructionDataDrop(DropPosition.Pos12, 1.625f)),
                                new Instruction(InstructionType.Wait, new InstructionDataWait(1.625f * 4f)),
                                new Instruction(InstructionType.Drop, new InstructionDataDrop(DropPosition.Pos13, 1.75f)),
                                new Instruction(InstructionType.Wait, new InstructionDataWait(1.75f * 4f)),
                                new Instruction(InstructionType.Drop, new InstructionDataDrop(DropPosition.Pos14, 1.875f)),
                                new Instruction(InstructionType.Wait, new InstructionDataWait(1.875f * 4f)),
                                new Instruction(InstructionType.Drop, new InstructionDataDrop(DropPosition.Pos15, 2f))
                        ]);
                    else
                        currentSequence = sequences[testingSequence];
                    testingSequence = -1;
                }
                else
                {
                    int alivePlayers = GameManager.Instance.GetPlayersAlive();
                    List<Sequence> playableSequences = [];
                    foreach (Sequence sequence in sequences)
                        if ((sequence.minPlayers == -1 || sequence.minPlayers <= alivePlayers) && (sequence.maxPlayers == -1 || sequence.maxPlayers >= alivePlayers) && currentHeight + sequence.height <= MaxHeight)
                            playableSequences.Add(sequence);
                    if (playableSequences.Count == 0)
                        foreach (Sequence sequence in sequences)
                            if (currentHeight + sequence.height <= 26)
                                playableSequences.Add(sequence);
                    if (playableSequences.Count == 0) break;

                    currentSequence = playableSequences[random.Next(playableSequences.Count)];
                    flippedX = random.Next(2) == 1;
                    flippedY = random.Next(2) == 1;
                    rotated = random.Next(2) == 1;
                }
                ServerSend.SendChatMessage(1, $"Current Sequence: {currentSequence.name}");

                foreach (Instruction instruction in currentSequence.instructions)
                    switch (instruction.instructionType)
                    {
                        case InstructionType.Drop:
                            {
                                InstructionDataDrop drop = (InstructionDataDrop)instruction.instructionData;
                                Drop(GetModifiedDropPosition(drop.dropPosition, flippedX, flippedY, rotated), drop.dropTime * GetSpeedMultiplier(BlockDrop.GetFreezeTime()));
                                break;
                            }
                        case InstructionType.MultiDrop:
                            {
                                InstructionDataMultiDrop multiDrop = (InstructionDataMultiDrop)instruction.instructionData;
                                for (int dropped = 0; dropped < multiDrop.dropCount; dropped++)
                                {
                                    if (dropped != 0 && multiDrop.waitTime > 0f)
                                        yield return new WaitForSeconds(multiDrop.waitTime * GetSpeedMultiplier(BlockDrop.GetFreezeTime()));
                                    float speedMultiplier = GetSpeedMultiplier(BlockDrop.GetFreezeTime());
                                    foreach (DropPosition dropPosition in multiDrop.dropPositions)
                                        Drop(GetModifiedDropPosition(dropPosition, flippedX, flippedY, rotated), multiDrop.dropTime * speedMultiplier);
                                }
                                break;
                            }
                        case InstructionType.Wait:
                            {
                                InstructionDataWait wait = (InstructionDataWait)instruction.instructionData;
                                if (wait.waitTime > 0f)
                                    yield return new WaitForSeconds(wait.waitTime * GetSpeedMultiplier(BlockDrop.GetFreezeTime() - (wait.waitTime / 2f)));
                                break;
                            }
                    }

                currentHeight += currentSequence.height;
                yield return new WaitForSeconds(4f * GetSpeedMultiplier(BlockDrop.GetFreezeTime() - 2f));
            }

            if (BlockDrop.GetFreezeTime() > 3)
                ServerSend.SendGameModeTimer(3f, (int)BlockDrop.modeState);
            ServerSend.SendChatMessage(1, "Congratulations on making it to the top!");
        }


        // Get BlockDrop Instance
        [HarmonyPatch(typeof(Deobf_GameModeBlockDrop), nameof(Deobf_GameModeBlockDrop.InitMode))]
        [HarmonyPostfix]
        internal static void PostInitMode(Deobf_GameModeBlockDrop __instance)
            => BlockDrop = __instance;

        // Override, prevent vanilla Block Drop from starting and start Sequenced Drop
        [HarmonyPatch(typeof(Deobf_GameModeBlockDrop), nameof(Deobf_GameModeBlockDrop.OnFreezeOver))]
        [HarmonyPrefix]
        internal static bool PreOnFreezeOver()
        {
            if (!SteamManager.Instance.IsLobbyOwner())
                return false;

            Deobf_BlockDropBlockManager.Instance.StartCoroutine(Instance.ProcessSequences());
            foreach (ulong clientId in GameManager.Instance.activePlayers.Keys)
            {
                ServerSend.DropItem(clientId, 2, SharedObjectManager.Instance.GetNextId(), int.MaxValue);
                GiveUtil.GiveItem(clientId, 2, int.MaxValue);
            }
            return false;
        }

        // Override, make the game mode timer get set to 10 rather than 1 when ending early
        [HarmonyPatch(typeof(Deobf_GameModeBlockDrop), nameof(Deobf_GameModeBlockDrop.Method_Private_Void_4))]
        [HarmonyPrefix]
        internal static bool PreTryEndEarly()
        {
            if (SteamManager.Instance.IsLobbyOwner() && BlockDrop.modeState == GameMode_ModeState.Playing && GameManager.Instance.GetPlayersAlive() <= 1 && BlockDrop.GetFreezeTime() > 10f)
                ServerSend.SendGameModeTimer(10f, (int)BlockDrop.modeState);
            return false;
        }

        // Don't send item use packets for revolvers, leads to packet loss in edge cases when Decals attached to dropped blocks are deleted when dropped blocks are merged
        [HarmonyPatch(typeof(ServerSend), nameof(ServerSend.UseItem))]
        [HarmonyPrefix]
        internal static bool PreServerSendUseItem(int param_1)
            => param_1 != 2;
    }
}