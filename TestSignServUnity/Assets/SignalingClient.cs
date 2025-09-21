using UnityEngine;
using NativeWebSocket;
using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// ПРОСТОЙ клиент для подключения к signaling серверу
/// Помогает пирам найти друг друга и обменяться WebRTC данными
/// </summary>
public class SignalingClient : MonoBehaviour
{
     [Header("Server Settings")]
    public string serverUrl = "ws://95.165.133.136:8080/ws";
    public string roomCode = "test_room";
    public string thisPeerID = "unity_client";
    
    // Внутреннее состояние
    private WebSocket webSocket;
    private bool isConnected = false;
    
    // События - просто и понятно
    public event Action<string> OnPeerJoined;           // Кто-то присоединился
    public event Action<string> OnPeerLeft;             // Кто-то ушёл
    public event Action<SignalingMessage> OnSignalingMessage; // WebRTC сообщение
    public event Action OnConnected;                    // Мы подключились
    
    // Публичные свойства
    public string PeerId => thisPeerID;
    public bool IsConnected => isConnected;
    public List<string> PeersInRoom = new();

    void Start()
    {
        // Генерируем уникальный ID для этого пира
        thisPeerID = "unity_client_" + UnityEngine.Random.Range(1000, 9999);
        PeersInRoom.Add(thisPeerID);
    }
    
    void Update()
    {
        // Обрабатываем WebSocket сообщения
        webSocket?.DispatchMessageQueue();
    }
    
    async void OnApplicationQuit()
    {
        await webSocket?.Close();
    }
    
    /// <summary>
    /// ПОДКЛЮЧИТЬСЯ к signaling серверу
    /// </summary>
    public async void Connect()
    {
        if (isConnected) return;
        
        Debug.Log($"🔌 Подключаемся к серверу: {roomCode}");
        
        string url = $"{serverUrl}?peer_id={thisPeerID}&room={roomCode}";
        webSocket = new WebSocket(url);
        
        // Настраиваем события WebSocket
        webSocket.OnOpen += () =>
        {
            Debug.Log("✅ Подключились к signaling серверу");
            isConnected = true;
            OnConnected?.Invoke();
        };
        
        webSocket.OnMessage += (bytes) =>
        {
            string message = Encoding.UTF8.GetString(bytes);
            HandleMessage(message);
        };
        
        webSocket.OnError += (error) => Debug.LogError($"❌ Ошибка WebSocket: {error}");
        
        webSocket.OnClose += (code) =>
        {
            Debug.Log("🚪 Отключились от signaling сервера");
            isConnected = false;
            PeersInRoom.Clear();
        };
        
        await webSocket.Connect();
    }
    
    /// <summary>
    /// ОТКЛЮЧИТЬСЯ от signaling сервера
    /// </summary>
    public async void Disconnect()
    {
        await webSocket?.Close();
    }
    
    /// <summary>
    /// Обработать сообщение от сервера
    /// </summary>
    private void HandleMessage(string message)
    {
        try
        {
            var msg = JsonUtility.FromJson<SignalingMessage>(message);
            if (msg == null) return;
            
            Debug.Log($"📨 {msg.type} от {msg.from}");
            
            switch (msg.type)
            {
                case "peer_joined":
                    HandlePeerJoined(msg);
                    break;
                case "peer_left":
                    HandlePeerLeft(msg);
                    break;
                case "offer":
                case "answer":
                case "ice_candidate":
                    OnSignalingMessage?.Invoke(msg); // Передаём в WebRTCManager
                    break;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"❌ Ошибка парсинга сообщения: {e.Message}");
        }
    }
    
    void HandlePeerJoined(SignalingMessage msg)
    {
        // Пытаемся получить полный список пиров из payload
        try
        {
            if (!string.IsNullOrEmpty(msg.payload))
            {
                var data = JsonUtility.FromJson<PeerJoinedData>(msg.payload);
                if (data?.all_peers != null)
                {
                    // Обновляем список всех пиров в комнате
                    PeersInRoom.Clear();
                    PeersInRoom.Add(thisPeerID); // Себя тоже добавляем
                    
                    foreach (string peerId in data.all_peers)
                    {
                        if (peerId != thisPeerID) PeersInRoom.Add(peerId);
                    }
                    
                    Debug.Log($"📋 Обновили список пиров: {string.Join(", ", PeersInRoom)}");
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"⚠️ Не удалось разобрать peer_joined: {e.Message}");
        }

        // Добавляем пира если его нет (для совместимости)
        if (!PeersInRoom.Contains(msg.from) && msg.from != thisPeerID)
        {
            PeersInRoom.Add(msg.from);
        }
        
        OnPeerJoined?.Invoke(msg.from);
    }
    
    void HandlePeerLeft(SignalingMessage msg)
    {
        if (PeersInRoom.Remove(msg.from))
        {
            Debug.Log($"🚪 {msg.from} покинул комнату");
            OnPeerLeft?.Invoke(msg.from);
        }
    }
    
    /// <summary>
    /// ОТПРАВИТЬ сообщение через signaling сервер
    /// </summary>
    public async void SendMessage(SignalingMessage message)
    {
        if (webSocket != null && isConnected)
        {
            string json = JsonUtility.ToJson(message);
            await webSocket.SendText(json);
        }
    }
} 