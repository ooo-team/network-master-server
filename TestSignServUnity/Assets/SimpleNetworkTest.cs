using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using System.Text;

[System.Serializable]
public class SignalMessage
{
    public string type;
    public string from;
    public string to;
    public string payload;
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
    private string logContent = "";
    
    void Start()
    {
        // Generate random peer ID
        peerId = "unity_client_" + UnityEngine.Random.Range(1000, 9999);
        
        // Setup button
        if (connectButton != null)
        {
            connectButton.onClick.AddListener(ToggleConnection);
            connectButton.GetComponentInChildren<Text>().text = "Connect";
        }
        
        Log("Simple Network Test Ready");
        Log($"Peer ID: {peerId}");
        Log($"Server: {serverUrl}");
        Log($"Room: {roomCode}");
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
    
    // Add this method to handle disconnect (you can call it from another button or key press)
    public void DisconnectButton()
    {
        if (isConnected)
        {
            Disconnect();
        }
    }
    
    void Connect()
    {
        if (isConnected) return;
        
        Log("Connecting...");
        StartCoroutine(ConnectWebSocket());
    }
    

    
    void Disconnect()
    {
        if (!isConnected) return;
        
        Log("Disconnecting...");
        
        try
        {
            cancellationTokenSource?.Cancel();
            webSocket?.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnecting", CancellationToken.None).Wait();
            webSocket?.Dispose();
        }
        catch (Exception e)
        {
            Log($"Error during disconnect: {e.Message}");
        }
        finally
        {
            webSocket = null;
            cancellationTokenSource = null;
            receiveTask = null;
            isConnected = false;
        }
        
        Log("Disconnected");
    }
    
    void SendTestMessage()
    {
        if (!isConnected) return;
        
        SendMessage(new SignalMessage
        {
            type = "offer",
            from = peerId,
            to = "", // Empty for broadcast
            payload = $"Hello from {peerId} at {DateTime.Now:HH:mm:ss}!"
        });
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
                    StartCoroutine(UpdateLogInMainThread(message));
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
        
        // Update log content in main thread using coroutine
        StartCoroutine(UpdateLogInMainThread(logEntry));
        
        Debug.Log(logEntry);
    }
    
    IEnumerator UpdateLogInMainThread(string message)
    {
        logContent += message + "\n";
        
        // Keep only last 20 messages
        string[] lines = logContent.Split('\n');
        if (lines.Length > 20)
        {
            logContent = string.Join("\n", lines, lines.Length - 20, 20);
        }
        
        if (logText != null)
        {
            logText.text = logContent;
        }
        
        yield return null;
    }
    
    void OnDestroy()
    {
        Disconnect();
    }
} 