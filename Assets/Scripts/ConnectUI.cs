using System;
using System.Reflection;
using FishNet.Managing;
using UnityEngine;
using UnityEngine.UI;

public class ConnectUI : MonoBehaviour
{
    [Header("References")]
    [Tooltip("FishNet NetworkManager present in this scene.")]
    [SerializeField] private NetworkManager networkManager;

    [Tooltip("UI InputField for server IP address.")]
    [SerializeField] private InputField ipField;

    [Tooltip("UI InputField for server port.")]
    [SerializeField] private InputField portField;

    [Tooltip("UI InputField for player nickname (max 20 chars). Sauvegardé dans PlayerPrefs.")]
    [SerializeField] private InputField nicknameField;

    [Tooltip("Button to trigger the connection.")]
    [SerializeField] private Button connectButton;

    [Header("Defaults")]
    [Tooltip("Default IP shown if input is empty.")]
    [SerializeField] private string defaultIp = "127.0.0.1";

    [Tooltip("Default port used if parsing fails or input is empty.")]
    [SerializeField] private ushort defaultPort = 7777;

    private const string NickPrefsKey = "player_nickname";
    private const int NickMaxLen = 20;

    private void Awake()
    {
        if (networkManager == null)
            networkManager = FindFirstObjectByType<NetworkManager>();

        // Pre-fill the UI if empty.
        if (ipField != null && string.IsNullOrWhiteSpace(ipField.text))
            ipField.text = defaultIp;
        if (portField != null && string.IsNullOrWhiteSpace(portField.text))
            portField.text = defaultPort.ToString();

        // Pré-remplir le pseudo avec la dernière valeur mémorisée.
        if (nicknameField != null && string.IsNullOrWhiteSpace(nicknameField.text))
        {
            string saved = PlayerPrefs.GetString(NickPrefsKey, string.Empty);
            if (string.IsNullOrWhiteSpace(saved))
            {
                saved = $"Player{UnityEngine.Random.Range(1000, 9999)}"; // valeur de confort
            }
            nicknameField.text = saved;
        }

        if (connectButton != null)
            connectButton.onClick.AddListener(OnClick_Connect);
    }

    private void OnDestroy()
    {
        if (connectButton != null)
            connectButton.onClick.RemoveListener(OnClick_Connect);
    }

    /// <summary>
    /// Called by the Connect button (or via UnityEvent) to start the client.
    /// </summary>
    public void OnClick_Connect()
    {
        if (networkManager == null)
        {
            Debug.LogError("[ConnectUI] NetworkManager is not assigned or found.");
            return;
        }

        string ip = (ipField != null) ? ipField.text?.Trim() : null;
        if (string.IsNullOrWhiteSpace(ip))
            ip = defaultIp;

        ushort port = defaultPort;
        if (portField != null && ushort.TryParse(portField.text, out var parsed))
            port = parsed;

        // Lire / valider le pseudo et le sauvegarder pour que PlayerDisplayName le récupère au spawn.
        string nickname = (nicknameField != null) ? nicknameField.text : string.Empty;
        nickname = SanitizeNickname(nickname);
        if (string.IsNullOrWhiteSpace(nickname))
            nickname = $"Player{UnityEngine.Random.Range(1000, 9999)}";
        PlayerPrefs.SetString(NickPrefsKey, nickname);
        PlayerPrefs.Save();

        var transport = networkManager.TransportManager?.Transport;
        if (transport == null)
        {
            Debug.LogError("[ConnectUI] No Transport found on NetworkManager.TransportManager.");
            return;
        }

        bool addressSet = TrySetClientAddress(transport, ip);
        bool portSet = TrySetPort(transport, port);

        if (!addressSet)
            Debug.LogWarning($"[ConnectUI] Could not set client address on transport via known members. Using transport defaults. Wanted='{ip}'.");
        if (!portSet)
            Debug.LogWarning($"[ConnectUI] Could not set port on transport via known members. Using transport defaults. Wanted={port}.");

        Debug.Log($"[ConnectUI] Connecting to {ip}:{port} ...");
        networkManager.ClientManager.StartConnection();
    }

    #region Transport configuration helpers (reflection)
    private static bool TrySetClientAddress(object transport, string ip)
    {
        if (transport == null) return false;
        Type t = transport.GetType();

        // Common property/field names used by FishNet transports
        string[] propNames = { "Address", "ClientAddress", "RemoteAddress", "Hostname" };
        foreach (var name in propNames)
        {
            var prop = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (prop != null && prop.CanWrite && prop.PropertyType == typeof(string))
            {
                try { prop.SetValue(transport, ip); return true; } catch { }
            }
        }

        string[] fieldNames = { "Address", "ClientAddress", "RemoteAddress", "Hostname" };
        foreach (var name in fieldNames)
        {
            var field = t.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (field != null && field.FieldType == typeof(string))
            {
                try { field.SetValue(transport, ip); return true; } catch { }
            }
        }

        // Some transports expose methods like SetClientAddress(string)
        var m = t.GetMethod("SetClientAddress", BindingFlags.Public | BindingFlags.Instance);
        if (m != null)
        {
            var ps = m.GetParameters();
            if (ps.Length == 1 && ps[0].ParameterType == typeof(string))
            {
                try { m.Invoke(transport, new object[] { ip }); return true; } catch { }
            }
        }

        return false;
    }

    private static bool TrySetPort(object transport, ushort port)
    {
        if (transport == null) return false;
        Type t = transport.GetType();

        // Common property/field names for port
        string[] propNames = { "Port", "ClientPort", "RemotePort" };
        foreach (var name in propNames)
        {
            var prop = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (prop != null && prop.CanWrite && (prop.PropertyType == typeof(ushort) || prop.PropertyType == typeof(int)))
            {
                try { object v = prop.PropertyType == typeof(int) ? (object)(int)port : port; prop.SetValue(transport, v); return true; } catch { }
            }
        }

        string[] fieldNames = { "Port", "ClientPort", "RemotePort" };
        foreach (var name in fieldNames)
        {
            var field = t.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (field != null && (field.FieldType == typeof(ushort) || field.FieldType == typeof(int)))
            {
                try { object v = field.FieldType == typeof(int) ? (object)(int)port : port; field.SetValue(transport, v); return true; } catch { }
            }
        }

        // Some transports expose methods like SetPort(ushort/int)
        var m = t.GetMethod("SetPort", BindingFlags.Public | BindingFlags.Instance);
        if (m != null)
        {
            var ps = m.GetParameters();
            if (ps.Length == 1 && (ps[0].ParameterType == typeof(ushort) || ps[0].ParameterType == typeof(int)))
            {
                try { object v = ps[0].ParameterType == typeof(int) ? (object)(int)port : port; m.Invoke(transport, new object[] { v }); return true; } catch { }
            }
        }

        return false;
    }
    #endregion

    private static string SanitizeNickname(string input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        input = input.Trim();
        if (input.Length > NickMaxLen)
            input = input.Substring(0, NickMaxLen);

        System.Text.StringBuilder sb = new System.Text.StringBuilder(input.Length);
        foreach (char c in input)
        {
            if (char.IsLetterOrDigit(c) || c == ' ' || c == '_' || c == '-')
                sb.Append(c);
        }
        return sb.ToString();
    }
}
