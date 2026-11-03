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
 * PLAY MUSIC RESULT HANDLER
 * ========================================================================
 *
 * PURPOSE:
 * Plays the music a ResWizPlayMusic result names for the player: a street's theme, a quest cue or combat music.
 *
 * USAGE EXAMPLE:
 * Found by reflection in ResultDispatcher; zone triggers run it, such as Triton Avenue's theme on entering the zone.
 *
 * NOTE:
 * The music name is a SoundDefTemplate's file name ("WizardCity4M" is .../WizardCity4M.xml). Given that template's id,
 * the client plays its m_soundFile.
 *
 * TODO:
 * - Are m_flagA, m_flagB and m_flagC bits 0, 1 and 2 of Flags, in that order?
 *
 * Created by: kohdra32 with Claude (claude-opus-5-5)
 * Version: KALI 1.0
 * Last Updated: 11/02/2026
 */

using System;
using System.Collections.Concurrent;
using Akka.Actor;
using Imlight.Common;
using Imlight.CoreLib.Shared.Resources;
using Imcodec.ObjectProperty.TypeCache;
using Imcodec.MessageLayer.Generated;

namespace Imlight.CoreLib.Game.Results.Handlers;

internal sealed class ResWizPlayMusicHandler : BaseResultHandler<ResWizPlayMusic> {

    // 0 for a music name the game files have no template for, so it is looked up and logged once.
    private static readonly ConcurrentDictionary<string, uint> s_templateIds = new(StringComparer.OrdinalIgnoreCase);

    public override bool Execute(IResultContext context) {
        string musicName = Result?.m_musicName;
        if (string.IsNullOrEmpty(musicName) || context.GetPlayerRef() is not { } player) {
            return false;
        }

        var templateId = s_templateIds.GetOrAdd(musicName, FindTemplateId);
        if (templateId == 0) {
            return false;
        }

        var msg = new WIZARD_12_PROTOCOL.MSG_PLAYMUSIC {
            SoundID = templateId,
            FadeTime = Result.m_fadeTime,
            Flags = (sbyte) ((Result.m_flagA ? 1 : 0) | (Result.m_flagB ? 2 : 0) | (Result.m_flagC ? 4 : 0)),
            SoundFilename = "",
            StartDelay = Result.m_startDelay,
        };

        player.Tell(msg);

        return true;
    }

    private static uint FindTemplateId(string musicName) {
        var fileName = "/" + musicName + ".xml";
        if (CoreObjectFactory.TryGetCoreTemplateID(
                location => location?.m_filename.ToString().EndsWith(fileName, StringComparison.OrdinalIgnoreCase) == true,
                out var templateId)) {
            return templateId;
        }

        Logger.Error("Music {0} has no sound template in the game files.", Logger.Args(musicName));

        return 0;
    }

}
