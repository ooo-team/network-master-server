using UnityEngine;
using NativeWebSocket;
using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// Клиент для подключения к signaling server'у через WebSocket
/// Обрабатывает подключение к комнате и обмен сообщениями между peer'ами
/// </summary>
public class SignalingClient : MonoBehaviour
{
    [Header("Signaling Server Settings")]
    /// <summary>
    /// URL WebSocket signaling server'а
    /// </summary>
    public string serverUrl = "ws://95.165.133.136:8080/ws";
    
    /// <summary>
    /// Название комнаты для подключения peer'ов
    /// </summary>
    public string roomCode = "test_room";
    
    /// <summary>
    /// ID этого peer'а (генерируется автоматически)
    /// </summary>
    public string peerIdPrefix = "unity_client";
    
    // WebSocket connection
    private WebSocket webSocket;
    private bool isConnected = false;
    private List<string> connectedPeers = new List<string>();
    
    // Events
    /// <summary>
    /// Событие: новый peer присоединился к комнате
    /// </summary>
    public event Action<string> OnPeerJoined;
    
    /// <summary>
    /// Событие: peer покинул комнату
    /// </summary>
    public event Action<string> OnPeerLeft;
    
    /// <summary>
    /// Событие: получено WebRTC signaling сообщение (offer, answer, ice_candidate)
    /// </summary>
    public event Action<SignalingMessage> OnSignalingMessage;
    
    // Properties
    public string PeerId => peerIdPrefix;
    public bool IsConnected => isConnected;
    public List<string> ConnectedPeers => new List<string>(connectedPeers);
    
    void Start()
    {
        // Generate random peer ID
        peerIdPrefix = "unity_client_" + UnityEngine.Random.Range(1000, 9999);
    }
    
    void Update()
    {
        // Dispatch WebSocket messages
        if (webSocket != null)
        {
            webSocket.DispatchMessageQueue();
        }
    }
    
    async void OnApplicationQuit()
    {
        if (webSocket != null)
        {
            await webSocket.Close();
        }
    }
    
    /// <summary>
    /// Подключиться к signaling server'у
    /// </summary>
    public async void Connect()
    {
        if (isConnected) return;
        
        Debug.Log("Connecting to signaling server...");
        
        string fullUrl = $"{serverUrl}?peer_id={peerIdPrefix}&room={roomCode}";
        webSocket = new WebSocket(fullUrl);
        
        webSocket.OnOpen += () =>
        {
            Debug.Log("Connected to signaling server");
            isConnected = true;
        };
        
        webSocket.OnMessage += (bytes) =>
        {
            string message = Encoding.UTF8.GetString(bytes);
            HandleSignalingMessage(message);
        };
        
        webSocket.OnError += (e) =>
        {
            Debug.LogError($"Signaling error: {e}");
        };
        
        webSocket.OnClose += (e) =>
        {
            Debug.Log("Signaling connection closed");
            isConnected = false;
            connectedPeers.Clear();
        };
        
        await webSocket.Connect();
    }
    
    /// <summary>
    /// Отключиться от signaling server'а
    /// </summary>
    public async void Disconnect()
    {
        if (webSocket != null)
        {
            await webSocket.Close();
        }
    }
    
    /// <summary>
    /// Обработать входящее сообщение от signaling server'а
    /// </summary>
    private void HandleSignalingMessage(string message)
    {
        try
        {
            SignalingMessage signalMsg = JsonUtility.FromJson<SignalingMessage>(message);
            
            switch (signalMsg.type)
            {
                case "peer_joined":
                    HandlePeerJoined(signalMsg);
                    break;
                case "peer_left":
                    HandlePeerLeft(signalMsg);
                    break;
                case "offer":
                case "answer":
                case "ice_candidate":
                    OnSignalingMessage?.Invoke(signalMsg);
                    break;
                default:
                    Debug.Log($"Unknown message type: {signalMsg.type}");
                    break;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to parse signaling message: {e.Message}");
        }
    }
    
    /// <summary>
    /// Обработать присоединение нового peer'а
    /// </summary>
    private void HandlePeerJoined(SignalingMessage msg)
    {
        if (!connectedPeers.Contains(msg.from) && msg.from != peerIdPrefix)
        {
            connectedPeers.Add(msg.from);
            Debug.Log($"Peer joined: {msg.from}");
            OnPeerJoined?.Invoke(msg.from);
        }
    }
    
    /// <summary>
    /// Обработать отключение peer'а
    /// </summary>
    private void HandlePeerLeft(SignalingMessage msg)
    {
        if (connectedPeers.Contains(msg.from))
        {
            connectedPeers.Remove(msg.from);
            Debug.Log($"Peer left: {msg.from}");
            OnPeerLeft?.Invoke(msg.from);
        }
    }
    
    /// <summary>
    /// Отправить сообщение через signaling server
    /// </summary>
    public async void SendMessage(SignalingMessage message)
    {
        if (webSocket != null && isConnected)
        {
            string jsonMessage = JsonUtility.ToJson(message);
            await webSocket.SendText(jsonMessage);
        }
    }
} 