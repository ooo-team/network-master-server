using UnityEngine;
using Unity.WebRTC;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// Менеджер WebRTC соединений
/// Создает RTCPeerConnection, управляет SDP offer/answer и ICE candidates
/// </summary>
public class WebRTCManager : MonoBehaviour
{
    [Header("WebRTC Configuration")]
    /// <summary>
    /// Конфигурация WebRTC соединения (ICE серверы, политики и т.д.)
    /// </summary>
    public RTCConfiguration rtcConfig = new RTCConfiguration{
        iceServers = new[]
        {
            new RTCIceServer { urls = new[] { "stun:stun.l.google.com:19302", "stun:stun1.l.google.com:19302" } },

            new RTCIceServer
            {
                urls = new string[] { "turn:global.relay.metered.ca:443" },
                username = "b7adc85b4cf785c04869754c",
                credential = "ZZ3Ln89FzaDMrF5n"
            }
        }   
    };

    // WebRTC connections
    private RTCPeerConnection localPeer;
    private RTCPeerConnection remotePeer;
    private RTCDataChannel dataChannel;
    
    // Internal state
    private bool isInitiator = false;
    private string targetPeerId = "";
    
    // Events
    /// <summary>
    /// Событие: получено сообщение через DataChannel
    /// </summary>
    public event Action<string> OnDataChannelMessage;
    
    /// <summary>
    /// Событие: DataChannel открылся
    /// </summary>
    public event Action OnDataChannelOpen;
    
    /// <summary>
    /// Событие: DataChannel закрылся
    /// </summary>
    public event Action OnDataChannelClose;
    
    /// <summary>
    /// Событие: изменилось состояние ICE соединения
    /// </summary>
    public event Action<RTCIceConnectionState> OnIceConnectionStateChanged;
    
    /// <summary>
    /// Событие: изменилось состояние соединения
    /// </summary>
    public event Action<RTCPeerConnectionState> OnConnectionStateChanged;

    /// <summary>
    /// Создать WebRTC соединение
    /// </summary>
    /// <param name="asInitiator">true если этот peer инициирует соединение</param>
    /// <param name="peerId">ID целевого peer'а</param>
    public void CreateConnection(bool asInitiator, string peerId)
    {
        isInitiator = asInitiator;
        targetPeerId = peerId;

        if (asInitiator)
        {
            CreateLocalPeerConnection();
            CreateDataChannel();
            StartCoroutine(CreateAndSendOffer());
        }
        else
        {
            CreateRemotePeerConnection();
        }
    }

    /// <summary>
    /// Создать локальный RTCPeerConnection (для инициатора)
    /// </summary>
    private void CreateLocalPeerConnection()
    {
        localPeer = new RTCPeerConnection(ref rtcConfig);
        
        localPeer.OnIceCandidate = candidate =>
        {
            SendICECandidate(candidate);
        };
        
        localPeer.OnIceConnectionChange = state =>
        {
            OnIceConnectionStateChanged?.Invoke(state);
        };
        
        localPeer.OnConnectionStateChange = state =>
        {
            OnConnectionStateChanged?.Invoke(state);
        };
    }

    /// <summary>
    /// Создать удаленный RTCPeerConnection (для получателя)
    /// </summary>
    private void CreateRemotePeerConnection()
    {
        remotePeer = new RTCPeerConnection(ref rtcConfig);
        
        remotePeer.OnIceCandidate = candidate =>
        {
            SendICECandidate(candidate);
        };
        
        remotePeer.OnDataChannel = channel =>
        {
            SetupDataChannel(channel);
        };
        
        remotePeer.OnIceConnectionChange = state =>
        {
            OnIceConnectionStateChanged?.Invoke(state);
        };
    }

    /// <summary>
    /// Создать DataChannel для передачи данных
    /// </summary>
    private void CreateDataChannel()
    {
        dataChannel = localPeer.CreateDataChannel("data");
        SetupDataChannel(dataChannel);
    }

    /// <summary>
    /// Настроить DataChannel (обработчики событий)
    /// </summary>
    private void SetupDataChannel(RTCDataChannel channel)
    {
        channel.OnMessage = bytes =>
        {
            string message = Encoding.UTF8.GetString(bytes);
            OnDataChannelMessage?.Invoke(message);
        };
        
        channel.OnOpen += () =>
        {
            OnDataChannelOpen?.Invoke();
        };
        
        channel.OnClose += () =>
        {
            OnDataChannelClose?.Invoke();
        };
    }


    /// <summary>
    /// Создать и отправить SDP offer
    /// </summary>
    private IEnumerator CreateAndSendOffer()
    {
        var offerOp = localPeer.CreateOffer();
        yield return offerOp;
        
        if (!offerOp.IsError)
        {
            var desc = offerOp.Desc;
            yield return localPeer.SetLocalDescription(ref desc);
            
            var offerMessage = new SignalingMessage
            {
                type = "offer",
                from = GetComponent<SignalingClient>().PeerId,
                to = targetPeerId,
                payload = offerOp.Desc.sdp
            };
            
            GetComponent<SignalingClient>().SendMessage(offerMessage);
        }
        else
        {
            Debug.LogError($"Failed to create offer: {offerOp.Error.message}");
        }
    }

    /// <summary>
    /// Отправить ICE candidate через signaling
    /// </summary>
    private void SendICECandidate(RTCIceCandidate candidate)
    {
        var message = new SignalingMessage
        {
            type = "ice_candidate",
            from = GetComponent<SignalingClient>().PeerId,
            to = targetPeerId,
            payload = candidate.Candidate
        };
        
        GetComponent<SignalingClient>().SendMessage(message);
    }

    /// <summary>
    /// Обработать signaling сообщение (offer, answer, ice_candidate)
    /// </summary>
    public void HandleSignalingMessage(SignalingMessage message)
    {
        switch (message.type)
        {
            case "offer":
                StartCoroutine(HandleOffer(message));
                break;
            case "answer":
                StartCoroutine(HandleAnswer(message));
                break;
            case "ice_candidate":
                HandleICECandidate(message);
                break;
        }
    }

    /// <summary>
    /// Обработать SDP offer
    /// </summary>
    private IEnumerator HandleOffer(SignalingMessage message)
    {
        if (remotePeer == null)
        {
            CreateRemotePeerConnection();
        }

        var desc = new RTCSessionDescription
        {
            type = RTCSdpType.Offer,
            sdp = message.payload.ToString()
        };

        yield return remotePeer.SetRemoteDescription(ref desc);
        
        var answerOp = remotePeer.CreateAnswer();
        yield return answerOp;
        
        if (!answerOp.IsError)
        {
            var answerDesc = answerOp.Desc;
            yield return remotePeer.SetLocalDescription(ref answerDesc);
            
            var answerMessage = new SignalingMessage
            {
                type = "answer",
                from = GetComponent<SignalingClient>().PeerId,
                to = message.from,
                payload = answerOp.Desc.sdp
            };
            
            GetComponent<SignalingClient>().SendMessage(answerMessage);
        }
    }

    /// <summary>
    /// Обработать SDP answer
    /// </summary>
    private IEnumerator HandleAnswer(SignalingMessage message)
    {
        if (localPeer != null)
        {
            var desc = new RTCSessionDescription
            {
                type = RTCSdpType.Answer,
                sdp = message.payload.ToString()
            };

            yield return localPeer.SetRemoteDescription(ref desc);
        }
    }

    /// <summary>
    /// Обработать ICE candidate
    /// </summary>
    private void HandleICECandidate(SignalingMessage message)
    {
        // В Unity WebRTC, ICE candidates обрабатываются автоматически
        // при установке remote description, поэтому можно пропустить
        // ручную обработку ICE candidates
        Debug.Log($"Received ICE candidate: {message.payload}");
    }

    /// <summary>
    /// Отправить сообщение через DataChannel
    /// </summary>
    public void SendMessage(string message)
    {
        if (dataChannel != null && dataChannel.ReadyState == RTCDataChannelState.Open)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(message);
            dataChannel.Send(bytes);
        }
    }

    /// <summary>
    /// Закрыть WebRTC соединение
    /// </summary>
    public void CloseConnection()
    {
        if (localPeer != null)
        {
            localPeer.Close();
            localPeer.Dispose();
            localPeer = null;
        }
        
        if (remotePeer != null)
        {
            remotePeer.Close();
            remotePeer.Dispose();
            remotePeer = null;
        }
        
        dataChannel = null;
        targetPeerId = "";
    }

    void OnDestroy()
    {
        CloseConnection();
    }
} 