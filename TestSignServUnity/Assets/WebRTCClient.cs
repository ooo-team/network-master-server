using UnityEngine;
using NativeWebSocket;
using System.Collections;
using System.Text;
using Unity.WebRTC;

public class WebRTCClient : MonoBehaviour {
    private RTCPeerConnection peer;
    private RTCDataChannel dataChannel;
    private WebSocketSignaler signaler;
    private string localId, remoteId;

    // public void SetLocalPeerId(string id) => localId = id;
    // public void SetRemotePeerId(string id) => remoteId = id;

    // public void StartConnection(bool isInitiator) {
    //     var config = new RTCConfiguration {
    //         iceServers = new[] {
    //             new RTCIceServer {
    //                 urls = new[] {
    //                     "stun:your-ip:3478",
    //                     "turn:your-ip:3478"
    //                 },
    //                 username = "user",
    //                 credential = "secret"
    //             }
    //         }
    //     };

    //     peer = new RTCPeerConnection(ref config);
    //     peer.OnIceCandidate = candidate => SendSignal("candidate", candidate);
    //     peer.OnDataChannel = channel => SetupChannel(channel);

    //     if (isInitiator) {
    //         dataChannel = peer.CreateDataChannel("data");
    //         SetupChannel(dataChannel);
    //         StartCoroutine(MakeOffer());
    //     }
    // }

    // private void SetupChannel(RTCDataChannel channel) {
    //     channel.OnMessage = bytes => Debug.Log("Received: " + Encoding.UTF8.GetString(bytes));
    //     channel.OnOpen = () => Debug.Log("Data channel open");
    // }

    // private IEnumerator MakeOffer() {
    //     var offerOp = peer.CreateOffer();
    //     yield return offerOp;
    //     yield return peer.SetLocalDescription(ref offerOp.Desc);
    //     SendSignal("offer", offerOp.Desc);
    // }

    // private void SendSignal(string type, object payload) {
    //     var msg = new {
    //         type,
    //         from = localId,
    //         to = remoteId,
    //         payload
    //     };
    //     string json = JsonUtility.ToJson(msg);
    //     signaler.Send(json);
    // }

    // public void OnSignalingMessage(string json) {
    //     var msg = JsonUtility.FromJson<SignalMessage>(json);
    //     if (msg.type == "offer") {
    //         StartCoroutine(HandleOffer(msg));
    //     } else if (msg.type == "answer") {
    //         peer.SetRemoteDescription(new RTCSessionDescription {
    //             type = RTCSdpType.Answer,
    //             sdp = msg.payload.sdp
    //         });
    //     } else if (msg.type == "candidate") {
    //         var c = new RTCIceCandidate(new RTCIceCandidateInit {
    //             candidate = msg.payload.candidate,
    //             sdpMid = msg.payload.sdpMid,
    //             sdpMLineIndex = msg.payload.sdpMLineIndex
    //         });
    //         peer.AddIceCandidate(c);
    //     }
    // }

    // private IEnumerator HandleOffer(SignalMessage msg) {
    //     var desc = new RTCSessionDescription {
    //         type = RTCSdpType.Offer,
    //         sdp = msg.payload.sdp
    //     };
    //     yield return peer.SetRemoteDescription(ref desc);
    //     var answerOp = peer.CreateAnswer();
    //     yield return answerOp;
    //     yield return peer.SetLocalDescription(ref answerOp.Desc);
    //     SendSignal("answer", answerOp.Desc);
    // }
}