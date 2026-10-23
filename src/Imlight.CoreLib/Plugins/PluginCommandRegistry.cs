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
 * PLUGIN COMMAND REGISTRY
 * ========================================================================
 *
 * PURPOSE:
 * Holds every plugin's chat commands and runs them for the command
 * dispatcher. Each plugin registers through its own scope so the registry
 * knows which plugin owns each command.
 *
 * USAGE EXAMPLE:
 * plugin.Commands = PluginManager.Commands.For(plugin);
 * if (PluginManager.Commands.TryExecute(name, args, context)) { return; }
 *
 * NOTE:
 * Names that collide with a server command or another plugin's command are
 * rejected at registration, so the lookup order in the dispatcher never
 * decides which command wins.
 *
 * Created by: phazejeff
 * Version: KALI 1.0
 * Last Updated: 10/23/2026
 */

using System;
using System.Collections.Concurrent;
using System.Linq;
using Imlight.Common;
using Imlight.CoreLib.Auth;
using Imlight.CoreLib.Game.Commands;
using Imlight.CoreLib.WizardData.Models.Player;
using Imlight.PluginApi;
using Imlight.PluginApi.Commands;

namespace Imlight.CoreLib.Plugins;

internal sealed class PluginCommandRegistry {

    private sealed record Registration(ImlightPlugin Owner, Action<CommandInvocation> Handler, CommandPermission Permission);

    private sealed class Scope(PluginCommandRegistry registry, ImlightPlugin owner) : ICommandRegistry {

        public void Register(string name, Action<CommandInvocation> handler,
                             CommandPermission permission = CommandPermission.None, params string[] aliases)
            => registry.Register(owner, name, handler, permission, aliases);

    }

    // Keyed by lower-case name and alias. Written at boot, read from the dispatcher actor.
    private readonly ConcurrentDictionary<string, Registration> _commands = new();

    /// <summary>
    /// The registry a plugin sees, which registers commands under that plugin.
    /// </summary>
    public ICommandRegistry For(ImlightPlugin plugin)
        => new Scope(this, plugin);

    /// <summary>
    /// Runs the plugin command called <paramref name="name"/>, if there is one, and reports whether there was.
    /// </summary>
    public bool TryExecute(string name, string[] args, CommandContext context) {
        name = name.ToLower();
        if (!_commands.TryGetValue(name, out var registration)) {
            return false;
        }

        var player = new PluginPlayer(context.Character, context.SessionActor);
        if (!AuthorityRequester.RequestAuthority(ToAuthLevel(registration.Permission), context.Account, $"Command {name}")) {
            player.SendMessage("You do not have permission to use this command.");
            return true;
        }

        try {
            registration.Handler(new CommandInvocation(player, name, [.. args.Where(x => x.Length > 0)]));
        }
        catch (Exception ex) {
            // A broken plugin must never take down the command dispatcher.
            Logger.Error("Plugin {Name} threw running command {Command}: {Exception}",
                Logger.Args(registration.Owner.Name, name, ex));
            player.SendMessage("An error occurred while executing the command.");
        }

        return true;
    }

    /// <summary>
    /// Removes every command the plugin registered.
    /// </summary>
    public void RemoveAll(ImlightPlugin plugin) {
        foreach (var (name, registration) in _commands) {
            if (registration.Owner == plugin) {
                _ = _commands.TryRemove(name, out _);
            }
        }
    }

    private void Register(ImlightPlugin owner, string name, Action<CommandInvocation> handler,
                          CommandPermission permission, string[] aliases) {
        ArgumentNullException.ThrowIfNull(handler);

        var names = aliases.Prepend(name).Select(x => x?.ToLower()).ToArray();
        foreach (var x in names) {
            if (string.IsNullOrEmpty(x) || x.Any(char.IsWhiteSpace)) {
                throw new ArgumentException($"Command name '{x}' must be a single word.");
            }
            if (CommandDispatcher.IsBuiltInCommand(x)) {
                throw new ArgumentException($"Command '{x}' is already a server command.");
            }
            if (_commands.TryGetValue(x, out var existing)) {
                throw new ArgumentException($"Command '{x}' is already registered by plugin {existing.Owner.Name}.");
            }
        }
        if (names.Distinct().Count() != names.Length) {
            throw new ArgumentException($"Command '{name}' lists the same name more than once.");
        }

        var registration = new Registration(owner, handler, permission);
        foreach (var x in names) {
            _commands[x] = registration;
        }
    }

    private static AuthLevel ToAuthLevel(CommandPermission permission) {
        var level = permission switch {
            CommandPermission.None => AuthLevel.None,
            CommandPermission.QualityAssurance => AuthLevel.QualityAssurance,
            CommandPermission.HallMonitor => AuthLevel.HallMonitor,
            CommandPermission.Developer => AuthLevel.Developer,
            CommandPermission.Administrator => AuthLevel.Administrator,
            _ => AuthLevel.Administrator
        };

#if !DEBUG
        // Same as server commands: developer commands are administrator-only outside debug builds.
        if (level == AuthLevel.Developer) {
            level = AuthLevel.Administrator;
        }
#endif

        return level;
    }

}
