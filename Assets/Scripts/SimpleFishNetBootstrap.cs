using System;
using FishNet.Managing;
using FishNet.Managing.Client;
using FishNet.Managing.Server;
using FishNet.Connection;
using FishNet.Transporting;
using FishNet.Object;
using UnityEngine;
using FishNet.Transporting.Tugboat;
using FishNet.Managing.Timing;

[DisallowMultipleComponent]
[DefaultExecutionOrder(-10000)]
public class SimpleFishNetBootstrap : MonoBehaviour
{
    public enum RunMode { Server, Client }

    [Header("Mode")]
    [SerializeField] private RunMode mode = RunMode.Server;

    [Header("Auto Start")]
    [Tooltip("If true, will automatically start on Awake for Server mode only. Client mode will not auto-connect.")]
    [SerializeField] private bool autoStartOnAwake = true;

    [Header("Client Settings")]
    [Tooltip("IP or hostname the client will connect to.")]
    [SerializeField] private string address = "127.0.0.1";

    [Tooltip("Port for the transport (FishNet Tugboat default is 7770 unless changed in the transport). This value is only applied if your chosen transport reads it from NetworkManager settings or command line. If your transport manages the port internally, leave at default.")]
    [SerializeField] private ushort port = 7770;

    [Header("Network Resilience")]
    [Tooltip("Timeout en secondes avant de déconnecter une connexion silencieuse côté serveur.")]
    [SerializeField] private int serverTimeoutSeconds = 15;
    [Tooltip("Timeout en secondes avant de déconnecter une connexion silencieuse côté client.")]
    [SerializeField] private int clientTimeoutSeconds = 15;

    [Header("Timing")]
    [Tooltip("Tick rate réseau FishNet. Laisser 0 pour ne pas modifier.")]
    [SerializeField] private ushort tickRate = 30;

    [Header("Debug Réseau")]
    [SerializeField] private bool logNetworkStats = true;
    [SerializeField] private float logIntervalSeconds = 5f;

    private NetworkManager _nm;

    private void Awake()
    {
        _nm = FindObjectOfType<NetworkManager>();
        if (_nm == null)
        {
            Debug.LogError("[SimpleFishNetBootstrap] No NetworkManager found in the scene. Please add one.");
            return;
        }

        // Appliquer configuration transport / timing avant tout démarrage.
        ConfigureTransportAndTiming();

        SubscribeServerEvents(true);

        TrySubscribeRttLogging(true);

        ApplyCommandLineOverrides();

        if (autoStartOnAwake)
        {
            if (mode == RunMode.Server)
            {
                StartServer();
            }
            else
            {
                Debug.Log("[SimpleFishNetBootstrap] Auto-start on Awake is disabled for Client mode. Use your Connect UI or call StartNow() manually.");
            }
        }
    }

    [ContextMenu("Start Now")] 
    public void StartNow()
    {
        if (_nm == null)
        {
            Debug.LogError("[SimpleFishNetBootstrap] Cannot start: NetworkManager is missing.");
            return;
        }

        switch (mode)
        {
            case RunMode.Server:
                StartServer();
                break;
            case RunMode.Client:
                StartClient();
                break;
            default:
                Debug.LogError("[SimpleFishNetBootstrap] Unknown mode.");
                break;
        }
    }

    private void StartServer()
    {
        if (_nm.ServerManager == null)
        {
            Debug.LogError("[SimpleFishNetBootstrap] ServerManager is missing on NetworkManager.");
            return;
        }

        if (_nm.ServerManager.Started)
        {
            Debug.Log("[SimpleFishNetBootstrap] Server already started.");
            // Even if already started, ensure scene NetworkObjects are spawned.
            TrySpawnSceneNetworkObjects();
            return;
        }

        bool started = _nm.ServerManager.StartConnection(port);
        Debug.Log(started
            ? "[SimpleFishNetBootstrap] Server started."
            : "[SimpleFishNetBootstrap] Failed to start server.");

        if (started)
        {
            TrySpawnSceneNetworkObjects();
        }
    }

    private void StartClient()
    {
        if (_nm.ClientManager == null)
        {
            Debug.LogError("[SimpleFishNetBootstrap] ClientManager is missing on NetworkManager.");
            return;
        }

        if (_nm.ClientManager.Started)
        {
            Debug.Log("[SimpleFishNetBootstrap] Client already started.");
            return;
        }

        try
        {
            bool started;
            var miAddrPort = typeof(ClientManager).GetMethod("StartConnection", new Type[] { typeof(string), typeof(ushort) });
            if (miAddrPort != null)
            {
                var result = miAddrPort.Invoke(_nm.ClientManager, new object[] { address, port });
                started = result is bool b && b;
            }
            else
            {
                var miAddrOnly = typeof(ClientManager).GetMethod("StartConnection", new Type[] { typeof(string) });
                if (miAddrOnly != null)
                {
                    var result = miAddrOnly.Invoke(_nm.ClientManager, new object[] { address });
                    started = result is bool b && b;
                }
                else
                {
                    started = _nm.ClientManager.StartConnection();
                }
            }

            Debug.Log(started
                ? $"[SimpleFishNetBootstrap] Client started. Connecting to {address}:{port}."
                : "[SimpleFishNetBootstrap] Failed to start client.");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SimpleFishNetBootstrap] Exception while starting client: {ex}");
        }
    }

    private void ApplyCommandLineOverrides()
    {
        try
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (string.Equals(a, "-server", StringComparison.OrdinalIgnoreCase))
                    mode = RunMode.Server;
                else if (string.Equals(a, "-client", StringComparison.OrdinalIgnoreCase))
                    mode = RunMode.Client;
                else if (string.Equals(a, "-address", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    address = args[++i];
                else if (string.Equals(a, "-port", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length && ushort.TryParse(args[i + 1], out var p))
                {
                    port = p;
                    i++;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[SimpleFishNetBootstrap] Failed to parse command line overrides: {ex.Message}");
        }
    }
    private void OnDestroy()
    {
        SubscribeServerEvents(false);
        TrySubscribeRttLogging(false);
    }
    
    private void TrySpawnSceneNetworkObjects()
    {
        if (_nm == null || _nm.ServerManager == null || !_nm.ServerManager.Started)
            return;

        int attempted = 0;
        int spawned = 0;
        var all = FindObjectsOfType<NetworkObject>(true);
        foreach (var no in all)
        {
            if (no == null) continue;
            attempted++;
            if (!no.IsSpawned)
            {
                try
                {
                    _nm.ServerManager.Spawn(no);
                    spawned++;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[SimpleFishNetBootstrap] Failed to spawn NetworkObject '{no.name}': {ex.Message}");
                }
            }
        }
        Debug.Log($"[SimpleFishNetBootstrap] Checked {attempted} NetworkObjects in scene, spawned {spawned} that were not yet spawned.");
    }

    private void SubscribeServerEvents(bool subscribe)
    {
        if (_nm == null)
            return;
        var sm = _nm.ServerManager;
        if (sm == null)
            return;

        if (subscribe)
            sm.OnRemoteConnectionState += ServerManager_OnRemoteConnectionState;
        else
            sm.OnRemoteConnectionState -= ServerManager_OnRemoteConnectionState;
    }

    private void ServerManager_OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
    {
        if (args.ConnectionState == RemoteConnectionState.Started)
        {
            Debug.Log($"[SimpleFishNetBootstrap][Server] Client connected. Id={conn.ClientId}, TransportIndex={args.TransportIndex}");
        }
        else if (args.ConnectionState == RemoteConnectionState.Stopped)
        {
            Debug.Log($"[SimpleFishNetBootstrap][Server] Client disconnected. Id={conn.ClientId}, TransportIndex={args.TransportIndex}");
        }
    }

    /// <summary>
    /// Configure le transport Tugboat (adresse, port, timeouts) et le TimeManager (tick rate) avant de démarrer.
    /// </summary>
    private void ConfigureTransportAndTiming()
    {
        try
        {
            if (_nm == null) return;

            // Configurer Transport (Tugboat par défaut dans ce projet).
            var transport = _nm.TransportManager != null ? _nm.TransportManager.Transport : null;
            if (transport is Tugboat tb)
            {
                // Port côté serveur et client.
                tb.SetPort(port);
                // Adresse cible côté client (au cas où Client.StartConnection() sans paramètres serait utilisé).
                tb.SetClientAddress(address);
                // Bind serveur sur toutes interfaces IPv4 pour connexions distantes.
                tb.SetServerBindAddress("0.0.0.0", FishNet.Transporting.IPAddressType.IPv4);

                // Timeouts plus tolérants pour réseaux distants.
                if (serverTimeoutSeconds > 0)
                    tb.SetTimeout(serverTimeoutSeconds, asServer: true);
                if (clientTimeoutSeconds > 0)
                    tb.SetTimeout(clientTimeoutSeconds, asServer: false);
            }

            // Configurer le tick rate si demandé.
            if (tickRate > 0)
            {
                TimeManager tm = _nm.TimeManager;
                if (tm != null)
                {
                    tm.SetTickRate(tickRate);
                    Debug.Log($"[SimpleFishNetBootstrap] TimeManager tickRate réglé à {tickRate}.");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[SimpleFishNetBootstrap] ConfigureTransportAndTiming exception: {ex.Message}");
        }
    }

    private void TrySubscribeRttLogging(bool subscribe)
    {
        if (_nm == null) return;
        var tm = _nm.TimeManager;
        if (tm == null) return;

        if (subscribe)
        {
            if (logNetworkStats)
            {
                // Log périodique car l’événement peut être très fréquent.
                if (logIntervalSeconds <= 0f) logIntervalSeconds = 5f;
                CancelInvoke(nameof(LogNetworkStats));
                InvokeRepeating(nameof(LogNetworkStats), 2f, logIntervalSeconds);
            }
        }
        else
        {
            CancelInvoke(nameof(LogNetworkStats));
        }
    }

    private void LogNetworkStats()
    {
        if (_nm == null || _nm.TimeManager == null) return;
        long rtt = _nm.TimeManager.RoundTripTime;
        float pl = 0f;
        try
        {
            var transport = _nm.TransportManager != null ? _nm.TransportManager.Transport : null;
            if (transport is Tugboat tb)
                pl = tb.GetPacketLoss(false);
        }
        catch { }
        Debug.Log($"[SimpleFishNetBootstrap] RTT={rtt} ms, PacketLoss~{pl:0.0}% vers serveur {address}:{port}.");
    }
}
