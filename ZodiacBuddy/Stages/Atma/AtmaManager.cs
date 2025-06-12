﻿using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using ECommons;
using ECommons.Automation;
using ECommons.Automation.LegacyTaskManager;
using ECommons.Commands;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game;
///using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;
using Lumina.Excel.Sheets;
using System;
using System.Linq;
using ZodiacBuddy.Stages.Atma.Data;
using static FFXIVClientStructs.FFXIV.Client.System.String.Utf8String.Delegates;
using RelicNote = FFXIVClientStructs.FFXIV.Client.Game.UI.RelicNote;

namespace ZodiacBuddy.Stages.Atma;
/// <summary>
/// Your buddy for the Atma enhancement stage.
/// </summary>
internal class AtmaManager : IDisposable {
    /// <summary>
    /// Initializes a new instance of the <see cref="AtmaManager"/> class.
    /// </summary>
    public AtmaManager() {
        Service.AddonLifecycle.RegisterListener(AddonEvent.PostReceiveEvent, "RelicNoteBook", ReceiveEventDetour);
    }
    /// <inheritdoc/>
    public void Dispose() {
        Svc.Framework.Update -= WaitForBetweenAreasAndExecute;
        Svc.Framework.Update -= MonitorPathingAndDismount;
        Service.AddonLifecycle.UnregisterListener(ReceiveEventDetour);
    }
    public bool IsPathGenerating => VNavmesh.Nav.PathfindInProgress();
    public bool IsPathing => VNavmesh.Path.IsRunning();
    public bool NavReady => VNavmesh.Nav.IsReady();
    private readonly TaskManager TaskManager = new();
    private static uint GetNearestAetheryte(MapLinkPayload mapLink) {
        var closestAetheryteId = 0u;
        var closestDistance = double.MaxValue;

        static float ConvertRawPositionToMapCoordinate(int pos, float scale) {
            var c = scale / 100.0f;
            var scaledPos = pos * c / 1000.0f;

            return (41.0f / c * ((scaledPos + 1024.0f) / 2048.0f)) + 1.0f;
        }

        var aetherytes = Service.DataManager.GetExcelSheet<Aetheryte>();
        var mapMarkers = Service.DataManager.GetSubrowExcelSheet<MapMarker>();

        foreach (var aetheryte in aetherytes) {
            if (!aetheryte.IsAetheryte)
                continue;

            if (aetheryte.Territory.Value.RowId != mapLink.TerritoryType.RowId)
                continue;

            var map = aetheryte.Map.Value;
            var scale = map.SizeFactor;
            var name = map.PlaceName.Value.Name.ExtractText();

            var mapMarker = mapMarkers
	            .SelectMany(markers => markers)
	            .FirstOrDefault(m => m.DataType == 3 && m.DataKey.RowId == aetheryte.RowId);
            
            if (mapMarker.RowId is 0) {
                Service.PluginLog.Debug($"Could not find aetheryte: {name}");
                return 0;
            }

            var aetherX = ConvertRawPositionToMapCoordinate(mapMarker.X, scale);
            var aetherY = ConvertRawPositionToMapCoordinate(mapMarker.Y, scale);

            // var aetheryteName = aetheryte.PlaceName.Value!;
            // Service.PluginLog.Debug($"Aetheryte found: {aetherName} ({aetherX} ,{aetherY})");
            var distance = Math.Pow(aetherX - mapLink.XCoord, 2) + Math.Pow(aetherY - mapLink.YCoord, 2);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestAetheryteId = aetheryte.RowId;
            }
        }

        return closestAetheryteId;
    }

    private unsafe void Teleport(uint aetheryteId) {
        if (Service.ClientState.LocalPlayer == null) return;
        if (Service.Configuration.DisableTeleport) return;

        Telepo.Instance()->Teleport(aetheryteId, 0);
    }

    private unsafe void ReceiveEventDetour(AddonEvent type, AddonArgs args) {
        try {
            if (args is AddonReceiveEventArgs receiveEventArgs && (AtkEventType)receiveEventArgs.AtkEventType is AtkEventType.ButtonClick) {
                this.ReceiveEvent((AddonRelicNoteBook*)receiveEventArgs.Addon, (AtkEvent*)receiveEventArgs.AtkEvent);
            }
        }
        catch (Exception ex) {
            Service.PluginLog.Error(ex, "Exception during hook: AddonRelicNotebook.ReceiveEvent:Click");
        }
    }

    private unsafe void ReceiveEvent(AddonRelicNoteBook* addon, AtkEvent* eventData)
    {
        if (!EzThrottler.Throttle("RelicNoteClick", 500))
            return;

        var relicNote = RelicNote.Instance();
        if (relicNote == null)
            return;

        var bookId = relicNote->RelicNoteId;
        var index = addon->CategoryList->SelectedItemIndex;
        var targetComponent = eventData->Target;

        var selectedTarget = targetComponent switch
        {
            // Enemies
            _ when index == 0 && IsOwnerNode(targetComponent, addon->Enemy0.CheckBox) => BraveBook.GetValue(bookId).Enemies[0],
            _ when index == 0 && IsOwnerNode(targetComponent, addon->Enemy1.CheckBox) => BraveBook.GetValue(bookId).Enemies[1],
            _ when index == 0 && IsOwnerNode(targetComponent, addon->Enemy2.CheckBox) => BraveBook.GetValue(bookId).Enemies[2],
            _ when index == 0 && IsOwnerNode(targetComponent, addon->Enemy3.CheckBox) => BraveBook.GetValue(bookId).Enemies[3],
            _ when index == 0 && IsOwnerNode(targetComponent, addon->Enemy4.CheckBox) => BraveBook.GetValue(bookId).Enemies[4],
            _ when index == 0 && IsOwnerNode(targetComponent, addon->Enemy5.CheckBox) => BraveBook.GetValue(bookId).Enemies[5],
            _ when index == 0 && IsOwnerNode(targetComponent, addon->Enemy6.CheckBox) => BraveBook.GetValue(bookId).Enemies[6],
            _ when index == 0 && IsOwnerNode(targetComponent, addon->Enemy7.CheckBox) => BraveBook.GetValue(bookId).Enemies[7],
            _ when index == 0 && IsOwnerNode(targetComponent, addon->Enemy8.CheckBox) => BraveBook.GetValue(bookId).Enemies[8],
            _ when index == 0 && IsOwnerNode(targetComponent, addon->Enemy9.CheckBox) => BraveBook.GetValue(bookId).Enemies[9],
            // Dungeons
            _ when index == 1 && IsOwnerNode(targetComponent, addon->Dungeon0.CheckBox) => BraveBook.GetValue(bookId).Dungeons[0],
            _ when index == 1 && IsOwnerNode(targetComponent, addon->Dungeon1.CheckBox) => BraveBook.GetValue(bookId).Dungeons[1],
            _ when index == 1 && IsOwnerNode(targetComponent, addon->Dungeon2.CheckBox) => BraveBook.GetValue(bookId).Dungeons[2],
            // FATEs
            _ when index == 2 && IsOwnerNode(targetComponent, addon->Fate0.CheckBox) => BraveBook.GetValue(bookId).Fates[0],
            _ when index == 2 && IsOwnerNode(targetComponent, addon->Fate1.CheckBox) => BraveBook.GetValue(bookId).Fates[1],
            _ when index == 2 && IsOwnerNode(targetComponent, addon->Fate2.CheckBox) => BraveBook.GetValue(bookId).Fates[2],
            // Leves
            _ when index == 3 && IsOwnerNode(targetComponent, addon->Leve0.CheckBox) => BraveBook.GetValue(bookId).Leves[0],
            _ when index == 3 && IsOwnerNode(targetComponent, addon->Leve1.CheckBox) => BraveBook.GetValue(bookId).Leves[1],
            _ when index == 3 && IsOwnerNode(targetComponent, addon->Leve2.CheckBox) => BraveBook.GetValue(bookId).Leves[2],
            _ => throw new ArgumentException($"Unexpected index and/or node: {index}, {(nint)targetComponent:X}"),
        };

        var zoneName = !string.IsNullOrEmpty(selectedTarget.LocationName)
            ? $"{selectedTarget.LocationName}, {selectedTarget.ZoneName}"
            : selectedTarget.ZoneName;

        // Service.PluginLog.Debug($"Target selected: {selectedTarget.Name} in {zoneName}.");
        if (Service.Configuration.BraveEchoTarget)
        {
            var sb = new SeStringBuilder()
                .AddText("Target selected: ")
                .AddUiForeground(selectedTarget.Name, 62);

            if (index == 3) // leves
                sb.AddText($" from {selectedTarget.Issuer}");

            sb.AddText($" in {zoneName}.");

            Service.Plugin.PrintMessage(sb.BuiltString);
        }

        if (Service.Configuration.BraveCopyTarget)
        {
            Service.Plugin.PrintMessage($"Copied {selectedTarget.Name} to clipboard.");
            ImGui.SetClipboardText(selectedTarget.Name);
        }

        var aetheryteId = GetNearestAetheryte(selectedTarget.Position);
        if (aetheryteId == 0)
        {
            if (index == 1)
            {
                // Dungeons
                AgentContentsFinder.Instance()->OpenRegularDuty(selectedTarget.ContentsFinderConditionId);
            }
            else
            {
                Service.PluginLog.Warning($"Could not find an aetheryte for {zoneName}");
            }
        }
        else
        {
            Service.GameGui.OpenMapWithMapLink(selectedTarget.Position);
            this.Teleport(aetheryteId);

            if (!Service.Configuration.IsAtmaManagerEnabled)
                return;
            if (!awaitingTeleportFromRelicBookClick)
            {
                awaitingTeleportFromRelicBookClick = true;
                Svc.Framework.Update += WaitForBetweenAreasAndExecute;
            }
            return;
        }
    }
    private bool monitoringPathing = false;
    private DateTime unmountStartTime;
    private void EnqueueUnmountAfterNav()
    {
        if (monitoringPathing) return;
        monitoringPathing = true;
        unmountStartTime = DateTime.Now;
        Svc.Framework.Update += MonitorPathingAndDismount;
    }
    private unsafe void MonitorPathingAndDismount(IFramework _)
    {
        if (VNavmesh.Nav.PathfindInProgress() || VNavmesh.Path.IsRunning())
            return;
        monitoringPathing = false;
        Svc.Framework.Update -= MonitorPathingAndDismount;
        EnqueueDismount();
    }
    public bool CanAct
    {
        get
        {
            var player = Svc.ClientState.LocalPlayer;
            if (player == null || player.IsDead || Player.IsAnimationLocked)
                return false;
            var c = Svc.Condition;
            if (c[ConditionFlag.BetweenAreas]
                || c[ConditionFlag.BetweenAreas51]
                || c[ConditionFlag.OccupiedInQuestEvent]
                || c[ConditionFlag.OccupiedSummoningBell]
                || c[ConditionFlag.BeingMoved]
                || c[ConditionFlag.Casting]
                || c[ConditionFlag.Casting87]
                || c[ConditionFlag.Jumping]
                || c[ConditionFlag.Jumping61]
                || c[ConditionFlag.LoggingOut]
                || c[ConditionFlag.Occupied]
                || c[ConditionFlag.Occupied39]
                || c[ConditionFlag.Unconscious]
                || c[ConditionFlag.ExecutingGatheringAction]
                || c[ConditionFlag.MountOrOrnamentTransition]
                || c[85] && !c[ConditionFlag.Gathering])
                return false;
            return true;
        }
    }
    private unsafe void EnqueueDismount()
    {
        var am = ActionManager.Instance();
        TaskManager.Enqueue(() =>
        {
            if (Svc.Condition[ConditionFlag.Mounted])
                am->UseAction(ActionType.Mount, 0);
        }, "Dismount");
        TaskManager.Enqueue(() => !Svc.Condition[ConditionFlag.InFlight] && CanAct, 1000, "Wait for not in flight");
        TaskManager.Enqueue(() =>
        {
            if (Svc.Condition[ConditionFlag.Mounted])
                am->UseAction(ActionType.Mount, 0);
        }, "Dismount 2");
        TaskManager.Enqueue(() => !Svc.Condition[ConditionFlag.Mounted] && CanAct, 1000, "Wait for dismount");
        TaskManager.Enqueue(() =>
        {
            if (!Svc.Condition[ConditionFlag.Mounted])
                TaskManager.DelayNextImmediate(500);
        });
    }
    private unsafe void EnqueueMountUp()
    {
        TaskManager.Enqueue(() => NavReady);
        if (Svc.Condition[ConditionFlag.Mounted])
        {
            Service.PluginLog.Debug("Already mounted, skipping EnqueueMountUp.");
            return;
        }
        TaskManager.Enqueue(() =>
        {
            var am = ActionManager.Instance();
            const uint rouletteId = 9;
            if (am->GetActionStatus(ActionType.GeneralAction, rouletteId) == 0)
            {
                Service.PluginLog.Debug("Attempting to use mount roulette...");
                if (am->UseAction(ActionType.GeneralAction, rouletteId))
                {
                    Service.PluginLog.Debug("Using mount roulette.");
                }
                else
                {
                    Service.PluginLog.Warning("Failed to use mount roulette.");
                }
            }
            else
            {
                Service.PluginLog.Warning("Mount roulette unavailable.");
            }
            return true;
        });
        TaskManager.Enqueue(() => Svc.Condition[ConditionFlag.Mounted]);
        TaskManager.Enqueue(() =>
        {
            Chat.ExecuteCommand("/vnav flyflag");
            EnqueueUnmountAfterNav();
            hasEnteredBetweenAreas = false;
            awaitingTeleportFromRelicBookClick = false;
            hasQueuedMountTasks = false;
            return true;
        });
    }
    private bool hasQueuedMountTasks = false;
    private bool hasEnteredBetweenAreas = false;
    private bool awaitingTeleportFromRelicBookClick = false;
    internal void WaitForBetweenAreasAndExecute(IFramework framework)
    {
        if (!Service.Configuration.IsAtmaManagerEnabled || !awaitingTeleportFromRelicBookClick)
            return;
        if (!hasEnteredBetweenAreas)
        {
            if (Svc.Condition[ConditionFlag.BetweenAreas])
            {
                hasEnteredBetweenAreas = true;
            }
        }
        else
        {
            if (!Svc.Condition[ConditionFlag.BetweenAreas] && GenericHelpers.IsScreenReady() && !hasQueuedMountTasks)
            {
                hasQueuedMountTasks = true;
                EnqueueMountUp();
                
            }
        }
    }
    static unsafe bool IsOwnerNode(AtkEventTarget* target, AtkComponentCheckBox* checkbox)
            => target == checkbox->AtkComponentButton.OwnerNode;
    }
