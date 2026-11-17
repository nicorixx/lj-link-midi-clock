// SPDX-License-Identifier: GPL-2.0-or-later
// Copyright (c) 2026 nicorixx
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace LinkMidiClock;

record Timeline(double Bpm, double Beat, long When, int Peers, long Received);

static class ClockMath
{
    public static long Now => (long)Math.Round(Stopwatch.GetTimestamp() * (1_000_000.0 / Stopwatch.Frequency));
    public static double Field(string line, string key) => double.Parse(Regex.Match(line, @":" + key + @"\s+([-+\d.eE]+)").Groups[1].Value, CultureInfo.InvariantCulture);
    public static double Position(Timeline s, long now) => s.Beat * 24 + (now - s.When) * s.Bpm * 24 / 60_000_000.0;
    public static bool Valid(Timeline s, long now) => s.Peers > 0 && s.Bpm >= 20 && s.Bpm <= 999 && now - s.Received < 1_000_000 && now >= s.Received;
}

static class Midi
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct Caps { public ushort Manufacturer, Product; public uint Version; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name; public ushort Technology, Voices, Notes, ChannelMask; public uint Support; }
    [DllImport("winmm.dll")] public static extern uint midiOutGetNumDevs();
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)] public static extern uint midiOutGetDevCapsW(UIntPtr id, out Caps caps, uint size);
    [DllImport("winmm.dll")] public static extern uint midiOutOpen(out IntPtr handle, uint id, IntPtr callback, IntPtr instance, uint flags);
    [DllImport("winmm.dll")] public static extern uint midiOutShortMsg(IntPtr handle, uint message);
    [DllImport("winmm.dll")] public static extern uint midiOutClose(IntPtr handle);
    [DllImport("winmm.dll")] public static extern uint timeBeginPeriod(uint period);
    [DllImport("winmm.dll")] public static extern uint timeEndPeriod(uint period);
    public static void Check(uint result) { if (result != 0) throw new IOException($"MIDI-Fehler {result}. Port belegt oder Gerät getrennt? MIDI-Port prüfen und neu starten."); }
}

sealed class Bridge : IDisposable
{
    public Timeline State = new(120, 0, 0, 0, 0);
    public string Error = "";
    public long Sent, Skipped;
    private readonly CancellationTokenSource cancel = new();
    private Process? helper;
    private TcpClient? client;
    private Thread? clock;
    private IntPtr midi;
    private Task? network;
    private readonly bool transport;
    private readonly Action<uint> send;
    public Bridge(uint? port, bool transport, Action<uint>? output = null)
    {
        this.transport = transport;
        if (port.HasValue) Midi.Check(Midi.midiOutOpen(out midi, port.Value, IntPtr.Zero, IntPtr.Zero, 0));
        send = output ?? (message => { if (midi != IntPtr.Zero) Midi.Check(Midi.midiOutShortMsg(midi, message)); });
        network = Task.Run(Network);
        clock = new Thread(SendClock) { IsBackground = true, Name = "MIDI Clock", Priority = ThreadPriority.AboveNormal };
        clock.Start();
    }
    private async Task Network()
    {
        try
        {
            int port = 0;
            for (int attempt = 0; attempt < 40; attempt++)
            {
                var listener = new TcpListener(IPAddress.Loopback, Random.Shared.Next(20000, 32000));
                try { listener.Start(); port = ((IPEndPoint)listener.LocalEndpoint).Port; break; }
                catch (SocketException) { }
                finally { listener.Stop(); }
            }
            if (port == 0) throw new IOException("Kein freier lokaler TCP-Port verfügbar.");
            var path = Components.Helper();
            helper = Process.Start(new ProcessStartInfo(path, $"--port={port} --poll=10 --daemon") { UseShellExecute = false, CreateNoWindow = true });
            for (int i = 0; ; i++)
            {
                cancel.Token.ThrowIfCancellationRequested();
                client = new TcpClient { NoDelay = true };
                try { await client.ConnectAsync(IPAddress.Loopback, port, cancel.Token); break; }
                catch (SocketException) when (i < 40 && helper is { HasExited: false }) { client.Dispose(); await Task.Delay(100, cancel.Token); }
            }
            using var reader = new StreamReader(client.GetStream());
            using var writer = new StreamWriter(client.GetStream()) { AutoFlush = true, NewLine = "\n" };
            // Only read Link state. Never set session tempo, phase or playback.
            double bpm = 120; int peers = 0;
            while (!cancel.IsCancellationRequested)
            {
                await writer.WriteLineAsync($"status\nbeat-at-time {ClockMath.Now} 4");
                bool gotBeat = false;
                while (!gotBeat)
                {
                    string? line = await reader.ReadLineAsync(cancel.Token).AsTask().WaitAsync(TimeSpan.FromSeconds(2), cancel.Token);
                    if (line == null) throw new IOException("Link-Verbindung beendet.");
                    if (line.StartsWith("status ")) { bpm = ClockMath.Field(line, "bpm"); peers = (int)ClockMath.Field(line, "peers"); }
                    if (line.StartsWith("beat-at-time "))
                    {
                        State = new Timeline(bpm, ClockMath.Field(line, "beat"), (long)ClockMath.Field(line, "when"), peers, ClockMath.Now);
                        gotBeat = true;
                    }
                }
                await Task.Delay(20, cancel.Token);
            }
        }
        catch (Exception e) when (cancel.IsCancellationRequested) { _ = e; }
        catch (Exception e) { Error = "Link: " + e.Message; cancel.Cancel(); }
    }
    private void SendClock()
    {
        bool active = false; long next = 0;
        Midi.timeBeginPeriod(1);
        try
        {
            while (!cancel.IsCancellationRequested)
            {
                long now = ClockMath.Now;
                var s = Volatile.Read(ref State);
                if (!ClockMath.Valid(s, now))
                {
                    if (active && transport) send(0xFC);
                    active = false; Thread.Sleep(2); continue;
                }
                double position = ClockMath.Position(s, now);
                if (!active)
                {
                    next = (long)Math.Floor(position) + 1;
                    if (transport) send(0xFA);
                    active = true;
                }
                // Resync after a timeline jump or stall, without sending a burst of overdue ticks.
                if (position - next >= 1 || next - position > 2)
                {
                    Interlocked.Add(ref Skipped, Math.Max(1, (long)Math.Floor(position) - next + 1));
                    next = (long)Math.Floor(position) + 1;
                }
                if (position >= next)
                {
                    send(0xF8);
                    Interlocked.Increment(ref Sent); next++;
                }
                double remainingUs = (next - position) * 60_000_000 / (s.Bpm * 24);
                if (remainingUs > 1600) Thread.Sleep(1); else Thread.SpinWait(80);
            }
        }
        catch (Exception e) { Error = e.Message; cancel.Cancel(); }
        finally
        {
            if (active && transport) { try { send(0xFC); } catch { } }
            Midi.timeEndPeriod(1);
        }
    }
    public void Dispose()
    {
        cancel.Cancel(); client?.Dispose(); clock?.Join(3000);
        try { network?.Wait(3000); } catch { }
        if (helper is { HasExited: false }) { helper.Kill(); helper.WaitForExit(2000); }
        helper?.Dispose();
        if (midi != IntPtr.Zero) { Midi.midiOutClose(midi); midi = IntPtr.Zero; }
        cancel.Dispose();
    }
}

sealed class MainForm : Form
{
    readonly ComboBox ports = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 350 };
    readonly Button refresh = new() { Text = "Aktualisieren", AutoSize = true };
    readonly Button run = new() { Text = "Clock starten", AutoSize = true };
    readonly CheckBox transport = new() { Text = "MIDI Start/Stop zusätzlich senden", AutoSize = true };
    readonly Label tempo = new() { Text = "— BPM", Font = new Font("Segoe UI", 32, FontStyle.Bold), AutoSize = true };
    readonly Label status = new() { Text = "Bereit", AutoSize = true, MaximumSize = new Size(510, 0) };
    readonly Label count = new() { AutoSize = true };
    readonly System.Windows.Forms.Timer timer = new() { Interval = 200 };
    Bridge? bridge;
    VirtualPort? virtualPort;
    record Port(uint Id, string Name) { public override string ToString() => Name; }
    public MainForm()
    {
        Text = "LJ Link → MIDI Clock · Daslight 4";
        ClientSize = new Size(610, 460); MinimumSize = new Size(630, 500);
        Font = new Font("Segoe UI", 10); StartPosition = FormStartPosition.CenterScreen;
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22), FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        Controls.Add(panel);
        panel.Controls.Add(new Label { Text = "ABLETON LINK  →  MIDI CLOCK", AutoSize = true });
        panel.Controls.Add(tempo);
        panel.Controls.Add(new Label { Text = "MIDI-Ausgang", AutoSize = true });
        var row = new FlowLayoutPanel { Width = 520, Height = 38 }; row.Controls.Add(ports); row.Controls.Add(refresh); panel.Controls.Add(row);
        panel.Controls.Add(transport); panel.Controls.Add(run); panel.Controls.Add(status); panel.Controls.Add(count);
        panel.Controls.Add(new Label { Text = "LJ Link: Ableton-Link-Ausgabe einschalten.\nDaslight 4: Eingang „LJ Link Clock“ wählen, BPM auf MIDI.\nDieses Programm vor Daslight starten und geöffnet lassen.\nClock läuft bei erkanntem Link-Teilnehmer, unabhängig von Play/Stop.", AutoSize = true, Margin = new Padding(0, 15, 0, 0) });
        panel.Controls.Add(new Label { Text = "Integrierter Port: Windows MIDI Services API · Vorschau", AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(0, 10, 0, 0) });
        refresh.Click += (_, _) => RefreshPorts();
        run.Click += (_, _) => Toggle();
        timer.Tick += (_, _) => UpdateStatus(); timer.Start(); RefreshPorts();
        Shown += (_, _) => {
            try { virtualPort = new VirtualPort(); RefreshPorts(); status.Text = "Eigener MIDI-Port „LJ Link Clock“ ist bereit."; }
            catch (Exception e) { status.Text = "Integrierter Port nicht verfügbar: " + e.Message + " Ein vorhandener MIDI-Port kann alternativ gewählt werden."; }
        };
        FormClosing += (_, _) => { timer.Stop(); bridge?.Dispose(); bridge = null; virtualPort?.Dispose(); };
    }
    void RefreshPorts()
    {
        ports.Items.Clear();
        if (virtualPort != null) ports.Items.Add(new Port(uint.MaxValue, "LJ Link Clock (integriert)"));
        for (uint i = 0; i < Midi.midiOutGetNumDevs(); i++)
            if (Midi.midiOutGetDevCapsW((UIntPtr)i, out var caps, (uint)Marshal.SizeOf<Midi.Caps>()) == 0 && caps.Name != "LJ Clock Sender" && caps.Name != VirtualPort.Name) ports.Items.Add(new Port(i, caps.Name));
        if (ports.Items.Count > 0) ports.SelectedIndex = 0;
        else status.Text = "MIDI-Port wird vorbereitet …";
    }
    void Toggle()
    {
        if (bridge != null) { bridge.Dispose(); bridge = null; SetRunning(false); status.Text = "Gestoppt"; return; }
        if (ports.SelectedItem is not Port p) { status.Text = "Bitte zuerst einen MIDI-Ausgang anlegen und auswählen."; return; }
        try { bridge = p.Id == uint.MaxValue ? new Bridge(null, transport.Checked, virtualPort!.Send) : new Bridge(p.Id, transport.Checked); SetRunning(true); }
        catch (Exception e) { status.Text = e.Message; }
    }
    void SetRunning(bool active) { ports.Enabled = refresh.Enabled = transport.Enabled = !active; run.Text = active ? "Clock stoppen" : "Clock starten"; }
    void UpdateStatus()
    {
        if (bridge == null) return;
        var s = Volatile.Read(ref bridge.State);
        tempo.Text = s.Received > 0 ? $"{s.Bpm:F2} BPM" : "— BPM";
        status.Text = bridge.Error.Length > 0 ? bridge.Error : ClockMath.Valid(s, ClockMath.Now) ? $"Clock sendet · {s.Peers} Link-Teilnehmer" : "Warte auf Link-Teilnehmer … Clock pausiert.";
        count.Text = $"MIDI-Ticks: {Interlocked.Read(ref bridge.Sent):N0} · Timing-Aussetzer: {Interlocked.Read(ref bridge.Skipped):N0}";
    }
}

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        using var singleton = new Mutex(true, @"Local\LJLinkMidiClockIntegrated", out bool first);
        if (!first && !args.Contains("--self-test")) { if (args.Length == 0) MessageBox.Show("Die MIDI-Clock-Brücke läuft bereits."); Environment.ExitCode = 2; return; }
        if (args.Contains("--list-ports"))
        {
            var entries = Windows.Devices.Midi2.Transports.Loopback.MidiLoopbackManager.GetActiveLoopbackEntries();
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "ports.json"), System.Text.Json.JsonSerializer.Serialize(entries.Select(e => new { e.AssociationId, A = e.EndpointA.Name, B = e.EndpointB.Name, DescriptionA = e.EndpointA.Description, DescriptionB = e.EndpointB.Description })));
            return;
        }
        if (args.Contains("--port-test"))
        {
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "port-test.txt"), PortTest.Run()); }
            catch (Exception e) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "port-test.txt"), e.ToString()); Environment.ExitCode = 1; }
            return;
        }
        if (args.Contains("--link-test"))
        {
            using var probe = new Bridge(null, false);
            Thread.Sleep(3000);
            var s = Volatile.Read(ref probe.State);
            string result = $"Link probe: BPM={s.Bpm:F3}; peers={s.Peers}; ticks={probe.Sent}; skipped={probe.Skipped}; error={probe.Error}";
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "link-test.txt"), result);
            if (s.Received == 0 || probe.Error.Length > 0) Environment.ExitCode = 1;
            return;
        }
        if (args.Contains("--self-test"))
        {
            void Assert(bool ok, string message) { if (!ok) throw new Exception(message); }
            Assert(ClockMath.Field("status { :bpm 123.450000 :peers 2 }", "bpm") == 123.45, "BPM parser");
            Assert(ClockMath.Field("beat-at-time { :beat -2.5 :when 123000 }", "beat") == -2.5, "Negative beat parser");
            foreach (double bpm in new[] { 60.0, 90, 120, 128, 180 })
            {
                var s = new Timeline(bpm, 0, 1_000_000, 1, 1_000_000);
                Assert(Math.Abs(ClockMath.Position(s, 61_000_000) - bpm * 24) < 0.00001, "24 PPQN");
                Assert(ClockMath.Valid(s, 1_100_000), "Live peer");
                Assert(!ClockMath.Valid(s with { Peers = 0 }, 1_100_000), "Peer loss");
                Assert(!ClockMath.Valid(s, 2_000_001), "Stale timeline");
            }
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "self-test.txt"), "PASS: parser, 24 PPQN at 5 tempos, peer loss, stale timeline.");
            return;
        }
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26200))
        {
            MessageBox.Show("The integrated MIDI connection requires Windows 11 25H2 or later.", "LJ Link MIDI Clock");
            Environment.ExitCode = 1; return;
        }
        ApplicationConfiguration.Initialize(); Application.Run(new MainForm());
    }
}

static class Components
{
    public static string Helper()
    {
        var assembly = typeof(Components).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith("Carabiner.exe")))!;
        using var buffer = new MemoryStream(); stream.CopyTo(buffer); byte[] bytes = buffer.ToArray();
        string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
        string directory = Path.Combine(Path.GetTempPath(), "LJLinkMidiClock", hash);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "Carabiner.exe");
        if (File.Exists(path) && Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))) == hash) return path;
        string temporary = Path.Combine(directory, Guid.NewGuid() + ".tmp");
        File.WriteAllBytes(temporary, bytes); File.Move(temporary, path, true); return path;
    }
}
