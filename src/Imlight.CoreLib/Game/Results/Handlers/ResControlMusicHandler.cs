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
 */

using System.Collections.Generic;
using Akka.Actor;
using Imlight.Common;
using Imcodec.ObjectProperty.TypeCache;
using Imcodec.MessageLayer.Generated;
using Imlight.CoreLib.Game.Results.Contexts;

namespace Imlight.CoreLib.Game.Results.Handlers;

internal sealed class ResControlMusicHandler : BaseResultHandler<ResControlBackgroundMusic> {

    // todo: is 1 Restart? The client reads Action as a plain number and stops the music on 0.
    private const int ActionStop = 0;
    private const int ActionRestart = 1;

    private const string RestartActionName = "Restart";

    // Imcodec reads m_action under hash 0x67633059 while the game files store it under 0x5F9F4ADC, so it arrives
    // empty. These are the game files' only triggers whose action is "Restart".
    private static readonly HashSet<string> s_restartTriggers = [
        "BG_RestartMuisc",
        "Return_BG_Music_Trigger",
        "StartMusic",
        "Start_BG",
        "StreetMusician_MusicTrigger_Exit",
        "StreetMusician_TriggerExit_03",
        "Trigg_PlaySting_03",
    ];

    public override bool Execute(IResultContext context) {
        if (Result == null) {
            Logger.Error("Tried to create a {0}, but the result was null.",
                Logger.Args(GetType().Name));

            return false;
        }

        var msg = new WIZARD_12_PROTOCOL.MSG_CONTROLMUSIC {
            FadeTime = 2.0f,
            Action = IsRestart(context) ? ActionRestart : ActionStop,
        };

        context.GetPlayerRef().Tell(msg);

        return true;
    }

    private bool IsRestart(IResultContext context) {
        string action = Result.m_action;
        if (!string.IsNullOrEmpty(action)) {
            return action == RestartActionName;
        }

        return (context as GenericResultContext)?.TriggerName is { } triggerName && s_restartTriggers.Contains(triggerName);
    }

}
