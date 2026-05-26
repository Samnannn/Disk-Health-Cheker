using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace DiskHealthLite;

internal static class AdminLauncher
{
    public static bool EnsureElevated()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        WindowsPrincipal principal = new(identity);
        if (principal.IsInRole(WindowsBuiltInRole.Administrator))
        {
            return true;
        }

        try
        {
            ProcessStartInfo startInfo = new(Application.ExecutablePath)
            {
                UseShellExecute = true,
                Verb = "runas"
            };
            Process.Start(startInfo);
        }
        catch
        {
            MessageBox.Show(
                "Disk Health Lite needs Administrator permission to read disk reliability counters.",
                "Disk Health Lite",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        return false;
    }
}

public partial class Form1 : Form
{
    private readonly ListView diskList = new();
    private readonly Label statusLabel = new();
    private readonly Label diskNameLabel = new();
    private readonly Label diskMetaLabel = new();
    private readonly Label diskSerialLabel = new();
    private readonly Label healthPercentLabel = new();
    private readonly Label healthNoteLabel = new();
    private readonly Panel healthTrack = new();
    private readonly Panel healthFill = new();
    private readonly TextBox detailsBox = new();
    private readonly Button refreshButton = new();
    private readonly System.Windows.Forms.Timer refreshTimer = new();
    private readonly Dictionary<string, MetricCard> cards = new();
    private const int RefreshIntervalSeconds = 15;
    private const string GitHubUrl = "https://github.com/Samnannn";
    private const string InstagramUrl = "https://www.instagram.com/_samnan__?igsh=ejAzbHI0YTQ2N3Nw";

    private static readonly string GitHubIconPath = Path.Combine(AppContext.BaseDirectory, "assets", "git.png");
    private static readonly string InstagramIconPath = Path.Combine(AppContext.BaseDirectory, "assets", "ig.png");

    private List<DiskInfo> disks = [];
    private int? selectedDiskId;
    private bool isRefreshing;

    public Form1()
    {
        InitializeComponent();
        BuildInterface();

        refreshButton.Click += async (_, _) => await RefreshDisksAsync();
        diskList.SelectedIndexChanged += (_, _) => SelectCurrentListItem();
        refreshTimer.Interval = RefreshIntervalSeconds * 1000;
        refreshTimer.Tick += async (_, _) => await RefreshDisksAsync();
        Shown += async (_, _) =>
        {
            refreshTimer.Start();
            await RefreshDisksAsync();
        };
    }

    private void BuildInterface()
    {
        Text = "Disk Health Lite";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1060, 680);
        Size = new Size(1180, 720);
        BackColor = Color.FromArgb(242, 245, 249);
        Font = new Font("Segoe UI", 10);

        // ── Root layout ───────────────────────────────────────────────
        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = BackColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));   // header
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // body
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));   // footer

        // ── Header ────────────────────────────────────────────────────
        Panel header = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(10, 17, 35),
            Padding = new Padding(28, 0, 20, 0)
        };

        // Title block (left)
        TableLayoutPanel titleStack = new()
        {
            Dock = DockStyle.Left,
            Width = 440,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent
        };
        titleStack.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        titleStack.RowStyles.Add(new RowStyle(SizeType.Percent, 42));

        Label titleLabel = new()
        {
            Text = "Disk Health Lite",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 14, FontStyle.Bold),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.BottomLeft,
            AutoEllipsis = true
        };
        Label subtitleLabel = new()
        {
            Text = "Health · Temperature · Power-on time · Lifetime writes",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 8, FontStyle.Regular),
            ForeColor = Color.FromArgb(148, 163, 184),
            TextAlign = ContentAlignment.TopLeft,
            AutoEllipsis = true
        };
        titleStack.Controls.Add(titleLabel, 0, 0);
        titleStack.Controls.Add(subtitleLabel, 0, 1);

        // Status label (centre-right)
        statusLabel.Dock = DockStyle.Fill;
        statusLabel.TextAlign = ContentAlignment.MiddleCenter;
        statusLabel.Font = new Font("Segoe UI", 8.5f);
        statusLabel.ForeColor = Color.FromArgb(148, 163, 184);
        statusLabel.Text = "Starting…";

        // Refresh button (right) — fixed size, vertically centred via a wrapper
        refreshButton.Text = "Refresh";
        refreshButton.FlatStyle = FlatStyle.Flat;
        refreshButton.BackColor = Color.FromArgb(37, 99, 235);
        refreshButton.ForeColor = Color.White;
        refreshButton.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        refreshButton.FlatAppearance.BorderSize = 0;
        refreshButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(59, 130, 246);
        refreshButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(29, 78, 216);
        refreshButton.Size = new Size(104, 34);
        refreshButton.Cursor = Cursors.Hand;

        Panel btnWrapper = new()
        {
            Dock = DockStyle.Right,
            Width = 124,
            BackColor = Color.Transparent
        };
        refreshButton.Location = new Point(10, 25);
        btnWrapper.Resize += (_, _) =>
        {
            refreshButton.Location = new Point(10, Math.Max(0, (btnWrapper.Height - refreshButton.Height) / 2));
        };
        btnWrapper.Controls.Add(refreshButton);

        // status sits between title and button
        Panel statusWrapper = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent
        };
        statusLabel.Dock = DockStyle.Fill;
        statusWrapper.Controls.Add(statusLabel);

        header.Controls.Add(btnWrapper);       // Right (added first so Fill works)
        header.Controls.Add(statusWrapper);    // Fill
        header.Controls.Add(titleStack);       // Left

        // ── Body ──────────────────────────────────────────────────────
        TableLayoutPanel body = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(18, 16, 18, 16),
            BackColor = BackColor
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 306));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // Left panel — disk list
        Panel leftOuter = new()
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 14, 0),
            BackColor = Color.White
        };
        leftOuter.Paint += PaintRoundedBorder;

        TableLayoutPanel leftPanel = new()
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(1),
            RowCount = 2,
            ColumnCount = 1,
            BackColor = Color.White,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        leftPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        leftPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        Label diskListTitle = new()
        {
            Text = "  Detected disks",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            ForeColor = Color.FromArgb(30, 41, 55),
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.FromArgb(248, 250, 252),
            BorderStyle = BorderStyle.None
        };
        leftPanel.Controls.Add(diskListTitle, 0, 0);

        diskList.Dock = DockStyle.Fill;
        diskList.Margin = new Padding(8, 4, 8, 8);
        diskList.View = View.Details;
        diskList.FullRowSelect = true;
        diskList.HideSelection = false;
        diskList.BorderStyle = BorderStyle.None;
        diskList.Font = new Font("Segoe UI", 9);
        diskList.Columns.Add("Disk", 198);
        diskList.Columns.Add("Health", 70);
        leftPanel.Controls.Add(diskList, 0, 1);
        leftOuter.Controls.Add(leftPanel);

        // Right / main panel
        Panel mainOuter = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White
        };
        mainOuter.Paint += PaintRoundedBorder;

        TableLayoutPanel mainPanel = new()
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(1),
            Padding = new Padding(28, 22, 28, 22),
            ColumnCount = 1,
            RowCount = 5,
            BackColor = Color.White,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 248));
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));

        // Disk header
        TableLayoutPanel diskHeader = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0)
        };
        diskHeader.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        diskHeader.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        diskHeader.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));

        diskNameLabel.Dock = DockStyle.Fill;
        diskNameLabel.Font = new Font("Segoe UI", 15, FontStyle.Bold);
        diskNameLabel.TextAlign = ContentAlignment.BottomLeft;
        diskNameLabel.ForeColor = Color.FromArgb(15, 23, 42);
        diskNameLabel.AutoEllipsis = true;

        diskMetaLabel.Dock = DockStyle.Fill;
        diskMetaLabel.Font = new Font("Segoe UI", 9);
        diskMetaLabel.ForeColor = Color.FromArgb(100, 116, 139);
        diskMetaLabel.TextAlign = ContentAlignment.TopLeft;
        diskMetaLabel.AutoEllipsis = true;

        diskSerialLabel.Dock = DockStyle.Fill;
        diskSerialLabel.Font = new Font("Segoe UI", 8);
        diskSerialLabel.ForeColor = Color.FromArgb(148, 163, 184);
        diskSerialLabel.TextAlign = ContentAlignment.TopLeft;
        diskSerialLabel.AutoEllipsis = true;
        diskHeader.Controls.Add(diskNameLabel, 0, 0);
        diskHeader.Controls.Add(diskMetaLabel, 0, 1);
        diskHeader.Controls.Add(diskSerialLabel, 0, 2);
        mainPanel.Controls.Add(diskHeader, 0, 0);

        // Health bar section
        TableLayoutPanel healthPanel = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            Margin = new Padding(0, 4, 0, 8)
        };
        healthPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        healthPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
        healthPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        healthPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        healthPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        Label healthLabel = new()
        {
            Text = "Health",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            ForeColor = Color.FromArgb(31, 41, 55),
            TextAlign = ContentAlignment.MiddleLeft
        };
        healthPanel.Controls.Add(healthLabel, 0, 0);

        healthPercentLabel.Dock = DockStyle.Fill;
        healthPercentLabel.Font = new Font("Segoe UI", 10, FontStyle.Bold);
        healthPercentLabel.TextAlign = ContentAlignment.MiddleRight;
        healthPercentLabel.ForeColor = Color.FromArgb(31, 41, 55);
        healthPanel.Controls.Add(healthPercentLabel, 1, 0);

        healthTrack.Dock = DockStyle.Fill;
        healthTrack.Margin = new Padding(0, 2, 88, 2);
        healthTrack.BackColor = Color.FromArgb(226, 232, 240);
        healthFill.Location = new Point(0, 0);
        healthFill.Height = 14;
        healthFill.Width = 0;
        healthTrack.Controls.Add(healthFill);
        healthTrack.Resize += (_, _) => SetHealthBar(disks.FirstOrDefault(d => d.Id == selectedDiskId)?.HealthPercent);
        healthPanel.Controls.Add(healthTrack, 0, 1);
        healthPanel.SetColumnSpan(healthTrack, 2);

        healthNoteLabel.Dock = DockStyle.Fill;
        healthNoteLabel.Margin = new Padding(0, 4, 0, 0);
        healthNoteLabel.Font = new Font("Segoe UI", 9);
        healthNoteLabel.ForeColor = Color.FromArgb(71, 85, 105);
        healthNoteLabel.AutoEllipsis = true;
        healthPanel.Controls.Add(healthNoteLabel, 0, 2);
        healthPanel.SetColumnSpan(healthNoteLabel, 2);
        mainPanel.Controls.Add(healthPanel, 0, 1);

        // Metric cards grid
        TableLayoutPanel metricGrid = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 2,
            Margin = new Padding(0, 4, 0, 8)
        };
        metricGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        metricGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        metricGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.334f));
        metricGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        metricGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        AddCard(metricGrid, "health", "Disk health", 0, 0);
        AddCard(metricGrid, "temp", "Temperature", 1, 0);
        AddCard(metricGrid, "hours", "Power-on time", 2, 0);
        AddCard(metricGrid, "writes", "Lifetime writes", 0, 1);
        AddCard(metricGrid, "wear", "Wear value", 1, 1);
        AddCard(metricGrid, "status", "Windows status", 2, 1);
        mainPanel.Controls.Add(metricGrid, 0, 2);

        detailsBox.Dock = DockStyle.Fill;
        detailsBox.Margin = new Padding(0, 6, 0, 0);
        detailsBox.Multiline = true;
        detailsBox.ReadOnly = true;
        detailsBox.BorderStyle = BorderStyle.None;
        detailsBox.Font = new Font("Segoe UI", 8.5f);
        detailsBox.BackColor = Color.FromArgb(248, 250, 252);
        detailsBox.ForeColor = Color.FromArgb(71, 85, 105);
        mainPanel.Controls.Add(detailsBox, 0, 4);

        mainOuter.Controls.Add(mainPanel);

        body.Controls.Add(leftOuter, 0, 0);
        body.Controls.Add(mainOuter, 1, 0);

        // ── Footer ────────────────────────────────────────────────────
        Panel footer = BuildFooter();

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(body, 0, 1);
        root.Controls.Add(footer, 0, 2);
        Controls.Add(root);
    }

    // Subtle rounded border painter for card panels
    private static void PaintRoundedBorder(object? sender, PaintEventArgs e)
    {
        if (sender is not Panel panel) return;
        using Pen pen = new(Color.FromArgb(220, 228, 240));
        Rectangle rect = new(0, 0, panel.Width - 1, panel.Height - 1);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.DrawRectangle(pen, rect);
    }

    private Panel BuildFooter()
    {
        Panel footer = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(10, 17, 35),
            Padding = new Padding(24, 0, 24, 0)
        };

        // Right side: icons + name badge
        FlowLayoutPanel links = new()
        {
            Dock = DockStyle.Right,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            BackColor = Color.Transparent
        };

        ToolTip toolTip = new();

        Control githubIcon = MakeFooterImageIcon(GitHubIconPath, GitHubUrl, "GH", "GitHub", toolTip, tintWhite: false);
        Control instagramIcon = MakeFooterImageIcon(InstagramIconPath, InstagramUrl, "IG", "Instagram", toolTip, tintWhite: false);
        Label samBadge = MakeHeaderBadge("Sam");

        links.Controls.Add(githubIcon);
        links.Controls.Add(instagramIcon);
        links.Controls.Add(samBadge);

        // Left brand label
        Label brand = new()
        {
            Text = "Disk Health Lite",
            Dock = DockStyle.Left,
            Width = 200,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            ForeColor = Color.FromArgb(148, 163, 184),
            TextAlign = ContentAlignment.MiddleLeft
        };

        footer.Controls.Add(links);
        footer.Controls.Add(brand);
        return footer;
    }

    /// <summary>
    /// Creates a footer icon control. Loads the image from disk; falls back to a
    /// styled text label if the file is missing.
    /// </summary>
    private static Control MakeFooterImageIcon(
        string imagePath, string url,
        string fallbackText, string tooltipText,
        ToolTip toolTip,
        bool tintWhite = false)
    {
        // Wrapper panel — 30 × 30, vertically centred in the 44 px footer
        Panel wrapper = new()
        {
            Size = new Size(30, 30),
            Margin = new Padding(0, 7, 10, 0),   // top margin centres it in 44 px
            BackColor = Color.Transparent,
            Cursor = Cursors.Hand
        };

        bool loaded = false;
        if (File.Exists(imagePath))
        {
            try
            {
                if (Path.GetExtension(imagePath).Equals(".svg", StringComparison.OrdinalIgnoreCase))
                {
                    WebBrowser browser = new()
                    {
                        Dock = DockStyle.Fill,
                        ScrollBarsEnabled = false,
                        IsWebBrowserContextMenuEnabled = false,
                        AllowWebBrowserDrop = false,
                        WebBrowserShortcutsEnabled = false,
                        Cursor = Cursors.Hand
                    };
                    string svg = File.ReadAllText(imagePath);
                    string safeUrl = url.Replace("\"", "%22");
                    browser.DocumentText = $$"""
                        <!doctype html>
                        <html>
                        <head>
                        <meta http-equiv="X-UA-Compatible" content="IE=edge" />
                        <style>
                            html, body { width:100%; height:100%; margin:0; padding:0; overflow:hidden; background:#0a1123; }
                            a { display:block; width:30px; height:30px; cursor:pointer; }
                            svg { width:30px !important; height:30px !important; display:block; }
                        </style>
                        </head>
                        <body><a href="{{safeUrl}}" title="{{tooltipText}}">{{svg}}</a></body>
                        </html>
                        """;
                    browser.Navigating += (_, e) =>
                    {
                        if (e.Url is not null && !e.Url.AbsoluteUri.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
                        {
                            e.Cancel = true;
                            OpenUrl(url);
                        }
                    };
                    wrapper.Controls.Add(browser);
                    toolTip.SetToolTip(browser, tooltipText);
                    loaded = true;
                }
                else
                {
                    Image img = Image.FromFile(imagePath);

                    // Optionally produce a white-tinted copy for dark backgrounds
                    if (tintWhite)
                    {
                        img = TintImageWhite(img);
                    }

                    PictureBox pb = new()
                    {
                        Image = img,
                        SizeMode = PictureBoxSizeMode.Zoom,
                        Dock = DockStyle.Fill,
                        BackColor = Color.Transparent,
                        Cursor = Cursors.Hand
                    };
                    pb.Click += (_, _) => OpenUrl(url);
                    wrapper.Controls.Add(pb);
                    toolTip.SetToolTip(pb, tooltipText);
                    loaded = true;
                }
            }
            catch { /* fall through to text fallback */ }
        }

        if (!loaded)
        {
            // Text fallback styled like the original link labels
            Label lbl = new()
            {
                Text = fallbackText,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(219, 234, 254),
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8, FontStyle.Bold),
                Cursor = Cursors.Hand,
                BorderStyle = BorderStyle.FixedSingle
            };
            lbl.Click += (_, _) => OpenUrl(url);
            wrapper.Controls.Add(lbl);
            toolTip.SetToolTip(lbl, tooltipText);
        }

        wrapper.Click += (_, _) => OpenUrl(url);
        toolTip.SetToolTip(wrapper, tooltipText);
        return wrapper;
    }

    /// <summary>
    /// Returns a copy of <paramref name="source"/> where every pixel is replaced
    /// with white at the same opacity — suitable for showing a logo on a dark bar.
    /// </summary>
    private static Image TintImageWhite(Image source)
    {
        Bitmap bmp = new(source.Width, source.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using Graphics g = Graphics.FromImage(bmp);
        g.DrawImage(source, 0, 0);

        for (int y = 0; y < bmp.Height; y++)
        {
            for (int x = 0; x < bmp.Width; x++)
            {
                Color c = bmp.GetPixel(x, y);
                // Keep alpha; push RGB to white
                bmp.SetPixel(x, y, Color.FromArgb(c.A, 255, 255, 255));
            }
        }
        return bmp;
    }

    private void AddCard(TableLayoutPanel parent, string key, string title, int column, int row)
    {
        MetricCard card = new(title);
        cards[key] = card;
        parent.Controls.Add(card.Panel, column, row);
    }

    private static Label MakeDockLabel(string text, int size, FontStyle style, Color color)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", size, style),
            ForeColor = color,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft
        };
    }

    private static Label MakeHeaderBadge(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            BackColor = Color.FromArgb(30, 64, 175),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            Padding = new Padding(10, 4, 10, 4),
            Margin = new Padding(0, 6, 0, 0)
        };
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            Clipboard.SetText(url);
            MessageBox.Show("Could not open the link, so it was copied to clipboard.", "Disk Health Lite");
        }
    }

    private async Task RefreshDisksAsync()
    {
        if (isRefreshing)
        {
            return;
        }

        isRefreshing = true;
        refreshButton.Enabled = false;
        statusLabel.Text = "Refreshing…";

        try
        {
            int? previousSelection = selectedDiskId;
            disks = await DiskReader.ReadAsync();
            PopulateDiskList(previousSelection);
            statusLabel.Text = $"Updated {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            statusLabel.Text = "Refresh failed";
            MessageBox.Show(ex.Message, "Disk Health Lite", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            refreshButton.Enabled = true;
            isRefreshing = false;
        }
    }

    private void PopulateDiskList(int? previousSelection)
    {
        diskList.BeginUpdate();
        diskList.Items.Clear();

        foreach (DiskInfo disk in disks)
        {
            string healthText = disk.HealthPercent is int health ? $"{health}%" : "N/A";
            ListViewItem item = new($"  Disk {disk.Id}: {disk.Name}");
            item.SubItems.Add(healthText);
            item.Tag = disk.Id;

            if (disk.HealthPercent < 50)
            {
                item.ForeColor = Color.FromArgb(185, 28, 28);
            }

            diskList.Items.Add(item);
        }

        diskList.EndUpdate();

        DiskInfo? selected = disks.FirstOrDefault(d => d.Id == previousSelection) ?? disks.FirstOrDefault();
        selectedDiskId = selected?.Id;

        if (selected is null)
        {
            ShowDisk(null);
            return;
        }

        foreach (ListViewItem item in diskList.Items)
        {
            if (item.Tag is int id && id == selected.Id)
            {
                item.Selected = true;
                item.Focused = true;
                break;
            }
        }

        ShowDisk(selected);
    }

    private void SelectCurrentListItem()
    {
        if (diskList.SelectedItems.Count == 0)
        {
            return;
        }

        if (diskList.SelectedItems[0].Tag is not int id)
        {
            return;
        }

        selectedDiskId = id;
        ShowDisk(disks.FirstOrDefault(d => d.Id == selectedDiskId));
    }

    private void ShowDisk(DiskInfo? disk)
    {
        if (disk is null)
        {
            diskNameLabel.Text = "No disk selected";
            diskMetaLabel.Text = "";
            diskSerialLabel.Text = "";
            healthPercentLabel.Text = "--";
            healthNoteLabel.Text = "";
            SetHealthBar(null);
            foreach (MetricCard card in cards.Values)
            {
                card.SetValue("Not reported", "");
            }
            detailsBox.Text = "";
            return;
        }

        diskNameLabel.Text = disk.Name;
        diskMetaLabel.Text = $"Disk {disk.Id}  ·  {disk.MediaType}  ·  {disk.BusType}  ·  {FormatBytes(disk.Size)}  ·  {disk.DriveLetters}";
        diskSerialLabel.Text = $"Serial: {disk.Serial}";

        if (disk.HealthPercent is int health)
        {
            healthPercentLabel.Text = $"{health}%";
            SetHealthBar(health);
            cards["health"].SetValue($"{health}%", "100 − wear value");
            healthNoteLabel.Text = health switch
            {
                >= 80 => "Disk looks healthy based on the counters Windows reports.",
                >= 50 => "Health is reduced. Keep backups current and watch this drive.",
                _     => "Health is low. Back up important data and consider replacement."
            };
        }
        else
        {
            healthPercentLabel.Text = "N/A";
            SetHealthBar(null);
            cards["health"].SetValue("Not reported", "Wear value unavailable");
            healthNoteLabel.Text = "Windows did not report a wear value for this disk.";
        }

        Color tempColor = disk.Temperature >= 55
            ? Color.FromArgb(220, 38, 38)
            : Color.FromArgb(17, 24, 39);
        cards["temp"].SetValue(disk.Temperature is int temp ? $"{temp} °C" : "Not reported",
            "Current sensor reading", tempColor);
        cards["hours"].SetValue(FormatHours(disk.PowerOnHours), "Total powered-on time");
        cards["writes"].SetValue(FormatBytes(disk.LifetimeWrites), "Host bytes written");
        cards["wear"].SetValue(disk.Wear is int wear ? $"{wear}%" : "Not reported", "Health = 100 − wear");

        Color statusColor = disk.HealthStatus.Equals("Healthy", StringComparison.OrdinalIgnoreCase)
            ? Color.FromArgb(22, 163, 74)
            : Color.FromArgb(220, 38, 38);
        cards["status"].SetValue(disk.HealthStatus, disk.OperationalStatus, statusColor);

        List<string> detailLines =
        [
            $"Auto refresh: every {RefreshIntervalSeconds} seconds.  External drives appear after Windows detects them."
        ];
        if (!string.IsNullOrWhiteSpace(disk.DataSource))
        {
            detailLines.Add($"SMART source: {disk.DataSource}");
        }
        if (!string.IsNullOrWhiteSpace(disk.CounterError))
        {
            detailLines.Add($"Counter note: {disk.CounterError}");
        }

        detailsBox.Text = string.Join(Environment.NewLine, detailLines);
    }

    private void SetHealthBar(int? percent)
    {
        if (percent is null)
        {
            healthFill.Width = 0;
            healthFill.BackColor = Color.FromArgb(203, 213, 225);
            return;
        }

        int safePercent = Math.Clamp(percent.Value, 0, 100);
        healthFill.Height = healthTrack.ClientSize.Height;
        healthFill.Width = (int)Math.Round(healthTrack.ClientSize.Width * (safePercent / 100d));
        healthFill.BackColor = safePercent switch
        {
            >= 80 => Color.FromArgb(34, 197, 94),
            >= 50 => Color.FromArgb(245, 158, 11),
            _     => Color.FromArgb(239, 68, 68)
        };
    }

    private static string FormatHours(double? hours)
    {
        if (hours is null) return "Not reported";
        long wholeHours = (long)Math.Floor(hours.Value);
        long days = wholeHours / 24;
        long remainingHours = wholeHours % 24;
        return days > 0 ? $"{days:N0} days, {remainingHours} hrs" : $"{wholeHours:N0} hours";
    }

    private static string FormatBytes(double? bytes)
    {
        if (bytes is null) return "Not reported";
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        double value = bytes.Value;
        int index = 0;
        while (value >= 1024 && index < units.Length - 1)
        {
            value /= 1024;
            index++;
        }
        return index == 0 ? $"{value:N0} {units[index]}" : $"{value:N2} {units[index]}";
    }

    // ── MetricCard ────────────────────────────────────────────────────────────

    private sealed class MetricCard
    {
        public Panel Panel { get; } = new();
        private readonly Label valueLabel = new();
        private readonly Label hintLabel = new();

        public MetricCard(string title)
        {
            Panel.Dock = DockStyle.Fill;
            Panel.Margin = new Padding(0, 0, 10, 10);
            Panel.Padding = new Padding(16, 12, 16, 12);
            Panel.MinimumSize = new Size(200, 100);
            Panel.BackColor = Color.FromArgb(248, 250, 252);
            Panel.Paint += (_, e) =>
            {
                using Pen pen = new(Color.FromArgb(226, 232, 240));
                Rectangle rect = new(0, 0, Panel.Width - 1, Panel.Height - 1);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.DrawRectangle(pen, rect);
            };

            TableLayoutPanel layout = new()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = Color.Transparent
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));

            Label titleLabel = new()
            {
                Text = title,
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                ForeColor = Color.FromArgb(100, 116, 139),
                TextAlign = ContentAlignment.MiddleLeft
            };

            valueLabel.Dock = DockStyle.Fill;
            valueLabel.Font = new Font("Segoe UI", 14, FontStyle.Bold);
            valueLabel.ForeColor = Color.FromArgb(15, 23, 42);
            valueLabel.AutoEllipsis = false;
            valueLabel.TextAlign = ContentAlignment.MiddleLeft;

            hintLabel.Dock = DockStyle.Fill;
            hintLabel.Font = new Font("Segoe UI", 7.5f);
            hintLabel.ForeColor = Color.FromArgb(148, 163, 184);
            hintLabel.AutoEllipsis = true;
            hintLabel.TextAlign = ContentAlignment.MiddleLeft;

            layout.Controls.Add(titleLabel, 0, 0);
            layout.Controls.Add(valueLabel, 0, 1);
            layout.Controls.Add(hintLabel, 0, 2);
            Panel.Controls.Add(layout);
        }

        public void SetValue(string value, string hint, Color? color = null)
        {
            valueLabel.Text = value;
            valueLabel.ForeColor = color ?? Color.FromArgb(15, 23, 42);
            hintLabel.Text = hint;
        }
    }
}

// ── DiskInfo record ───────────────────────────────────────────────────────────

internal sealed record DiskInfo(
    int Id,
    string Name,
    string Serial,
    string MediaType,
    string BusType,
    double? Size,
    string HealthStatus,
    string OperationalStatus,
    int? HealthPercent,
    int? Wear,
    int? Temperature,
    double? PowerOnHours,
    double? LifetimeWrites,
    string DriveLetters,
    string? DataSource,
    string? CounterError);

// ── DiskReader ────────────────────────────────────────────────────────────────

internal static class DiskReader
{
    public static async Task<List<DiskInfo>> ReadAsync()
    {
        string script = """
            $appDir = $env:DISK_HEALTH_LITE_DIR
            $results = @()
            try {
                $physicalDisks = @(Get-PhysicalDisk -ErrorAction Stop | Sort-Object DeviceId)
                foreach ($pd in $physicalDisks) {
                    $counter = $null
                    $counterError = $null
                    try {
                        $counter = $pd | Get-StorageReliabilityCounter -ErrorAction Stop
                    } catch {
                        $counterError = $_.Exception.Message
                    }

                    $letters = @()
                    try {
                        $letters = @(Get-Partition -DiskNumber $pd.DeviceId -ErrorAction Stop |
                            Where-Object { $_.DriveLetter } |
                            ForEach-Object { "$($_.DriveLetter):" })
                    } catch {}

                    $wear = $null
                    $temperature = $null
                    $powerOnHours = $null
                    $lifetimeWrites = $null
                    $dataSource = 'Windows Storage'
                    if ($counter -ne $null) {
                        $wear = $counter.Wear
                        $temperature = $counter.Temperature
                        $powerOnHours = $counter.PowerOnHours
                        foreach ($name in @('BytesWritten', 'TotalBytesWritten', 'HostWrites', 'LifetimeWrites')) {
                            if ($counter.PSObject.Properties[$name] -and $counter.$name -ne $null) {
                                $lifetimeWrites = $counter.$name
                                break
                            }
                        }
                    }

                    if ($powerOnHours -eq $null -or $lifetimeWrites -eq $null) {
                        try {
                            $smartRows = @(Get-CimInstance -Namespace root\wmi -ClassName MSStorageDriver_FailurePredictData -ErrorAction Stop)
                            $modelKey = "$($pd.FriendlyName)".Replace(' ', '').ToLowerInvariant()
                            $serialKey = "$($pd.SerialNumber)".Replace('_', '').Replace('.', '').Replace(' ', '').ToLowerInvariant()
                            $matchingRows = @($smartRows | Where-Object {
                                $instance = "$($_.InstanceName)".Replace('_', '').Replace('.', '').Replace(' ', '').ToLowerInvariant()
                                ($modelKey.Length -gt 0 -and $instance.Contains($modelKey.Substring(0, [Math]::Min(8, $modelKey.Length)))) -or
                                ($serialKey.Length -gt 0 -and $instance.Contains($serialKey.Substring(0, [Math]::Min(8, $serialKey.Length))))
                            })
                            if ($matchingRows.Count -eq 0) {
                                $matchingRows = $smartRows
                            }

                            foreach ($row in $matchingRows) {
                                $bytes = $row.VendorSpecific
                                if ($bytes -eq $null -or $bytes.Count -lt 362) { continue }
                                for ($offset = 2; $offset -le 361; $offset += 12) {
                                    $id = [int]$bytes[$offset]
                                    if ($id -eq 0) { continue }
                                    $raw = [uint64]0
                                    for ($i = 0; $i -lt 6; $i++) {
                                        $raw += ([uint64]$bytes[$offset + 5 + $i]) -shl (8 * $i)
                                    }

                                    if ($powerOnHours -eq $null -and $id -eq 9) {
                                        $powerOnHours = [double]$raw
                                        $dataSource = 'Windows Storage + WMI SMART'
                                    }
                                    if ($lifetimeWrites -eq $null -and ($id -eq 241 -or $id -eq 242)) {
                                        $lifetimeWrites = [double]$raw * 512
                                        $dataSource = 'Windows Storage + WMI SMART'
                                    }
                                }
                                if ($powerOnHours -ne $null -and $lifetimeWrites -ne $null) { break }
                            }
                        } catch {}
                    }

                    $smartctl = $null
                    $smartctlCandidates = @(
                        $(if ($appDir) { Join-Path $appDir 'tools\smartctl.exe' }),
                        (Get-Command smartctl.exe -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Source),
                        (Get-Command smartctl -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Source),
                        'C:\Program Files\smartmontools\bin\smartctl.exe',
                        'C:\Program Files (x86)\smartmontools\bin\smartctl.exe'
                    ) | Where-Object { $_ -and (Test-Path $_) }

                    if ($smartctlCandidates.Count -eq 0) {
                        try {
                            $builtSmartctl = Get-ChildItem -Path 'C:\Users\mohds\Downloads\Compressed\smartmontools-main\smartmontools-main' -Recurse -Filter smartctl.exe -ErrorAction SilentlyContinue |
                                Select-Object -First 1 -ExpandProperty FullName
                            if ($builtSmartctl) {
                                $smartctlCandidates = @($builtSmartctl)
                            }
                        } catch {}
                    }

                    if ($smartctlCandidates.Count -gt 0) {
                        $smartctl = $smartctlCandidates[0]
                    }

                    if ($smartctl -ne $null) {
                        try {
                            $smartJson = & $smartctl -a -j "/dev/pd$($pd.DeviceId)" 2>$null | ConvertFrom-Json
                            if ($smartJson) {
                                $dataSource = 'Windows Storage + smartctl'
                                if ($wear -eq $null -and $smartJson.endurance_used.current_percent -ne $null) {
                                    $wear = $smartJson.endurance_used.current_percent
                                }
                                if ($wear -eq $null -and $smartJson.nvme_smart_health_information_log.percentage_used -ne $null) {
                                    $wear = $smartJson.nvme_smart_health_information_log.percentage_used
                                }
                                if ($temperature -eq $null -and $smartJson.temperature.current -ne $null) {
                                    $temperature = $smartJson.temperature.current
                                }
                                if ($powerOnHours -eq $null -and $smartJson.power_on_time.hours -ne $null) {
                                    $powerOnHours = $smartJson.power_on_time.hours
                                }
                                if ($powerOnHours -eq $null -and $smartJson.nvme_smart_health_information_log.power_on_hours -ne $null) {
                                    $powerOnHours = $smartJson.nvme_smart_health_information_log.power_on_hours
                                }
                                if ($lifetimeWrites -eq $null -and $smartJson.nvme_smart_health_information_log.data_units_written -ne $null) {
                                    $lifetimeWrites = [double]$smartJson.nvme_smart_health_information_log.data_units_written * 512000
                                }
                                if ($lifetimeWrites -eq $null -and $smartJson.ata_smart_attributes.table -ne $null) {
                                    $lbaWritten = $smartJson.ata_smart_attributes.table |
                                        Where-Object { $_.name -match 'Total_LBAs_Written|Host_Writes|NAND_Writes' } |
                                        Select-Object -First 1
                                    if ($lbaWritten -and $lbaWritten.raw.value -ne $null) {
                                        $lifetimeWrites = [double]$lbaWritten.raw.value * 512
                                    }
                                }
                            }
                        } catch {}
                    }

                    $healthPercent = $null
                    if ($wear -ne $null) {
                        $healthPercent = [Math]::Max(0, [Math]::Min(100, 100 - [int]$wear))
                    } elseif ("$($pd.HealthStatus)" -eq 'Healthy') {
                        $healthPercent = 100
                    }

                    $results += [pscustomobject]@{
                        Id = [int]$pd.DeviceId
                        Name = "$($pd.FriendlyName)"
                        Serial = "$($pd.SerialNumber)".Trim()
                        MediaType = "$($pd.MediaType)"
                        BusType = "$($pd.BusType)"
                        Size = [double]$pd.Size
                        HealthStatus = "$($pd.HealthStatus)"
                        OperationalStatus = "$($pd.OperationalStatus)"
                        HealthPercent = $healthPercent
                        Wear = $wear
                        Temperature = $temperature
                        PowerOnHours = $powerOnHours
                        LifetimeWrites = $lifetimeWrites
                        DriveLetters = ($letters -join ', ')
                        DataSource = $dataSource
                        CounterError = $counterError
                    }
                }
                @($results) | ConvertTo-Json -Depth 5 -Compress
            } catch {
                [pscustomobject]@{ Error = $_.Exception.Message } | ConvertTo-Json -Compress
            }
            """;

        string encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        ProcessStartInfo startInfo = new("powershell.exe")
        {
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encodedCommand}",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.Environment["DISK_HEALTH_LITE_DIR"] = AppContext.BaseDirectory;

        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start PowerShell.");
        string output = await process.StandardOutput.ReadToEndAsync();
        string error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "PowerShell disk query failed." : error.Trim());
        }

        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        using JsonDocument document = JsonDocument.Parse(output);
        if (document.RootElement.ValueKind == JsonValueKind.Object &&
            document.RootElement.TryGetProperty("Error", out JsonElement errorElement))
        {
            throw new InvalidOperationException(errorElement.GetString() ?? "Disk query failed.");
        }

        JsonElement arrayElement = document.RootElement;
        if (arrayElement.ValueKind == JsonValueKind.Object)
        {
            return [ReadDisk(arrayElement)];
        }

        List<DiskInfo> result = [];
        foreach (JsonElement item in arrayElement.EnumerateArray())
        {
            result.Add(ReadDisk(item));
        }

        return result;
    }

    private static DiskInfo ReadDisk(JsonElement item)
    {
        int? wear = GetInt(item, "Wear");
        int? healthPercent = GetInt(item, "HealthPercent");
        if (healthPercent is null && wear is not null)
        {
            healthPercent = Math.Clamp(100 - wear.Value, 0, 100);
        }

        return new DiskInfo(
            GetInt(item, "Id") ?? 0,
            GetString(item, "Name", "Unknown disk"),
            GetString(item, "Serial", "Not reported"),
            GetString(item, "MediaType", "Unknown"),
            GetString(item, "BusType", "Unknown"),
            GetDouble(item, "Size"),
            GetString(item, "HealthStatus", "Unknown"),
            GetString(item, "OperationalStatus", "Unknown"),
            healthPercent,
            wear,
            GetInt(item, "Temperature"),
            GetDouble(item, "PowerOnHours"),
            GetDouble(item, "LifetimeWrites"),
            GetString(item, "DriveLetters", ""),
            GetNullableString(item, "DataSource"),
            GetNullableString(item, "CounterError"));
    }

    private static string GetString(JsonElement item, string name, string fallback)
        => GetNullableString(item, name) ?? fallback;

    private static string? GetNullableString(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out JsonElement value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }
        return value.ToString();
    }

    private static int? GetInt(JsonElement item, string name)
    {
        double? value = GetDouble(item, name);
        return value is null ? null : Convert.ToInt32(value.Value);
    }

    private static double? GetDouble(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out JsonElement value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number))
        {
            return number;
        }
        return double.TryParse(value.ToString(), out double parsed) ? parsed : null;
    }
}
