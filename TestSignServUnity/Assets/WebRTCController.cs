using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// ПРОСТОЙ контроллер для mesh-чата
/// Соединяет UI с WebRTC и Signaling
/// </summary>
public class WebRTCController : MonoBehaviour
{
    [Header("📱 UI Элементы")]
    public Button connectButton;        // Подключиться к комнате
    public Button disconnectButton;     // Отключиться
    public TMP_InputField roomInput;    // Название комнаты
    public TextMeshProUGUI statusText;  // Статус соединения
    public TextMeshProUGUI peerListText;// Список пиров
    public TMP_InputField messageInput; // Ввод сообщения
    public Button sendMessageButton;    // Отправить сообщение
    public TextMeshProUGUI messagesText;// Все сообщения
    
    // Наши компоненты
    private SignalingClient signaling;
    private WebRTCManager webrtc;
    
    // Сообщения чата
    private readonly List<string> messages = new();

    void Start()
    {
        // Получаем компоненты (или создаём если нет)
        signaling = GetComponent<SignalingClient>() ?? gameObject.AddComponent<SignalingClient>();
        webrtc = GetComponent<WebRTCManager>() ?? gameObject.AddComponent<WebRTCManager>();
        
        // Настраиваем кнопки
        connectButton?.onClick.AddListener(() => ConnectToRoom());
        disconnectButton?.onClick.AddListener(() => DisconnectFromRoom());
        sendMessageButton?.onClick.AddListener(() => SendMessage());
        
        // Подписываемся на события
        signaling.OnPeerJoined += WhenPeerJoined;
        signaling.OnPeerLeft += WhenPeerLeft;
        signaling.OnSignalingMessage += webrtc.HandleSignalingMessage;
        signaling.OnConnected += WhenConnectedToSignaling;
        
        webrtc.OnMessageReceived += WhenMessageReceived;
        webrtc.OnPeerConnected += WhenPeerConnected;
        webrtc.OnPeerDisconnected += WhenPeerDisconnected;
        
        // Устанавливаем начальное состояние UI
        UpdateUI();
        
        // Устанавливаем название комнаты по умолчанию
        if (roomInput != null) roomInput.text = signaling.roomCode;
    }

    // === ПРОСТЫЕ МЕТОДЫ ДЛЯ КНОПОК ===
    
    void ConnectToRoom()
    {
        if (roomInput != null) signaling.roomCode = roomInput.text;
        signaling.Connect();
        UpdateUI();
    }
    
    void DisconnectFromRoom()
    {
        webrtc.DisconnectAll();
        signaling.Disconnect();
        UpdateUI();
    }
    
    void SendMessage()
    {
        if (messageInput != null && !string.IsNullOrEmpty(messageInput.text))
        {
            string msg = messageInput.text;
            AddMessage($"Я: {msg}");
            messageInput.text = "";
            webrtc.BroadcastMessage(msg);
        }
    }

    // === ПРОСТЫЕ ОБРАБОТЧИКИ СОБЫТИЙ ===
    
    void WhenPeerJoined(string peerId)
    {
        // Только обновляем список пиров, без сообщения в чат
        UpdatePeerList();
        ConnectToAllPeers(); // Подключаемся ко всем пирам в комнате
    }
    
    void WhenPeerLeft(string peerId)
    {
        // Только обновляем список пиров, без сообщения в чат
        webrtc.DisconnectPeer(peerId);
        UpdatePeerList();
    }
    
    void WhenConnectedToSignaling()
    {
        // Только обновляем список пиров, без сообщения в чат
        UpdatePeerList();
        ConnectToAllPeers(); // Подключаемся к уже существующим пирам
    }
    
    void WhenMessageReceived(string fromPeer, string message)
    {
        // ЭТО остается в чате - пользовательские сообщения
        AddMessage($"{fromPeer}: {message}");
    }
    
    void WhenPeerConnected(string peerId)
    {
        // Только обновляем список пиров, без сообщения в чат
        UpdatePeerList();
    }
    
    void WhenPeerDisconnected(string peerId)
    {
        // Только обновляем список пиров, без сообщения в чат
        UpdatePeerList();
    }
    
    /// <summary>
    /// ГЛАВНЫЙ МЕТОД: Подключиться ко всем пирам в комнате
    /// </summary>
    void ConnectToAllPeers()
    {
        if (signaling != null && signaling.IsConnected)
        {
            webrtc.ConnectToAllPeers(signaling.PeersInRoom, signaling.PeerId);
        }
    }

    // === ПРОСТЫЕ МЕТОДЫ UI ===
    
    void UpdatePeerList()
    {
        if (peerListText == null) return;
        
        var lines = new List<string>();
        
        // Добавляем себя с информацией о signaling
        if (signaling?.IsConnected == true)
        {
            lines.Add($"📱 {signaling.PeerId} (Я)");
            lines.Add($"🌐 Signaling: WebSocket OK");
            lines.Add(""); // Пустая строка для разделения
        }
        
        // Добавляем других пиров с детальной информацией
        if (signaling?.PeersInRoom != null)
        {
            foreach (string peerId in signaling.PeersInRoom)
            {
                if (peerId != signaling.PeerId)
                {
                    lines.Add($"👤 {peerId}");
                    
                    if (webrtc.IsConnectedToPeer(peerId))
                    {
                        lines.Add("  🔗 WebRTC: ✅ Connected");
                        // Получаем детальную информацию о соединении
                        string connectionInfo = webrtc.GetConnectionDetails(peerId);
                        if (!string.IsNullOrEmpty(connectionInfo))
                        {
                            var details = connectionInfo.Split(',');
                            foreach (var detail in details)
                            {
                                lines.Add($"  {detail.Trim()}");
                            }
                        }
                    }
                    else
                    {
                        lines.Add("  🔗 WebRTC: ⏳ Connecting...");
                    }
                    lines.Add(""); // Пустая строка между пирами
                }
            }
        }
        
        // Показываем список
        if (lines.Count == 0)
        {
            peerListText.text = "🏠 Никого нет в комнате\n\nПодключитесь к серверу чтобы увидеть других игроков";
        }
        else
        {
            peerListText.text = string.Join("\n", lines);
        }
    }
    
    void AddMessage(string message)
    {
        messages.Add(message);
        
        // Ограничиваем количество сообщений
        if (messages.Count > 50) messages.RemoveAt(0);
        
        // Обновляем UI
        if (messagesText != null)
        {
            messagesText.text = string.Join("\n", messages);
        }
    }

    void UpdateUI()
    {
        bool connected = signaling?.IsConnected == true;
        
        // Состояние кнопок
        if (connectButton != null) connectButton.interactable = !connected;
        if (disconnectButton != null) disconnectButton.interactable = connected;
        if (roomInput != null) roomInput.interactable = !connected;
        
        // Статус
        if (statusText != null)
        {
            if (connected)
            {
                int connectedPeers = webrtc.ConnectedPeersCount;
                int totalPeers = (signaling.PeersInRoom?.Count ?? 1) - 1;
                statusText.text = $"🟢 Подключен - Mesh: {connectedPeers}/{totalPeers}";
            }
            else
            {
                statusText.text = "🔴 Не подключен";
            }
        }
    }
    
    void Update()
    {
        UpdateUI(); // Обновляем UI каждый кадр
    }

    void OnDestroy()
    {
        // Отписываемся от событий
        if (signaling != null)
        {
            signaling.OnPeerJoined -= WhenPeerJoined;
            signaling.OnPeerLeft -= WhenPeerLeft;
            signaling.OnSignalingMessage -= webrtc.HandleSignalingMessage;
            signaling.OnConnected -= WhenConnectedToSignaling;
        }
        
        if (webrtc != null)
        {
            webrtc.OnMessageReceived -= WhenMessageReceived;
            webrtc.OnPeerConnected -= WhenPeerConnected;
            webrtc.OnPeerDisconnected -= WhenPeerDisconnected;
        }
    }
} 