/*
 * Imlight
 * Copyright (C) 2025 Revive101
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with this program. If not, see <http://www.gnu.org/licenses/>.
 *
 * ========================================================================
 * INTERACT QUEST UNDERWAY COMPONENT
 * ========================================================================
 * 
 * PURPOSE:
 * Manages quest underway interactions for NPCs when players have active quests from them.
 * Shows the grey '?' wizbang when a player has a quest in progress from this NPC.
 * 
 * USAGE EXAMPLE:
 * Automatically attached to NPCs that give quests based on quest dialog configuration.
 * 
 * NOTE:
 * Only attaches to NPCs that have "Prep" dialogs in quest templates.
 * 
 * TODO:
 * 
 * Created by: Jooty
 * Version: KALI 1.0
 * Last Updated: 10/22/2025
 */

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Imcodec.MessageLayer.Generated;
using Imcodec.ObjectProperty;
using Imcodec.ObjectProperty.TypeCache;
using Imcodec.Types;
using Imlight.Common;
using Imlight.CoreLib.Game.WizBang;
using Imlight.CoreLib.Game.Zone.Core;
using Imlight.CoreLib.WizardData.Collections;
using Imlight.CoreLib.WizardData.Models.Player;

namespace Imlight.CoreLib.Game.Zone.Components;

internal class PlayerQuestUnderwayState {

    public bool HasUnderwayQuest { get; set; }
    public QuestTemplate UnderwayQuest { get; set; }
    public DateTime LastUpdated { get; set; }

}

internal sealed class InteractQuestUnderwayComponent(ZoneEntity entity)
    : ZoneEntityComponent(entity), IServiceComponent, IComponentFactory {

    private const string PREP_NPC_ICON = "Prep";
    private const double WIZBANG_UPDATE_INTERVAL_SECONDS = 1.0;

    public string ServiceName => "QuestUnderwayService";
    public string NpcIcon => "";
    public string NpcNameKey => null;
    public string NpcTextKey => null;
    public string StateName => "";
    public string InteractWizBang => "";
    public string DisplayKey => "";

    private WizBangs _wizBang = WizBangs.None;
    public WizBangs WizBang {
        get => _wizBang;
        private set {
            _wizBang = value;
        }
    }

    private readonly List<QuestTemplate> _givesQuests = [];

    // Quests in which this NPC is the target of a persona goal (not necessarily the quest giver),
    // with the names of those goals. While such a goal is done and the quest is not, the NPC shows
    // the unfinished-quest marker.
    private readonly Dictionary<string, List<string>> _personaTargetGoals = [];
    private readonly Dictionary<string, QuestTemplate> _personaTargetQuests = [];
    private readonly Dictionary<ulong, DateTime> _lastWizBangUpdate = [];
    private readonly Dictionary<ulong, PlayerQuestUnderwayState> _cachedPlayerStates = [];

    public static bool ShouldAttachToEntity(CoreTemplate template) {
        if (template is not GameObjectTemplate goTemplate ||
            !template.m_behaviors.Any(x => x is NPCBehaviorTemplate) ||
            template.m_behaviors.Any(x => x is DuelistBehaviorTemplate)) {
            return false;
        }

        var allQuests = QuestTemplateCollection.GetAllQuests();
        if (allQuests is null) {
            return false;
        }

        // Check if this NPC gives any quests by looking for "Prep" dialogs.
        var givesQuests = allQuests
            .Where(x => x?.m_dialogList is ActorDialogList)
            .Any(quest => {
                var dialogList = quest.m_dialogList as ActorDialogList;
                var prepDialogEntry = dialogList.m_dialogs?.FirstOrDefault(de => de.m_dialogTag == "Prep");
                var templateId = prepDialogEntry?.m_dialogEntries?.FirstOrDefault()?.m_actorTemplateID;

                return templateId == goTemplate.m_templateID;
            });

        if (givesQuests) {
            return true;
        }

        // Or this NPC is the target of a persona goal in some quest.
        string npcName = goTemplate.m_objectName;

        return allQuests
            .Where(x => x is not null)
            .SelectMany(quest => quest.m_goals?.OfType<PersonaGoalTemplate>() ?? [])
            .Any(goal => PersonaGoalTargetsNpc(goal, npcName, goTemplate.m_templateID));
    }

    private static bool PersonaGoalTargetsNpc(PersonaGoalTemplate goal, string npcName, uint npcTemplateId) {
        // 1. Goal-level persona name, ignoring the "_Persona" suffix and letter case.
        string personaName = goal.m_personaName;
        if (!string.IsNullOrEmpty(npcName) && !string.IsNullOrEmpty(personaName)
            && string.Equals(personaName.Replace("_Persona", ""), npcName, StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        // 2. Fall back to the goal's dialog-entry actor template ID.
        if (npcTemplateId != 0 && goal.m_dialogList is ActorDialogList dialogList && dialogList.m_dialogs is not null) {
            foreach (var dlg in dialogList.m_dialogs) {
                if (dlg?.m_dialogEntries is null) {
                    continue;
                }

                foreach (var entry in dlg.m_dialogEntries) {
                    if (entry is not null && entry.m_actorTemplateID == npcTemplateId) {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    public IEnumerable<ServiceOptionBase> GetServiceOptions(Wizard wizard) {
        if (wizard is null) {
            yield break;
        }

        // Persona goals take priority over the underway entry.
        if (InteractPersonaGoalComponent.HasPendingGoalFor(Entity, wizard)) {
            yield break;
        }

        var state = GetOrUpdatePlayerState(wizard);

        // The memento wizbang tick reads this property; refresh it from live state.
        WizBang = state.HasUnderwayQuest ? WizBangs.UnfinishedQuest : WizBangs.None;

        if (!state.HasUnderwayQuest || state.UnderwayQuest == null) {
            yield break;
        }

        yield return new PrepEntry {
            m_displayKey = state.UnderwayQuest.m_questTitle,
            m_iconKey = PREP_NPC_ICON,
            m_serviceName = ServiceName,
        };
    }

    public override void OnStart() {
        // Cache the quests this NPC can give during startup.
        var questTemplates = QuestTemplateCollection
            .GetAllQuests()
            .Where(x => x is not null)
            .ToList();

        foreach (var questTemplate in questTemplates) {
            if (questTemplate.m_dialogList is not ActorDialogList dialogList) {
                continue;
            }

            var prepDialogEntry = dialogList.m_dialogs.FirstOrDefault(de => de.m_dialogTag == "Prep");
            var templateId = prepDialogEntry?.m_dialogEntries?.FirstOrDefault()?.m_actorTemplateID;

            if (templateId == entity.ActiveGameObject.m_templateID) {
                _givesQuests.Add(questTemplate);
            }
        }

        _givesQuests.Sort((a, b) => string.Compare(a.m_questTitle, b.m_questTitle, StringComparison.Ordinal));

        // Cache the quests in which this NPC is the target of a persona goal.
        string npcName = "";
        uint npcTemplateId = 0;
        if (Entity.Template is GameObjectTemplate goTemplate) {
            npcName = goTemplate.m_objectName;
            npcTemplateId = goTemplate.m_templateID;
        }

        foreach (var questTemplate in questTemplates) {
            foreach (var goal in questTemplate.m_goals?.OfType<PersonaGoalTemplate>() ?? []) {
                if (!PersonaGoalTargetsNpc(goal, npcName, npcTemplateId)) {
                    continue;
                }

                if (!_personaTargetGoals.TryGetValue(questTemplate.m_questName, out var goalNames)) {
                    goalNames = [];
                    _personaTargetGoals[questTemplate.m_questName] = goalNames;
                    _personaTargetQuests[questTemplate.m_questName] = questTemplate;
                }

                goalNames.Add(goal.m_goalName);
            }
        }
    }

    public override void OnPlayerJoin(CoreObject playerObj, IActorRef playerActor, Wizard playerWizard) {
        if (_givesQuests.Count <= 0 && _personaTargetGoals.Count <= 0) {
            WizBang = WizBangs.None;

            return;
        }

        var state = GetOrUpdatePlayerState(playerWizard, forceUpdate: true);
        WizBang = state.HasUnderwayQuest ? WizBangs.UnfinishedQuest : WizBangs.None;
    }

    public override void OnPlayerMove(CoreObject playerObj, IActorRef playerActor, Wizard playerWizard) {
        if (playerWizard is null || (_givesQuests.Count <= 0 && _personaTargetGoals.Count <= 0)) {
            return;
        }

        var now = DateTime.UtcNow;
        var playerId = playerWizard.CharId;

        if (_lastWizBangUpdate.TryGetValue(playerId, out var lastUpdate)) {
            if ((now - lastUpdate).TotalSeconds < WIZBANG_UPDATE_INTERVAL_SECONDS) {
                return;
            }
        }

        _lastWizBangUpdate[playerId] = now;
        var state = GetOrUpdatePlayerState(playerWizard, forceUpdate: true);
        WizBang = state.HasUnderwayQuest ? WizBangs.UnfinishedQuest : WizBangs.None;
    }

    public void OnServiceInteraction(IActorRef playerActor, Wizard playerCharacter, CoreObject playerObject, uint serviceOptionIndex) {
        var state = GetOrUpdatePlayerState(playerCharacter);
        if (state.HasUnderwayQuest && state.UnderwayQuest != null) {
            ShowQuestUnderwayDialog(playerActor, state.UnderwayQuest);
        }
    }

    private PlayerQuestUnderwayState GetOrUpdatePlayerState(Wizard wizard, bool forceUpdate = false) {
        var playerId = wizard.CharId;
        var now = DateTime.UtcNow;

        if (!forceUpdate && _cachedPlayerStates.TryGetValue(playerId, out var cachedState)) {
            if ((now - cachedState.LastUpdated).TotalSeconds < WIZBANG_UPDATE_INTERVAL_SECONDS) {
                return cachedState;
            }
        }

        var state = new PlayerQuestUnderwayState { LastUpdated = now };

        // Check if player has any active quests from this NPC.
        foreach (var quest in _givesQuests) {
            var hasQuest = wizard.HasQuest(quest.m_questName);
            var hasCompleted = wizard.HasCompletedQuest(quest.m_questName);

            // Only show underway wizbang if player has the quest but hasn't completed it.
            if (hasQuest && !hasCompleted) {
                state.HasUnderwayQuest = true;
                state.UnderwayQuest = quest;

                break; // Take the first underway quest.
            }
        }

        // Not a giver of an unfinished quest: this NPC may be the target of a persona goal the player
        // has already completed in a quest that is still underway.
        if (!state.HasUnderwayQuest) {
            foreach (var instance in wizard.QuestBehavior.CurrentQuestInstances) {
                if (!_personaTargetGoals.TryGetValue(instance.QuestName, out var goalNames)
                    || wizard.HasCompletedQuest(instance.QuestName)) {
                    continue;
                }

                if (goalNames.Any(goalName => instance.IsGoalCompleted(goalName))
                    && _personaTargetQuests.TryGetValue(instance.QuestName, out var targetQuest)) {
                    state.HasUnderwayQuest = true;
                    state.UnderwayQuest = targetQuest;

                    break;
                }
            }
        }

        _cachedPlayerStates[playerId] = state;

        return state;
    }

    private void ShowQuestUnderwayDialog(IActorRef playerActor, QuestTemplate quest) {
        var dialogList = quest.m_dialogList as ActorDialogList;
        var underwayDialogList = dialogList?.m_dialogs.FirstOrDefault(de => de.m_dialogTag == "Underway");

        if (underwayDialogList == null) {
            Logger.Error("Quest {0} has no 'Underway' dialog entry.",
                Logger.Args(quest.m_questName));

            return;
        }

        SendActorDialog(playerActor, underwayDialogList, "QuestInfo");
    }

    private void SendActorDialog(IActorRef playerActor, ActorDialog dialogEntry, string completionType, ulong questId = 0, ulong goalId = 0) {
        var serializer = new ObjectSerializer(Versionable: false);
        if (!serializer.Serialize(dialogEntry, 16, out var serializedData)) {
            Logger.Error("Failed to serialize '{0}' dialog.",
                Logger.Args(completionType));

            return;
        }

        var dialogMsg = new WIZARD_12_PROTOCOL.MSG_ACTORDIALOG {
            MobileID = questId > 0 ? new GID(Entity.ActiveGameObject.m_nMobileID) : Entity.ActiveGameObject.m_nMobileID,
            QuestID = questId,
            GoalID = goalId,
            CompletionType = completionType,
            ActorDialog = serializedData,
            Persona = "",
            PersonaName = "",
            PersonaIcon = "",
        };

        playerActor.Tell(dialogMsg);
    }

}