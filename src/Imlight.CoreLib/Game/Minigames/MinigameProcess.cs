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
 * MINIGAME PROCESS MANAGEMENT SYSTEM
 * ========================================================================
 * 
 * PURPOSE:
 * Manages the lifecycle and score settlement for individual minigame instances,
 * handling player connections, round tracking, score validation, and leaderboard replies.
 * 
 * USAGE EXAMPLE:
 * Create a new MinigameProcess with a process name, ID, and minigame index.
 * Process automatically handles message routing, leaderboard updates, and score settlement.
 * 
 * NOTE:
 * Utilizes Akka.NET actor system for process management.
 * A score settles once per round: the client opens a round, then reports one score.
 * A kiosk round grants its rewards before the window reply is sent.
 * 
 * TODO:
 * 
 * Created by: Jooty
 * Version: KALI 1.0
 * Last Updated: 10/14/2026
 */

using System;
using System.Linq;
using Akka.Actor;
using Imcodec.MessageLayer;
using Imcodec.MessageLayer.Generated;
using Imcodec.ObjectProperty;
using Imcodec.ObjectProperty.TypeCache;
using Imlight.Common;
using Imlight.CoreLib.Game.Minigames;
using Imlight.CoreLib.Game.Processes;
using Imlight.CoreLib.Shared.Networking;
using Imlight.CoreLib.Shared.Packets;
using Imlight.CoreLib.WizardData.Collections;
using Imlight.CoreLib.WizardData.Models.Player;

/// <summary>
/// Manages the lifecycle and score settlement for a specific minigame instance.
/// </summary>
internal sealed class MinigameProcess : Process {

    private const uint QUERY_WIZARD_TIMEOUT_IN_MS = 5000;

    private const long LoggedScoreCapMultiplier = 100;

    private bool IsMinigameActive { get; set; }
    private bool _roundOpen;

    private readonly byte _minigameIndex;
    private readonly MinigameInfo _minigameInfo;
    private readonly ObjectSerializer _serializer = new(
        Versionable: false,
        Behaviors: SerializerFlags.None
    );

    // ctor
    public MinigameProcess(string processName, uint processId, byte minigameIndex) : base(processName, processId) {
        this._minigameIndex = minigameIndex;
        this._minigameInfo = Imlight.CoreLib.Game.Minigames.MinigameConfig.GetMinigameInfo(minigameIndex);

        if (this._minigameInfo == null) {
            throw new Exception($"Minigame at index {minigameIndex} not found.");
        }
    }

    [MessageHandler(typeof(IMessage))]
    internal void ReceiveElse(IMessage message) {
        HadActivity = true;

        if (!MinigameProtocol.IsKnownService(message.ServiceId)) {
            return;
        }

        if (MinigameProtocol.IsRoundStart(message)) {
            HandleConnect();
        } else if (MinigameProtocol.TryGetScore(message, out var score)) {
            HandleRewards(score);
        }
    }

    private void HandleConnect() {
        IsMinigameActive = true;
        _roundOpen = true;

        var msg = new WIZARD_12_PROTOCOL.MSG_ENTERMINIGAME();
        Sender.Tell(msg, Self);
    }

    private void HandleRewards(int score) {
        var leaderboard = CreateScoreTrackingList();

        // Handle leaderboard query before game completion.
        if (score == -1) {
            SendLeaderboardResponse(leaderboard);

            return;
        }

        if (score < -1) {
            Logger.Warning("{Process} ignored the invalid score {Score}",
                Logger.Args(ProcessName, score));
            SendLeaderboardResponse(leaderboard);

            return;
        }

        if (!_roundOpen) {
            Logger.Warning("{Process} ignored the score {Score}, no round is open",
                Logger.Args(ProcessName, score));
            SendLeaderboardResponse(leaderboard);

            return;
        }

        _roundOpen = false;

        var character = QuerySenderCharacter();
        if (character is null) {
            SendLeaderboardResponse(leaderboard);

            return;
        }

        SettleRound(score, character, leaderboard);
    }

    private void SettleRound(int score, CHARACTER_103_PROTOCOL.MSG_CHARACTER character, ScoreTrackingList leaderboard)
        => SettleKioskRound(score, character, leaderboard);

    private void SettleKioskRound(int score, CHARACTER_103_PROTOCOL.MSG_CHARACTER character, ScoreTrackingList leaderboard) {
        if (ExceedsScoreCap(score, character.Wizard)) {
            SendLeaderboardResponse(leaderboard);

            return;
        }

        var reward = MinigameRewardGranter.Grant(Sender, character.Wizard, _minigameInfo, score);
        SendFinalRewardsResponse(score, leaderboard, reward, character.Wizard);
    }

    private bool ExceedsScoreCap(int score, Wizard wizard) {
        var thresholds = _minigameInfo.m_scoreThresholds;
        if (thresholds is null || thresholds.Count == 0) {
            return false;
        }

        // The cap is a multiple of the top threshold. Left unset, scores far above it are only logged.
        var topThreshold = thresholds[^1];
        var capMultiplier = ConfigurationManager.GetValue("Minigames.ScoreCapMultiplier", 0.0);
        if (capMultiplier > 0 && score > capMultiplier * topThreshold) {
            Logger.Warning("{Process} rejected the score {Score} of {Wizard}, above {Multiplier} times {Threshold}",
                Logger.Args(ProcessName, score, wizard.PlayerNameBehavior.GetWizardName(), capMultiplier, topThreshold));

            return true;
        }

        if (capMultiplier <= 0 && score > LoggedScoreCapMultiplier * topThreshold) {
            Logger.Warning("{Process} accepted the unusually high score {Score} of {Wizard}, top threshold {Threshold}",
                Logger.Args(ProcessName, score, wizard.PlayerNameBehavior.GetWizardName(), topThreshold));
        }

        return false;
    }

    private void SendLeaderboardResponse(ScoreTrackingList leaderboard) {
        if (!_serializer.Serialize(leaderboard, 5, out var leaderboardData)) {
            Logger.Error("{0} {1} failed to serialize leaderboard data.",
                Logger.Args(nameof(MinigameProcess), ProcessName));

            return;
        }

        var reply = new WIZARD_12_PROTOCOL.MSG_MINIGAMEREWARDS {
            Data = "",
            Scores = leaderboardData,
            MinigameIndex = _minigameIndex,
            FinalPhase = 0,
            Score = -1,
            Success = 0
        };
        Sender.Tell(reply, Self);
    }

    private void SendFinalRewardsResponse(int score, ScoreTrackingList leaderboard, MinigameRewardResult reward, Wizard wizard) {
        // Track the score for the player.
        var genderString = wizard.PlayerNameBehavior.Gender.ToString();
        genderString = genderString.Replace("eGender.", "");
        var scoreTrack = new Imlight.CoreLib.WizardData.Models.Misc.ScoreTracking {
            MinigameName = _minigameInfo.m_name,
            GameScore = score,
            GamerId = (Imcodec.Types.GID) wizard.CharId,
            GamerNameIndices = wizard.PlayerNameBehavior.NameIndices,
            GamerNameOverride = wizard.PlayerNameBehavior.NameOverride,
            GamerGender = genderString
        };
        var best = StoreScore(scoreTrack);

        // The board shows each gamer once, with their best score.
        var shown = leaderboard.m_scores
            .Where(x => x.m_gamerID.Full != best.GamerId.Full)
            .Append(best.GetClientBehaviorInstance())
            .OrderByDescending(x => x.m_gameScore)
            .Take(ScoreTrackingCollection.LeaderboardSize);
        leaderboard.m_scores = [.. shown];

        // Serialize the leaderboard data.
        if (!_serializer.Serialize(leaderboard, 5, out var leaderboardData)) {
            Logger.Error("{0} {1} failed to serialize leaderboard data.",
                Logger.Args(nameof(MinigameProcess), ProcessName));

            return;
        }

        // Serialize the loot.
        if (!_serializer.Serialize(reward.Loot, 5, out var lootData)) {
            Logger.Error("{0} {1} failed to serialize loot data.",
                Logger.Args(nameof(MinigameProcess), ProcessName));

            return;
        }

        var replyEnd = new WIZARD_12_PROTOCOL.MSG_MINIGAMEREWARDS {
            Data = lootData,
            Scores = leaderboardData,
            MinigameIndex = _minigameIndex,
            FinalPhase = reward.FinalPhase,
            Score = score,
            Success = reward.Success ? 1 : 0
        };
        Sender.Tell(replyEnd, Self);
    }

    private Imlight.CoreLib.WizardData.Models.Misc.ScoreTracking StoreScore(Imlight.CoreLib.WizardData.Models.Misc.ScoreTracking scoreTrack) {
        try {
            return ScoreTrackingCollection.AddScoreTracking(scoreTrack);
        } catch (Exception ex) {
            // The rewards were granted already, so the window still opens with this score on the board.
            Logger.Error("{Process} failed to store the score {Score}: {Reason}",
                Logger.Args(ProcessName, scoreTrack.GameScore, ex.GetBaseException().Message));

            return scoreTrack;
        }
    }

    private CHARACTER_103_PROTOCOL.MSG_CHARACTER QuerySenderCharacter() {
        var queryMsg = new CHARACTER_103_PROTOCOL.MSG_QUERYACTIVEWIZARD();
        var reason = "the reply holds no wizard";
        try {
            var timeout = TimeSpan.FromMilliseconds(QUERY_WIZARD_TIMEOUT_IN_MS);
            var response = Sender
                .Ask<CHARACTER_103_PROTOCOL.MSG_CHARACTER>(queryMsg, timeout)
                .Result;

            if (response?.Wizard is not null) {
                return response;
            }
        } catch (Exception ex) {
            reason = ex.GetBaseException().Message;
        }

        Logger.Error("{0} {1} failed to query character data in {2} ms: {3}",
            Logger.Args(nameof(MinigameProcess), ProcessName, QUERY_WIZARD_TIMEOUT_IN_MS, reason));

        return null;
    }

    private ScoreTrackingList CreateScoreTrackingList() {
        var scoreTrackingList = new ScoreTrackingList {
            m_scores = []
        };

        var leaderboard = ScoreTrackingCollection.GetLeaderboard(ProcessName);
        foreach (var entry in leaderboard) {
            scoreTrackingList.m_scores.Add(entry.GetClientBehaviorInstance());
        }

        return scoreTrackingList;
    }

}
