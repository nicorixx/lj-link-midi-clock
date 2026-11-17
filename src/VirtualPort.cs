// SPDX-License-Identifier: GPL-2.0-or-later
// Copyright (c) 2026 nicorixx
// Uses the loopback API pattern from Microsoft's MIT-licensed MIDI Services samples.
// See THIRD_PARTY_NOTICES.md and licenses/MICROSOFT-MIT.txt.
using Windows.Devices.Midi2;
using Windows.Devices.Midi2.Transports.Loopback;
using System.Runtime.InteropServices;

namespace LinkMidiClock;

sealed class VirtualPort : IDisposable
{
    public const string Name = "LJ Link Clock";
    private Guid? association;
    private IntPtr output;
    public VirtualPort(Action<string>? trace = null)
    {
        try
        {
            trace?.Invoke("Service");
            if (!MidiApi.EnsureServiceAvailable()) throw new IOException("Windows-MIDI-Dienst ist nicht verfügbar.");
            if (!MidiLoopbackManager.IsTransportAvailable) throw new IOException("Windows-MIDI-Loopback ist nicht verfügbar.");
            // Only remove stale pairs produced by this application. The process-wide mutex
            // prevents removing endpoints belonging to another running bridge instance.
            foreach (var existing in MidiLoopbackManager.GetActiveLoopbackEntries())
            {
                if (existing.EndpointA.Name == "LJ Clock Sender" && existing.EndpointB.Name == Name &&
                    existing.EndpointA.Description == "Clock output of LJ Link bridge" &&
                    existing.EndpointB.Description == "MIDI Clock input for Daslight")
                {
                    var removed = MidiLoopbackManager.RemoveTransientLoopback(new MidiLoopbackRemovalConfig(existing.AssociationId));
                    if (!removed.Success) throw new IOException("Alter LJ-MIDI-Port konnte nicht bereinigt werden. Brücke schließen und erneut starten.");
                }
            }
            trace?.Invoke("CreateLoopback");
            const string id = "LJLinkClockBridge01";
            var a = new MidiLoopbackEndpointDefinition { Name = "LJ Clock Sender", Description = "Clock output of LJ Link bridge", UniqueId = id + "A" };
            var b = new MidiLoopbackEndpointDefinition { Name = Name, Description = "MIDI Clock input for Daslight", UniqueId = id + "B" };
            var response = MidiLoopbackManager.CreateTransientLoopback(new MidiLoopbackCreationConfig(a, b));
            if (!response.Success) throw new IOException("Windows konnte den MIDI-Port nicht erstellen.");
            association = response.CreatedLoopbackEntry.AssociationId;
            trace?.Invoke("LoopbackCreated");
            for (int attempt = 0; attempt < 50; attempt++)
            {
                for (uint i = 0; i < Midi.midiOutGetNumDevs(); i++)
                {
                    if (Midi.midiOutGetDevCapsW((UIntPtr)i, out var caps, (uint)Marshal.SizeOf<Midi.Caps>()) == 0 && caps.Name.Contains("LJ Clock Sender"))
                    { Midi.Check(Midi.midiOutOpen(out output, i, IntPtr.Zero, IntPtr.Zero, 0)); trace?.Invoke("Opened"); return; }
                }
                Thread.Sleep(100);
            }
            throw new IOException("Erstellter MIDI-Port ist für klassische MIDI-Anwendungen nicht sichtbar.");
        }
        catch { Dispose(); throw; }
    }
    public void Send(uint status)
    {
        Midi.Check(Midi.midiOutShortMsg(output, status));
    }
    public void Dispose()
    {
        if (output != IntPtr.Zero) { Midi.midiOutClose(output); output = IntPtr.Zero; }
        if (association is Guid id)
        { MidiLoopbackManager.RemoveTransientLoopback(new MidiLoopbackRemovalConfig(id)); association = null; }
    }
}

static class PortTest
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void Callback(IntPtr handle, uint message, UIntPtr instance, UIntPtr data, UIntPtr timestamp);
    [DllImport("winmm.dll")] static extern uint midiInGetNumDevs();
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct Caps { public ushort Manufacturer, Product; public uint Version; [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string Name; public uint Support; }
    [DllImport("winmm.dll", CharSet=CharSet.Unicode)] static extern uint midiInGetDevCapsW(UIntPtr id, out Caps caps, uint size);
    [DllImport("winmm.dll")] static extern uint midiInOpen(out IntPtr handle, uint id, Callback callback, UIntPtr instance, uint flags);
    [DllImport("winmm.dll")] static extern uint midiInStart(IntPtr handle);
    [DllImport("winmm.dll")] static extern uint midiInStop(IntPtr handle);
    [DllImport("winmm.dll")] static extern uint midiInClose(IntPtr handle);
    public static string Run()
    {
        using var port = new VirtualPort(message => File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "port-progress.txt"), message + "\n"));
        uint? index = null;
        var names = new List<string>();
        for (int attempt = 0; attempt < 50 && index == null; attempt++)
        {
            names.Clear();
            for (uint i = 0; i < midiInGetNumDevs(); i++)
            {
                if (midiInGetDevCapsW((UIntPtr)i, out var caps, (uint)Marshal.SizeOf<Caps>()) == 0)
                { names.Add(caps.Name); if (caps.Name.Contains(VirtualPort.Name)) index = i; }
            }
            if (index == null) Thread.Sleep(100);
        }
        if (index == null) throw new IOException("Port nicht in WinMM sichtbar: " + string.Join(", ", names));
        int ticks = 0;
        Callback callback = (h,m,i,d,t) => { if (m == 0x3C3 && ((ulong)d & 255) == 0xF8) Interlocked.Increment(ref ticks); };
        Midi.Check(midiInOpen(out var handle, index.Value, callback, UIntPtr.Zero, 0x30000));
        try
        {
            Midi.Check(midiInStart(handle));
            for (int i = 0; i < 24; i++) { port.Send(0xF8); Thread.Sleep(10); }
            Thread.Sleep(200);
            if (ticks != 24) throw new IOException($"WinMM empfing {ticks} von 24 Clock-Nachrichten.");
            return $"PASS: Eigener virtueller MIDI-Port in WinMM sichtbar, {ticks}/24 MIDI-Clock-Nachrichten empfangen.";
        }
        finally { midiInStop(handle); midiInClose(handle); GC.KeepAlive(callback); }
    }
}
