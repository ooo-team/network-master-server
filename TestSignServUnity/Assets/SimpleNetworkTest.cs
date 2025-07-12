using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Linq;
using System.Collections.Generic;

[System.Serializable]
public class SignalMessage
{
    public string type;
    public string from;
    public string to;
    public string payload;
}

[System.Serializable]
public class SignalMessageWithPayload
{
    public string type;
    public string from;
    public string to;
    public PeerJoinedPayload payload;
}

[System.Serializable]
public class SignalMessageOffer
{
    public string type;
    public string from;
    public string to;
    public string payload;
}

[System.Serializable]
public class PeerJoinedPayload
{
    public string peer_id;
}

public class SimpleNetworkTest : MonoBehaviour
{
    [Header("UI Elements")]
    public Button connectButton;
    public Text logText;
    
    [Header("Server Settings")]
    public string serverUrl = "ws://95.165.133.136:8080/ws";
    public string roomCode = "test_room";
    public string peerId = "unity_client";
    private ClientWebSocket webSocket;
    private CancellationTokenSource cancellationTokenSource;
    private Task receiveTask;
    private bool isConnected = false;
    private System.Collections.Generic.List<string> connectedPeers = new System.Collections.Generic.List<string>();
    private readonly Queue<Action> mainThreadActions = new Queue<Action>();

    void RunOnMainThread(Action action)
    {
        lock (mainThreadActions)
        {
            mainThreadActions.Enqueue(action);
        }
    }

    void Start()
    {
        // Generate random peer ID
        peerId = "unity_client_" + UnityEngine.Random.Range(1000, 9999);
        
        // Setup buttons
        if (connectButton != null)
        {
            connectButton.onClick.AddListener(ToggleConnection);
            connectButton.GetComponentInChildren<Text>().text = "Connect";
        }
        else
        {
            Debug.LogError("connectButton is not assigned!");
        }
        
        Log("Simple Network Test Ready");
        Log($"Peer ID: {peerId}");
        Log($"Server: {serverUrl}");
        Log($"Room: {roomCode}");
    }
    void Update()
    {
        lock (mainThreadActions)
        {
            while (mainThreadActions.Count > 0)
            {
                mainThreadActions.Dequeue().Invoke();
            }
        }
    }

    
    
    public void ToggleConnection()
    {
        if (isConnected)
        {
            SendTestMessage();
        }
        else
        {
            Connect();
        }
    }

    void Connect()
    {
        if (isConnected) return;
        
        Log("Connecting...");
        StartCoroutine(ConnectWebSocket());
    }
    

    
    void SendTestMessage()
    {
        if (!isConnected) return;
        Log($"📤 Peers: {connectedPeers.Count}");
        string time = DateTime.Now.ToString("HH:mm:ss");
        // If we have connected peers, send to the first one
        foreach (string targetPeer in connectedPeers)
        {
            Log($"📤 Sending message to specific peer: {targetPeer}");
            SendMessage(new SignalMessage
            {
                type = "offer",
                from = peerId,
                to = targetPeer,
                payload = $"Hello from {peerId} to {targetPeer} at {time}!"
            });
        }
        
        
    }
    
    IEnumerator ConnectWebSocket()
    {
        webSocket = new ClientWebSocket();
        cancellationTokenSource = new CancellationTokenSource();
        
        string fullUrl = $"{serverUrl}?peer_id={peerId}&room={roomCode}";
        var connectTask = webSocket.ConnectAsync(new Uri(fullUrl), cancellationTokenSource.Token);
        
        while (!connectTask.IsCompleted)
        {
            yield return null;
        }
        
        if (connectTask.Exception != null)
        {
            Log($"Connection failed: {connectTask.Exception.Message}");
            yield break;
        }
        
        Log("Connected!");
        isConnected = true;
        connectButton.GetComponentInChildren<Text>().text = "Send message";
        connectButton.onClick.RemoveAllListeners();
        connectButton.onClick.AddListener(SendTestMessage);
        
        // Start receiving messages
        receiveTask = Task.Run(() => ReceiveMessagesAsync(cancellationTokenSource.Token));

    }
    
    void SendMessage(SignalMessage message)
    {
        if (webSocket != null && isConnected)
        {
            string jsonMessage = JsonUtility.ToJson(message);
            StartCoroutine(SendWebSocketMessage(jsonMessage));
        }
    }
    
    IEnumerator SendWebSocketMessage(string message)
    {
        if (webSocket != null && isConnected)
        {
            byte[] messageBytes = Encoding.UTF8.GetBytes(message);
            var sendTask = webSocket.SendAsync(new ArraySegment<byte>(messageBytes), WebSocketMessageType.Text, true, cancellationTokenSource.Token);
            
            while (!sendTask.IsCompleted)
            {
                yield return null;
            }
            
            try
            {
                if (sendTask.Exception != null)
                {
                    Log($"Send failed: {sendTask.Exception.Message}");
                }
                else
                {
                    Log($"Sent: {message}");
                }
            }
            catch (Exception e)
            {
                Log($"Send error: {e.Message}");
            }
        }
    }
    
    async Task ReceiveMessagesAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        
        try
        {
            while (webSocket != null && webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                
                if (result.MessageType == WebSocketMessageType.Text)
                {
                    string message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    Log($"Received: {message}");
                    string messageText = $"{message}";
                    
                    // Try to parse as SignalMessage with proper payload structure
                    try
                    {
                        SignalMessageWithPayload signalMsg = JsonUtility.FromJson<SignalMessageWithPayload>(message);
                        Log($"Parsed message - Type: {signalMsg.type}, From: {signalMsg.from}, To: {signalMsg.to}");
                        if (!connectedPeers.Contains(signalMsg.from) && signalMsg.from != "" && signalMsg.from != peerId)
                        {
                            connectedPeers.Add(signalMsg.from);
                            Log($"📋 Added peer to list: {signalMsg.from}");
                            Log($"📊 Total connected peers: {connectedPeers.Count}");
                        }
                        // Handle different message types
                        switch (signalMsg.type)
                        {
                            
                            case "peer_joined":
                                Log($"🎉 New peer joined: {signalMsg.from}");
                                if (signalMsg.payload != null && !string.IsNullOrEmpty(signalMsg.payload.peer_id))
                                {
                                    if (!connectedPeers.Contains(signalMsg.payload.peer_id))
                                    {
                                        connectedPeers.Add(signalMsg.payload.peer_id);
                                        Log($"📋 Added peer to list: {signalMsg.payload.peer_id}");
                                        Log($"📊 Total connected peers: {connectedPeers.Count}");
                                    }
                                }
                                else
                                {
                                    Log($"⚠️ Invalid peer_joined payload");
                                }
                                break;
                            case "peer_left":
                                Log($"👋 Peer left: {signalMsg.from}");
                                if (signalMsg.payload != null && !string.IsNullOrEmpty(signalMsg.payload.peer_id))
                                {
                                    if (connectedPeers.Contains(signalMsg.payload.peer_id))
                                    {
                                        connectedPeers.Remove(signalMsg.payload.peer_id);
                                        Log($"📋 Removed peer from list: {signalMsg.payload.peer_id}");
                                        Log($"📊 Total connected peers: {connectedPeers.Count}");
                                    }
                                }
                                else
                                {
                                    Log($"⚠️ Invalid peer_left payload");
                                }
                                break;
                            case "offer":
                                SignalMessageOffer offer = JsonUtility.FromJson<SignalMessageOffer>(message);
                                RunOnMainThread(() =>
                                {
                                    if (logText != null)
                                    {
                                        logText.text = offer.payload;
                                    }
                                });
                                break;
                            case "answer":
                            case "ice_candidate":
                                Log($"📡 Signaling message: {signalMsg.type} from {signalMsg.from}");
                                break;
                            default:
                                Log($"❓ Unknown message type: {signalMsg.type}");
                                break;
                        }
                    }
                    catch (Exception parseEx)
                    {
                        Log($"Failed to parse message as SignalMessageWithPayload: {parseEx.Message}");
                        Log($"Raw message: {message}");
                    }
                }
                else if (result.MessageType == WebSocketMessageType.Close)
                {
                    Log("Server closed connection");
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            Log("Receiving cancelled");
        }
        catch (Exception e)
        {
            Log($"Receive error: {e.Message}");
        }
        finally
        {
            if (isConnected)
            {
                isConnected = false;
                Log("Connection lost");
            }
        }
    }
    
    void Log(string message)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss");
        string logEntry = $"[{timestamp}] {message}";
        
        Debug.Log(logEntry);
    }
    

    
} 