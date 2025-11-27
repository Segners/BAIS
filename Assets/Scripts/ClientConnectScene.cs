using FishNet.Managing;
using FishNet.Managing.Client;
using FishNet.Transporting;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Simple controller for a "Connect" scene:
/// - (Optionally) spawns a HUD prefab that lets the player enter server IP/port.
/// - Listens for FishNet client connection success and then loads the target client scene.
///
/// Usage:
/// 1) Create a new Unity scene (e.g. "ConnectScene").
/// 2) Add an empty GameObject and attach this component.
/// 3) Assign your NetworkManager in the inspector.
/// 4) (Optional) Drag FishNet Demo prefab "NetworkHudCanvas" into Hud Prefab field so players can type IP/port.
/// 5) Ensure the ConnectScene is first in Build Settings. When client connects, the scene will switch to ClientScene.
///
/// Note: For production you likely want to use FishNet.SceneManager with server-driven scene changes.
/// This component does a local LoadScene after a successful client connect to satisfy the requested flow.
/// </summary>
public class ClientConnectScene : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Reference to the FishNet NetworkManager in this scene.")]
    [SerializeField] private NetworkManager networkManager;

    [Tooltip("Optional: A UI prefab that contains fields/buttons to enter IP/Port and connect. The FishNet demo prefab 'NetworkHudCanvas' works.")]
    [SerializeField] private GameObject hudPrefab;

    [Header("Flow")] 
    [Tooltip("Scene name to load on successful client connection.")]
    [SerializeField] private string clientSceneName = "ClientScene";

    [Tooltip("If true, instantiates the HUD prefab at Start when none exists in the scene.")]
    [SerializeField] private bool autoSpawnHud = true;

    private GameObject _spawnedHud;

    private void Awake()
    {
        if (networkManager == null)
            networkManager = FindFirstObjectByType<NetworkManager>();

        if (networkManager == null)
        {
            Debug.LogError("[ClientConnectScene] NetworkManager not found in scene. Please assign it in the inspector.");
        }
    }

    private void OnEnable()
    {
        if (networkManager != null && networkManager.ClientManager != null)
            networkManager.ClientManager.OnClientConnectionState += OnClientConnectionState;
    }

    private void Start()
    {
        // Optionally spawn the HUD (for entering IP/port and connecting)
        if (autoSpawnHud && hudPrefab != null && _spawnedHud == null)
        {
            _spawnedHud = Instantiate(hudPrefab);
        }
    }

    private void OnDisable()
    {
        if (networkManager != null && networkManager.ClientManager != null)
            networkManager.ClientManager.OnClientConnectionState -= OnClientConnectionState;
    }

    private void OnDestroy()
    {
        if (_spawnedHud != null)
            Destroy(_spawnedHud);
    }

    private void OnClientConnectionState(ClientConnectionStateArgs args)
    {
        if (args.ConnectionState == LocalConnectionState.Started)
        {
            if (string.IsNullOrWhiteSpace(clientSceneName))
            {
                Debug.LogWarning("[ClientConnectScene] Client connected but no clientSceneName specified.");
                return;
            }

            Debug.Log($"[ClientConnectScene] Connected to server. Loading scene '{clientSceneName}'...");

            // Load the client gameplay scene locally.
            // In a fully networked flow, the server would direct scene changes via FishNet.SceneManager.
            SceneManager.LoadScene(clientSceneName);
        }
    }
}
