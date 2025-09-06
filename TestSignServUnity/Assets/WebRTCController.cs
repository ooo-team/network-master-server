using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

[RequireComponent(typeof(SignalingClient))]
[RequireComponent(typeof(WebRTCManager))]

/// <summary>
/// Главный контроллер WebRTC системы
/// Объединяет SignalingClient и WebRTCManager, управляет UI
/// </summary>
public class WebRTCController : MonoBehaviour
{
    [Header("UI References")]
    /// <summary>
    /// Кнопка подключения к signaling server'у
    /// </summary>
    public Button connectButton;
    
    /// <summary>
    /// Кнопка отключения от signaling server'а
    /// </summary>
    public Button disconnectButton;
    
    /// <summary>
    /// Поле ввода названия комнаты
    /// </summary>
    public TMP_InputField roomInput;
    
    /// <summary>
    /// Текст статуса подключения
    /// </summary>
    public TextMeshProUGUI statusText;
    
    /// <summary>
    /// Текст для отображения списка peer'ов
    /// </summary>
    public TextMeshProUGUI peerListText;
    
    /// <summary>
    /// Поле ввода сообщения для отправки
    /// </summary>
    public TMP_InputField messageInput;
    
    /// <summary>
    /// Кнопка отправки сообщения
    /// </summary>
    public Button sendMessageButton;
    
    /// <summary>
    /// Область для отображения сообщений
    /// </summary>
    public TextMeshProUGUI messagesText;
    
    [Header("Components")]
    /// <summary>
    /// Клиент для signaling server'а
    /// </summary>
    private SignalingClient signalingClient;
    
    /// <summary>
    /// Менеджер WebRTC соединений
    /// </summary>
    private WebRTCManager webRTCManager;
    
    /// <summary>
    /// Список peer'ов в комнате
    /// </summary>
    private readonly List<string> peersInRoom = new ();
    
    /// <summary>
    /// ID текущего подключенного peer'а
    /// </summary>
    private string connectedPeerId = "";
    
    /// <summary>
    /// Сообщения чата
    /// </summary>
    private readonly List<string> chatMessages = new ();

    void Start()
    {
        // Получить компоненты
        signalingClient = GetComponent<SignalingClient>();
        webRTCManager = GetComponent<WebRTCManager>();
        
        if (signalingClient == null)
        {
            signalingClient = gameObject.AddComponent<SignalingClient>();
        }
        
        if (webRTCManager == null)
        {
            webRTCManager = gameObject.AddComponent<WebRTCManager>();
        }
        
        // Настроить UI
        SetupUI();
        
        // Подписаться на события
        SubscribeToEvents();
        
        // Установить начальное состояние
        UpdateUI();
    }

    /// <summary>
    /// Настроить UI элементы
    /// </summary>
    private void SetupUI()
    {
        if (connectButton != null)
            connectButton.onClick.AddListener(OnConnectClicked);
            
        if (disconnectButton != null)
            disconnectButton.onClick.AddListener(OnDisconnectClicked);
            
        if (sendMessageButton != null)
            sendMessageButton.onClick.AddListener(OnSendMessageClicked);
            
        if (roomInput != null)
            roomInput.text = signalingClient.roomCode;
    }

    /// <summary>
    /// Подписаться на события компонентов
    /// </summary>
    private void SubscribeToEvents()
    {
        // Signaling events
        signalingClient.OnPeerJoined += OnPeerJoined;
        signalingClient.OnPeerLeft += OnPeerLeft;
        signalingClient.OnSignalingMessage += OnSignalingMessage;
        signalingClient.OnConnected += OnSignalingConnected;
        
        // WebRTC events
        webRTCManager.OnDataChannelMessage += OnDataChannelMessage;
        webRTCManager.OnDataChannelOpen += OnDataChannelOpen;
        webRTCManager.OnDataChannelClose += OnDataChannelClose;
        webRTCManager.OnIceConnectionStateChanged += OnIceConnectionStateChanged;
        webRTCManager.OnConnectionStateChanged += OnConnectionStateChanged;
    }

    /// <summary>
    /// Обработчик нажатия кнопки подключения
    /// </summary>
    private void OnConnectClicked()
    {
        if (roomInput != null)
        {
            signalingClient.roomCode = roomInput.text;
        }
        
        signalingClient.Connect();
        UpdateUI();
    }

    /// <summary>
    /// Обработчик нажатия кнопки отключения
    /// </summary>
    private void OnDisconnectClicked()
    {
        // Закрыть WebRTC соединение
        webRTCManager.CloseConnection();
        connectedPeerId = "";
        
        // Отключиться от signaling server'а
        signalingClient.Disconnect();
        UpdateUI();
    }

    /// <summary>
    /// Обработчик нажатия кнопки отправки сообщения
    /// </summary>
    private void OnSendMessageClicked()
    {
        if (messageInput != null && !string.IsNullOrEmpty(messageInput.text))
        {
            string message = messageInput.text;
            AddChatMessage($"Me: {message}");
            messageInput.text = "";
            
            // Отправить через WebRTC DataChannel
            webRTCManager.SendMsg(message);
        }
    }

    /// <summary>
    /// Обработчик присоединения peer'а
    /// </summary>
    private void OnPeerJoined(string peerId)
    {
        if (!peersInRoom.Contains(peerId))
        {
            peersInRoom.Add(peerId);
            UpdatePeerList();
            
            // Если это первый peer, инициировать WebRTC соединение
            if (peersInRoom.Count == 1)
            {
                StartWebRTCConnection(peerId, true);
            }
        }
    }

    /// <summary>
    /// Обработчик отключения peer'а
    /// </summary>
    private void OnPeerLeft(string peerId)
    {
        if (peersInRoom.Contains(peerId))
        {
            peersInRoom.Remove(peerId);
            UpdatePeerList();
            
            // Если это был подключенный peer, закрыть соединение
            if (connectedPeerId == peerId)
            {
                webRTCManager.CloseConnection();
                connectedPeerId = "";
                AddChatMessage($"Peer {peerId} disconnected");
            }
        }
    }

    /// <summary>
    /// Обработчик signaling сообщений
    /// </summary>
    private void OnSignalingMessage(SignalingMessage message)
    {
        // Передать сообщение в WebRTCManager для обработки
        webRTCManager.HandleSignalingMessage(message);
    }

    /// <summary>
    /// Обработчик подключения к signaling server'у
    /// </summary>
    private void OnSignalingConnected()
    {
        // Обновляем UI чтобы показать себя в списке peer'ов
        UpdatePeerList();
    }

    /// <summary>
    /// Обработчик сообщений через DataChannel
    /// </summary>
    private void OnDataChannelMessage(string message)
    {
        AddChatMessage($"Peer: {message}");
    }

    /// <summary>
    /// Обработчик открытия DataChannel
    /// </summary>
    private void OnDataChannelOpen()
    {
        AddChatMessage("WebRTC connection established!");
        UpdateUI();
    }

    /// <summary>
    /// Обработчик закрытия DataChannel
    /// </summary>
    private void OnDataChannelClose()
    {
        AddChatMessage("WebRTC connection closed");
        connectedPeerId = "";
        UpdateUI();
    }

    /// <summary>
    /// Обработчик изменения состояния ICE соединения
    /// </summary>
    private void OnIceConnectionStateChanged(Unity.WebRTC.RTCIceConnectionState state)
    {
        AddChatMessage($"ICE State: {state}");
    }

    /// <summary>
    /// Обработчик изменения состояния соединения
    /// </summary>
    private void OnConnectionStateChanged(Unity.WebRTC.RTCPeerConnectionState state)
    {
        AddChatMessage($"Connection State: {state}");
    }

    /// <summary>
    /// Начать WebRTC соединение с peer'ом
    /// </summary>
    private void StartWebRTCConnection(string peerId, bool asInitiator)
    {
        connectedPeerId = peerId;
        webRTCManager.CreateConnection(asInitiator, peerId);
        AddChatMessage($"Starting WebRTC connection with {peerId} (as {(asInitiator ? "initiator" : "receiver")})");
    }

    /// <summary>
    /// Обновить список peer'ов в UI
    /// </summary>
    private void UpdatePeerList()
    {
        if (peerListText == null)
        {
            Debug.LogError("peerListText not attached");
            return;
        }
        
        // Создаем полный список всех клиентов включая себя
        List<string> allClients = new();
        
        // Добавляем себя в начало списка с пометкой (Me)
        if (signalingClient != null && signalingClient.IsConnected)
        {
            allClients.Add($"{signalingClient.PeerId} (Me)");
        }
        
        // Добавляем остальных peer'ов
        foreach (string peerId in peersInRoom)
        {
            if (peerId != signalingClient.PeerId) // Избегаем дублирования
            {
                allClients.Add(peerId);
            }
        }
        
        // Отображаем список
        if (allClients.Count == 0)
        {
            peerListText.text = "No clients connected";
        }
        else if (allClients.Count == 1 && signalingClient.IsConnected)
        {
            peerListText.text = "Clients in room:\n" + allClients[0] + "\n(Waiting for others...)";
        }
        else
        {
            peerListText.text = "Clients in room:\n" + string.Join("\n", allClients);
        }
    }

    /// <summary>
    /// Добавить сообщение в чат
    /// </summary>
    private void AddChatMessage(string message)
    {
        chatMessages.Add(message);
        
        // Ограничить количество сообщений
        if (chatMessages.Count > 50)
        {
            chatMessages.RemoveAt(0);
        }
        
        // Обновить UI
        if (messagesText != null)
        {
            messagesText.text = string.Join("\n", chatMessages);
        }
    }

    /// <summary>
    /// Обновить состояние UI
    /// </summary>
    private void UpdateUI()
    {
        bool isConnected = signalingClient.IsConnected;
        
        if (connectButton != null)
            connectButton.interactable = !isConnected;
            
        if (disconnectButton != null)
            disconnectButton.interactable = isConnected;
            
        if (roomInput != null)
            roomInput.interactable = !isConnected;
            
        if (sendMessageButton != null)
            sendMessageButton.interactable = !string.IsNullOrEmpty(connectedPeerId);
            
        if (messageInput != null)
            messageInput.interactable = !string.IsNullOrEmpty(connectedPeerId);
            
        if (statusText != null)
        {
            string status = isConnected ? "Connected" : "Disconnected";
            if (!string.IsNullOrEmpty(connectedPeerId))
            {
                status += $" | WebRTC: {connectedPeerId}";
            }
            statusText.text = status;
        }
    }

    void Update()
    {
        // Обновлять UI каждый кадр для отзывчивости
        UpdateUI();
    }

    void OnDestroy()
    {
        // Отписаться от событий
        if (signalingClient != null)
        {
            signalingClient.OnPeerJoined -= OnPeerJoined;
            signalingClient.OnPeerLeft -= OnPeerLeft;
            signalingClient.OnSignalingMessage -= OnSignalingMessage;
            signalingClient.OnConnected -= OnSignalingConnected;
        }
        
        if (webRTCManager != null)
        {
            webRTCManager.OnDataChannelMessage -= OnDataChannelMessage;
            webRTCManager.OnDataChannelOpen -= OnDataChannelOpen;
            webRTCManager.OnDataChannelClose -= OnDataChannelClose;
            webRTCManager.OnIceConnectionStateChanged -= OnIceConnectionStateChanged;
            webRTCManager.OnConnectionStateChanged -= OnConnectionStateChanged;
        }
    }
} 