using UnityEngine;
using NativeWebSocket;
using System.Text;
using System.Threading.Tasks;

public class WebSocketSignaler : MonoBehaviour {
    public WebRTCClient rtc;
    private WebSocket ws;

    public async void Connect(string url, string myId) {
        ws = new WebSocket(url);
        ws.OnMessage += (bytes) => {
            string json = Encoding.UTF8.GetString(bytes);
            // rtc.OnSignalingMessage(json);
        };
        await ws.Connect();
        // rtc.SetLocalPeerId(myId);
    }

    public async void Send(string json) {
        if (ws != null && ws.State == WebSocketState.Open) {
            await ws.SendText(json);
        }
    }
}