using System.Reflection;
using StorageHub.Contracts.Ipc;

namespace StorageHub.Desktop.Tests;

public sealed class SettingsFormTests
{
    [Fact]
    public void Every_control_in_a_card_ends_on_the_same_edge()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            using var form = new SettingsForm();
            form.CreateControl();

            // What the rows are for: a page is a column of settings whose controls share one
            // trailing edge, whether the row carries a description, a switch, a number or a
            // drop-down. Before, a label could sit above its field on one page and beside it on
            // the next, and no two fields started at the same place.
            var cards = Descendants(form).OfType<SettingsCard>().ToArray();
            Assert.NotEmpty(cards);
            foreach (var card in cards)
            {
                var edges = card.Rows
                    .Where(static row => row.Controls.Count > 0)
                    .Select(static row => row.Controls
                        .OfType<Control>()
                        .Max(static control => control.Right))
                    .Distinct()
                    .ToArray();
                Assert.True(
                    edges.Length <= 1,
                    $"{card.Name} puts its controls on {edges.Length} different edges");
            }
        });
    }

    [Fact]
    public void Buttons_beside_a_field_match_it_rather_than_towering_over_it()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            using var probe = new StorageHubTextField();
            using var host = new Form();
            host.Controls.Add(probe);
            host.CreateControl();

            using var beside = new StorageHubButton { Text = "Import key…" };
            using var standalone = new StorageHubButton { Text = "OK" };
            host.Controls.Add(beside);
            host.Controls.Add(standalone);

            // Every button derives its height from the font through the same padding an input
            // uses, so one sitting beside a field lines up with it at any display scaling. There
            // used to be a second, taller shape for standalone buttons, fixed at 34 pixels
            // whatever the scaling was; that is what left a Browse button towering over its box.
            Assert.InRange(beside.Height, probe.Height - 2, probe.Height + 2);
            Assert.Equal(beside.Height, standalone.Height);
        });
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    [Fact]
    public void Settings_cards_keep_readable_width()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            using var settings = new SettingsForm();
            settings.CreateControl();

            var pages = GetField<Dictionary<string, Control>>(settings, "_pages");
            var cards = pages.Values
                .SelectMany(DescendantsAndSelf)
                .OfType<SettingsCard>()
                .ToArray();

            Assert.NotEmpty(cards);
            Assert.All(cards, card =>
            {
                // A card measures itself from its rows rather than carrying a height somebody
                // assigned, which is what used to leave a section ending at a different distance
                // below its last control on every page.
                Assert.False(card.AutoSize);
                Assert.NotEmpty(card.Rows);
                Assert.True(card.Height > 0);
                Assert.Equal(
                    card.Rows.Sum(row => row.GetPreferredSize(new Size(card.Width, 0)).Height),
                    card.Height);
            });
        });
    }

    [Fact]
    public void Navigation_is_a_tree_and_about_remains_in_the_help_menu()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            using var settings = new SettingsForm();
            var navigation = GetField<TreeView>(settings, "_categories");

            Assert.Contains(navigation.Nodes.Cast<TreeNode>(), node => node.Text == "Transfers & sync");
            Assert.Contains(navigation.Nodes.Cast<TreeNode>(), node => node.Text == "Appearance");
            var connections = Assert.Single(navigation.Nodes.Cast<TreeNode>(), node => node.Text == "Connections & trust");
            Assert.Contains(connections.Nodes.Cast<TreeNode>(), node => node.Text == "Storage");
            Assert.Contains(connections.Nodes.Cast<TreeNode>(), node => node.Text == "Clients");
            Assert.DoesNotContain(navigation.Nodes.Cast<TreeNode>(), node => node.Text == "General");
            Assert.Empty(navigation.Nodes.Cast<TreeNode>()
                .Where(node => node.Text == "Transfers & sync")
                .SelectMany(node => node.Nodes.Cast<TreeNode>()));
            Assert.DoesNotContain(navigation.Nodes.Cast<TreeNode>(), node => node.Text.Contains("About", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void EveryCatalogProviderHasItsOwnSettingsPageUnderTheCorrectType()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            using var settings = new SettingsForm();
            var navigation = GetField<TreeView>(settings, "_categories");
            var pages = GetField<Dictionary<string, Control>>(settings, "_pages");
            var connections = Assert.Single(navigation.Nodes.Cast<TreeNode>(), node => node.Text == "Connections & trust");

            foreach (var type in new[] { ConnectionProfileType.Storage, ConnectionProfileType.Client })
            {
                var typeLabel = type == ConnectionProfileType.Storage ? "Storage" : "Clients";
                var typeNode = Assert.Single(connections.Nodes.Cast<TreeNode>(), node => node.Text == typeLabel);
                var expected = ConnectionProviderCatalog.All.Where(provider => provider.Type == type).ToArray();
                Assert.Equal(expected.Select(provider => provider.DisplayName), typeNode.Nodes.Cast<TreeNode>().Select(node => node.Text));
                // Storage and Clients are captions over their providers, not destinations: the
                // page that used to restate each provider's summary is gone.
                Assert.DoesNotContain($"ConnectionType:{type}", pages.Keys);
                foreach (var provider in expected)
                {
                    var page = Assert.IsType<SettingsPagePanel>(pages[$"Provider:{provider.Kind}"]);
                    Assert.Contains(
                        page.Controls.Cast<Control>().SelectMany(DescendantsAndSelf),
                        control => control.Name == $"ProviderSettings:{provider.Kind}");
                    Assert.Contains(
                        page.Controls.OfType<Button>(),
                        button => button.AccessibleName == $"Configure {provider.DisplayName} connection");
                }
            }
        });
    }

    [Fact]
    public void Apply_persists_dark_appearance()
    {
        var root = Path.Combine(Path.GetTempPath(), $"storagehub-appearance-{Guid.NewGuid():N}");
        try
        {
            var store = new DesktopConfigStore(root);
            SyncRunReviewControlTests.RunOnSta(() =>
            {
                using var settings = new SettingsForm(store, saved: null);
                var appearance = GetField<StorageHubChoiceField>(settings, "_appearance");
                appearance.SelectedItem = DesktopAppearance.Dark;
                Assert.Equal(DesktopAppearance.Dark, DesktopAppearanceService.EffectiveAppearance);
                Assert.True(InvokeTrySave(settings));
                Assert.Equal(DesktopAppearance.Dark, DesktopAppearanceService.EffectiveAppearance);
            });
            Assert.Equal(DesktopAppearance.Dark, store.Load().Appearance);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            SyncRunReviewControlTests.RunOnSta(
                () => DesktopAppearanceService.SetAppearance(DesktopAppearance.Light));
        }
    }

    [Fact]
    public void Appearance_supports_system_preview_and_cancel_rolls_back_to_last_applied_choice()
    {
        var root = Path.Combine(Path.GetTempPath(), $"storagehub-system-appearance-{Guid.NewGuid():N}");
        try
        {
            var store = new DesktopConfigStore(root);
            store.Save(DesktopUpdatePreferences.Defaults with { Appearance = DesktopAppearance.Light });
            SyncRunReviewControlTests.RunOnSta(() =>
            {
                DesktopAppearanceService.SetSystemDarkModeReaderForTests(() => true);
                DesktopAppearanceService.SetAppearance(DesktopAppearance.Light);
                using var settings = new SettingsForm(store, saved: null);
                _ = settings.Handle;
                var appearance = GetField<StorageHubChoiceField>(settings, "_appearance");
                Assert.Equal(3, appearance.Items.Count);

                appearance.SelectedItem = DesktopAppearance.System;
                Assert.Equal(DesktopAppearance.System, DesktopAppearanceService.Appearance);
                Assert.Equal(DesktopAppearance.Dark, DesktopAppearanceService.EffectiveAppearance);

                settings.DialogResult = DialogResult.Cancel;
                settings.Close();
                Assert.Equal(DesktopAppearance.Light, DesktopAppearanceService.Appearance);
            });
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            SyncRunReviewControlTests.RunOnSta(() =>
            {
                DesktopAppearanceService.SetSystemDarkModeReaderForTests(null);
                DesktopAppearanceService.SetAppearance(DesktopAppearance.System);
            });
        }
    }

    [Fact]
    public void Explicit_appearance_takes_precedence_over_system_changes()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            var systemIsDark = false;
            DesktopAppearanceService.SetSystemDarkModeReaderForTests(() => systemIsDark);
            DesktopAppearanceService.SetAppearance(DesktopAppearance.Dark);
            systemIsDark = false;
            DesktopAppearanceService.RefreshSystemAppearance();
            Assert.Equal(DesktopAppearance.Dark, DesktopAppearanceService.EffectiveAppearance);
            DesktopAppearanceService.SetSystemDarkModeReaderForTests(null);
            DesktopAppearanceService.SetAppearance(DesktopAppearance.System);
        });
    }

    [Fact]
    public void ApplyPersistsConnectionAndUpdateChoicesTogether()
    {
        var root = Path.Combine(Path.GetTempPath(), $"storagehub-settings-form-{Guid.NewGuid():N}");
        try
        {
            var store = new DesktopConfigStore(root);
            SyncRunReviewControlTests.RunOnSta(() =>
            {
                using var settings = new SettingsForm(store, saved: null);
                GetField<StorageHubToggle>(settings, "_checkAutomatically").Checked = false;
                GetField<StorageHubNumberField>(settings, "_maximumTransferConcurrency").Value = 9;
                GetField<StorageHubNumberField>(settings, "_perConnectionConcurrency").Value = 3;
                GetField<StorageHubNumberField>(settings, "_maximumSyncConcurrency").Value = 4;
                GetField<StorageHubToggle>(settings, "_warnBeforeUnsafeExternalEdit").Checked = false;
                var discovery = GetField<StorageHubChoiceField>(settings, "_sshDiscovery");
                discovery.SelectedIndex = discovery.Items
                    .Cast<object>()
                    .Select((choice, index) => (choice, index))
                    .Single(item => item.choice.ToString()!.Contains(
                        "automatically",
                        StringComparison.OrdinalIgnoreCase))
                    .index;

                Assert.True(InvokeTrySave(settings));
            });

            var saved = store.Load();
            Assert.False(saved.CheckAutomatically);
            Assert.Equal(SshHostKeyDiscoveryMode.Automatic, saved.SshHostKeyDiscovery);
            Assert.True(saved.AdaptiveConcurrency);
            Assert.Equal(9, saved.MaximumTransferConcurrency);
            Assert.Equal(3, saved.PerConnectionConcurrency);
            Assert.Equal(4, saved.MaximumSyncConcurrency);
            Assert.False(saved.WarnBeforeUnsafeExternalEdit);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void ProviderPagesPersistGeneralDefaultsAndNewProfilesConsumeThem()
    {
        var root = Path.Combine(Path.GetTempPath(), $"storagehub-provider-defaults-{Guid.NewGuid():N}");
        var sshKeyReference = "shs_" + new string('A', 43);
        try
        {
            var store = new DesktopConfigStore(root);
            SyncRunReviewControlTests.RunOnSta(() =>
            {
                using var settings = new SettingsForm(store, saved: null);
                var controls = GetField<Dictionary<string, Control>>(settings, "_connectionDefaultControls");
                Assert.DoesNotContain(controls.Keys, key =>
                    key.Contains("profileName", StringComparison.Ordinal) ||
                    key.Contains("username", StringComparison.Ordinal) ||
                    key.Contains("password", StringComparison.Ordinal) ||
                    key.Contains("fingerprint", StringComparison.OrdinalIgnoreCase));
                Assert.IsType<StorageHubNumberField>(controls["Ftp.port"]).Value = 2121;
                Assert.IsType<StorageHubTextField>(controls["Ftp.initialPath"]).Text = "/incoming";
                Assert.IsType<StorageHubNumberField>(controls["Ftp.connectTimeoutSeconds"]).Value = 90;
                Assert.False(Assert.IsType<StorageHubNumberField>(controls["Ftp.operationTimeoutSeconds"]).Enabled);
                Assert.False(Assert.IsType<StorageHubNumberField>(controls["Ftp.maximumRetryAttempts"]).Enabled);
                // The picker registers the field that holds the reference, not the panel around
                // it: the buttons beside it are the row's accessory now.
                Assert.IsType<StorageHubTextField>(controls["Ssh.privateKeyReference"]).Text = sshKeyReference;

                Assert.True(InvokeTrySave(settings));
            });

            var saved = store.Load();
            var ftp = ConnectionDefaultSettings.Get(StorageProviderKind.Ftp, saved.ConnectionDefaults);
            Assert.Equal("2121", ftp.FieldValues["port"]);
            Assert.Equal("/incoming", ftp.FieldValues["initialPath"]);
            Assert.Equal(90, ftp.ConnectTimeoutSeconds);
            Assert.Equal(90, ftp.OperationTimeoutSeconds);
            Assert.Equal(0, ftp.MaximumRetryAttempts);
            var ssh = ConnectionDefaultSettings.Get(StorageProviderKind.Ssh, saved.ConnectionDefaults);
            Assert.Equal(sshKeyReference, ssh.FieldValues["privateKeyReference"]);
            SyncRunReviewControlTests.RunOnSta(() =>
            {
                using var manager = new ConnectionManagerForm(
                    initialProvider: StorageProviderKind.Ftp,
                    connectionDefaults: saved.ConnectionDefaults);
                var fields = GetField<Dictionary<string, Control>>(manager, "_editorFields");
                Assert.Equal(2121, Assert.IsType<StorageHubNumberField>(fields["port"]).Value);
                Assert.Equal("/incoming", Assert.IsType<StorageHubTextField>(fields["initialPath"]).Text);
                Assert.Empty(Assert.IsType<StorageHubTextField>(fields["host"]).Text);
                Assert.Empty(Assert.IsType<StorageHubTextField>(fields["username"]).Text);
            });
            SyncRunReviewControlTests.RunOnSta(() =>
            {
                using var manager = new ConnectionManagerForm(
                    initialProvider: StorageProviderKind.Ssh,
                    connectionDefaults: saved.ConnectionDefaults);
                var fields = GetField<Dictionary<string, Control>>(manager, "_editorFields");
                Assert.Equal(
                    sshKeyReference,
                    Assert.IsType<TableLayoutPanel>(fields["privateKeyReference"])
                        .Controls.OfType<StorageHubTextField>().Single().Text);
                Assert.Empty(
                    Assert.IsType<TableLayoutPanel>(fields["privateKeyPassphraseReference"])
                        .Controls.OfType<StorageHubTextField>().Single().Text);
            });

            var localDefaults = new ConnectionProviderDefaults(
                ConnectTimeoutSeconds: 12,
                OperationTimeoutSeconds: 45,
                MaximumRetryAttempts: 2,
                FieldValues: new Dictionary<string, string>());
            var draft = ConnectionEditorDraftFactory.Build(
                StorageProviderKind.Local,
                new Dictionary<string, string>
                {
                    ["profileName"] = "Local test",
                    ["rootPath"] = Path.GetTempPath()
                },
                localDefaults);
            Assert.Equal(12, draft.OperationalOptions.ConnectTimeoutSeconds);
            Assert.Equal(45, draft.OperationalOptions.OperationTimeoutSeconds);
            Assert.Equal(2, draft.OperationalOptions.MaximumRetryAttempts);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Every_settings_page_fits_at_supported_window_sizes()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            using var settings = new SettingsForm();
            Assert.False(settings.MinimumSize.IsEmpty);

            var supportedSizes = new[]
            {
                new Size(Math.Min(settings.MinimumSize.Width, 1022), settings.MinimumSize.Height),
                new Size(1120, 780)
            };
            settings.MinimumSize = Size.Empty;
            settings.Size = supportedSizes[0];
            settings.Show();
            var navigation = GetField<TreeView>(settings, "_categories");
            var pages = GetField<Dictionary<string, Control>>(settings, "_pages");
            var screenshotDirectory = Environment.GetEnvironmentVariable("STORAGEHUB_SETTINGS_SCREENSHOT_DIR");
            if (!string.IsNullOrWhiteSpace(screenshotDirectory))
            {
                Directory.CreateDirectory(screenshotDirectory);
            }

            foreach (var size in supportedSizes)
            {
                settings.Size = size;
                System.Windows.Forms.Application.DoEvents();
                foreach (var button in settings.Controls.Cast<Control>()
                             .SelectMany(DescendantsAndSelf)
                             .OfType<Button>()
                             .Where(button => button.Text is "OK" or "Cancel" or "Apply"))
                {
                    var bounds = settings.RectangleToClient(
                        button.Parent!.RectangleToScreen(button.Bounds));
                    Assert.True(
                        settings.ClientRectangle.Contains(bounds),
                        $"The {button.Text} button was clipped at {size}. Client={settings.ClientRectangle}; Button={bounds}.");
                }

                var index = 0;
                foreach (var node in DescendantNodes(navigation.Nodes))
                {
                    if (!pages.ContainsKey(node.Name))
                    {
                        continue;
                    }

                    navigation.SelectedNode = node;
                    System.Windows.Forms.Application.DoEvents();
                    var page = Assert.IsType<SettingsPagePanel>(pages[node.Name]);
                    var children = string.Join(", ", page.Controls.Cast<Control>()
                        .Select(control => $"{control.Name}:{control.Bounds}"));
                    Assert.False(
                        page.HorizontalScroll.Visible,
                        $"{node.Text} displayed a horizontal scrollbar at {size}. Client={page.ClientSize}; Display={page.DisplayRectangle}; Children={children}");
                    Assert.All(
                        page.Controls.Cast<Control>().Where(control => control is TableLayoutPanel or FlowLayoutPanel),
                        control => Assert.True(
                            page.ClientSize.Width - control.Width <= SystemInformation.VerticalScrollBarWidth + 2,
                            $"{node.Text} content did not fill the page. Page={page.ClientSize.Width}; Content={control.Width}."));

                    if (!string.IsNullOrWhiteSpace(screenshotDirectory) && size.Width == 1120)
                    {
                        using var bitmap = new Bitmap(settings.ClientSize.Width, settings.ClientSize.Height);
                        settings.DrawToBitmap(bitmap, settings.ClientRectangle);
                        var safeName = string.Concat(node.Text.Select(character =>
                            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
                        bitmap.Save(Path.Combine(screenshotDirectory, $"{index++:00}-{safeName}.png"));
                    }
                }
            }

            var workspaceLayout = GetField<StorageHubChoiceField>(settings, "_defaultWorkspaceLayout");
            Assert.Equal("Top and bottom", workspaceLayout.GetItemText(WorkspaceLayout.TopAndBottom));
        });
    }

    [Fact]
    public void StorageProviderPagesExposeBasicAndEnforceableAdvancedDefaults()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            using var settings = new SettingsForm();
            var pages = GetField<Dictionary<string, Control>>(settings, "_pages");
            var controls = GetField<Dictionary<string, Control>>(settings, "_connectionDefaultControls");

            foreach (var provider in ConnectionProviderCatalog.All.Where(
                provider => provider.Type == ConnectionProfileType.Storage))
            {
                var page = pages[$"Provider:{provider.Kind}"];
                var text = page.Controls.Cast<Control>()
                    .SelectMany(DescendantsAndSelf)
                    .Select(control => control.Text)
                    .ToArray();
                Assert.Contains("Basic defaults", text);
                Assert.Contains("Advanced connection behavior", text);
                Assert.Contains(ConnectionDefaultSettings.Key(
                    provider.Kind,
                    ConnectionDefaultSettings.ConnectTimeoutKey), controls.Keys);
                Assert.Contains(ConnectionDefaultSettings.Key(
                    provider.Kind,
                    ConnectionDefaultSettings.OperationTimeoutKey), controls.Keys);
                Assert.Contains(ConnectionDefaultSettings.Key(
                    provider.Kind,
                    ConnectionDefaultSettings.RetryAttemptsKey), controls.Keys);
            }

            Assert.Contains("S3.prefix", controls.Keys);
            Assert.Contains("S3.endpoint", controls.Keys);
            Assert.Contains("S3.region", controls.Keys);
            Assert.Contains("Ftps.tlsMode", controls.Keys);
            Assert.Contains("Ftps.trustMode", controls.Keys);
            Assert.Contains("Sftp.authenticationMode", controls.Keys);
        });
    }

    [Fact]
    public void SshProviderPagePersistsTerminalAndStartupShellPreferences()
    {
        var root = Path.Combine(Path.GetTempPath(), $"storagehub-ssh-settings-{Guid.NewGuid():N}");
        try
        {
            var store = new DesktopConfigStore(root);
            SyncRunReviewControlTests.RunOnSta(() =>
            {
                using var settings = new SettingsForm(store, saved: null);
                var pages = GetField<Dictionary<string, Control>>(settings, "_pages");
                var sshPage = pages["Provider:Ssh"];
                Assert.Contains(
                    sshPage.Controls.Cast<Control>().SelectMany(DescendantsAndSelf),
                    control => control.Name == "SshTerminalSettings");

                GetField<StorageHubChoiceField>(settings, "_sshTerminalName").Text = "screen-256color";
                GetField<StorageHubTextField>(settings, "_sshStartupCommand").Text = "bash -l";
                GetField<StorageHubNumberField>(settings, "_sshKeepAliveSeconds").Value = 75;
                GetField<StorageHubChoiceField>(settings, "_sshFontFamily").Text = "Consolas";
                GetField<StorageHubNumberField>(settings, "_sshFontSize").Value = 12.5M;
                GetField<StorageHubNumberField>(settings, "_sshScrollbackLines").Value = 6_000;
                GetField<StorageHubNumberField>(settings, "_sshRefreshInterval").Value = 100;
                GetField<StorageHubToggle>(settings, "_sshRenderBoldText").Checked = false;

                Assert.True(InvokeTrySave(settings));
            });

            var saved = Assert.IsType<SshTerminalPreferences>(store.Load().SshTerminal);
            Assert.Equal("screen-256color", saved.TerminalName);
            Assert.Equal("bash -l", saved.StartupCommand);
            Assert.Equal(75, saved.KeepAliveSeconds);
            Assert.Equal("Consolas", saved.FontFamily);
            Assert.Equal(12.5F, saved.FontSize);
            Assert.Equal(6_000, saved.ScrollbackLines);
            Assert.Equal(100, saved.RefreshIntervalMilliseconds);
            Assert.False(saved.RenderBoldText);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static bool InvokeTrySave(SettingsForm settings)
    {
        var method = typeof(SettingsForm).GetMethod("TrySave", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsType<bool>(method.Invoke(settings, null));
    }

    private static T GetField<T>(object instance, string name)
        where T : class
    {
        var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsType<T>(field.GetValue(instance));
    }

    /// <summary>
    /// An ampersand in settings text is an ampersand, not a keyboard shortcut.
    /// </summary>
    /// <remarks>
    /// A stock Label reads "&" as the marker for an access key and paints neither it nor the
    /// letter after it, so the "Transfers &amp; sync" page was headed "Transfers  sync" -- an
    /// ampersand swallowed, two spaces left behind, and no underline to show for it. The rows and
    /// captions draw their own text with NoPrefix; the page heading and its summary are Labels,
    /// and had to be told.
    /// </remarks>
    [Fact]
    public void SettingsTextNeverSwallowsAnAmpersand()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            using var settings = new SettingsForm();
            settings.CreateControl();

            var offenders = DescendantsAndSelf(settings)
                .OfType<Label>()
                .Where(label => label.Text.Contains('&', StringComparison.Ordinal) && label.UseMnemonic)
                .Select(label => label.Text)
                .ToList();

            Assert.True(
                offenders.Count == 0,
                "These settings labels would paint their ampersand as an access key: " +
                string.Join(", ", offenders));
        });
    }

    private static IEnumerable<Control> DescendantsAndSelf(Control root)
    {
        yield return root;
        foreach (Control child in root.Controls)
        {
            foreach (var descendant in DescendantsAndSelf(child))
            {
                yield return descendant;
            }
        }
    }

    private static IEnumerable<TreeNode> DescendantNodes(TreeNodeCollection nodes)
    {
        foreach (TreeNode node in nodes)
        {
            yield return node;
            foreach (var descendant in DescendantNodes(node.Nodes))
            {
                yield return descendant;
            }
        }
    }
}
