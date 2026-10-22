using Montro.Remote;

namespace Montro.Core;

/// <summary>Einstellungen der Fernsteuerung (Mac: Einstellungen › Fernsteuerung).</summary>
public sealed partial class AppState
{
    private bool remoteEnabled = true;
    private int remotePort = RemoteApi.DefaultPort;
    private bool remoteLocalOnly;
    private RemoteApi? remote;

    /// <summary>Server der Fernsteuerung (null in Tests ohne Oberfläche).</summary>
    public RemoteApi? Remote => remote;

    public bool RemoteEnabled
    {
        get => remoteEnabled;
        set { if (Set(ref remoteEnabled, value)) RemoteChanged(); }
    }

    public int RemotePort
    {
        get => remotePort;
        set { if (Set(ref remotePort, Math.Clamp(value, 1, 65535))) RemoteChanged(); }
    }

    /// <summary>Nur Verbindungen von diesem Rechner (127.0.0.1) – für fremde Netze.</summary>
    public bool RemoteLocalOnly
    {
        get => remoteLocalOnly;
        set { if (Set(ref remoteLocalOnly, value)) RemoteChanged(); }
    }

    private void RemoteChanged()
    {
        QueueSave();
        remote?.Restart();
    }

    private void StartRemote(SavedState s)
    {
        remoteEnabled = s.RemoteEnabled ?? true;
        remotePort = s.RemotePort is int p and > 0 and < 65536 ? p : RemoteApi.DefaultPort;
        remoteLocalOnly = s.RemoteLocalOnly;
        remote = new RemoteApi(this);
        remote.Restart();
    }
}
