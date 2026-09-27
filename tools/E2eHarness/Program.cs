using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Automation;
using System.Windows.Automation.Provider;

// E2E driver for VMDesk: launches the real app, adds the localhost RDP VM if
// needed, drives Connect through the UI, and asserts the session surfaces
// inside the app with no mstsc.exe handoff. The Windows password is typed by
// the operator in the app window — the harness never touches it.

var exe = args.Length > 0 ? args[0] : FindDefaultExe();
if (!File.Exists(exe))
    Fail($"VMDesk.exe not found at {exe}. Build the app first.");

static string FindDefaultExe()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VMDesk.slnx")))
        dir = dir.Parent;
    var root = dir?.FullName ?? throw new InvalidOperationException("repo root with VMDesk.slnx not found");
    return Path.Combine(root, "src", "VMDesk.App", "bin", "Debug", "net10.0-windows", "win-x64", "VMDesk.exe");
}

var shots = Path.Combine(AppContext.BaseDirectory, "shots");
Directory.CreateDirectory(shots);

var host = Environment.GetEnvironmentVariable("E2E_HOST") ?? "127.0.0.1";
var user = Environment.GetEnvironmentVariable("E2E_USER") ?? Environment.UserName;

Console.WriteLine($"[e2e] exe={exe}");
foreach (var p in Process.GetProcessesByName("VMDesk"))
    try { p.Kill(entireProcessTree: true); p.WaitForExit(3000); } catch { }
Thread.Sleep(500);

var proc = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true })!;
Console.WriteLine($"[e2e] started pid={proc.Id}");

var main = WaitWindow("VMDesk", TimeSpan.FromSeconds(40));
Shot(main, "01-main-start");
Console.WriteLine("[e2e] main window up");

// --- ensure the localhost VM row exists ---
if (FindRowForHost(main, host) is null)
{
    Console.WriteLine($"[e2e] no row for {host}; opening Add VM");
    var addBtn = FindButtonByText(main, "Add VM");
    if (addBtn is null) Fail2("Add VM button not found");
    Invoke(addBtn!, "Add VM");
    Thread.Sleep(1200);
    Shot(null, "debug-after-add-click");
    var addWin = WaitWindow("Add VM", TimeSpan.FromSeconds(10));
    SetById(addWin, "NameBox", "Localhost RDP");
    SetById(addWin, "HostBox", host);
    SetById(addWin, "UserBox", user);
    SetById(addWin, "PortBox", "3389");
    var addButton = ById(addWin, "AddButton");
    if (addButton is null) Fail2("AddButton not found");
    Invoke(addButton!, "AddButton");
    Thread.Sleep(1500);
    main = WaitWindow("VMDesk", TimeSpan.FromSeconds(10)) ?? main;
    if (FindRowForHost(main, host) is null)
    {
        Shot(main, "02-add-vm-failed");
        Fail2("VM row was not created (screenshot 02-add-vm-failed)");
    }
    Console.WriteLine("[e2e] VM row created");
}
else Console.WriteLine($"[e2e] row for {host} already present");

if (Environment.GetEnvironmentVariable("E2E_STAGE") == "add")
{
    Shot(main, "add-stage-done");
    Console.WriteLine("[e2e] ADD-STAGE PASS: VM row present. Close VMDesk.");
    try { proc.Kill(entireProcessTree: true); } catch { }
    return 0;
}

if (Environment.GetEnvironmentVariable("E2E_STAGE") == "shots")
{
    Shot(main, "theme-start");
    foreach (var (radio, tag) in new[] { ("Dark", "dark"), ("Light", "light") })
    {
        var settingsBtn = FindButtonByText(main, "Settings");
        if (settingsBtn is null) Fail2("Settings nav button not found");
        Invoke(settingsBtn!, "Settings");
        var settingsWin = WaitWindow("Settings", TimeSpan.FromSeconds(10));
        SelectRadio(settingsWin, radio);
        var apply = ById(settingsWin, "ApplyButton");
        if (apply is not null) Invoke(apply, "Apply");
        WaitGone("Settings", TimeSpan.FromSeconds(10));
        main = WaitWindow("VMDesk", TimeSpan.FromSeconds(10));
        Shot(main, $"theme-{tag}");
        Console.WriteLine($"[e2e] captured {tag}");
    }
    Console.WriteLine("SHOTS PASS");
    try { proc.Kill(entireProcessTree: true); } catch { }
    return 0;
}

// --- connect ---
var row = FindRowForHost(main, host)!;
var connect = Descendants(row, ControlType.Button, "Connect").FirstOrDefault();
if (connect is null) Fail2("Connect button not found in row");
Invoke(connect!, "Connect");
Console.WriteLine("[e2e] clicked Connect");

// The credential picker may open first. The operator types the password; we only wait.
var pickerDeadline = DateTime.UtcNow.AddSeconds(15);
AutomationElement? picker = null;
while (DateTime.UtcNow < pickerDeadline)
{
    picker = TryWindow("Choose a credential");
    if (picker is not null) break;
    if (TryMessageBox() is { } mb0) { ReportMessageBox(mb0); DismissBox(mb0.Element); }
    Thread.Sleep(400);
}
if (picker is not null)
{
    Shot(null, "03-credential-picker", picker);
    Console.WriteLine();
    Console.WriteLine(">>> The credential picker is open. Choose (or create via 'Manage credentials…') the");
    Console.WriteLine(">>> credential for your Windows account and TYPE THE PASSWORD IN THE APP WINDOW.");
    Console.WriteLine(">>> Then press Enter here.");
    Console.ReadLine();
    WaitGone("Choose a credential", TimeSpan.FromSeconds(20));
}

// Wait for the progress dialog, then for an outcome.
var progressSeen = WaitWindowImpl("Connecting", TimeSpan.FromSeconds(10), prefix: true);
Console.WriteLine($"[e2e] progress dialog seen: {progressSeen is not null}");

var deadline = DateTime.UtcNow.AddSeconds(TimeSpan.FromSeconds(60).TotalSeconds);
string outcome = "timeout";
while (DateTime.UtcNow < deadline)
{
    var mb = TryMessageBox();
    if (mb is not null)
    {
        ReportMessageBox(mb);
        Shot(null, "04-error-dialog", mb.Element);
        DismissBox(mb.Element);
        outcome = "error-dialog";
        break;
    }

    if (TryWindow("VMDesk Session") is { } sess)
    {
        Shot(null, "05-session-window", sess);
        outcome = "session-window";
        break;
    }

    var status = ById(main, "EmbeddedStatus");
    var statusText = status?.Current.Name ?? ReadText(status);
    if (!string.IsNullOrWhiteSpace(statusText) && statusText.Contains("Connected", StringComparison.OrdinalIgnoreCase))
    {
        Shot(main, "06-embedded-connected");
        outcome = "embedded-connected";
        break;
    }
    if (DateTime.UtcNow.Second % 10 == 0)
        Console.WriteLine($"[e2e] waiting… embedded status: '{statusText}'");
    Thread.Sleep(700);
}

// --- assertions ---
var mstsc = Process.GetProcessesByName("mstsc");
Console.WriteLine($"[e2e] mstsc processes running: {mstsc.Length} (must be 0)");
Shot(main, "07-final-main");

if (mstsc.Length > 0) Fail2("FAIL: mstsc.exe was launched — sessions must stay in-app.");
if (outcome == "error-dialog")
    Fail2("FAIL: connect surfaced an error dialog. Expected a connected session. Enable RDP on this PC first "
        + "(see the elevated block in the run log) and re-run.");
if (outcome == "timeout")
    Fail2("FAIL: no connected session and no error dialog within 60 s.");

Console.WriteLine();
Console.WriteLine($"E2E PASS: session surfaced in-app ({outcome}); screenshots in {shots}");
Console.WriteLine("Press Enter to close VMDesk.");
Console.ReadLine();
try { proc.Kill(entireProcessTree: true); } catch { }
return 0;

// ---------------- helpers ----------------

static AutomationElement WaitWindow(string title, TimeSpan timeout, bool prefix = false)
    => WaitWindowImpl(title, timeout, prefix) ?? throw new InvalidOperationException($"window '{title}' never appeared");

static AutomationElement? WaitWindowImpl(string title, TimeSpan timeout, bool prefix)
{
    var deadline = DateTime.UtcNow + timeout;
    while (DateTime.UtcNow < deadline)
    {
        var w = TryWindow(title, prefix);
        if (w is not null) return w;
        Thread.Sleep(400);
    }
    Console.WriteLine($"[e2e] windows on desktop: {string.Join(" | ", AllTopLevel()
        .Select(e => $"'{e.Current.Name}'({e.Current.ClassName})"))}");
    return null;
}

static void WaitGone(string title, TimeSpan timeout)
{
    var deadline = DateTime.UtcNow + timeout;
    while (DateTime.UtcNow < deadline && TryWindow(title) is not null) Thread.Sleep(400);
}

// WPF owned dialogs (ShowDialog) can nest under the owner window in the UIA tree
// instead of appearing as root children, so search both places.
static List<AutomationElement> AllTopLevel()
{
    var list = AutomationElement.RootElement.FindAll(TreeScope.Children, Condition.TrueCondition)
        .Cast<AutomationElement>().ToList();
    foreach (var appWindow in list
             .Where(e => (e.Current.Name ?? "").Contains("VMDesk", StringComparison.OrdinalIgnoreCase))
             .ToList())
    {
        try
        {
            list.AddRange(appWindow.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window))
                .Cast<AutomationElement>());
        }
        catch { }
    }
    return list;
}

static AutomationElement? TryWindow(string title, bool prefix = false)
{
    foreach (var w in AllTopLevel())
    {
        try
        {
            var name = w.Current.Name ?? "";
            if (prefix ? name.StartsWith(title, StringComparison.OrdinalIgnoreCase)
                       : name.Equals(title, StringComparison.OrdinalIgnoreCase))
                return w;
        }
        catch { }
    }
    return null;
}

Box? TryMessageBox()
{
    foreach (var w in AutomationElement.RootElement.FindAll(TreeScope.Children, Condition.TrueCondition)
             .Cast<AutomationElement>())
    {
        try
        {
            if (w.Current.ClassName != "#32770") continue;
            var texts = Truncate(string.Join(" | ", Descendants(w, ControlType.Text, null).Select(t => t.Current.Name)
                                 .Where(n => !string.IsNullOrWhiteSpace(n)).Distinct()), 300);
            return new Box(w, $"{w.Current.Name} :: {texts}");
        }
        catch { }
    }
    return null;
}

static void ReportMessageBox(Box mb) =>
    Console.WriteLine($"[e2e] dialog: {mb.Text}");

static void DismissBox(AutomationElement element)
{
    var ok = Descendants(element, ControlType.Button, null).FirstOrDefault();
    if (ok is not null) Invoke(ok, "dismiss");
}

static List<AutomationElement> Descendants(AutomationElement root, ControlType type, string? nameEquals)
{
    var conditions = new List<Condition> { new PropertyCondition(AutomationElement.ControlTypeProperty, type) };
    if (nameEquals is not null) conditions.Add(new PropertyCondition(AutomationElement.NameProperty, nameEquals));
    try
    {
        return root.FindAll(TreeScope.Descendants, conditions.Count == 1 ? conditions[0]
            : new AndCondition(conditions.ToArray())).Cast<AutomationElement>().ToList();
    }
    catch { return new List<AutomationElement>(); }
}

static AutomationElement? ById(AutomationElement root, string automationId)
{
    try
    {
        var c = root.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, automationId));
        return c is null ? null : (AutomationElement)c;
    }
    catch { return null; }
}

static AutomationElement? FindButtonByText(AutomationElement root, string text)
{
    // Chromeless buttons (icon + TextBlock children) expose no Name; match on subtree text.
    foreach (var btn in Descendants(root, ControlType.Button, null))
    {
        try
        {
            var names = btn.FindAll(TreeScope.Descendants, Condition.TrueCondition)
                .Cast<AutomationElement>().Select(e => e.Current.Name ?? "");
            if (names.Any(n => n.Contains(text, StringComparison.OrdinalIgnoreCase)))
                return btn;
        }
        catch { }
    }
    return null;
}

static AutomationElement? FindRowForHost(AutomationElement main, string host)
{
    foreach (var row in Descendants(main, ControlType.DataItem, null))
    {
        try
        {
            var texts = row.FindAll(TreeScope.Descendants, Condition.TrueCondition)
                .Cast<AutomationElement>().Select(e => e.Current.Name ?? "");
            if (texts.Any(t => t.Contains(host, StringComparison.OrdinalIgnoreCase)))
                return row;
        }
        catch { }
    }
    return null;
}

static void Invoke(AutomationElement el, string label)
{
    // Pattern invoke is position/z-order independent; real pointer clicks proved
    // unreliable because other apps can sit over the target. The earlier failure
    // was the window finder missing nested dialogs, not the click.
    ((InvokePattern)el.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
    Console.WriteLine($"[e2e] invoked {label} (name='{el.Current.Name}' id='{el.Current.AutomationId}' "
        + $"class='{el.Current.ClassName}' enabled={el.Current.IsEnabled} rect={el.Current.BoundingRectangle})");
    Thread.Sleep(600);
}

static void SelectRadio(AutomationElement window, string name)
{
    var radio = Descendants(window, ControlType.RadioButton, name).FirstOrDefault();
    if (radio is null) Fail2($"radio '{name}' not found");
    ((SelectionItemPattern)radio!.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
    Console.WriteLine($"[e2e] selected radio {name}");
    Thread.Sleep(300);
}

static void SetValue(AutomationElement el, string text)
{
    ((ValuePattern)el.GetCurrentPattern(ValuePattern.Pattern)).SetValue(text);
    Thread.Sleep(150);
}

static void SetById(AutomationElement window, string id, string text)
{
    var el = ById(window, id);
    if (el is null)
    {
        Fail2($"{id} not found");
        return;
    }
    SetValue(el, text);
    Console.WriteLine($"[e2e] {id} := {text}");
}

static string ReadText(AutomationElement? el)
{
    if (el is null) return "";
    try
    {
        if (el.GetCurrentPattern(TextPattern.Pattern) is TextPattern tp)
        {
            var range = tp.DocumentRange;
            return range.GetText(-1);
        }
    }
    catch { }
    return el.Current.Name ?? "";
}

void Shot(AutomationElement? window, string name, AutomationElement? overrideEl = null)
{
    var el = overrideEl ?? window;
    var workArea = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
    System.Drawing.Rectangle bounds;
    try
    {
        var r = el?.Current.BoundingRectangle ?? default;
        bounds = r.Width >= 100 && r.Height >= 100
            ? new System.Drawing.Rectangle((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height)
            : workArea;
    }
    catch { bounds = workArea; }

    var file = Path.Combine(shots, name + ".png");
    var bmp = new Bitmap(bounds.Width, bounds.Height);
    using (var g = Graphics.FromImage(bmp))
        g.CopyFromScreen(bounds.Location, System.Drawing.Point.Empty, bounds.Size);
    bmp.Save(file, System.Drawing.Imaging.ImageFormat.Png);
    Console.WriteLine($"[e2e] shot -> {file}");
}

static void Fail(string message)
{
    Console.WriteLine(message);
    Environment.Exit(1);
}

static void Fail2(string message)
{
    Console.WriteLine($"[e2e] {message}");
    Environment.Exit(1);
}

static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

record Box(AutomationElement Element, string Text);
