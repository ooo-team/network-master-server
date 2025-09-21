using UnityEngine;
using Unity.WebRTC;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Linq;

/// <summary>
/// ПРОСТОЙ менеджер для WebRTC соединений в mesh-сети
/// Каждый пир = одно соединение. Всё просто и понятно!
/// </summary>
public class WebRTCManager : MonoBehaviour
{
     [Header("WebRTC Settings")]
    public RTCConfiguration rtcConfig = new() {
        iceServers = new[]
        {
            new RTCIceServer { urls = new[] { "stun:stun.l.google.com:19302" } },
            new RTCIceServer
            {
                urls = new[] { "turn:global.relay.metered.ca:443" },
                username = "b7adc85b4cf785c04869754c",
                credential = "ZZ3Ln89FzaDMrF5n"
            }
        }   
    };

    // Простая структура для хранения соединения с пиром
    private class PeerConnection
    {
        public RTCPeerConnection webrtc;     // Само WebRTC соединение
        public RTCDataChannel dataChannel;  // Канал для сообщений
        public bool isConnected = false;    // Готово ли соединение
         public string connectionType = "Detecting..."; // Тип соединения (STUN/TURN/Direct)
        public int candidatesReceived = 0;   // Количество полученных ICE кандидатов
        
        public PeerConnection()
        {
            // Всё создаём сразу в конструкторе - проще понять
        }
    }

    // Все наши соединения: ID пира → соединение
    private Dictionary<string, PeerConnection> connections = new Dictionary<string, PeerConnection>();
    
    // Ссылка на signaling клиент (для отправки offer/answer)
    private SignalingClient signaling;
    
    // События - просто и понятно
    public event Action<string, string> OnMessageReceived;  // (от кого, сообщение)
    public event Action<string> OnPeerConnected;            // (ID пира)
    public event Action<string> OnPeerDisconnected;         // (ID пира)

    void Start()
    {
        signaling = GetComponent<SignalingClient>();
    }
    
    /// <summary>
    /// ГЛАВНЫЙ МЕТОД: Подключиться ко всем пирам в комнате
    /// Вызывается когда получили список пиров
    /// </summary>
    public void ConnectToAllPeers(List<string> peerIds, string myId)
    {
        Debug.Log($"🔗 ConnectToAllPeers called. My ID: {myId}, Peers: [{string.Join(", ", peerIds)}]");
        Debug.Log($"🔗 Current connections: [{string.Join(", ", connections.Keys)}]");
        
        foreach (string peerId in peerIds)
        {
            if (peerId != myId)
            {
                if (connections.ContainsKey(peerId))
                {
                    Debug.Log($"⏭️ Already have connection to {peerId}, skipping");
                    continue;
                }
                
                // Кто будет инициатором? Тот, у кого ID меньше лексикографически
                // Это гарантирует, что только один создаст offer
                bool iAmInitiator = string.Compare(myId, peerId) < 0;
                Debug.Log($"🤝 {myId} vs {peerId}: I am {(iAmInitiator ? "INITIATOR" : "RECEIVER")} (compare result: {string.Compare(myId, peerId)})");
                CreateConnectionToPeer(peerId, iAmInitiator);
            }
            else
            {
                Debug.Log($"⏭️ Skipping myself: {peerId}");
            }
        }
    }
    
    /// <summary>
    /// Создать соединение с одним пиром
    /// </summary>
    private void CreateConnectionToPeer(string peerId, bool iAmInitiator)
    {
        Debug.Log($"Connecting to {peerId} (I am {(iAmInitiator ? "initiator" : "receiver")})");
        
        var peer = new PeerConnection();
        connections[peerId] = peer;
        
        // Создаём WebRTC соединение
        peer.webrtc = new RTCPeerConnection(ref rtcConfig);
        
        // Настраиваем события
        peer.webrtc.OnIceCandidate = candidate => SendIceCandidate(peerId, candidate);
        peer.webrtc.OnDataChannel = channel => SetupDataChannel(peerId, channel);
        
        if (iAmInitiator)
        {
            // Я инициатор - создаю канал и отправляю offer
            peer.dataChannel = peer.webrtc.CreateDataChannel("messages");
            SetupDataChannel(peerId, peer.dataChannel);
            StartCoroutine(SendOffer(peerId));
        }
    }

    /// <summary>
    /// Настроить канал для сообщений с пиром
    /// </summary>
    private void SetupDataChannel(string peerId, RTCDataChannel channel)
    {
        if (!connections.ContainsKey(peerId))
        {
            Debug.LogError($"❌ No peer connection found for {peerId} when setting up data channel");
            return;
        }

        connections[peerId].dataChannel = channel;
        
        Debug.Log($"🔧 Setting up data channel for {peerId} (ReadyState: {channel.ReadyState})");
        
        // Когда приходит сообщение
        channel.OnMessage = bytes =>
        {
            string message = Encoding.UTF8.GetString(bytes);
            Debug.Log($"📨 Message from {peerId}: {message}");
            OnMessageReceived?.Invoke(peerId, message);
        };
        
        // Когда канал открывается - соединение готово!
        channel.OnOpen += () =>
        {
            connections[peerId].isConnected = true;
            Debug.Log($"✅ Data channel OPENED for {peerId} - connection ready!");
            OnPeerConnected?.Invoke(peerId);
        };
        
        // Когда канал закрывается
        channel.OnClose += () =>
        {
            connections[peerId].isConnected = false;
            Debug.Log($"❌ Data channel CLOSED for {peerId}");
            OnPeerDisconnected?.Invoke(peerId);
        };
        
        // Если канал уже открыт (может быть в некоторых случаях)
        if (channel.ReadyState == RTCDataChannelState.Open)
        {
            connections[peerId].isConnected = true;
            Debug.Log($"✅ Data channel already OPEN for {peerId}");
            OnPeerConnected?.Invoke(peerId);
        }
    }


    /// <summary>
    /// Отправить offer пиру (я инициатор)
    /// </summary>
    private IEnumerator SendOffer(string peerId)
    {
        Debug.Log($"🚀 Creating offer for {peerId}...");
        
        if (!connections.ContainsKey(peerId))
        {
            Debug.LogError($"❌ No connection found for {peerId} when sending offer");
            yield break;
        }
        
        var peer = connections[peerId];
        var offer = peer.webrtc.CreateOffer();
        yield return offer;
        
        if (!offer.IsError)
        {
            var desc = offer.Desc;
            var setLocalResult = peer.webrtc.SetLocalDescription(ref desc);
            yield return setLocalResult;
            
            // Отправляем offer через signaling
            signaling.SendMessage(new SignalingMessage
            {
                type = "offer",
                from = signaling.PeerId,
                to = peerId,
                payload = desc.sdp
            });
            
            Debug.Log($"📤 Sent offer to {peerId} (SDP length: {desc.sdp.Length})");
        }
        else
        {
            Debug.LogError($"❌ Failed to create offer for {peerId}: {offer.Error}");
        }
    }

    /// <summary>
    /// Отправить ICE кандидат
    /// </summary>
    private void SendIceCandidate(string peerId, RTCIceCandidate candidate)
    {
        signaling.SendMessage(new SignalingMessage
        {
            type = "ice_candidate",
            from = signaling.PeerId,
            to = peerId,
            payload = candidate.Candidate
        });
    }

    /// <summary>
    /// ГЛАВНЫЙ МЕТОД: Обработать сообщение от signaling сервера
    /// </summary>
    public void HandleSignalingMessage(SignalingMessage msg)
    {
        Debug.Log($"📨 Got {msg.type} from {msg.from}");
        
        switch (msg.type)
        {
            case "offer":
                StartCoroutine(HandleOffer(msg.from, msg.payload));
                break;
            case "answer":
                StartCoroutine(HandleAnswer(msg.from, msg.payload));
                break;
            case "ice_candidate":
                HandleIceCandidate(msg.from, msg.payload);
                break;
        }
    }

    /// <summary>
    /// КРИТИЧНО: Обработать ICE candidate
    /// Без этого соединение не установится!
    /// </summary>
    private void HandleIceCandidate(string fromPeerId, string candidateString)
    {
        if (!connections.ContainsKey(fromPeerId))
        {
            Debug.LogWarning($"⚠️ ICE candidate from unknown peer {fromPeerId}");
            return;
        }
        
        try
        {
            var peer = connections[fromPeerId];
            if (peer.webrtc != null)
            {
                var candidate = new RTCIceCandidate(new RTCIceCandidateInit
                {
                    candidate = candidateString,
                    sdpMid = "0",
                    sdpMLineIndex = 0
                });
                
                peer.webrtc.AddIceCandidate(candidate);
                peer.candidatesReceived++;
                
                // Определяем тип соединения по ICE кандидату
                DetectConnectionType(peer, candidateString);
                
                Debug.Log($"🧊 Added ICE candidate #{peer.candidatesReceived} from {fromPeerId} ({peer.connectionType})");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ Failed to add ICE candidate from {fromPeerId}: {e}");
        }
    }
    
     /// <summary>
     /// Определить тип соединения по ICE кандидату
     /// </summary>
     private void DetectConnectionType(PeerConnection peer, string candidateString)
     {
         if (candidateString.Contains("typ host"))
         {
             peer.connectionType = "Direct";
         }
         else if (candidateString.Contains("typ srflx"))
         {
             peer.connectionType = "STUN";
         }
         else if (candidateString.Contains("typ relay"))
         {
             peer.connectionType = "TURN";
         }
         else if (candidateString.Contains("typ prflx"))
         {
             peer.connectionType = "P2P";
         }
         else
         {
             peer.connectionType = "Unknown";
         }
     }

    /// <summary>
    /// Получили offer от пира - отвечаем answer
    /// </summary>
    private IEnumerator HandleOffer(string fromPeerId, string sdp)
    {
        Debug.Log($"📥 Handling offer from {fromPeerId}");
        
        // ИСПРАВЛЕНИЕ: Проверяем, не должны ли МЫ быть инициатором
        bool iShouldBeInitiator = string.Compare(signaling.PeerId, fromPeerId) < 0;
        if (iShouldBeInitiator)
        {
            Debug.LogWarning($"⚠️ Received offer from {fromPeerId}, but I should be initiator! Ignoring.");
            yield break;
        }
        
        // Если соединения нет - создаём (я получатель)
        if (!connections.ContainsKey(fromPeerId))
        {
            CreateConnectionToPeer(fromPeerId, false);
        }
        
        var peer = connections[fromPeerId];
        if (peer?.webrtc == null)
        {
            Debug.LogError($"❌ No WebRTC connection for {fromPeerId}");
            yield break;
        }
        
        // Устанавливаем полученный offer как remote description
        var desc = new RTCSessionDescription { type = RTCSdpType.Offer, sdp = sdp };
        var setRemoteOp = peer.webrtc.SetRemoteDescription(ref desc);
        yield return setRemoteOp;
        
        // Создаём и отправляем answer
        var answer = peer.webrtc.CreateAnswer();
        yield return answer;
        
        if (!answer.IsError)
        {
            var answerDesc = answer.Desc;
            var setLocalOp = peer.webrtc.SetLocalDescription(ref answerDesc);
            yield return setLocalOp;
            
            signaling.SendMessage(new SignalingMessage
            {
                type = "answer",
                from = signaling.PeerId,
                to = fromPeerId,
                payload = answerDesc.sdp
            });
            
            Debug.Log($"📤 Sent answer to {fromPeerId}");
        }
        else
        {
            Debug.LogError($"❌ Failed to create answer for {fromPeerId}: {answer.Error}");
        }
    }

    /// <summary>
    /// Получили answer от пира - соединение почти готово!
    /// </summary>
    private IEnumerator HandleAnswer(string fromPeerId, string sdp)
    {
        Debug.Log($"📥 Handling answer from {fromPeerId}");
        
        if (!connections.ContainsKey(fromPeerId))
        {
            Debug.LogWarning($"⚠️ Received answer from unknown peer {fromPeerId}");
            yield break;
        }
        
        var peer = connections[fromPeerId];
        if (peer?.webrtc == null)
        {
            Debug.LogError($"❌ No WebRTC connection for {fromPeerId}");
            yield break;
        }
        
        var desc = new RTCSessionDescription { type = RTCSdpType.Answer, sdp = sdp };
        var setRemoteOp = peer.webrtc.SetRemoteDescription(ref desc);
        yield return setRemoteOp;
        
        Debug.Log($"✅ Answer processed for {fromPeerId}");
    }

    /// <summary>
    /// ОТПРАВИТЬ СООБЩЕНИЕ ВСЕМ подключенным пирам
    /// </summary>
    public new void BroadcastMessage(string message)
    {
        byte[] data = Encoding.UTF8.GetBytes(message);
        int sent = 0;
        
        Debug.Log($"📢 Broadcasting message: '{message}' to {connections.Count} total connections");
        
        foreach (var kvp in connections)
        {
            string peerId = kvp.Key;
            var conn = kvp.Value;
            
            Debug.Log($"  📋 Peer {peerId}: connected={conn.isConnected}, dataChannel={conn.dataChannel != null}, channelState={conn.dataChannel?.ReadyState}");
            
            if (conn.isConnected && conn.dataChannel != null && conn.dataChannel.ReadyState == RTCDataChannelState.Open)
            {
                try
                {
                    conn.dataChannel.Send(data);
                    sent++;
                    Debug.Log($"  ✅ Sent to {peerId}");
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"  ❌ Failed to send to {peerId}: {e.Message}");
                }
            }
            else
            {
                Debug.Log($"  ⏭️ Skipped {peerId} - not ready for sending");
            }
        }
        
        Debug.Log($"📢 Broadcasted to {sent}/{connections.Count} peers: {message}");
    }
    
    /// <summary>
    /// Отправить сообщение конкретному пиру
    /// </summary>
    public void SendMessageToPeer(string peerId, string message)
    {
        if (connections.TryGetValue(peerId, out var conn) && conn.isConnected)
        {
            byte[] data = Encoding.UTF8.GetBytes(message);
            conn.dataChannel.Send(data);
            Debug.Log($"📤 Sent to {peerId}: {message}");
        }
        else
        {
            Debug.LogWarning($"❌ Can't send to {peerId} - not connected");
        }
    }

    /// <summary>
    /// Отключить пира (когда он покидает комнату)
    /// </summary>
    public void DisconnectPeer(string peerId)
    {
        if (connections.TryGetValue(peerId, out var conn))
        {
            conn.webrtc?.Close();
            conn.webrtc?.Dispose();
            connections.Remove(peerId);
            Debug.Log($"🚪 Disconnected from {peerId}");
        }
    }
    
    /// <summary>
    /// Закрыть ВСЕ соединения
    /// </summary>
    public void DisconnectAll()
    {
        foreach (var conn in connections.Values)
        {
            conn.webrtc?.Close();
            conn.webrtc?.Dispose();
        }
        connections.Clear();
        Debug.Log("🚪 Disconnected from all peers");
    }
    
    // === ПРОСТЫЕ UTILITY МЕТОДЫ ===
    
    public int ConnectedPeersCount => connections.Count(c => c.Value.isConnected);
    public List<string> ConnectedPeerIds => connections.Where(c => c.Value.isConnected).Select(c => c.Key).ToList();
    public bool IsConnectedToPeer(string peerId) => connections.TryGetValue(peerId, out var c) && c.isConnected;
    
     /// <summary>
     /// Получить детальную информацию о соединении с пиром
     /// </summary>
     public string GetConnectionDetails(string peerId)
     {
         if (!connections.TryGetValue(peerId, out var conn) || conn.webrtc == null)
             return "No connection";
             
         var connState = conn.webrtc.ConnectionState;
         var iceState = conn.webrtc.IceConnectionState;
         
         // Компактный формат без эмоджи
         string status = GetConnectionStatus(connState, iceState);
         string type = conn.connectionType;
         
         if (conn.candidatesReceived > 0)
         {
             return $"{status} | {type} | ICE:{conn.candidatesReceived}";
         }
         else
         {
             return $"{status} | {type}";
         }
     }
    
     /// <summary>
     /// Получить компактный статус соединения
     /// </summary>
     private string GetConnectionStatus(Unity.WebRTC.RTCPeerConnectionState connState, Unity.WebRTC.RTCIceConnectionState iceState)
     {
         // Приоритет отдаем состоянию соединения
         return connState switch
         {
             Unity.WebRTC.RTCPeerConnectionState.Connected => "Connected",
             Unity.WebRTC.RTCPeerConnectionState.Connecting => "Connecting",
             Unity.WebRTC.RTCPeerConnectionState.New => "New",
             Unity.WebRTC.RTCPeerConnectionState.Disconnected => "Disconnected",
             Unity.WebRTC.RTCPeerConnectionState.Failed => "Failed",
             Unity.WebRTC.RTCPeerConnectionState.Closed => "Closed",
             _ => iceState switch
             {
                 Unity.WebRTC.RTCIceConnectionState.Connected => "ICE Connected",
                 Unity.WebRTC.RTCIceConnectionState.Completed => "ICE Complete",
                 Unity.WebRTC.RTCIceConnectionState.Checking => "ICE Checking",
                 Unity.WebRTC.RTCIceConnectionState.Failed => "ICE Failed",
                 _ => "Unknown"
             }
         };
     }
    
     /// <summary>
     /// Получить краткую сводку о всех соединениях (для UI)
     /// </summary>
     public string GetConnectionsSummary()
     {
         if (connections.Count == 0)
             return "No connections";
             
         var summary = new List<string>();
         foreach (var kvp in connections)
         {
             string peerId = kvp.Key;
             var conn = kvp.Value;
             
             // Короткий ID (последние 4 символа)
             string shortId = peerId.Length > 4 ? peerId.Substring(peerId.Length - 4) : peerId;
             
             if (conn.isConnected)
             {
                 summary.Add($"{shortId}({conn.connectionType})");
             }
             else
             {
                 var state = conn.webrtc?.ConnectionState.ToString() ?? "None";
                 summary.Add($"{shortId}({state})");
             }
         }
         
         return string.Join(", ", summary);
     }
     
     // Для совместимости со старым кодом
     public void SendMsg(string message) => BroadcastMessage(message);
     public int GetActiveConnectionsCount() => ConnectedPeersCount;
     public bool IsPeerConnected(string peerId) => IsConnectedToPeer(peerId);

    void OnDestroy()
    {
        DisconnectAll();
    }
} 