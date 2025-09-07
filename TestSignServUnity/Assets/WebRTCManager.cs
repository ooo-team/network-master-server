using UnityEngine;
using Unity.WebRTC;
using System;
using System.Collections;
using System.Text;

[RequireComponent(typeof(SignalingClient))]
public class WebRTCManager : MonoBehaviour
{
    [Header("WebRTC Configuration")]
    public RTCConfiguration rtcConfig = new()
    {
        iceServers = new[]
        {
            new RTCIceServer {
                urls = new[] { "stun:stun.l.google.com:19302?transport=udp", "stun:stun1.l.google.com:19302?transport=udp" }
            },
            new RTCIceServer {
                urls = new[] { "turn:global.relay.metered.ca:3478?transport=udp" },
                username = "b7adc85b4cf785c04869754c",
                credential = "ZZ3Ln89FzaDMrF5n"
            },
            new RTCIceServer
            {
                urls = new[] { "turn:global.relay.metered.ca:443?transport=tcp",
                            "turns:global.relay.metered.ca:443?transport=tcp" },
                username = "b7adc85b4cf785c04869754c",
                credential = "ZZ3Ln89FzaDMrF5n"
            }
        }
    };

    // Single PeerConnection per client
    private RTCPeerConnection pc;
    private RTCDataChannel dataChannel;

    // Role & target
    private bool isInitiator = false;
    private string targetPeerId = string.Empty;

    // Events
    public event Action<string> OnDataChannelMessage;
    public event Action OnDataChannelOpen;
    public event Action OnDataChannelClose;
    public event Action<RTCIceConnectionState> OnIceConnectionStateChanged;
    public event Action<RTCPeerConnectionState> OnConnectionStateChanged;

    private SignalingClient signaling;

    void Awake()
    {
        signaling = GetComponent<SignalingClient>();
    }

    /// <summary>
    /// Create one RTCPeerConnection and start negotiation depending on the role.
    /// </summary>
    public void CreateConnection(bool asInitiator, string peerId)
    {
        isInitiator = asInitiator;
        targetPeerId = peerId;

        // Clean previous
        CloseConnection();

        // Build PC
        pc = new RTCPeerConnection(ref rtcConfig);

        // ICE → signal
        pc.OnIceCandidate = cand =>
        {
            if (cand == null) return;
            SendICECandidate(cand);
        };

        // State logs
        pc.OnIceConnectionChange = state =>
        {
            OnIceConnectionStateChanged?.Invoke(state);
        };
        pc.OnConnectionStateChange = state =>
        {
            OnConnectionStateChanged?.Invoke(state);
        };

        // Incoming DataChannel (on the answerer typically)
        pc.OnDataChannel = ch =>
        {
            SetupDataChannel(ch);
        };

        if (isInitiator)
        {
            // Create DC BEFORE CreateOffer
            CreateDataChannel();
            StartCoroutine(CreateAndSendOffer());
        }
    }

    private void CreateDataChannel()
    {
        // Default reliable ordered
        dataChannel = pc.CreateDataChannel("data");
        SetupDataChannel(dataChannel);
    }

    private void SetupDataChannel(RTCDataChannel channel)
    {
        if (channel == null) return;
        dataChannel = channel;

        dataChannel.OnMessage = bytes =>
        {
            string message = Encoding.UTF8.GetString(bytes);
            OnDataChannelMessage?.Invoke(message);
        };

        dataChannel.OnOpen += () =>
        {
            OnDataChannelOpen?.Invoke();
        };

        dataChannel.OnClose += () =>
        {
            OnDataChannelClose?.Invoke();
        };
    }

    // === Negotiation ===
    private IEnumerator CreateAndSendOffer()
    {
        var offerOp = pc.CreateOffer();
        yield return offerOp;

        if (offerOp.IsError)
        {
            Debug.LogError($"Failed to create offer: {offerOp.Error.message}");
            yield break;
        }

        var offer = offerOp.Desc;
        var setLocal = pc.SetLocalDescription(ref offer);
        yield return setLocal;

        if (setLocal.IsError)
        {
            Debug.LogError($"Failed to SetLocalDescription(offer): {setLocal.Error.message}");
            yield break;
        }

        // Wrap SDP in SDPPayload JSON inside SignalingMessage.payload (string)
        var sdpPayload = new SDPPayload { type = "offer", sdp = offer.sdp };
        var msg = new SignalingMessage
        {
            type = "offer",
            from = signaling.PeerId,
            to = targetPeerId,
            payload = JsonUtility.ToJson(sdpPayload)
        };
        signaling.SendMessage(msg);
    }

    private IEnumerator CreateAndSendAnswer()
    {
        var answerOp = pc.CreateAnswer();
        yield return answerOp;
        if (answerOp.IsError)
        {
            Debug.LogError($"Failed to create answer: {answerOp.Error.message}");
            yield break;
        }

        var answer = answerOp.Desc;
        var setLocal = pc.SetLocalDescription(ref answer);
        yield return setLocal;
        if (setLocal.IsError)
        {
            Debug.LogError($"Failed to SetLocalDescription(answer): {setLocal.Error.message}");
            yield break;
        }

        var sdpPayload = new SDPPayload { type = "answer", sdp = answer.sdp };
        var msg = new SignalingMessage
        {
            type = "answer",
            from = signaling.PeerId,
            to = targetPeerId,
            payload = JsonUtility.ToJson(sdpPayload)
        };
        signaling.SendMessage(msg);
    }

    // === Signaling handlers ===
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

    private IEnumerator HandleOffer(SignalingMessage message)
    {
        // Ensure PC exists (receiver path)
        if (pc == null)
        {
            // If we got an offer unexpectedly, become receiver for this peer
            CreateConnection(false, message.from);
        }
        if (string.IsNullOrEmpty(targetPeerId)) targetPeerId = message.from;

        if (string.IsNullOrEmpty(message.payload))
        {
            Debug.LogError("HandleOffer: empty payload");
            yield break;
        }

        var sdpPayload = JsonUtility.FromJson<SDPPayload>(message.payload);
        var remote = new RTCSessionDescription { type = RTCSdpType.Offer, sdp = sdpPayload.sdp };

        var setRemote = pc.SetRemoteDescription(ref remote);
        yield return setRemote;
        if (setRemote.IsError)
        {
            Debug.LogError($"SetRemoteDescription(offer) error: {setRemote.Error.message}");
            yield break;
        }

        // Create/send answer
        yield return CreateAndSendAnswer();
    }

    private IEnumerator HandleAnswer(SignalingMessage message)
    {
        if (pc == null)
        {
            Debug.LogWarning("HandleAnswer: pc is null, ignoring");
            yield break;
        }
        if (string.IsNullOrEmpty(message.payload))
        {
            Debug.LogError("HandleAnswer: empty payload");
            yield break;
        }

        var sdpPayload = JsonUtility.FromJson<SDPPayload>(message.payload);
        var remote = new RTCSessionDescription { type = RTCSdpType.Answer, sdp = sdpPayload.sdp };

        var setRemote = pc.SetRemoteDescription(ref remote);
        yield return setRemote;
        if (setRemote.IsError)
        {
            Debug.LogError($"SetRemoteDescription(answer) error: {setRemote.Error.message}");
        }
    }

    private void SendICECandidate(RTCIceCandidate candidate)
    {
        var payload = new ICECandidatePayload
        {
            candidate = candidate.Candidate,
            sdpMid = candidate.SdpMid,
            sdpMLineIndex = candidate.SdpMLineIndex
        };

        var msg = new SignalingMessage
        {
            type = "ice_candidate",
            from = signaling.PeerId,
            to = targetPeerId,
            payload = JsonUtility.ToJson(payload)
        };
        signaling.SendMessage(msg);
    }

    private void HandleICECandidate(SignalingMessage message)
    {
        if (pc == null)
        {
            Debug.LogWarning("ICE received but pc is null — ignoring");
            return;
        }
        if (string.IsNullOrEmpty(message.payload))
        {
            Debug.LogWarning("Received ICE with empty payload");
            return;
        }

        var payload = JsonUtility.FromJson<ICECandidatePayload>(message.payload);
        if (payload == null || string.IsNullOrEmpty(payload.candidate))
        {
            Debug.LogWarning("ICE payload parse failed or empty candidate");
            return;
        }

        var init = new RTCIceCandidateInit
        {
            candidate = payload.candidate,
            sdpMid = payload.sdpMid,
            sdpMLineIndex = payload.sdpMLineIndex
        };

        try
        {
            var ice = new RTCIceCandidate(init);
            pc.AddIceCandidate(ice);
        }
        catch (Exception e)
        {
            Debug.LogError($"AddIceCandidate error: {e.Message}");
        }
    }

    // === DataChannel send ===
    public void SendMsg(string message)
    {
        if (dataChannel != null && dataChannel.ReadyState == RTCDataChannelState.Open)
        {
            var bytes = Encoding.UTF8.GetBytes(message);
            dataChannel.Send(bytes);
        }
        else
        {
            Debug.LogWarning("SendMsg: data channel is not open");
        }
    }

    public void CloseConnection()
    {
        try
        {
            if (dataChannel != null)
            {
                dataChannel.Close();
                dataChannel.Dispose();
            }
        }
        catch { }
        finally { dataChannel = null; }

        try
        {
            if (pc != null)
            {
                pc.Close();
                pc.Dispose();
            }
        }
        catch { }
        finally { pc = null; }

        // Keep targetPeerId/isInitiator so we can reconnect with same role if needed
    }

    void OnDestroy()
    {
        CloseConnection();
    }
}

