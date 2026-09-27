using System.Reflection;
using VMDesk.Core.Models;
using VMDesk.Rdp.Interop;
using Xunit;

namespace VMDesk.Rdp.Tests;

/// <summary>
/// The availability probe must be backed by a real CoCreateInstance so it can
/// never claim an availability that CreateControl cannot deliver, and the
/// session must fail loudly instead of silently no-opping when the ActiveX
/// control is missing or cannot be wired.
/// </summary>
public class ControlInstantiationTests
{
    private static MicrosoftRdpSession NewSession() => new(
        Guid.NewGuid(),
        "probe-vm",
        new NullLogFactory(),
        () => Task.FromResult<CredentialData?>(new CredentialData("probe-ref", "tester", "not-a-real-secret")));

    [Fact]
    public void Wrapper_clsids_are_readable_from_the_AxHost_attribute()
    {
        // The probe bug this guards: tlbimp stores the coclass GUID in the internal
        // AxHost+ClsidAttribute, not a CoClassAttribute. Reading the wrong attribute
        // made Probe skip every candidate and always report unavailable, which sent
        // every connect to external mstsc.
        var checked_ = 0;
        foreach (var name in new[]
        {
            "AxMSTSCLib.AxMsRdpClient9NotSafeForScripting",
            "AxMSTSCLib.AxMsRdpClient10NotSafeForScripting",
            "AxMSTSCLib.AxMsRdpClient11NotSafeForScripting",
            "AxMSTSCLib.AxMsRdpClient12NotSafeForScripting",
        })
        {
            var type = typeof(AxMSTSCLib.AxMsRdpClient9NotSafeForScripting).Assembly.GetType(name);
            if (type is null) continue;
            checked_++;
            Assert.True(RdpControlFactory.TryGetClsid(type, out var clsid),
                name + " must expose its coclass CLSID to the probe");
            Assert.NotEqual(Guid.Empty, clsid);
        }
        Assert.True(checked_ > 0, "At least one RdpClient wrapper type must be loadable.");
    }

    [Fact]
    public void Probe_agrees_with_real_instantiation()
    {
        // Both directions, no vacuous pass: a false probe against a control that CAN
        // be created is exactly the bug that routed every session to mstsc.exe.
        var (available, details) = RdpControlFactory.Probe();
        Assert.False(string.IsNullOrEmpty(details));

        // AxHost needs STA like the WPF UI thread; xunit's default MTA would fail the
        // creation for threading reasons, not availability reasons.
        Exception? creationFailure = null;
        var thread = new Thread(() =>
        {
            try { var c = RdpControlFactory.CreateControl(); c.Control.Dispose(); }
            catch (Exception ex) { creationFailure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (creationFailure is null)
            Assert.True(available, "Probe must not report unavailable when CreateControl succeeds: " + details);
        else if (available)
            Assert.Fail("Probe claims availability but CreateControl failed: " + creationFailure);
    }

    [Fact]
    public void Probe_details_explain_instantiation_failure_when_unavailable()
    {
        var (available, details) = RdpControlFactory.Probe();
        if (available) return; // a machine with a working coclass has nothing to explain
        // Unavailable must be proven by real activation, not just a registry lookup.
        Assert.Contains("instantiat", details, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Factory_returns_only_a_coclass_that_actually_instantiates()
    {
        // Regression pair: the AxHost ctor is lazy — an unregistered coclass only
        // explodes at handle creation (CLASS_E_CLASSNOTAVAILABLE), which used to be
        // caught by the factory's orphaned CreateControl(). Without pre-validation
        // the factory hands out a coclass the host cannot realize.
        RdpControl? created = null;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { created = RdpControlFactory.CreateControl(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) return; // no coclass available at all on this machine
        Assert.True(RdpControlFactory.TryGetClsid(created!.ControlType, out var clsid),
            created.CoclassName + " must expose a CLSID");
        Assert.True(RdpControlFactory.TryInstantiate(clsid),
            "Factory returned " + created.CoclassName + " which CoCreateInstance cannot activate");
        created.Control.Dispose();
    }

    [Fact]
    public void Factory_control_is_delivered_without_an_orphaned_handle()
    {
        // Regression: the factory called CreateControl() while the control had no
        // parent. After WindowsFormsHost reparented it, the AxHost never recreated
        // the handle and Connect() became a silent no-op (no socket, no events) —
        // the exact "stuck at Connecting… then Disconnected" symptom. The host must
        // own handle creation.
        System.Windows.Forms.AxHost? control = null;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { control = RdpControlFactory.CreateControl().Control; }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) return; // machine without a usable coclass: nothing to assert
        Assert.False(control!.IsHandleCreated,
            "Factory must not pre-create the HWND while the control is unparented");
        control.Dispose();
    }

    [Fact]
    public void StartConnect_without_a_control_fails_loud()
    {
        var session = NewSession();
        var ex = Assert.Throws<VmConnectionException>(() => session.StartConnect());
        Assert.Contains("could not be created", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PrepareControl_never_connects_and_is_safe_to_repeat()
    {
        var session = NewSession();
        session.PrepareControl(); // On this machine creation may fail; the failure is captured, not thrown.
        session.PrepareControl(); // A double prepare must be cheap and must not throw.

        // MSTSCLib is referenced by path (not transitively), so read the client via reflection.
        var client = typeof(MicrosoftRdpSession)
            .GetField("_client", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(session);
        var connected = client is null
            ? 0
            : Convert.ToInt32(client.GetType().GetProperty("Connected")!.GetValue(client));
        Assert.Equal(0, connected); // PrepareControl must never start a connection.
    }

    [Fact]
    public void PrepareControl_defers_the_OCX_until_the_control_is_parented()
    {
        // Root cause of the in-app "does not expose IMsRdpClient9" failure: AxHost.GetOcx()
        // only returns an instance once the control has a window handle — which happens when
        // the host parents it, AFTER PrepareControl runs (parent-before-connect ordering).
        // So Prepare must not require the OCX, and ApplyOptions — which runs post-parenting —
        // must realize it.
        var errors = new List<string>();
        object? clientAfterParenting = null;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var session = NewSession();
                session.SessionError += (_, e) => errors.Add(e.FriendlyMessage);
                session.PrepareControl();
                if (errors.Count > 0)
                {
                    throw new Exception("PrepareControl failed before the control was even parented: "
                        + string.Join("; ", errors));
                }

                var control = (System.Windows.Forms.Control)session.HostControl!;
                using var form = new System.Windows.Forms.Form
                {
                    ShowInTaskbar = false,
                    Size = new System.Drawing.Size(200, 200),
                };
                var panel = new System.Windows.Forms.Panel { Dock = System.Windows.Forms.DockStyle.Fill };
                form.Controls.Add(panel);
                panel.Controls.Add(control);
                form.Show();
                System.Windows.Forms.Application.DoEvents();

                if (!control.IsHandleCreated)
                {
                    throw new Exception("Test setup: parenting did not create the control handle.");
                }

                session.Pending.Host = "198.51.100.7"; // TEST-NET-2; ApplyOptions must not connect.
                session.ApplyOptions();

                clientAfterParenting = typeof(MicrosoftRdpSession)
                    .GetField("_client", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .GetValue(session);

                form.Close();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null && failure.Message.StartsWith("Test setup", StringComparison.Ordinal))
        {
            return; // environment could not realize a handle at all
        }

        Assert.Null(failure);
        Assert.NotNull(clientAfterParenting); // ApplyOptions must have acquired IMsRdpClient9.
    }

    [Fact]
    public async Task ConnectAsync_surfaces_a_real_exception_instead_of_hanging()
    {
        var session = NewSession();
        session.Pending.Host = "198.51.100.7"; // TEST-NET-2, never routable.

        var connect = session.ConnectAsync(new CancellationTokenSource(TimeSpan.FromSeconds(20)).Token);
        var completed = await Task.WhenAny(connect, Task.Delay(TimeSpan.FromSeconds(30))) == connect;

        Assert.True(completed, "ConnectAsync must fail fast when the ActiveX control cannot be created or wired.");
        await Assert.ThrowsAnyAsync<Exception>(() => connect);
    }
}
