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

namespace Imlight.Setup;

/// <summary>
/// The two builds of this program. ImlightSetup installs original Imlight; ImlightManager (built with
/// -p:ImlightEdition=Manager) installs the Niduus/Imlight fork, which adds the randomizer and ID lookup.
/// They install side by side under different names.
/// </summary>
internal static class Edition {

#if MANAGER_EDITION
    internal const bool IS_MANAGER = true;
    internal const string NAME = "Imlight Manager";
    internal const string DEFAULT_REPOSITORY = "Niduus/Imlight";
    internal const string INSTALL_FOLDER_NAME = "ImlightManager";
    internal const string RELEASE_ASSET = "ImlightManager.exe";
    internal const string REGISTRY_KEY = "ImlightManager";
#else
    internal const bool IS_MANAGER = false;
    internal const string NAME = "Imlight Server";
    internal const string DEFAULT_REPOSITORY = "Revive101/Imlight";
    internal const string INSTALL_FOLDER_NAME = "ImlightServer";
    internal const string RELEASE_ASSET = "ImlightSetup.exe";
    internal const string REGISTRY_KEY = "ImlightServer";
#endif

}
