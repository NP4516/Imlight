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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Imlight.Setup;

/// <summary>
/// The app's window: Play (this server, other private servers, the command box), Server (start, stop,
/// randomizer, log), Settings, Accounts and Tools.
/// </summary>
internal sealed class ManagerWindow : Window {

    private static readonly IBrush CARD = new SolidColorBrush(Color.FromArgb(20, 128, 128, 128));
    private static readonly IBrush LINE = new SolidColorBrush(Color.FromArgb(50, 128, 128, 128));
    private static readonly IBrush GOOD = new SolidColorBrush(Color.FromRgb(46, 160, 92));
    private static readonly IBrush WARN = new SolidColorBrush(Color.FromRgb(214, 150, 40));
    private static readonly IBrush BAD = new SolidColorBrush(Color.FromRgb(220, 70, 60));
    private static readonly IBrush MUTED = new SolidColorBrush(Color.FromArgb(170, 128, 128, 128));
    private static readonly IBrush LOG_BACK = new SolidColorBrush(Color.FromRgb(17, 21, 28));
    private static readonly IBrush LOG_TEXT = new SolidColorBrush(Color.FromRgb(216, 222, 233));
    private static readonly FontFamily MONO = new("Cascadia Mono, Consolas, Menlo, DejaVu Sans Mono, monospace");

    private readonly ServerManager _manager;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };

    // Header and message bar.
    private readonly Border _dot = new() { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Background = MUTED };
    private readonly TextBlock _statusText = new() { Text = "Checking…", Foreground = MUTED, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Border _messageBar = new() { Padding = new Thickness(16, 10), IsVisible = false };

    // Play.
    private readonly Button _playButton = Primary("Play Wizard101");
    private readonly TextBlock _playHelp = Sub("Starts Wizard101 connected to the server on this computer. If the server is off, it starts it first.");
    private readonly ComboBox _commandAccount = new() { MinWidth = 200, PlaceholderText = "Account" };
    private readonly TextBox _commandText = new() { Watermark = ".mod additem 1317127", FontFamily = MONO };
    private readonly StackPanel _commandHistory = new() { Spacing = 4, Margin = new Thickness(0, 10, 0, 0) };
    private readonly StackPanel _serverRows = new() { Spacing = 6 };
    private readonly TextBox _newServerName = new() { Watermark = "Name (optional)", Width = 160 };
    private readonly TextBox _newServerHost = new() { Watermark = "Address, such as play.example.com", MinWidth = 260 };
    private readonly TextBox _newServerPort = new() { Watermark = "Port", Width = 90 };

    // Server.
    private readonly Button _startButton = Primary("Start server");
    private readonly Button _restartButton = Plain("Restart");
    private readonly Button _stopButton = Plain("Stop server");
    private readonly Border _randomizerCard;
    private readonly ToggleSwitch _randomizer = new() { OnContent = "On", OffContent = "Off" };
    private readonly CheckBox _hideDebug = new() { Content = "Hide debug lines", IsChecked = true };
    private readonly CheckBox _hideWarnings = new() { Content = "Hide warnings" };
    private readonly CheckBox _follow = new() { Content = "Follow", IsChecked = true };
    private readonly StackPanel _logLines = new();
    private readonly ScrollViewer _logScroll;
    private bool _updatingRandomizer;

    // Settings.
    private readonly TextBox _settingSearch = new() { Watermark = "Search settings" };
    private readonly StackPanel _settingsList = new() { Spacing = 8 };
    private readonly Dictionary<(string Section, string Key), Setting> _pending = [];
    private readonly Border _saveBar = new() { IsVisible = false, Padding = new Thickness(16, 10) };
    private readonly TextBlock _saveCount = new() { VerticalAlignment = VerticalAlignment.Center };
    private List<(string Section, List<Setting> Settings)> _settings = [];

    // Accounts.
    private readonly TextBox _newUsername = new() { Watermark = "Username", Width = 180 };
    private readonly TextBox _newPassword = new() { Watermark = "Password", PasswordChar = '•', Width = 180 };
    private readonly ComboBox _newLevel = new() { Width = 180 };
    private readonly StackPanel _accountRows = new() { Spacing = 6 };
    private readonly TextBlock _accountsNote = Sub("");

    // Tools.
    private readonly Button _idButton = Plain("Spell, item and creature IDs");

    internal ManagerWindow(ServerManager manager) {
        _manager = manager;
        Title = Edition.NAME;
        Width = 1120;
        Height = 820;
        MinWidth = 640;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _randomizerCard = Card(new DockPanel {
            Children = {
                WithDock(_randomizer, Dock.Right),
                new StackPanel {
                    Children = {
                        Heading("Randomizer mode"),
                        Sub("Shuffles schools, spells, drops and more for every character, kept balanced. Fine-tune it under Settings, Randomizer."),
                    },
                },
            },
        });
        _randomizerCard.IsVisible = false;
        _logScroll = new ScrollViewer {
            Height = 440,
            Background = LOG_BACK,
            Content = new Border { Padding = new Thickness(12), Child = _logLines },
        };

        var tabs = new TabControl {
            Padding = new Thickness(0),
            ItemsSource = new[] {
                Tab("Play", PlayTab()),
                Tab("Server", ServerTab()),
                Tab("Settings", SettingsTab()),
                Tab("Accounts", AccountsTab()),
                Tab("Tools", ToolsTab()),
            },
        };
        tabs.SelectionChanged += async (_, _) => {
            switch ((tabs.SelectedItem as TabItem)?.Header as string) {
                case "Play": await LoadPlayAsync(); break;
                case "Settings" when _settings.Count == 0: LoadSettings(); break;
                case "Accounts": await LoadAccountsAsync(); break;
            }
        };

        var header = new DockPanel {
            Margin = new Thickness(24, 14, 24, 4),
            Children = {
                new TextBlock { Text = Edition.NAME, FontSize = 20, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center },
                new StackPanel {
                    Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(18, 0, 0, 0),
                    Children = { WithCenter(_dot), _statusText },
                },
            },
        };

        _messageBar.Child = _message;
        _saveBar.Child = new StackPanel {
            Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center,
            Children = { _saveCount, Action(Plain("Discard"), DiscardSettings), Action(Primary("Save changes"), SaveSettingsAsync) },
        };
        _saveBar.Background = CARD;

        Content = new DockPanel {
            Children = {
                WithDock(header, Dock.Top),
                WithDock(_messageBar, Dock.Bottom),
                WithDock(_saveBar, Dock.Bottom),
                tabs,
            },
        };

        _timer.Tick += (_, _) => Refresh();
        Opened += async (_, _) => {
            Refresh();
            await LoadPlayAsync();
            _timer.Start();
        };
        Closed += (_, _) => _timer.Stop();
    }

    // ---------------------------------------------------------------------
    // Tabs
    // ---------------------------------------------------------------------

    private Control PlayTab() {
        Action(_playButton, async () => Say(await _manager.PlayHereAsync()));
        _playButton.FontSize = 16;
        _playButton.Padding = new Thickness(22, 12);

        var run = Action(Primary("Run"), RunCommandAsync);
        _commandText.KeyDown += async (_, e) => {
            if (e.Key == Avalonia.Input.Key.Enter) {
                e.Handled = true;
                await Guard(RunCommandAsync);
            }
        };

        return Page(
            Card(new DockPanel {
                Children = {
                    WithDock(WithCenter(_playButton), Dock.Right),
                    new StackPanel { Margin = new Thickness(0, 0, 16, 0), Children = { Heading("Play on my server"), _playHelp } },
                },
            }),
            Card(new StackPanel {
                Children = {
                    Heading("Command box"),
                    Sub("Paste an in-game command here instead of typing it in game. It runs for the character that account has logged in, " +
                        "with that account's permissions, and the reply shows in game."),
                    new DockPanel {
                        Margin = new Thickness(0, 10, 0, 0),
                        Children = {
                            WithDock(_commandAccount, Dock.Left),
                            WithDock(run, Dock.Right),
                            WithMargin(_commandText, new Thickness(8, 0)),
                        },
                    },
                    _commandHistory,
                },
            }),
            Card(new StackPanel {
                Children = {
                    Heading("Other servers"),
                    Sub("Play on someone else's private server with your Wizard101. The live game is blocked, so these only work for private servers."),
                    WithMargin(_serverRows, new Thickness(0, 10, 0, 10)),
                    new WrapPanel {
                        Children = {
                            Spaced(_newServerName), Spaced(_newServerHost), Spaced(_newServerPort),
                            Spaced(Action(Plain("Save server"), SaveServerAsync)),
                        },
                    },
                },
            }));
    }

    private Control ServerTab() {
        Action(_startButton, () => Task.FromResult(Say(_manager.StartServer())));
        Action(_restartButton, async () => {
            if (await ConfirmAsync("Restart the server? Everyone playing is disconnected.")) {
                Say(await _manager.RestartServerAsync());
            }
        });
        Action(_stopButton, async () => {
            if (await ConfirmAsync("Stop the server? Everyone playing is disconnected.")) {
                Say(_manager.StopServer());
            }
        });
        _stopButton.Foreground = BAD;
        _randomizer.IsCheckedChanged += async (_, _) => {
            if (!_updatingRandomizer) {
                await Guard(() => SetRandomizerAsync(_randomizer.IsChecked == true));
            }
        };
        foreach (var box in new[] { _hideDebug, _hideWarnings }) {
            box.IsCheckedChanged += (_, _) => RefreshLog();
        }

        return Page(
            Card(new StackPanel {
                Children = {
                    Heading("Server"),
                    Sub("Start the server here. It gets the game files from your Wizard101 folder by itself. Closing this window stops it."),
                    WithMargin(Row(_startButton, _restartButton, _stopButton), new Thickness(0, 10, 0, 0)),
                },
            }),
            _randomizerCard,
            Card(new StackPanel {
                Children = {
                    new DockPanel {
                        Margin = new Thickness(0, 0, 0, 8),
                        Children = {
                            WithDock(Row(_hideDebug, _hideWarnings, _follow), Dock.Right),
                            Heading("Log"),
                        },
                    },
                    new Border { CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = _logScroll },
                    WithMargin(Sub("The same message repeated in a row shows once, with how many times it happened."), new Thickness(0, 8, 0, 0)),
                },
            }));
    }

    private Control SettingsTab() {
        _settingSearch.TextChanged += (_, _) => RenderSettings();

        return Page(
            Card(new StackPanel {
                Children = {
                    Heading("Settings"),
                    Sub("Every setting in the server's Imlight.ini. Changes apply the next time the server starts."),
                    WithMargin(_settingSearch, new Thickness(0, 10, 0, 0)),
                },
            }),
            _settingsList);
    }

    private Control AccountsTab() {
        _newLevel.ItemsSource = PlayerAccounts.AUTH_LEVELS.Select(LevelName).ToList();
        _newLevel.SelectedIndex = 0;

        return Page(
            Card(new StackPanel {
                Children = {
                    Heading("New account"),
                    Sub("Accounts can only be changed while the server is running."),
                    WithMargin(new WrapPanel {
                        Children = {
                            Spaced(_newUsername), Spaced(_newPassword), Spaced(_newLevel),
                            Spaced(Action(Primary("Create account"), CreateAccountAsync)),
                        },
                    }, new Thickness(0, 10, 0, 0)),
                },
            }),
            Card(new StackPanel {
                Children = {
                    new DockPanel {
                        Children = { WithDock(Action(Plain("Refresh"), LoadAccountsAsync), Dock.Right), Heading("Accounts") },
                    },
                    WithMargin(_accountsNote, new Thickness(0, 6, 0, 10)),
                    _accountRows,
                },
            }));
    }

    private Control ToolsTab() {
        Action(_idButton, () => Open(_manager.Status().IdLookupPage
            ?? throw new PanelException("The ID list is not ready yet. Start the server and give it a minute to build the list.")));

        return Page(Card(new StackPanel {
            Children = {
                Heading("Tools"),
                Sub("Shortcuts to the server's files."),
                WithMargin(new WrapPanel {
                    Children = {
                        Spaced(_idButton),
                        Spaced(Action(Plain("Open log folder"), () => Open(_manager.LogFolder))),
                        Spaced(Action(Plain("Open settings folder"), () => Open(_manager.ConfigFolder))),
                        Spaced(Action(Plain("Open install folder"), () => Open(_manager.InstallFolder))),
                    },
                }, new Thickness(0, 10, 0, 10)),
                Sub($"{Edition.NAME} {SelfUpdate.CurrentVersion}, installed in {_manager.InstallFolder}."),
            },
        }));
    }

    // ---------------------------------------------------------------------
    // Status and log
    // ---------------------------------------------------------------------

    private void Refresh() {
        var status = _manager.Status();
        _dot.Background = status.Ready ? GOOD : status.Running ? WARN : MUTED;
        _statusText.Text = status.Ready ? "Running, ready for players" : status.Running ? "Starting…" : "Stopped";
        _startButton.IsEnabled = !status.Running;
        _stopButton.IsEnabled = status.Running;
        _restartButton.IsEnabled = status.Running;
        _playButton.IsEnabled = status.ClientFound;
        if (!status.ClientFound) {
            _playHelp.Text = "Wizard101 could not be found. Run \"Update Imlight\" from the Start menu once.";
        }

        _randomizerCard.IsVisible = status.Randomizer is not null;
        _updatingRandomizer = true;
        _randomizer.IsChecked = status.Randomizer == true;
        _updatingRandomizer = false;
        RefreshLog();
    }

    private void RefreshLog() {
        List<LogLine> lines;
        try {
            lines = _manager.ReadLog();
        }
        catch (Exception) {
            return;
        }

        var atBottom = _logScroll.Offset.Y + _logScroll.Viewport.Height >= _logScroll.Extent.Height - 30;
        _logLines.Children.Clear();
        if (lines.Count == 0) {
            _logLines.Children.Add(new TextBlock { Text = "No log yet. Start the server to see it here.", Foreground = MUTED });

            return;
        }

        foreach (var line in lines) {
            if ((_hideDebug.IsChecked == true && line.Level is "DBG" or "VRB") || (_hideWarnings.IsChecked == true && line.Level == "WRN")) {
                continue;
            }

            var text = new SelectableTextBlock {
                Text = line.Count > 1 ? $"{line.Text}   ×{line.Count}" : line.Text,
                FontFamily = MONO,
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap,
                Foreground = line.Level switch {
                    "WRN" => WARN,
                    "ERR" or "FTL" => BAD,
                    "DBG" or "VRB" => MUTED,
                    _ => LOG_TEXT,
                },
            };
            _logLines.Children.Add(text);
        }

        if (_follow.IsChecked == true && atBottom) {
            Dispatcher.UIThread.Post(() => _logScroll.ScrollToEnd(), DispatcherPriority.Background);
        }
    }

    private async Task SetRandomizerAsync(bool on) {
        var restart = _manager.SaveSettings([new Setting("Randomizer", "Enabled", on ? "true" : "false", "")]);
        _settings = [];
        Say($"Randomizer mode is now {(on ? "on" : "off")}.");
        if (restart && await ConfirmAsync($"Randomizer mode is now {(on ? "on" : "off")}. Restart the server now to use it?")) {
            Say(await _manager.RestartServerAsync());
        }
    }

    // ---------------------------------------------------------------------
    // Play
    // ---------------------------------------------------------------------

    private async Task LoadPlayAsync() {
        RenderServers();
        try {
            using var accounts = _manager.Accounts();
            var names = (await accounts.ListAsync()).Select(a => a.Username).ToList();
            var keep = _commandAccount.SelectedItem as string;
            _commandAccount.ItemsSource = names;
            _commandAccount.SelectedItem = keep is not null && names.Contains(keep) ? keep : names.FirstOrDefault();
            _commandAccount.PlaceholderText = "Account";
        }
        catch (Exception ex) when (ex is AccountException or PanelException) {
            _commandAccount.ItemsSource = null;
            _commandAccount.PlaceholderText = "Start the server to pick an account";
        }
    }

    private void RenderServers() {
        _serverRows.Children.Clear();
        var servers = GameClient.LoadServers(_manager.InstallFolder);
        if (servers.Count == 0) {
            _serverRows.Children.Add(Sub("No other servers saved yet."));

            return;
        }

        foreach (var server in servers) {
            _serverRows.Children.Add(new DockPanel {
                Children = {
                    WithDock(Row(
                        Action(Primary("Play"), async () => {
                            await GameClient.LaunchAsync(_manager.InstallFolder, server.Host, server.Port);
                            Say($"Starting Wizard101 on {server.Name}.");
                        }),
                        Danger(Action(Plain("Remove"), async () => {
                            if (await ConfirmAsync($"Remove {server.Name}?")) {
                                GameClient.RemoveServer(_manager.InstallFolder, server.Name);
                                RenderServers();
                                Say($"{server.Name} was removed.");
                            }
                        }))), Dock.Right),
                    new TextBlock {
                        Text = $"{server.Name}    {server.Host}:{server.Port}", VerticalAlignment = VerticalAlignment.Center,
                    },
                },
            });
        }
    }

    private async Task SaveServerAsync() {
        var port = int.TryParse(_newServerPort.Text, out var parsed) ? parsed : 0;
        var saved = await GameClient.AddServerAsync(_manager.InstallFolder, _newServerName.Text ?? "", _newServerHost.Text ?? "", port);
        _newServerName.Text = _newServerHost.Text = _newServerPort.Text = "";
        RenderServers();
        Say($"{saved.Name} was saved.");
    }

    private async Task RunCommandAsync() {
        var command = _commandText.Text?.Trim() ?? "";
        if (command.Length == 0) {
            return;
        }

        var result = new TextBlock { Text = "Sending…", Foreground = MUTED, TextWrapping = TextWrapping.Wrap };
        var reuse = new Button { Content = command, FontFamily = MONO, Background = Brushes.Transparent, Padding = new Thickness(4, 2) };
        reuse.Click += (_, _) => _commandText.Text = command;
        _commandHistory.Children.Insert(0, new DockPanel { Children = { WithDock(reuse, Dock.Left), WithCenter(result) } });
        while (_commandHistory.Children.Count > 20) {
            _commandHistory.Children.RemoveAt(_commandHistory.Children.Count - 1);
        }

        try {
            result.Text = await _manager.SendCommandAsync(_commandAccount.SelectedItem as string ?? "", command);
            result.Foreground = GOOD;
            _commandText.Text = "";
        }
        catch (PanelException ex) {
            result.Text = ex.Message;
            result.Foreground = BAD;
        }
    }

    // ---------------------------------------------------------------------
    // Settings
    // ---------------------------------------------------------------------

    private void LoadSettings() {
        _settings = _manager.ReadSettings();
        _pending.Clear();
        UpdateSaveBar();
        RenderSettings();
    }

    private void RenderSettings() {
        var query = _settingSearch.Text?.Trim() ?? "";
        _settingsList.Children.Clear();
        foreach (var (section, settings) in _settings) {
            var matches = settings.Where(s => query.Length == 0
                || $"{section} {s.Key} {s.Help}".Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count == 0) {
                continue;
            }

            var rows = new StackPanel();
            foreach (var setting in matches) {
                rows.Children.Add(SettingRow(setting));
            }

            _settingsList.Children.Add(new Expander {
                Header = section,
                IsExpanded = query.Length > 0 || section == "Randomizer",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Content = rows,
            });
        }
    }

    private Control SettingRow(Setting setting) {
        var current = _pending.TryGetValue((setting.Section, setting.Key), out var changed) ? changed.Value : setting.Value;
        Control input;
        if (bool.TryParse(setting.Value, out _)) {
            var toggle = new ToggleSwitch { IsChecked = current.Equals("true", StringComparison.OrdinalIgnoreCase), OnContent = "", OffContent = "" };
            toggle.IsCheckedChanged += (_, _) => Change(setting, toggle.IsChecked == true ? "true" : "false");
            input = toggle;
        }
        else {
            var box = new TextBox { Text = current };
            if (setting.Key.Contains("password", StringComparison.OrdinalIgnoreCase)) {
                box.PasswordChar = '•';
            }

            box.TextChanged += (_, _) => Change(setting, box.Text ?? "");
            input = box;
        }

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("260,*"), Margin = new Thickness(0, 8) };
        grid.Children.Add(new TextBlock { Text = setting.Key, FontWeight = FontWeight.Medium, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(input, 1);
        grid.Children.Add(input);

        var row = new StackPanel { Children = { grid } };
        if (setting.Help.Length > 0) {
            row.Children.Add(Sub(setting.Help));
        }

        return new Border { BorderBrush = LINE, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 0, 0, 8), Child = row };
    }

    private void Change(Setting setting, string value) {
        if (value == setting.Value) {
            _pending.Remove((setting.Section, setting.Key));
        }
        else {
            _pending[(setting.Section, setting.Key)] = setting with { Value = value };
        }

        UpdateSaveBar();
    }

    private void UpdateSaveBar() {
        _saveBar.IsVisible = _pending.Count > 0;
        _saveCount.Text = $"{_pending.Count} unsaved change{(_pending.Count == 1 ? "" : "s")}";
    }

    private Task DiscardSettings() {
        _pending.Clear();
        UpdateSaveBar();
        RenderSettings();

        return Task.CompletedTask;
    }

    private async Task SaveSettingsAsync() {
        var count = _pending.Count;
        var restart = _manager.SaveSettings(_pending.Values.ToList());
        LoadSettings();
        Refresh();
        Say($"Saved {count} setting{(count == 1 ? "" : "s")}.");
        if (restart && await ConfirmAsync("Restart the server now to use the new settings? Everyone playing is disconnected.")) {
            Say(await _manager.RestartServerAsync());
        }
    }

    // ---------------------------------------------------------------------
    // Accounts
    // ---------------------------------------------------------------------

    private async Task LoadAccountsAsync() {
        _accountRows.Children.Clear();
        List<AccountSummary> list;
        try {
            using var accounts = _manager.Accounts();
            list = await accounts.ListAsync();
        }
        catch (Exception ex) when (ex is AccountException or PanelException) {
            _accountsNote.Text = ex.Message;

            return;
        }

        _accountsNote.Text = $"{list.Count} account{(list.Count == 1 ? "" : "s")}. Level changes apply the next time that player logs in. " +
            "To delete an account, use .account delete in game.";
        foreach (var account in list) {
            _accountRows.Children.Add(AccountRow(account));
        }
    }

    private Control AccountRow(AccountSummary account) {
        var level = new ComboBox { ItemsSource = PlayerAccounts.AUTH_LEVELS.Select(LevelName).ToList(), Width = 170 };
        level.SelectedIndex = Math.Max(0, Array.IndexOf(PlayerAccounts.AUTH_LEVELS, account.AuthLevel));
        level.SelectionChanged += async (_, _) => {
            var chosen = PlayerAccounts.AUTH_LEVELS[Math.Max(0, level.SelectedIndex)];
            if (chosen != account.AuthLevel) {
                await Guard(async () => {
                    using var accounts = _manager.Accounts();
                    await accounts.SetAuthLevelAsync(account.Username, chosen);
                    Say($"{account.Username} is now {LevelName(chosen)}. It applies the next time they log in.");
                });
            }
        };

        var password = Action(Plain("New password"), async () => {
            if (await AskAsync($"New password for {account.Username}", secret: true) is { Length: > 0 } text) {
                using var accounts = _manager.Accounts();
                await accounts.SetPasswordAsync(account.Username, text);
                Say($"The password for {account.Username} was changed.");
            }
        });
        var lockButton = Action(Plain(account.IsLocked ? "Unlock" : "Lock"), async () => {
            if (!account.IsLocked && !await ConfirmAsync($"Lock {account.Username}? They cannot log in until unlocked.")) {
                return;
            }

            using var accounts = _manager.Accounts();
            await accounts.SetLockedAsync(account.Username, !account.IsLocked);
            Say(account.IsLocked ? $"{account.Username} can log in again." : $"{account.Username} can no longer log in.");
            await LoadAccountsAsync();
        });
        if (!account.IsLocked) {
            Danger(lockButton);
        }

        var lastLogin = account.LastLoginTime is { } time && !time.StartsWith("0001", StringComparison.Ordinal) && DateTime.TryParse(time, out var parsed)
            ? parsed.ToLocalTime().ToString("g")
            : "Never";
        var details = new TextBlock {
            Text = $"{account.Characters} character{(account.Characters == 1 ? "" : "s")} · last login {lastLogin}" + (account.IsLocked ? " · Locked" : ""),
            Foreground = account.IsLocked ? BAD : MUTED,
        };

        return new Border {
            BorderBrush = LINE, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 6),
            Child = new DockPanel {
                Children = {
                    WithDock(Row(level, password, lockButton), Dock.Right),
                    new StackPanel {
                        VerticalAlignment = VerticalAlignment.Center,
                        Children = { new TextBlock { Text = account.Username, FontWeight = FontWeight.SemiBold }, details },
                    },
                },
            },
        };
    }

    private async Task CreateAccountAsync() {
        using var accounts = _manager.Accounts();
        var username = _newUsername.Text ?? "";
        await accounts.CreateAsync(username, _newPassword.Text ?? "", PlayerAccounts.AUTH_LEVELS[Math.Max(0, _newLevel.SelectedIndex)]);
        _newUsername.Text = _newPassword.Text = "";
        Say($"Account {username} was created.");
        await LoadAccountsAsync();
    }

    private static string LevelName(string level) => level switch {
        "None" => "Player",
        "QualityAssurance" => "Quality assurance",
        "HallMonitor" => "Hall monitor",
        _ => level,
    };

    // ---------------------------------------------------------------------
    // Messages and dialogs
    // ---------------------------------------------------------------------

    private string Say(string text, bool isError = false) {
        _message.Text = text;
        _messageBar.Background = isError ? BAD : CARD;
        _message.Foreground = isError ? Brushes.White : null;
        _messageBar.IsVisible = true;

        return text;
    }

    /// <summary>
    /// Runs an action, showing any problem in the message bar instead of failing.
    /// </summary>
    private async Task Guard(Func<Task> action) {
        try {
            await action();
        }
        catch (Exception ex) when (ex is PanelException or AccountException) {
            Say(ex.Message, isError: true);
        }
        catch (Exception ex) {
            Say($"Something went wrong: {ex.Message}", isError: true);
        }
    }

    private Task Open(string path) {
        ServerManager.Open(path);

        return Task.CompletedTask;
    }

    private Task<bool> ConfirmAsync(string question) {
        var dialog = Dialog(question, null, out var ok);

        return dialog.ShowDialog<bool>(this);
    }

    private async Task<string?> AskAsync(string question, bool secret) {
        var box = new TextBox { PasswordChar = secret ? '•' : '\0' };
        var dialog = Dialog(question, box, out _);
        var confirmed = await dialog.ShowDialog<bool>(this);

        return confirmed ? box.Text : null;
    }

    private static Window Dialog(string question, Control? input, out Button ok) {
        var dialog = new Window {
            Title = Edition.NAME, Width = 420, SizeToContent = SizeToContent.Height, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        ok = Primary("OK");
        ok.Click += (_, _) => dialog.Close(true);
        var cancel = Plain("Cancel");
        cancel.Click += (_, _) => dialog.Close(false);
        var body = new StackPanel { Margin = new Thickness(20), Spacing = 14, Children = { new TextBlock { Text = question, TextWrapping = TextWrapping.Wrap } } };
        if (input is not null) {
            body.Children.Add(input);
        }

        body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, ok } });
        dialog.Content = body;

        return dialog;
    }

    // ---------------------------------------------------------------------
    // Building blocks
    // ---------------------------------------------------------------------

    private Button Action(Button button, Func<Task> action) {
        button.Click += async (_, _) => {
            button.IsEnabled = false;
            try {
                await Guard(action);
            }
            finally {
                button.IsEnabled = true;
            }
        };

        return button;
    }

    private static TabItem Tab(string header, Control content) => new() { Header = header, Content = content, FontSize = 17, Padding = new Thickness(14, 4) };

    private static Control Page(params Control[] cards) {
        var stack = new StackPanel { Margin = new Thickness(24, 12, 24, 40), Spacing = 14, MaxWidth = 1060 };
        stack.Children.AddRange(cards);

        return new ScrollViewer { Content = stack };
    }

    private static Border Card(Control content) => new() {
        Background = CARD, CornerRadius = new CornerRadius(12), Padding = new Thickness(18), Child = content,
    };

    private static TextBlock Heading(string text) => new() { Text = text, FontSize = 16, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 4) };

    private static TextBlock Sub(string text) => new() { Text = text, Foreground = MUTED, TextWrapping = TextWrapping.Wrap };

    private static Button Primary(string text) => new() { Content = text, Classes = { "accent" } };

    private static Button Plain(string text) => new() { Content = text };

    private static Button Danger(Button button) {
        button.Foreground = BAD;

        return button;
    }

    private static StackPanel Row(params Control[] controls) {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        row.Children.AddRange(controls);

        return row;
    }

    private static T Spaced<T>(T control) where T : Control {
        control.Margin = new Thickness(0, 0, 10, 8);

        return control;
    }

    private static T WithDock<T>(T control, Dock dock) where T : Control {
        DockPanel.SetDock(control, dock);

        return control;
    }

    private static T WithCenter<T>(T control) where T : Control {
        control.VerticalAlignment = VerticalAlignment.Center;

        return control;
    }

    private static T WithMargin<T>(T control, Thickness margin) where T : Control {
        control.Margin = margin;

        return control;
    }

}
