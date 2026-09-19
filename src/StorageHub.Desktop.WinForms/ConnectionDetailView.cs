using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>Which editor tab a caller wants opened.</summary>
public enum ConnectionEditorTab
{
    General = 0,
    Authentication = 1,
    Trust = 2
}

/// <summary>A request to open the connection editor, on a saved connection or on a new one.</summary>
internal sealed record ConnectionEditRequest(Guid? ConnectionId, ConnectionEditorTab Tab);

/// <summary>
/// The foot of the connections panel: what the selected connection is, and what can be done about it.
///
/// Facts are grouped key/value rows — Server, Authentication, Security, Transfer, Organisation,
/// Status — because a saved connection is mostly a pile of settings, and a flat list of them reads
/// as noise. Only rows that carry a value are drawn, so a Local profile does not show five empty
/// S3 fields.
///
/// It renders in two passes. The listing carries the name, folder, tags and health, so those appear
/// the instant a row is clicked; the host, port and authentication method live only on the full
/// profile, which is fetched separately and folded in when it arrives. Blocking selection on that
/// fetch would put an IPC round trip on every arrow key.
/// </summary>
internal sealed class ConnectionDetailView : Panel
{
    private const int KeyWidth = 104;

    private readonly Label _empty;
    private readonly Panel _body;
    private readonly Label _name;
    private readonly Panel _badge;
    private readonly Panel _scroll;
    private readonly FlowLayoutPanel _facts;
    private readonly StorageHubButton _attention;
    private readonly StorageHubButton _open;
    private readonly StorageHubButton _test;
    private readonly ToolTip _valueTips = new();
    private readonly Font _sectionFont;
    private readonly HashSet<string> _collapsedSections = new(StringComparer.Ordinal);
    private readonly Label _descriptionTitle;
    private readonly Label _descriptionBody;
    private readonly Panel _description;
    private string? _currentSection;
    private int _rowIndex;

    private ConnectionSummary? _connection;
    private ConnectionProfileDocument? _profile;
    private string? _pendingStatus;
    private ConnectionEditorTab _attentionTab = ConnectionEditorTab.General;

    internal ConnectionDetailView()
    {
        Padding = this.LogicalToDeviceUnits(new Padding(12, 10, 12, 10));
        BackColor = StorageHubTheme.SurfaceMuted;
        AccessibleName = Ui.Connections.DetailViewTitle;

        _empty = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = StorageHubTheme.TextMuted,
            Text = Ui.Connections.DetailEmpty
        };

        _badge = new Panel { Dock = DockStyle.Left, Width = LogicalToDeviceUnits(32), BackColor = StorageHubTheme.SurfaceMuted };
        _badge.Paint += PaintBadge;

        _name = new Label
        {
            Dock = DockStyle.Fill,
            Padding = this.LogicalToDeviceUnits(new Padding(8, 0, 0, 0)),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Font = StorageHubTheme.CreateSectionFont(),
            ForeColor = StorageHubTheme.Text
        };

        var title = new Panel { Dock = DockStyle.Top, Height = this.TextBoxHeight(11) };
        title.Controls.Add(_name);
        title.Controls.Add(_badge);

        _facts = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Location = Point.Empty,
            Margin = Padding.Empty,
            Padding = this.LogicalToDeviceUnits(new Padding(0, 4, 0, 4))
        };
        _sectionFont = new Font(Font, FontStyle.Bold);

        // The pane a property grid keeps at the bottom: it explains whatever row the pointer or
        // the keyboard is on, so the grid itself stays terse.
        // Reuses the section font rather than building a second identical bold one. Its 18px box
        // was already a pixel short of the 20px line it holds at 125%, so the title clipped.
        _descriptionTitle = new Label
        {
            Dock = DockStyle.Top,
            Height = this.TextBoxHeight(_sectionFont, 1),
            Font = _sectionFont,
            ForeColor = StorageHubTheme.Text,
            TextAlign = ContentAlignment.MiddleLeft
        };
        _descriptionBody = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = StorageHubTheme.TextMuted,
            TextAlign = ContentAlignment.TopLeft,
            AutoEllipsis = true
        };
        _description = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = this.TextBoxHeight(26),
            Padding = this.LogicalToDeviceUnits(new Padding(8, 5, 8, 5)),
            BackColor = StorageHubTheme.SurfaceMuted,
            Visible = false
        };
        _description.Controls.Add(_descriptionBody);
        _description.Controls.Add(_descriptionTitle);
        _description.Paint += (sender, args) =>
        {
            if (sender is Control painted)
            {
                using var rule = new Pen(StorageHubTheme.Border);
                args.Graphics.DrawLine(rule, 0, 0, painted.Width, 0);
            }
        };

        _scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = StorageHubTheme.Surface };
        _scroll.ClientSizeChanged += (_, _) => ResizeRows();
        _scroll.Controls.Add(_facts);

        _attention = new StorageHubButton
        {
            Dock = DockStyle.Bottom,
            Height = this.TextBoxHeight(6),
            Visible = false,
            Margin = this.LogicalToDeviceUnits(new Padding(0, 4, 0, 4))
        };
        _attention.Variant = StorageHubButtonVariant.Secondary;
        _attention.ForeColor = StorageHubTheme.Warning;
        _attention.Click += (_, _) => EditRequested?.Invoke(this, _attentionTab);

        _open = CreateAction(Ui.Connections.DetailOpen, UiGlyph.Connect, (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty));
        _test = CreateAction(Ui.Connections.DetailTest, UiGlyph.Test, (_, _) => TestRequested?.Invoke(this, EventArgs.Empty));
        var edit = CreateAction(Ui.Connections.DetailEdit, UiGlyph.Rename, (_, _) => EditRequested?.Invoke(this, ConnectionEditorTab.General));
        // The Danger variant tints its own glyph, so the tone is left to it.
        var delete = CreateAction(
            Ui.Connections.DetailDelete,
            UiGlyph.Delete,
            (_, _) => DeleteRequested?.Invoke(this, EventArgs.Empty),
            StorageHubButtonVariant.Danger);

        // Wraps rather than clips: the panel resizes down to a narrow column, and a fixed row of
        // buttons silently loses the last one off the right edge.
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = Padding.Empty,
            Padding = this.LogicalToDeviceUnits(new Padding(0, 4, 0, 0))
        };
        actions.Controls.AddRange([_open, _test, edit, delete]);

        _body = new Panel { Dock = DockStyle.Fill, Visible = false, BackColor = StorageHubTheme.SurfaceMuted };
        _body.Controls.Add(_scroll);
        _body.Controls.Add(_description);
        _body.Controls.Add(_attention);
        _body.Controls.Add(actions);
        _body.Controls.Add(title);

        Controls.Add(_body);
        Controls.Add(_empty);
    }

    internal event EventHandler? OpenRequested;

    internal event EventHandler? TestRequested;

    internal event EventHandler<ConnectionEditorTab>? EditRequested;

    internal event EventHandler? DeleteRequested;

    /// <summary>First pass: everything the listing already knows, drawn immediately.</summary>
    internal void Show(ConnectionSummary? connection)
    {
        _connection = connection;
        _profile = null;
        _pendingStatus = null;
        _empty.Visible = connection is null;
        _body.Visible = connection is not null;
        if (connection is null)
        {
            return;
        }

        var descriptor = ConnectionProviderCatalog.Get(ConnectionCardFactory.MapProvider(connection.Provider));
        // A star marks a favourite rather than a button offering to make one: toggling belongs to
        // the row's own right-click menu, where every other action on a connection already lives.
        _name.Text = connection.IsFavorite
            ? "★  " + connection.DisplayName
            : connection.DisplayName;
        _name.ForeColor = StorageHubTheme.Text;
        _name.AccessibleName = Ui.Format(Ui.Connections.DetailHeaderAccessibleFormat, connection.DisplayName, descriptor.DisplayName);
        _name.AccessibleDescription = connection.IsFavorite
            ? Ui.Format(Ui.Connections.DetailIsFavoriteFormat, connection.DisplayName)
            : null;
        _open.Enabled = connection.IsEnabled;
        _test.Enabled = connection.IsEnabled;
        _badge.Invalidate();
        ApplyAttention(connection);
        Rebuild();
    }

    /// <summary>Second pass: the endpoint and authentication detail, once the profile has loaded.</summary>
    internal void ShowProfile(Guid connectionId, ConnectionProfileDocument? profile)
    {
        // The selection may have moved on while this was in flight.
        if (_connection?.ConnectionId != connectionId)
        {
            return;
        }

        _profile = profile;
        Rebuild();
    }

    /// <summary>Shown while a test is in flight, so a slow agent does not look like a dead button.</summary>
    internal void ShowTesting()
    {
        _pendingStatus = Ui.Connections.DetailTesting;
        _test.Enabled = false;
        Rebuild();
    }

    private void Rebuild()
    {
        if (_connection is not { } connection)
        {
            return;
        }

        _scroll.SuspendLayout();
        _facts.SuspendLayout();
        try
        {
            foreach (var control in _facts.Controls.Cast<Control>().ToArray())
            {
                control.Dispose();
            }

            _facts.Controls.Clear();
            _rowIndex = 0;

            var descriptor = ConnectionProviderCatalog.Get(ConnectionCardFactory.MapProvider(connection.Provider));
            var endpoint = _profile?.Draft.Endpoint;
            var auth = _profile?.Draft.Authentication;
            var options = _profile?.Draft.OperationalOptions;

            AddSection(Ui.Connections.SectionServer);
            AddFact(Ui.Connections.FieldProvider, descriptor.DisplayName);
            if (endpoint is null)
            {
                AddFact(Ui.Connections.FieldAddress, Ui.Connections.DetailLoading, muted: true);
            }
            else
            {
                AddFact(Ui.Connections.FieldHost, endpoint.Host);
                AddFact(Ui.Connections.FieldPort, endpoint.Port?.ToString(System.Globalization.CultureInfo.CurrentCulture));
                AddFact(Ui.Connections.FieldBucket, endpoint.Bucket);
                AddFact(Ui.Connections.FieldRegion, endpoint.Region);
                AddFact(Ui.Connections.FieldService, endpoint.ServiceEndpoint);
                AddFact(Ui.Connections.FieldPathStyle, endpoint.ForcePathStyle ? Ui.Connections.DetailForced : null);
                AddFact(Ui.Connections.FieldPath, endpoint.RootPath);
            }

            AddSection(Ui.Connections.SectionAuthentication);
            if (auth is null)
            {
                AddFact(Ui.Connections.FieldMethod, Ui.Connections.DetailLoading, muted: true);
            }
            else
            {
                AddFact(Ui.Connections.FieldMethod, DescribeAuthentication(auth.Kind));
                AddFact(Ui.Connections.FieldUsername, auth.Username);
                AddFact(
                    "Key format",
                    auth.Kind is ConnectionAuthenticationKind.SftpPrivateKey
                        or ConnectionAuthenticationKind.SshPrivateKeyPassword
                        ? auth.PrivateKeyFormat.ToString()
                        : null);

                // Presence, not the handle: the vault reference is noise here, and the fact worth
                // reading is simply whether the credential has been enrolled.
                AddFact(Ui.Connections.FieldPassword, Vaulted(auth.PasswordReference));
                AddFact(Ui.Connections.FieldAccessKey, Vaulted(auth.AccessKeyReference));
                AddFact(Ui.Connections.FieldSecretKey, Vaulted(auth.SecretKeyReference));
                AddFact(Ui.Connections.FieldSessionToken, Vaulted(auth.SessionTokenReference));
                AddFact(Ui.Connections.FieldPrivateKey, Vaulted(auth.PrivateKeyReference));
                AddFact(Ui.Connections.FieldKeyPassphrase, Vaulted(auth.PrivateKeyPassphraseReference));
            }

            if (endpoint is not null && DescribeSecurity(endpoint) is { Count: > 0 } security)
            {
                AddSection(Ui.Connections.SectionSecurity);
                foreach (var row in security)
                {
                    AddFact(row.Key, row.Value);
                }
            }

            if (options is not null)
            {
                AddSection(Ui.Connections.SectionTransfer);
                AddFact(Ui.Connections.FieldConnectTimeout, $"{options.ConnectTimeoutSeconds}s");
                AddFact(Ui.Connections.FieldOperationTimeout, $"{options.OperationTimeoutSeconds}s");
                AddFact(
                    "Retries",
                    options.MaximumRetryAttempts.ToString(System.Globalization.CultureInfo.CurrentCulture));
                AddFact(Ui.Connections.FieldProxy, options.ProxyEndpoint);
                AddFact(Ui.Connections.FieldUploadLimit, DescribeRate(options.UploadBytesPerSecond));
                AddFact(Ui.Connections.FieldDownloadLimit, DescribeRate(options.DownloadBytesPerSecond));
                AddFact(Ui.Connections.FieldEncoding, options.EncodingName);
            }

            AddSection(Ui.Connections.SectionOrganisation);
            AddFact(Ui.Connections.FieldFolder, string.IsNullOrWhiteSpace(connection.FolderPath) ? Ui.Connections.GroupUnsorted : connection.FolderPath);
            AddFact(Ui.Connections.FieldTags, connection.Tags.Length == 0 ? Ui.Connections.DetailNone : string.Join(" · ", connection.Tags));
            AddFact(Ui.Connections.FieldFavorite, connection.IsFavorite ? Ui.Connections.DetailYes : Ui.Connections.DetailNo);

            AddSection(Ui.Connections.SectionStatus);
            AddStatusFact(connection);
            if (connection.Health is { } health)
            {
                AddFact(
                    "Checked",
                    health.CheckedUtc.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture));
                AddFact(Ui.Connections.FieldRoundTrip, $"{health.ElapsedMilliseconds:N0} ms");
                AddFact(Ui.Connections.FieldDetail, health.Status);
            }

            ApplyCollapsedSections();
            ResizeRows();
        }
        finally
        {
            _facts.ResumeLayout(true);
            _scroll.ResumeLayout(true);
        }
    }

    /// <summary>
    /// A category bar, in the shape a property grid uses: a chevron, a heavier label, and a band
    /// that separates it from the rows beneath. Collapsing hides that category's rows only.
    /// </summary>
    private void AddSection(string title)
    {
        var header = new DetailCategoryHeader(title, _sectionFont)
        {
            Collapsed = _collapsedSections.Contains(title),
            Margin = new Padding(0, _facts.Controls.Count == 0 ? 0 : 6, 0, 0)
        };
        header.CollapsedChanged += (_, collapsed) =>
        {
            if (collapsed)
            {
                _ = _collapsedSections.Add(title);
            }
            else
            {
                _ = _collapsedSections.Remove(title);
            }

            ApplyCollapsedSections();
        };
        _facts.Controls.Add(header);
        _currentSection = title;
    }

    /// <summary>Hides the rows belonging to every collapsed category, leaving the bars in place.</summary>
    private void ApplyCollapsedSections()
    {
        _facts.SuspendLayout();
        try
        {
            var hidden = false;
            foreach (Control control in _facts.Controls)
            {
                if (control is DetailCategoryHeader header)
                {
                    hidden = header.Collapsed;
                    continue;
                }

                control.Visible = !hidden;
            }
        }
        finally
        {
            _facts.ResumeLayout(true);
        }

        ResizeRows();
    }

    private void AddFact(string key, string? value, bool muted = false)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var row = new DetailFactRow(key, value, muted, _rowIndex++)
        {
            Margin = Padding.Empty
        };
        row.Describe += (_, _) => ShowDescription(key, value);
        _valueTips.SetToolTip(row, value);
        _facts.Controls.Add(row);
    }

    private void AddStatusFact(ConnectionSummary connection)
    {
        if (_pendingStatus is { } pending)
        {
            AddFact(Ui.Connections.FieldState, pending, muted: true);
            return;
        }

        if (!connection.IsEnabled)
        {
            AddFact(Ui.Connections.FieldState, Ui.Connections.StateDisabled);
            return;
        }

        AddFact(Ui.Connections.FieldState, ConnectionCardFactory.DescribeHealth(connection.Health));
        if (_facts.Controls.Count > 0 &&
            _facts.Controls[^1] is Panel added &&
            added.Controls.OfType<Label>().FirstOrDefault(static label => label.Dock == DockStyle.Fill) is { } state)
        {
            state.ForeColor = connection.Health?.State switch
            {
                ConnectionHealthState.Healthy => StorageHubTheme.Success,
                ConnectionHealthState.NeedsAttention => StorageHubTheme.Warning,
                ConnectionHealthState.Unavailable => StorageHubTheme.Danger,
                _ => StorageHubTheme.TextMuted
            };
        }
    }

    private void ApplyAttention(ConnectionSummary connection)
    {
        // The two states a user can actually resolve get a route to the tab that resolves them.
        if (connection.IsEnabled && connection.Health is { RequiresCredentialAction: true })
        {
            _attentionTab = ConnectionEditorTab.Authentication;
            _attention.Text = Ui.Connections.DetailFixCredentials;
            _attention.Visible = true;
        }
        else if (connection.IsEnabled && connection.Health is { RequiresTrustAction: true })
        {
            _attentionTab = ConnectionEditorTab.Trust;
            _attention.Text = Ui.Connections.DetailReviewTrust;
            _attention.Visible = true;
        }
        else
        {
            _attention.Visible = false;
        }
    }

    /// <summary>
    /// The rows are docked panels inside a top-down flow, which does not stretch its children, so
    /// their width is set by hand whenever the scroll area changes.
    /// </summary>
    private bool _resizingRows;

    /// <summary>
    /// Puts a row's key and value in the description pane. The value is repeated there in full,
    /// because the grid ellipsizes anything too long for its column.
    /// </summary>
    private void ShowDescription(string key, string value)
    {
        _descriptionTitle.Text = key;
        _descriptionBody.Text = value;
        if (!_description.Visible)
        {
            _description.Visible = true;
        }
    }

    private void ResizeRows()
    {
        // Same shape as the sidebar: a row's width decides its height, the heights decide the
        // scrollbar, and the scrollbar decides the client width this started from. One guarded
        // pass with layout held, rather than a layout per row and a re-entry per scrollbar flip.
        if (_resizingRows)
        {
            return;
        }

        _resizingRows = true;
        try
        {
            // Bounded: a pass whose layout flipped the scrollbar is measured again, and a layout
            // that flips it back is accepted rather than chased.
            for (var pass = 0; pass < 3; pass++)
            {
                var measured = _scroll.ClientSize.Width;
                var width = Math.Max(
                    120,
                    measured - (_scroll.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0));
                _facts.SuspendLayout();
                try
                {
                    if (_facts.Width != width)
                    {
                        _facts.Width = width;
                    }

                    foreach (Control row in _facts.Controls)
                    {
                        if (row.Width != width)
                        {
                            row.Width = width;
                        }
                    }
                }
                finally
                {
                    _facts.ResumeLayout(true);
                }

                if (_scroll.ClientSize.Width == measured)
                {
                    break;
                }
            }
        }
        finally
        {
            _resizingRows = false;
        }
    }

    private static List<KeyValuePair<string, string>> DescribeSecurity(ConnectionEndpointDocument endpoint)
    {
        var rows = new List<KeyValuePair<string, string>>();
        void Add(string key, string value) => rows.Add(new KeyValuePair<string, string>(key, value));

        switch (endpoint.Provider)
        {
            case StorageConnectionProvider.Ftps:
                Add(Ui.Connections.FieldTls, DescribeTls(endpoint.TlsPolicy));
                Add(Ui.Connections.FieldFtpsMode, endpoint.FtpsTlsMode.ToString());
                if (endpoint.ClientCertificatePfxReference is not null)
                {
                    Add(Ui.Connections.FieldClientCert, Ui.Connections.DetailStoredInVault);
                }

                break;
            case StorageConnectionProvider.Sftp:
            case StorageConnectionProvider.Ssh:
                Add(
                    "Host key",
                    endpoint.SshHostKeyPolicy == ConnectionSshHostKeyPolicy.Pinned
                        ? Ui.Connections.DetailPinned
                        : Ui.Connections.DetailTrustOnFirstUse);
                break;
            case StorageConnectionProvider.S3:
                Add(Ui.Connections.FieldTls, DescribeTls(endpoint.TlsPolicy));
                break;
        }

        if (endpoint.AllowInsecureTransport)
        {
            Add(Ui.Connections.FieldTransport, Ui.Connections.TransportUnencrypted);
        }

        return rows;
    }

    private static string DescribeTls(ConnectionTlsCertificatePolicy policy) => policy switch
    {
        ConnectionTlsCertificatePolicy.SystemTrust => Ui.Connections.TlsSystemTrust,
        ConnectionTlsCertificatePolicy.Pinned => Ui.Connections.TlsPinnedCertificate,
        ConnectionTlsCertificatePolicy.TrustOnFirstUse => Ui.Connections.DetailTrustOnFirstUse,
        _ => Ui.Connections.TlsUnspecified
    };

    private static string DescribeAuthentication(ConnectionAuthenticationKind kind) => kind switch
    {
        ConnectionAuthenticationKind.None => Ui.Connections.AuthAnonymous,
        ConnectionAuthenticationKind.S3DefaultCredentialChain => Ui.Connections.AuthDefaultCredentialChain,
        ConnectionAuthenticationKind.CredentialReference => Ui.Connections.AuthStoredCredential,
        ConnectionAuthenticationKind.UsernamePassword => Ui.Connections.AuthUsernamePassword,
        ConnectionAuthenticationKind.S3AccessKey => Ui.Connections.AuthAccessKeyAndSecret,
        ConnectionAuthenticationKind.SftpPrivateKey => Ui.Connections.AuthPrivateKey,
        ConnectionAuthenticationKind.SshPrivateKeyPassword => Ui.Connections.AuthPrivateKeyAndPassword,
        _ => kind.ToString()
    };

    private static string? Vaulted(string? reference) =>
        string.IsNullOrWhiteSpace(reference) ? null : "Stored in vault";

    private static string? DescribeRate(long? bytesPerSecond) => bytesPerSecond switch
    {
        null or <= 0 => null,
        < 1024 => $"{bytesPerSecond} B/s",
        < 1024 * 1024 => $"{bytesPerSecond / 1024d:N1} KB/s",
        _ => $"{bytesPerSecond / (1024d * 1024d):N1} MB/s"
    };

    private void PaintBadge(object? sender, PaintEventArgs e)
    {
        if (_connection is null)
        {
            return;
        }

        var descriptor = ConnectionProviderCatalog.Get(ConnectionCardFactory.MapProvider(_connection.Provider));
        var accent = StorageHubTheme.ParseAccent(_connection.AccentColor ?? descriptor.AccentHex);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var bounds = new RectangleF(0, 3, 36, 28);
        using var path = UiShapes.RoundedRectangle(bounds, 7);
        using var fill = new SolidBrush(accent);
        e.Graphics.FillPath(fill, path);
        TextRenderer.DrawText(
            e.Graphics,
            descriptor.ShortName,
            Font,
            Rectangle.Round(bounds),
            StorageHubTheme.ContrastText(accent),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private StorageHubButton CreateAction(
        string text,
        UiGlyph glyph,
        EventHandler onClick,
        StorageHubButtonVariant variant = StorageHubButtonVariant.Secondary)
    {
        var button = new StorageHubButton
        {
            Text = text,
            Glyph = glyph,
            Variant = variant,
            // These four wrap in a narrow panel rather than losing the last one off the edge.
            Margin = this.LogicalToDeviceUnits(new Padding(0, 0, 4, 4)),
            AccessibleName = $"{text} connection"
        };
        button.Click += onClick;
        return button;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _valueTips.Dispose();
            _sectionFont.Dispose();
        }

        base.Dispose(disposing);
    }
}
