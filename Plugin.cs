using BepInEx;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Silverpine.ModdingTools;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SaltDialogueImpersonator;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency(
    Silverpine.ModdingTools.Plugin.PluginGuid,
    "1.9.0")]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "salt.silverpine.dialogueimpersonator";
    public const string PluginName = "Salt Dialogue Impersonator";
    public const string PluginVersion = "1.2.1";

    private Harmony _harmony;

    private void Awake()
    {
        ImpersonationController.RegisterHooks();
        _harmony = new Harmony(PluginGuid);
        _harmony.PatchAll();
    }

}

internal static class ImpersonationController
{
    private const string ButtonPrefix = "Impersonate";
    private const float PortraitSideOffsetX = 177f;
    private const string ImpersonateActionId =
        Plugin.PluginGuid + ".impersonate";
    private const string InputActorId =
        Plugin.PluginGuid + ".input-actor";

    private static readonly MethodInfo GenerateDialogMethod =
        AccessTools.Method(
            typeof(NeuralNPC),
            "GenerateDialog",
            new[] { typeof(Action), typeof(Action) }
        );

    private static readonly MethodInfo GenerateMultiDialogMethod =
        AccessTools.Method(
            typeof(NeuralNPC),
            "GenerateMultiDialog",
            new[] { typeof(NeuralNPC), typeof(Action), typeof(Action) }
        );

    private static readonly MethodInfo GetNextMultiSpeakerMethod =
        AccessTools.Method(
            typeof(NeuralNPC),
            "GetNextMultiSpeaker",
            new[] { typeof(NeuralNPC), typeof(NeuralNPC) }
        );

    internal static NeuralNPC SelectedNpc { get; private set; }
    internal static NeuralNPC PortraitNpc { get; private set; }

    internal static void RegisterHooks()
    {
        RegisterImpersonateAction();
        DialogueInputActors.Register(
            Plugin.PluginGuid,
            new DialogueInputActorDefinition
            {
                Id = InputActorId,
                Order = 0,
                GetNpc = GetSelectedNpcForHook,
                TrySubmit = TrySubmitAsSelectedNpc,
                Clear = ClearSelection
            });
    }

    private static void RegisterImpersonateAction()
    {
        DialogueActions.Register(
            Plugin.PluginGuid,
            new DialogueActionDefinition
            {
                Id = ImpersonateActionId,
                // Keep this registration stable for the lifetime of the
                // process. Re-registering it invalidates callbacks already
                // captured by the currently drawn button.
                Label = ButtonPrefix,
                Order = -100,
                RequireNpc = true,
                AllowInContinueOnlyMode = true,
                ReplacesNativeLabel = ButtonPrefix,
                OnSelected = _ => OpenNpcPicker()
            });
    }

    private static bool TrySubmitAsSelectedNpc(string text)
    {
        if (NeuralNPC.multiDialogParticipants != null)
        {
            return TryHandleMultiInput(text);
        }

        return TryHandleSingleInput(
            NeuralNPC.currentActiveDialogNeuralNPC,
            text);
    }

    private static bool TryGetSelectedNpc(out NeuralNPC npc)
    {
        npc = SelectedNpc;
        if (npc != null && IsActiveParticipant(npc))
        {
            return true;
        }

        bool selectionExpired = npc != null;
        SelectedNpc = null;
        if (selectionExpired)
        {
            RefreshCurrentButton();
        }
        return false;
    }

    private static NeuralNPC GetSelectedNpcForHook()
    {
        return TryGetSelectedNpc(out NeuralNPC npc) ? npc : null;
    }

    private static bool IsImpersonateButton(Button button)
    {
        if (button == null)
        {
            return false;
        }

        TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>();
        return label != null
            && label.text.StartsWith(ButtonPrefix, StringComparison.OrdinalIgnoreCase);
    }

    internal static string GetButtonLabel()
    {
        return TryGetSelectedNpc(out NeuralNPC npc)
            ? ButtonPrefix + ": " + npc.GetFinalName()
            : ButtonPrefix;
    }

    internal static void OpenNpcPicker()
    {
        if (GenericListUI.Instance == null)
        {
            Notify("The NPC picker is not available.");
            return;
        }

        var choices = new List<ListUIItem_Generic>
        {
            new ListUIItem_Generic(
                "Speak as Player",
                null,
                SelectedNpc == null ? "<color=#70e070>ACTIVE</color>" : "",
                delegate
                {
                    ClearSelection();
                    GenericListUI.Instance.Close();
                    Notify("NPC impersonation disabled.");
                }
            )
        };

        choices.AddRange(
            GetConversationNpcs()
                .OrderBy(npc => npc.GetFinalName())
                .Select(npc => new ListUIItem_Generic(
                    npc.GetFinalName(),
                    ListUIItem_Generic.GameObjectToIcon(npc.gameObject),
                    npc == SelectedNpc
                        ? "<color=#70e070>ACTIVE</color>"
                        : IsActiveParticipant(npc)
                            ? "<color=#d0d070>IN DIALOGUE</color>"
                            : "",
                    delegate
                    {
                        SelectedNpc = npc;
                        PortraitNpc = npc;
                        GenericListUI.Instance.Close();
                        RefreshCurrentButton();
                        RefreshCurrentPortrait(
                            DialogBox.SpriteSwitchMode.Normal);
                        Notify(
                            "Speaking as " + npc.GetFinalName()
                            + ". Submitted dialogue will be attributed to this NPC."
                        );
                    }
                ))
        );

        GenericListUI.Instance.Draw(choices);
        GenericListUI.Instance.Open();
    }

    private static IEnumerable<NeuralNPC> GetConversationNpcs()
    {
        if (NeuralNPC.multiDialogParticipants != null)
        {
            return NeuralNPC.multiDialogParticipants
                .Where(npc => npc != null)
                .Distinct()
                .ToArray();
        }

        NeuralNPC active = NeuralNPC.currentActiveDialogNeuralNPC;
        return active != null
            ? new[] { active }
            : Array.Empty<NeuralNPC>();
    }

    private static bool IsActiveParticipant(NeuralNPC npc)
    {
        if (NeuralNPC.multiDialogParticipants != null)
        {
            return NeuralNPC.multiDialogParticipants.Contains(npc);
        }

        return NeuralNPC.currentActiveDialogNeuralNPC == npc;
    }

    internal static void RefreshCurrentButton()
    {
        if (DialogBox.Instance == null)
        {
            return;
        }

        LayoutGroup layout = Traverse.Create(DialogBox.Instance)
            .Field("buttonOptionsLayoutGroup")
            .GetValue<LayoutGroup>();

        if (layout == null)
        {
            return;
        }

        foreach (Button button in layout.GetComponentsInChildren<Button>())
        {
            if (!IsImpersonateButton(button))
            {
                continue;
            }

            TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
            {
                label.text = GetButtonLabel();
            }
        }
    }

    internal static bool TryHandleSingleInput(NeuralNPC activeNpc, string text)
    {
        if (!TryGetSelectedNpc(out NeuralNPC selectedNpc))
        {
            RestorePlayerPortraitForPlayerInput();
            return false;
        }

        if (activeNpc == null
            || string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        List<NeuralNPC.DialogElement> added = AddImpersonatedTurn(
            activeNpc.dialogElements,
            selectedNpc,
            text
        );

        Action failureCallback = delegate
        {
            activeNpc.dialogElements.RemoveAll(added.Contains);
        };

        try
        {
            GenerateDialogMethod.Invoke(
                activeNpc,
                new object[] { failureCallback, null }
            );
        }
        catch (Exception exception)
        {
            failureCallback();
            Notify("Could not submit impersonated dialogue: " + GetMessage(exception));
        }

        return true;
    }

    internal static bool TryHandleMultiInput(string text)
    {
        if (!TryGetSelectedNpc(out NeuralNPC selectedNpc))
        {
            RestorePlayerPortraitForPlayerInput();
            return false;
        }

        if (string.IsNullOrWhiteSpace(text)
            || NeuralNPC.multiDialogParticipants == null
            || NeuralNPC.multiDialogParticipants.Count == 0)
        {
            return false;
        }

        HandleMultiInput(selectedNpc, text);
        return true;
    }

    private static async void HandleMultiInput(NeuralNPC selectedNpc, string text)
    {
        List<NeuralNPC> participants = NeuralNPC.multiDialogParticipants
            .Where(npc => npc != null)
            .Distinct()
            .ToList();

        var addedByNpc = new Dictionary<
            NeuralNPC,
            List<NeuralNPC.DialogElement>
        >();

        foreach (NeuralNPC participant in participants)
        {
            addedByNpc[participant] = AddImpersonatedTurn(
                participant.dialogElements,
                selectedNpc,
                text
            );
        }

        Action failureCallback = delegate
        {
            foreach (KeyValuePair<NeuralNPC, List<NeuralNPC.DialogElement>> pair
                in addedByNpc)
            {
                if (pair.Key != null)
                {
                    pair.Key.dialogElements.RemoveAll(pair.Value.Contains);
                }
            }
        };

        try
        {
            NeuralNPC toAsk = participants.Contains(selectedNpc)
                ? selectedNpc
                : NeuralNPC.currentActiveDialogNeuralNPC;

            DialogBox.Instance.DisplayLoading(
                "Choosing who responds to " + selectedNpc.GetFinalName() + ".",
                accessedFromInsideDialogBox: true
            );

            var nextSpeakerTask = (Task<NeuralNPC>)GetNextMultiSpeakerMethod.Invoke(
                null,
                new object[] { selectedNpc, toAsk }
            );
            NeuralNPC nextSpeaker = await nextSpeakerTask;

            if (nextSpeaker == null || !participants.Contains(nextSpeaker))
            {
                nextSpeaker = participants.FirstOrDefault(
                    npc => npc != selectedNpc
                ) ?? participants[0];
            }

            GenerateMultiDialogMethod.Invoke(
                null,
                new object[] { nextSpeaker, failureCallback, null }
            );
        }
        catch (Exception exception)
        {
            failureCallback();
            Notify("Could not submit impersonated dialogue: " + GetMessage(exception));
        }
    }

    private static List<NeuralNPC.DialogElement> AddImpersonatedTurn(
        List<NeuralNPC.DialogElement> history,
        NeuralNPC selectedNpc,
        string text)
    {
        string selectedName = selectedNpc.GetFinalName();
        string playerName = Player.Instance != null
            ? Player.Instance.playerName
            : "the player";
        string cleanedText = text.Trim();

        if (cleanedText.StartsWith(
            selectedName + ":",
            StringComparison.OrdinalIgnoreCase))
        {
            cleanedText = cleanedText.Substring(selectedName.Length + 1).TrimStart();
        }

        var added = new List<NeuralNPC.DialogElement>
        {
            history.AddToDialog(
                SpeakerType.NPC,
                selectedName + ": " + cleanedText
            ),
            history.AddToDialog(
                SpeakerType.System,
                "The preceding line was directly spoken in-scene by "
                + selectedName + ", not by " + playerName
                + ". Treat it as authoritative dialogue from " + selectedName
                + ". Continue naturally without reattributing the line to "
                + playerName + "."
            )
        };

        return added;
    }

    internal static void ClearSelection()
    {
        SelectedNpc = null;
        RefreshCurrentButton();
    }

    internal static void ResetAll()
    {
        SelectedNpc = null;
        PortraitNpc = null;
    }

    internal static void OverridePlayerPortrait(
        ref Sprite playerSprite,
        ref float playerSpriteScale,
        ref Vector2 playerSpriteOffset)
    {
        NeuralNPC portraitNpc = PortraitNpc;
        if (portraitNpc == null)
        {
            PortraitNpc = null;
            return;
        }

        playerSprite = portraitNpc.GetDialogSprite();
        if (TryGetSilcPlayerSidePortraitPlacement(
                portraitNpc,
                out Vector2 silcOffset,
                out float silcScale))
        {
            playerSpriteScale = silcScale;
            playerSpriteOffset = silcOffset;
            return;
        }

        playerSpriteScale = portraitNpc.dialogSpriteScale;
        playerSpriteOffset = ConvertNpcOffsetToPlayerSide(
            portraitNpc.dialogSpriteOffset);
    }

    private static bool TryGetSilcPlayerSidePortraitPlacement(
        NeuralNPC portraitNpc,
        out Vector2 offset,
        out float scale)
    {
        offset = default;
        scale = 1f;

        if (!TryGetNpcDefinition(
                portraitNpc,
                out CustomContentDefinition_NPC npcDefinition) ||
            string.IsNullOrWhiteSpace(
                npcDefinition.customPlayerCharacterDefinitionName) ||
            !CustomContentDefinition_PlayerCharacter.loaded.TryGetValue(
                npcDefinition.customPlayerCharacterDefinitionName,
                out CustomContentDefinition_PlayerCharacter portraitDefinition) ||
            portraitDefinition.assets == null)
        {
            return false;
        }

        CharacterAssetPack assets = portraitDefinition.assets;
        offset = new Vector2(
            assets.dialogSpriteOffsetX,
            assets.dialogSpriteOffsetY);
        scale = assets.dialogSpriteScale;
        return true;
    }

    private static bool TryGetNpcDefinition(
        NeuralNPC portraitNpc,
        out CustomContentDefinition_NPC definition)
    {
        definition = null;
        CustomNPCHandler handler =
            portraitNpc.GetComponent<CustomNPCHandler>();
        if (handler != null &&
            !string.IsNullOrWhiteSpace(handler.customContentName) &&
            CustomContentDefinition_NPC.loaded.TryGetValue(
                handler.customContentName,
                out definition))
        {
            return true;
        }

        string npcName = portraitNpc.GetFinalName();
        return !string.IsNullOrWhiteSpace(npcName) &&
            CustomContentDefinition_NPC.loaded.TryGetValue(
                npcName,
                out definition);
    }

    private static Vector2 ConvertNpcOffsetToPlayerSide(Vector2 npcSideOffset)
    {
        // Silverpine converts player-authored portrait offsets for the NPC
        // slot with (-x + 177, y). The transform is its own inverse, so use
        // it as the fallback for NPCs without SILC placement data.
        return new Vector2(
            PortraitSideOffsetX - npcSideOffset.x,
            npcSideOffset.y);
    }

    private static void RestorePlayerPortraitForPlayerInput()
    {
        if (PortraitNpc == null)
        {
            return;
        }

        PortraitNpc = null;
        RefreshCurrentPortrait(DialogBox.SpriteSwitchMode.Normal);
    }

    private static void RefreshCurrentPortrait(
        DialogBox.SpriteSwitchMode switchMode)
    {
        if (DialogBox.Instance == null || !DialogBox.Instance.isOpen)
        {
            return;
        }

        NeuralNPC activeNpc = NeuralNPC.currentActiveDialogNeuralNPC;
        if (activeNpc != null)
        {
            activeNpc.DoStartNPCMode(switchMode);
        }
    }

    private static void Notify(string message)
    {
        if (UpperNotificationUI.Instance != null)
        {
            UpperNotificationUI.Instance.OneOff(message);
        }
    }

    private static string GetMessage(Exception exception)
    {
        while (exception is TargetInvocationException
            && exception.InnerException != null)
        {
            exception = exception.InnerException;
        }

        return exception.Message;
    }
}

[HarmonyPatch(typeof(DialogBox), "DrawUpperButtons")]
[HarmonyAfter(Silverpine.ModdingTools.Plugin.PluginGuid)]
internal static class RefreshImpersonateButtonLabelPatch
{
    private static void Postfix()
    {
        ImpersonationController.RefreshCurrentButton();
    }
}

[HarmonyPatch(typeof(DialogBox), nameof(DialogBox.StartNPCMode))]
internal static class ImpersonatedPlayerPortraitPatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.Last)]
    private static void Prefix(
        ref Sprite playerSprite,
        ref float playerSpriteScale,
        ref Vector2 playerSpriteOffset)
    {
        ImpersonationController.OverridePlayerPortrait(
            ref playerSprite,
            ref playerSpriteScale,
            ref playerSpriteOffset);
    }
}

[HarmonyPatch(typeof(NeuralNPC), "OnInputCallback")]
internal static class SingleDialogueInputPatch
{
    private static bool Prefix(NeuralNPC __instance, string text)
    {
        return !ImpersonationController.TryHandleSingleInput(__instance, text);
    }
}

[HarmonyPatch(
    typeof(NeuralNPC),
    nameof(NeuralNPC.OnMultiInputCallback))]
internal static class MultiDialogueInputPatch
{
    private static bool Prefix(string text)
    {
        return !ImpersonationController.TryHandleMultiInput(text);
    }
}

[HarmonyPatch(typeof(DialogBox), nameof(DialogBox.EndDialog))]
internal static class EndDialoguePatch
{
    private static void Postfix()
    {
        ImpersonationController.ResetAll();
    }
}

[HarmonyPatch(typeof(DialogBox), nameof(DialogBox.CloseBox))]
internal static class CloseDialoguePatch
{
    private static void Postfix()
    {
        ImpersonationController.ResetAll();
    }
}
